namespace ClinScribe.Domain;

/// <summary>規格書第四章 Agent 自主等級。L5 為系統層永久禁止，故不在可組態列舉中。</summary>
public enum AutonomyLevel { L0 = 0, L1 = 1, L2 = 2, L3 = 3, L4 = 4 }

/// <summary>安全警示嚴重度（第十一/十五章）。</summary>
public enum FlagSeverity { Info, Warning, High, Critical }

/// <summary>草稿/文件狀態。任何臨床行動在核准+簽章前恆為 PendingDraft（核心不變式）。</summary>
public enum DraftStatus
{
    Draft,
    PendingReview,
    PendingApproval,
    Approved,
    Rejected,
    Signed,
    WrittenToEmr
}

/// <summary>AI 輸出文件型別（第十一章 noteType）。</summary>
public static class NoteTypes
{
    public const string Soap = "SOAP";
    public const string Discharge = "Discharge";
    public const string Consult = "Consult";
    public const string Nursing = "Nursing";
    public const string DraftOrder = "DraftOrder";
    public const string DraftPrescription = "DraftPrescription";
    public const string Education = "Education";
    public const string SafetyAlert = "SafetyAlert";
    public const string InsufficientData = "InsufficientData";
    public const string InjectionDetected = "InjectionDetected";
}

/// <summary>臨床角色（第二章）。</summary>
public static class ClinicalRoles
{
    public const string Physician = "Physician";
    public const string Nurse = "Nurse";
    public const string Pharmacist = "Pharmacist";
    public const string LabTech = "LabTech";
    public const string Radiographer = "Radiographer";
    public const string CaseManager = "CaseManager";
    public const string Clerk = "Clerk";
    public const string MedicalRecords = "MedicalRecords";
    public const string ItDept = "ItDept";
    public const string Security = "Security";
    public const string Compliance = "Compliance";
    public const string SysAdmin = "SysAdmin";
    public const string AiAdmin = "AiAdmin";
    public const string Vendor = "Vendor";
    public const string PatientPortal = "PatientPortal";
}
