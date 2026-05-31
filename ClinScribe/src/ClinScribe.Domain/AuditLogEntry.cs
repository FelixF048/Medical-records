namespace ClinScribe.Domain;

/// <summary>
/// 稽核紀錄（規格書第十二章）。append-only + 雜湊鏈，可回答 15 問。
/// RowHash = H(PrevHash || canonical(payload))。
/// </summary>
public record AuditLogEntry
{
    public long Id { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string Actor { get; init; } = "system";          // (1) 誰
    public string ActorRole { get; init; } = "system";
    public string? PatientId { get; init; }                 // (2) 病人 / 就醫
    public string? EncounterId { get; init; }
    public string Action { get; init; } = "";               // Read/Generate/ToolCall/Approve/Sign/WriteEmr...
    public IReadOnlyList<string> ReadResourceIds { get; init; } = [];   // (3)
    public string? ModelVersion { get; init; }              // (4)
    public string? PromptVersion { get; init; }             // (5)
    public string? KbVersion { get; init; }
    public string? AiOutputHash { get; init; }              // (6)
    public string? ToolName { get; init; }                  // (7)
    public bool? ToolAutoExecuted { get; init; }
    public IReadOnlyList<string> AcceptedSections { get; init; } = [];  // (8)
    public IReadOnlyList<string> ModifiedSections { get; init; } = [];  // (9)
    public IReadOnlyList<string> RejectedSections { get; init; } = [];  // (10)
    public string? ApprovedBy { get; init; }                // (11)
    public string? SignerId { get; init; }                  // (12)
    public DateTimeOffset? EmrWriteAt { get; init; }        // (13)
    public IReadOnlyList<string> SafetyFlagIds { get; init; } = [];     // (14)
    public long? IncidentId { get; init; }                  // (15)
    public string PrevHash { get; init; } = "";
    public string RowHash { get; init; } = "";
}

public record IncidentReport(
    long Id,
    string Type,
    FlagSeverity Severity,
    string Detail,
    string Status,
    string ReportedBy,
    DateTimeOffset At);
