using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;
using ClinScribe.Infrastructure.Snapshots;

namespace ClinScribe.Api.Services;

/// <summary>
/// 臨床草稿編排服務（對應第八章狀態機 + 第九章 Gateway 呼叫 + 第十二章 Audit）。
/// 強制安全不變式：
///  - AI 停用時拒絕生成。
///  - 正式寫入 EMR 必須先 Approved 且已簽章（紅線）。
/// </summary>
public sealed class ClinicalDraftService
{
    private readonly IAiGateway _gateway;
    private readonly IAuditService _audit;
    private readonly IDraftRepository _drafts;
    private readonly IIncidentService _incidents;
    private readonly IAiKillSwitch _killSwitch;
    private readonly ISnapshotService _snapshots;

    public ClinicalDraftService(IAiGateway gateway, IAuditService audit, IDraftRepository drafts,
        IIncidentService incidents, IAiKillSwitch killSwitch, ISnapshotService snapshots)
    {
        _gateway = gateway;
        _audit = audit;
        _drafts = drafts;
        _incidents = incidents;
        _killSwitch = killSwitch;
        _snapshots = snapshots;
    }

    public sealed record GenerateOutcome(bool Ok, string? Error, DraftNoteResponse? Draft);

    public async Task<GenerateOutcome> GenerateAsync(GenerateNoteRequest req, string actor,
        IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (!_killSwitch.IsEnabled(req.Department))
            return new(false, "AI_DISABLED", null);

        var items = await _snapshots.GetItemsAsync(req.PatientContextSnapshotId, ct);
        if (items.Count == 0)
        {
            // 若快照不存在，骨架自動由 encounter 建一份（正式版本應要求先建立快照）
            (req, items) = await EnsureSnapshotAsync(req, ct);
        }

        var aiReq = new AiGenerationRequest(req.NoteType, req.EncounterId, req.PatientContextSnapshotId,
            req.Department, req.MaxAutonomy, actor, roles, items);

        var draft = await _gateway.GenerateDraftAsync(aiReq, ct);
        await _drafts.SaveAsync(draft, ct);

        // 注入偵測 → 事件通報（第十七章）
        long? incidentId = null;
        if (draft.PromptInjectionDetected)
        {
            var inc = await _incidents.ReportAsync("PromptInjection", FlagSeverity.Critical,
                $"Encounter {req.EncounterId} 偵測到 Prompt Injection。", actor, ct);
            incidentId = inc.Id;
        }

        await _audit.AppendAsync(new AuditLogEntry
        {
            Actor = actor,
            ActorRole = roles.FirstOrDefault() ?? "unknown",
            EncounterId = req.EncounterId,
            Action = "GenerateDraft",
            ReadResourceIds = items.Select(i => i.ResourceId).ToList(),
            ModelVersion = draft.ModelVersion,
            PromptVersion = draft.PromptVersion,
            AiOutputHash = Hash(draft),
            ToolName = ToolRegistry.GenerateDraftNote,
            ToolAutoExecuted = true,
            SafetyFlagIds = draft.SafetyFlags.Select(f => f.RuleId).ToList(),
            IncidentId = incidentId
        }, ct);

        return new(true, null, draft);
    }

    private async Task<(GenerateNoteRequest, IReadOnlyList<SnapshotItem>)> EnsureSnapshotAsync(
        GenerateNoteRequest req, CancellationToken ct)
    {
        var (snapshotId, items) = await _snapshots.BuildAsync(req.EncounterId, ct);
        return (req with { PatientContextSnapshotId = snapshotId }, items);
    }

