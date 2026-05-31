using ClinScribe.AiGateway.Pipeline;
using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinScribe.AiGateway;

/// <summary>
/// AI Gateway 唯一對外出口（第九章 pipeline 編排）。
/// 流程：Sanitizer → InjectionDetector → SystemPrompt → (Model) → 規則組裝草稿
///       → SourceCitationEnforcer → SafetyGuardrail → 標示不確定性/需核准。
/// 安全不變式：所有臨床行動輸出為 pending；高風險 requiresClinicianApproval=true。
/// </summary>
public sealed class AiGatewayService : IAiGateway
{
    private readonly RequestSanitizer _sanitizer;
    private readonly PromptInjectionDetector _injection;
    private readonly SourceCitationEnforcer _citation;
    private readonly SafetyGuardrail _guardrail;
    private readonly SystemPromptRegistry _prompts;
    private readonly IModelProvider _model;
    private readonly AiGatewayOptions _opt;

    public AiGatewayService(
        RequestSanitizer sanitizer,
        PromptInjectionDetector injection,
        SourceCitationEnforcer citation,
        SafetyGuardrail guardrail,
        SystemPromptRegistry prompts,
        IModelProvider model,
        IOptions<AiGatewayOptions> opt)
    {
        _sanitizer = sanitizer;
        _injection = injection;
        _citation = citation;
        _guardrail = guardrail;
        _prompts = prompts;
        _model = model;
        _opt = opt.Value;
    }

    public Task<DraftNoteResponse> GenerateDraftAsync(AiGenerationRequest request, CancellationToken ct = default)
    {
        var draftId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // 1. Sanitize 快照內容
        var items = request.SnapshotItems
            .Select(i => i with { Content = _sanitizer.Sanitize(i.Content) })
            .ToList();

        // 2. Prompt Injection 偵測（R-INJ-019）— 命中即阻斷，回傳 InjectionDetected
        foreach (var i in items)
        {
            if (_injection.TryDetect(i.Content, out var matched))
            {
                return Task.FromResult(BuildInjectionResponse(draftId, request, now, i.SourceRefId, matched));
            }
        }

        // 3. 資料不足 → InsufficientData（R-DATA-001）
        if (items.Count == 0)
        {
            return Task.FromResult(BuildInsufficientResponse(draftId, request, now));
        }

        // 4. System Prompt（Registry）+ 規則化草稿組裝（事實僅來自快照）
        _ = _prompts.Get(_opt.SystemPromptVersion);
        var (sections, pending) = ComposeSections(request.NoteType, items);

        // 5. 來源引用強制（R-SRC-002）
        var (citedSections, addedUncertainties) = _citation.Enforce(sections, items);

        // 6. 安全 Guardrail（過敏/危急值/生命徵象/特殊族群/多重用藥/矛盾/禁語）
        var softened = citedSections.Select(s => s with { Content = _guardrail.Soften(s.Content) }).ToList();
        var flags = _guardrail.Evaluate(request.NoteType, softened, items).ToList();
        var missing = _guardrail.DetectMissing(request.NoteType, items).ToList();
        var contradictions = _guardrail.DetectContradictions(items).ToList();

        // 7. 需核准判定
        var hasCritical = flags.Any(f => f.Severity == FlagSeverity.Critical);
        var clinicalNoteTypes = new[] { NoteTypes.Soap, NoteTypes.Discharge, NoteTypes.Consult,
            NoteTypes.Nursing, NoteTypes.DraftOrder, NoteTypes.DraftPrescription, NoteTypes.Education };
        var requiresApproval = clinicalNoteTypes.Contains(request.NoteType) || hasCritical;

        var uncertainties = new List<string>();
        uncertainties.AddRange(addedUncertainties);

        var response = new DraftNoteResponse(
            DraftId: draftId,
            NoteType: request.NoteType,
            EncounterId: request.EncounterId,
            SnapshotId: request.SnapshotId,
            GeneratedAt: now,
            ModelVersion: _model.ModelVersion,
            PromptVersion: _opt.PromptVersion,
            Sections: softened,
            SourceReferences: items.Select(i => new SourceReference(
                i.SourceRefId, i.SourceType, i.SourceSystem, i.ResourceId, i.Content, i.RecordedAt)).ToList(),
            Uncertainties: uncertainties,
            SafetyFlags: flags,
            SuggestedActions: [],
            PendingClinicalActions: pending,
            RequiresClinicianApproval: requiresApproval,
            ApprovalRoleRequired: requiresApproval ? ResolveApprovalRole(request.NoteType, hasCritical) : null,
            CannotAutoExecuteReason: hasCritical
                ? "命中 Critical 安全規則，禁止自動定稿"
                : (requiresApproval ? "臨床文件需醫事人員覆核簽章" : null),
            Confidence: hasCritical ? 0.4 : 0.7,
            DataQualityIssues: missing.Count > 0 ? ["資料完整度不足"] : [],
            Contradictions: contradictions,
            MissingInformation: missing,
            AuditTags: [request.NoteType.ToLowerInvariant()]);

        return Task.FromResult(response);
    }

