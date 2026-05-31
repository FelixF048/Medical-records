using ClinScribe.Api.Auth;
using ClinScribe.Api.Services;
using ClinScribe.AiGateway;
using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;
using ClinScribe.Infrastructure.Snapshots;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClinScribe.Api.Endpoints;

/// <summary>規格書第七章 API（骨架子集）。所有臨床寫入皆需授權 + Audit。</summary>
public static class ApiEndpoints
{
    public static void MapClinScribeEndpoints(this WebApplication app)
    {
        // 1. 使用者資訊
        app.MapGet("/api/me", (HttpContext ctx) =>
            Results.Ok(new UserInfo(ctx.User.UserId(), ctx.User.UserId(), ctx.User.Roles())))
            .RequireAuthorization();

        // 2. 工作清單（待審核草稿）
        app.MapGet("/api/worklist", async (IDraftRepository drafts) =>
        {
            var pending = await drafts.ListPendingAsync();
            return Results.Ok(pending.Select(d => new WorkItem(
                d.DraftId.ToString(), d.NoteType, "", d.EncounterId,
                d.RequiresClinicianApproval ? "PendingApproval" : "Draft",
                d.SafetyFlags.Count == 0 ? null : d.SafetyFlags.Max(f => f.Severity))));
        }).RequireAuthorization();

        // 5. 建立資料快照
        app.MapPost("/api/encounters/{encounterId}/snapshots", async (
            string encounterId, ISnapshotService snap, IAuditService audit, HttpContext ctx) =>
        {
            var (id, items) = await snap.BuildAsync(encounterId);
            await audit.AppendAsync(new AuditLogEntry
            {
                Actor = ctx.User.UserId(), ActorRole = ctx.User.Roles().FirstOrDefault() ?? "",
                EncounterId = encounterId, Action = "BuildSnapshot",
                ReadResourceIds = items.Select(i => i.ResourceId).ToList()
            });
            return Results.Ok(new { snapshotId = id, count = items.Count });
        }).RequireAuthorization(AuthPolicies.ReadPatient);

        // 7/8/9/10/11. 產生 AI 草稿（依 noteType）
        app.MapPost("/api/ai/notes", async (
            GenerateNoteRequest req, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var outcome = await svc.GenerateAsync(req, ctx.User.UserId(), ctx.User.Roles(), ct);
            if (!outcome.Ok)
                return outcome.Error == "AI_DISABLED"
                    ? Results.Problem(statusCode: 503, title: "AI 已停用")
                    : Results.Problem(statusCode: 422, title: outcome.Error);
            return Results.Ok(outcome.Draft);
        }).RequireAuthorization(AuthPolicies.GenerateNote);

        // 16. 待核准處方草稿（高風險）
        app.MapPost("/api/ai/draft-prescription", async (
            GenerateNoteRequest req, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var fixedReq = req with { NoteType = NoteTypes.DraftPrescription };
            var outcome = await svc.GenerateAsync(fixedReq, ctx.User.UserId(), ctx.User.Roles(), ct);
            return outcome.Ok ? Results.Ok(outcome.Draft) : Results.Problem(statusCode: 422, title: outcome.Error);
        }).RequireAuthorization(AuthPolicies.GeneratePrescription);

        // 18. 取得草稿
        app.MapGet("/api/drafts/{id:guid}", async (Guid id, IDraftRepository drafts) =>
        {
            var d = await drafts.GetAsync(id);
            return d is null ? Results.NotFound() : Results.Ok(d);
        }).RequireAuthorization();

        // 24. 核准草稿（Approval Gate）
        app.MapPost("/api/approvals/{id:guid}/approve", async (
            Guid id, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var ok = await svc.ApproveAsync(id, ctx.User.UserId(), ctx.User.Roles(), ct);
            return ok ? Results.Ok(new { approved = true })
                      : Results.Problem(statusCode: 422, title: "無法核准（不存在或含未解的 Critical 安全旗標）");
        }).RequireAuthorization(AuthPolicies.ApproveClinical);

        // 28. 電子簽章
        app.MapPost("/api/signatures/{id:guid}", async (
            Guid id, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var ok = await svc.SignAsync(id, ctx.User.UserId(), ctx.User.Roles(), ct);
            return ok ? Results.Ok(new { signed = true })
                      : Results.Problem(statusCode: 422, title: "無法簽章（草稿不存在或尚未核准）");
        }).RequireAuthorization(AuthPolicies.SignRecord);

        // 27. 寫入正式 EMR（紅線：AI 不可自動；需核准+簽章）
        app.MapPost("/api/emr/final/{id:guid}", async (
            Guid id, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var (ok, err) = await svc.WriteFinalAsync(id, ctx.User.UserId(), ctx.User.Roles(), ct);
            return ok ? Results.Ok(new { written = true }) : Results.Problem(statusCode: 422, title: err);
        }).RequireAuthorization(AuthPolicies.WriteFinalEmr);

        // 29. 查詢 Audit Log + 鏈驗證
        app.MapGet("/api/audit", async (
            [FromQuery] string? patientId, [FromQuery] string? actor, IAuditService audit) =>
        {
            var items = await audit.QueryAsync(patientId, actor);
            var valid = await audit.VerifyChainAsync();
            return Results.Ok(new { chainValid = valid, count = items.Count, items });
        }).RequireAuthorization(AuthPolicies.ViewAudit);

        // 33. 停用 AI（kill-switch）
        app.MapPost("/api/ai/disable", (
            [FromQuery] string scope, IAiKillSwitch ks, HttpContext ctx) =>
        {
            ks.Disable(scope ?? "*", ctx.User.UserId(), "manual");
            return Results.Ok(new { disabled = scope ?? "*" });
        }).RequireAuthorization(AuthPolicies.ManageAi);

        // 21/25. 拒絕 / 退回草稿
        app.MapPost("/api/drafts/{id:guid}/reject", async (
            Guid id, [FromBody] RejectRequest? body, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var ok = await svc.RejectAsync(id, ctx.User.UserId(), ctx.User.Roles(), body?.Reason, ct);
            return ok ? Results.Ok(new { rejected = true })
                      : Results.Problem(statusCode: 422, title: "無法退回（不存在或已簽章/已寫入 EMR）");
        }).RequireAuthorization(AuthPolicies.ApproveClinical);

        // 12/13/14. 安全檢核（缺漏/矛盾/過敏；只回旗標不落稿狀態變更）
        app.MapPost("/api/safety/check", async (
            GenerateNoteRequest req, ClinicalDraftService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var outcome = await svc.GenerateAsync(req, ctx.User.UserId(), ctx.User.Roles(), ct);
            if (!outcome.Ok || outcome.Draft is null)
                return Results.Problem(statusCode: 422, title: outcome.Error ?? "檢核失敗");
            var d = outcome.Draft;
            return Results.Ok(new
            {
                d.SafetyFlags,
                d.MissingInformation,
                d.Contradictions,
                d.DataQualityIssues,
                d.RequiresClinicianApproval
            });
        }).RequireAuthorization(AuthPolicies.GenerateNote);

        // 30. 查詢模型 / Prompt 版本
        app.MapGet("/api/models/version", (IModelProvider model, IOptions<AiGatewayOptions> opt) =>
            Results.Ok(new
            {
                modelVersion = model.ModelVersion,
                promptVersion = opt.Value.PromptVersion,
                systemPromptVersion = opt.Value.SystemPromptVersion,
                useLiveProvider = opt.Value.UseLiveProvider
            })).RequireAuthorization();

        // 33b. 重新啟用 AI（kill-switch off）
        app.MapPost("/api/ai/enable", (
            [FromQuery] string scope, IAiKillSwitch ks, HttpContext ctx) =>
        {
            ks.Enable(scope ?? "*", ctx.User.UserId());
            return Results.Ok(new { enabled = scope ?? "*" });
        }).RequireAuthorization(AuthPolicies.ManageAi);

        // 35b. 建立事件通報
        app.MapPost("/api/incidents", async (
            CreateIncidentRequest req, IIncidentService inc, IAuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            var r = await inc.ReportAsync(req.Type, req.Severity, req.Detail, ctx.User.UserId(), ct);
            await audit.AppendAsync(new AuditLogEntry
            {
                Actor = ctx.User.UserId(), ActorRole = ctx.User.Roles().FirstOrDefault() ?? "",
                Action = "ReportIncident", IncidentId = r.Id
            }, ct);
            return Results.Ok(r);
        }).RequireAuthorization();

        // 35. 事件通報清單
        app.MapGet("/api/incidents", async (IIncidentService inc) =>
            Results.Ok(await inc.ListAsync()))
            .RequireAuthorization(AuthPolicies.ViewAudit);

        // 3. 病人清單（第十三章目錄）
        app.MapGet("/api/patients", async (
            IPatientDirectory dir, [FromQuery] int? skip, [FromQuery] int? take) =>
            Results.Ok(await dir.ListPatientsAsync(skip ?? 0, take ?? 50)))
            .RequireAuthorization(AuthPolicies.ReadPatient);

        // 4. 就醫清單（可依科別/場域過濾）
        app.MapGet("/api/encounters", async (
            IPatientDirectory dir, [FromQuery] string? department, [FromQuery] string? setting,
            [FromQuery] int? take) =>
        {
            var items = await dir.ListEncountersAsync(department, setting, take ?? 100);
            var total = await dir.CountEncountersAsync();
            return Results.Ok(new { total, count = items.Count, items });
        }).RequireAuthorization(AuthPolicies.ReadPatient);

        // 36. AI 品質報表（第十九章評測 harness）
        app.MapGet("/api/eval/quality", async (
            EvaluationService eval, [FromQuery] int? limit, CancellationToken ct) =>
        {
            if (!eval.HasDataset)
                return Results.Problem(statusCode: 409, title: "未啟用模擬資料集，無法評測。");
            var report = await eval.RunAsync(limit, ct);
            return Results.Ok(report);
        }).RequireAuthorization(AuthPolicies.ViewAudit);
    }
}

public sealed record RejectRequest(string? Reason);
public sealed record CreateIncidentRequest(string Type, FlagSeverity Severity, string Detail);
