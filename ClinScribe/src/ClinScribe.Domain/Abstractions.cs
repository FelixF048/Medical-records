namespace ClinScribe.Domain.Abstractions;

using ClinScribe.Domain;

/// <summary>append-only 稽核服務（第十二/十七章）。不提供更新或刪除。</summary>
public interface IAuditService
{
    Task<AuditLogEntry> AppendAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(string? patientId = null, string? actor = null, CancellationToken ct = default);
    /// <summary>驗證雜湊鏈完整性（防竄改）。</summary>
    Task<bool> VerifyChainAsync(CancellationToken ct = default);
}

/// <summary>草稿儲存庫。臨床行動在核准+簽章前狀態恆 ≤ PendingApproval。</summary>
public interface IDraftRepository
{
    Task<DraftNoteResponse> SaveAsync(DraftNoteResponse draft, CancellationToken ct = default);
    Task<DraftNoteResponse?> GetAsync(Guid draftId, CancellationToken ct = default);
    Task<DraftStatus?> GetStatusAsync(Guid draftId, CancellationToken ct = default);
    Task<IReadOnlyList<DraftNoteResponse>> ListPendingAsync(CancellationToken ct = default);
    Task<bool> SetStatusAsync(Guid draftId, DraftStatus status, string actor, CancellationToken ct = default);
}

/// <summary>事件通報（第十七章 incident workflow）。</summary>
public interface IIncidentService
{
    Task<IncidentReport> ReportAsync(string type, FlagSeverity severity, string detail, string reportedBy, CancellationToken ct = default);
    Task<IReadOnlyList<IncidentReport>> ListAsync(CancellationToken ct = default);
}

/// <summary>AI 停用開關（第十七章 kill-switch）。</summary>
public interface IAiKillSwitch
{
    bool IsEnabled(string? scope = null);
    void Disable(string scope, string actor, string reason);
    void Enable(string scope, string actor);
}

/// <summary>病人/就醫目錄（第十三章；唯讀查詢，供工作清單與總覽）。</summary>
public interface IPatientDirectory
{
    Task<IReadOnlyList<PatientSummaryDto>> ListPatientsAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItem>> ListEncountersAsync(string? department = null, string? setting = null, int take = 100, CancellationToken ct = default);
    Task<int> CountEncountersAsync(CancellationToken ct = default);
}