    private static string ResolveApprovalRole(string noteType, bool hasCritical) => noteType switch
    {
        NoteTypes.DraftPrescription => "Physician+Pharmacist",
        NoteTypes.Nursing => ClinicalRoles.Nurse,
        _ => ClinicalRoles.Physician
    };

    private static (List<DraftSection> sections, List<PendingClinicalAction> pending) ComposeSections(
        string noteType, IReadOnlyList<SnapshotItem> items)
    {
        var pending = new List<PendingClinicalAction>();
        var refIds = items.Select(i => i.SourceRefId).ToList();

        List<DraftSection> sections = noteType switch
        {
            NoteTypes.Soap =>
            [
                new("S", "主訴/病史", Summary(items, "Encounter.note", "HIS"), Refs(items, "Encounter.note"), 0.8),
                new("O", "客觀", Summary(items, "Observation", "FHIR"), Refs(items, "Observation"), 0.85),
                new("A", "評估(待醫師確認)", "依現有資料整理之鑑別方向，待醫師確認最終診斷。", [], 0.5),
                new("P", "計畫(待核准)", "建議症狀治療與衛教；任何處方為待核准草稿。", [], 0.5),
            ],
            NoteTypes.DraftPrescription =>
            [
                new("rx", "處方草稿(待核准)", Summary(items, "MedicationRequest", "FHIR"), Refs(items, "MedicationRequest"), 0.4),
            ],
            _ =>
            [
                new("summary", "摘要", string.Join("；", items.Select(i => i.Content)), refIds, 0.7),
            ]
        };

        if (noteType is NoteTypes.DraftPrescription)
            pending.Add(new PendingClinicalAction("Prescription", "處方草稿待核准", "Physician", true));
        if (noteType is NoteTypes.DraftOrder)
            pending.Add(new PendingClinicalAction("Order", "醫囑草稿待核准", "Physician", true));
        if (noteType is NoteTypes.Soap)
            pending.Add(new PendingClinicalAction("Prescription", "症狀治療處方(待核准)", "Physician", true));

        return (sections, pending);
    }

    private static string Summary(IReadOnlyList<SnapshotItem> items, string type, string system)
    {
        var hits = items.Where(i => i.SourceType == type).Select(i => i.Content).ToList();
        return hits.Count > 0 ? string.Join("；", hits) : "（無對應來源資料）";
    }

    private static List<string> Refs(IReadOnlyList<SnapshotItem> items, string type)
        => items.Where(i => i.SourceType == type).Select(i => i.SourceRefId).ToList();

    private DraftNoteResponse BuildInjectionResponse(Guid id, AiGenerationRequest req, DateTimeOffset now, string sourceRef, string matched)
        => new(id, NoteTypes.InjectionDetected, req.EncounterId, req.SnapshotId, now,
            _model.ModelVersion, _opt.PromptVersion, [], [],
            ["偵測到資料中含注入內容，已阻斷流程"],
            [new SafetyFlag("R-SEC-INJECTION", $"來源 {sourceRef} 含注入字串：'{matched}'", FlagSeverity.Critical, true, "阻斷流程並通報資安")],
            [], [], true, ClinicalRoles.Security, "偵測到 Prompt Injection", 0.0, [], [], [], ["security", "injection"],
            PromptInjectionDetected: true);

    private DraftNoteResponse BuildInsufficientResponse(Guid id, AiGenerationRequest req, DateTimeOffset now)
        => new(id, NoteTypes.InsufficientData, req.EncounterId, req.SnapshotId, now,
            _model.ModelVersion, _opt.PromptVersion, [], [],
            ["無足夠資料形成評估"], [], [], [], true, ClinicalRoles.Physician,
            "資料不足，依規則不得產生結論", 0.0,
            ["快照無資料"], [], ["主訴", "理學檢查", "檢驗"], ["insufficient"]);
}