    /// <summary>核准草稿（第八章 Approval Gate）。</summary>
    public async Task<bool> ApproveAsync(Guid draftId, string approver, IReadOnlyList<string> roles, CancellationToken ct)
    {
        var draft = await _drafts.GetAsync(draftId, ct);
        if (draft is null) return false;

        // 紅線：命中 Critical 安全旗標未解，不得核准
        if (draft.SafetyFlags.Any(f => f.Severity == FlagSeverity.Critical))
            return false;

        // 狀態守門：僅未終結草稿可被核准
        var status = await _drafts.GetStatusAsync(draftId, ct);
        if (status is not (DraftStatus.Draft or DraftStatus.PendingReview or DraftStatus.PendingApproval))
            return false;

        await _drafts.SetStatusAsync(draftId, DraftStatus.Approved, approver, ct);
        await _audit.AppendAsync(new AuditLogEntry
        {
            Actor = approver,
            ActorRole = roles.FirstOrDefault() ?? "unknown",
            EncounterId = draft.EncounterId,
            Action = "ApproveDraft",
            ApprovedBy = approver,
            AcceptedSections = draft.Sections.Select(s => s.Key).ToList()
        }, ct);
        return true;
    }

    /// <summary>退回/拒絕草稿（第八章；對應 API 21 拒絕、25 退回）。</summary>
    public async Task<bool> RejectAsync(Guid draftId, string reviewer, IReadOnlyList<string> roles, string? reason, CancellationToken ct)
    {
        var draft = await _drafts.GetAsync(draftId, ct);
        if (draft is null) return false;

        var status = await _drafts.GetStatusAsync(draftId, ct);
        // 已簽章/已寫入 EMR 之紀錄不可退回（須走撤回/修訂流程）
        if (status is DraftStatus.Signed or DraftStatus.WrittenToEmr) return false;

        await _drafts.SetStatusAsync(draftId, DraftStatus.Rejected, reviewer, ct);
        await _audit.AppendAsync(new AuditLogEntry
        {
            Actor = reviewer,
            ActorRole = roles.FirstOrDefault() ?? "unknown",
            EncounterId = draft.EncounterId,
            Action = "RejectDraft",
            RejectedSections = draft.Sections.Select(s => s.Key).ToList()
        }, ct);
        return true;
    }

    /// <summary>電子簽章（第八章；僅本人，第四章 SignClinicalRecord 不可自動）。</summary>
    public async Task<bool> SignAsync(Guid draftId, string signer, IReadOnlyList<string> roles, CancellationToken ct)
    {
        var draft = await _drafts.GetAsync(draftId, ct);
        if (draft is null) return false;

        // 紅線：必須先經核准（Approved）才能電子簽章
        var status = await _drafts.GetStatusAsync(draftId, ct);
        if (status is not DraftStatus.Approved) return false;

        await _drafts.SetStatusAsync(draftId, DraftStatus.Signed, signer, ct);
        await _audit.AppendAsync(new AuditLogEntry
        {
            Actor = signer,
            ActorRole = roles.FirstOrDefault() ?? "unknown",
            EncounterId = draft.EncounterId,
            Action = "Sign",
            SignerId = signer
        }, ct);
        return true;
    }

    /// <summary>
    /// 寫入正式 EMR（第四章紅線：AI 不可自動；必須 Approved + Signed）。
    /// </summary>
    public async Task<(bool ok, string? error)> WriteFinalAsync(Guid draftId, string actor,
        IReadOnlyList<string> roles, CancellationToken ct)
    {
        var draft = await _drafts.GetAsync(draftId, ct);
        if (draft is null) return (false, "NOTFOUND");

        // 紅線：必須已簽章（Signed）才可寫入正式 EMR
        var status = await _drafts.GetStatusAsync(draftId, ct);
        if (status is not DraftStatus.Signed)
            return (false, "NOT_SIGNED：正式 EMR 寫入前必須完成核准與電子簽章");

        await _drafts.SetStatusAsync(draftId, DraftStatus.WrittenToEmr, actor, ct);
        await _audit.AppendAsync(new AuditLogEntry
        {
            Actor = actor,
            ActorRole = roles.FirstOrDefault() ?? "unknown",
            EncounterId = draft.EncounterId,
            Action = "WriteFinalEmr",
            ToolName = ToolRegistry.WriteToFinalEMR,
            ToolAutoExecuted = false,
            SignerId = actor,
            EmrWriteAt = DateTimeOffset.UtcNow
        }, ct);
        return (true, null);
    }

    private static string Hash(DraftNoteResponse d)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(d);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
