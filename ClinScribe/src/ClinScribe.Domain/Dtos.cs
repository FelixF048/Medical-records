namespace ClinScribe.Domain;

// ===== 規格書第七章 C# DTO =====

public record GenerateNoteRequest(
    string EncounterId,
    Guid PatientContextSnapshotId,
    string NoteType,
    string? Department,
    AutonomyLevel MaxAutonomy);

public record DraftSection(
    string Key,
    string Title,
    string Content,
    List<string> SourceRefIds,
    double Confidence);

public record SourceReference(
    string Id,
    string SourceType,
    string SourceSystem,
    string ResourceId,
    string? Quote = null,
    DateTimeOffset? RecordedAt = null);

public record SafetyFlag(
    string RuleId,
    string Description,
    FlagSeverity Severity,
    bool RequiresApproval,
    string? RecommendedAction = null);

public record SuggestedAction(
    string Type,
    string Description,
    string Severity,
    bool RequiresClinicianApproval);

public record PendingClinicalAction(
    string ActionType,
    string Description,
    string RequiredRole,
    bool RequiresSignature);

/// <summary>AI 臨床草稿輸出（第十一章 JSON Schema 對應）。</summary>
public record DraftNoteResponse(
    Guid DraftId,
    string NoteType,
    string EncounterId,
    Guid SnapshotId,
    DateTimeOffset GeneratedAt,
    string ModelVersion,
    string PromptVersion,
    List<DraftSection> Sections,
    List<SourceReference> SourceReferences,
    List<string> Uncertainties,
    List<SafetyFlag> SafetyFlags,
    List<SuggestedAction> SuggestedActions,
    List<PendingClinicalAction> PendingClinicalActions,
    bool RequiresClinicianApproval,
    string? ApprovalRoleRequired,
    string? CannotAutoExecuteReason,
    double Confidence,
    List<string> DataQualityIssues,
    List<string> Contradictions,
    List<string> MissingInformation,
    List<string> AuditTags,
    bool PromptInjectionDetected = false);

public record ApprovalRequest(
    Guid DraftId,
    string ApprovalType,
    string RequiredRole,
    string RequestedBy);

public record ApprovalDecision(
    Guid ApprovalId,
    string Decision,
    string? Comment,
    string DecidedBy,
    DateTimeOffset DecidedAt);

public record AgentToolCall(
    string ToolName,
    bool AutoExecutable,
    bool RequiresApproval,
    string? ApprovalRole,
    object? Args = null);

public record AgentActionPlan(
    Guid SessionId,
    List<AgentToolCall> Steps,
    AutonomyLevel Level);

public record ClinicalReviewResult(
    Guid DraftId,
    string Reviewer,
    string Decision,
    List<DraftSection> EditedSections,
    DateTimeOffset At);

// ===== 其他骨架用 DTO =====

public record UserInfo(string UserId, string DisplayName, IReadOnlyList<string> Roles);

public record WorkItem(string Id, string Title, string PatientId, string EncounterId, string Status, FlagSeverity? TopSeverity);

public record PatientSummaryDto(string PatientId, string DisplayName, IReadOnlyList<string> Highlights, IReadOnlyList<SourceReference> Sources, IReadOnlyList<string> MissingInformation);
