using System.Security.Claims;
using ClinScribe.Domain;

namespace ClinScribe.Api.Auth;

/// <summary>
/// 規格書第二章 RBAC + ABAC。後端為唯一授權點（UI 隱藏不等於授權）。
/// 骨架以 header 模擬身分；正式版本改為 SSO/OIDC + MFA。
/// </summary>
public static class AuthPolicies
{
    public const string ReadPatient = "ReadPatient";
    public const string GenerateNote = "GenerateNote";
    public const string GeneratePrescription = "GenerateDraftPrescription";
    public const string ApproveClinical = "ApproveClinical";
    public const string SignRecord = "SignRecord";
    public const string WriteFinalEmr = "WriteFinalEmr";
    public const string ViewAudit = "ViewAudit";
    public const string ManageAi = "ManageAi";
}

/// <summary>ABAC 情境屬性（第二章）。骨架由 header 帶入示範值。</summary>
public sealed record AbacContext(
    bool IsCareTeamMember,
    bool IsAttendingForEncounter,
    bool SameDepartment,
    bool SameWard,
    bool IsOnShift,
    bool HasPatientConsent,
    bool IsEmergency,
    string Purpose,
    bool DeidentifiedOnly)
{
    public static AbacContext FromHeaders(IHeaderDictionary h) => new(
        IsCareTeamMember: Flag(h, "X-Abac-CareTeam", true),
        IsAttendingForEncounter: Flag(h, "X-Abac-Attending", true),
        SameDepartment: Flag(h, "X-Abac-SameDept", true),
        SameWard: Flag(h, "X-Abac-SameWard", true),
        IsOnShift: Flag(h, "X-Abac-OnShift", true),
        HasPatientConsent: Flag(h, "X-Abac-Consent", true),
        IsEmergency: Flag(h, "X-Abac-Emergency", false),
        Purpose: h.TryGetValue("X-Abac-Purpose", out var p) ? p.ToString() : "treatment",
        DeidentifiedOnly: Flag(h, "X-Abac-Deid", false));

    private static bool Flag(IHeaderDictionary h, string key, bool dflt)
        => h.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : dflt;
}

public static class ClaimsExtensions
{
    public static string UserId(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
    public static IReadOnlyList<string> Roles(this ClaimsPrincipal u) =>
        u.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
}
