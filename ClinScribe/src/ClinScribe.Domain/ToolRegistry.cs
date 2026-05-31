namespace ClinScribe.Domain;

/// <summary>第四章工具權限矩陣的一列。</summary>
public record ToolPermission(
    string ToolName,
    bool AutoExecutable,
    bool RequiresApproval,
    string? ApprovalRole,
    bool RequiresSignature);

/// <summary>
/// 工具權限矩陣（規格書第四章）。
/// WriteToFinalEMR 與 SignClinicalRecord 的 AutoExecutable=false 為系統層紅線，
/// AI Gateway / Tool 執行器必須拒絕任何由模型直接發起的呼叫。
/// </summary>
public static class ToolRegistry
{
    public const string ReadPatientData = "ReadPatientData";
    public const string SummarizeEncounter = "SummarizeEncounter";
    public const string GenerateDraftNote = "GenerateDraftNote";
    public const string GenerateDraftOrder = "GenerateDraftOrder";
    public const string GenerateDraftPrescription = "GenerateDraftPrescription";
    public const string GeneratePatientMessage = "GeneratePatientMessage";
    public const string CheckSafetyFlags = "CheckSafetyFlags";
    public const string NotifyClinician = "NotifyClinician";
    public const string WriteToDraftArea = "WriteToDraftArea";
    public const string WriteToFinalEMR = "WriteToFinalEMR";
    public const string SignClinicalRecord = "SignClinicalRecord";

    public static readonly IReadOnlyDictionary<string, ToolPermission> Matrix =
        new Dictionary<string, ToolPermission>(StringComparer.OrdinalIgnoreCase)
        {
            [ReadPatientData] = new(ReadPatientData, true, false, null, false),
            [SummarizeEncounter] = new(SummarizeEncounter, true, false, null, false),
            [GenerateDraftNote] = new(GenerateDraftNote, true, true, ClinicalRoles.Physician, true),
            [GenerateDraftOrder] = new(GenerateDraftOrder, true, true, ClinicalRoles.Physician, true),
            [GenerateDraftPrescription] = new(GenerateDraftPrescription, true, true, ClinicalRoles.Physician, true),
            [GeneratePatientMessage] = new(GeneratePatientMessage, true, true, ClinicalRoles.Physician, false),
            [CheckSafetyFlags] = new(CheckSafetyFlags, true, false, null, false),
            [NotifyClinician] = new(NotifyClinician, true, false, null, false),
            [WriteToDraftArea] = new(WriteToDraftArea, true, false, null, false),
            // 系統層紅線：不可自動執行
            [WriteToFinalEMR] = new(WriteToFinalEMR, false, true, ClinicalRoles.Physician, true),
            [SignClinicalRecord] = new(SignClinicalRecord, false, true, ClinicalRoles.Physician, true),
        };

    /// <summary>模型/Agent 是否被允許「自動」執行該工具。</summary>
    public static bool CanAutoExecute(string toolName) =>
        Matrix.TryGetValue(toolName, out var p) && p.AutoExecutable;
}
