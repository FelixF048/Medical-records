namespace ClinScribe.Domain.Abstractions;

using ClinScribe.Domain;

/// <summary>送進 AI Gateway 的請求（已含授權後的最小化資料）。</summary>
public record AiGenerationRequest(
    string NoteType,
    string EncounterId,
    Guid SnapshotId,
    string? Department,
    AutonomyLevel MaxAutonomy,
    string Actor,
    IReadOnlyList<string> ActorRoles,
    IReadOnlyList<SnapshotItem> SnapshotItems);

/// <summary>資料快照中的單筆來源項（AI 只能引用此集合內的 ResourceId）。</summary>
public record SnapshotItem(
    string SourceRefId,
    string SourceType,
    string SourceSystem,
    string ResourceId,
    string Content,
    DateTimeOffset? RecordedAt = null,
    IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>
/// AI Gateway 唯一對外出口（第九章）。前端與 Domain 服務僅透過此介面。
/// 實作須串接 pipeline：Sanitizer → Minimizer → SystemPrompt → InjectionDetector
/// → Model → SchemaValidator → SourceCitationEnforcer → SafetyGuardrail → AuditLogger。
/// </summary>
public interface IAiGateway
{
    Task<DraftNoteResponse> GenerateDraftAsync(AiGenerationRequest request, CancellationToken ct = default);
}

/// <summary>模型供應商抽象（第九章 ModelRouter / VersionLock）。</summary>
public interface IModelProvider
{
    string ModelVersion { get; }
    Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken ct = default);
}
