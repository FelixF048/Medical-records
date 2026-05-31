namespace ClinScribe.Web.Services;

/// <summary>
/// 目前使用者情境（Scoped，per-circuit）。
/// ⚠️ 規格第十七章：禁止用 Singleton 保存使用者/病人狀態。骨架用於示範角色切換與 ABAC。
/// </summary>
public sealed class CurrentUser
{
    public string UserId { get; set; } = "dr.wang";
    public List<string> Roles { get; set; } = ["Physician"];

    public bool IsCareTeamMember { get; set; } = true;
    public bool IsAttending { get; set; } = true;
    public bool IsOnShift { get; set; } = true;
    public bool HasConsent { get; set; } = true;
    public bool IsEmergency { get; set; }
}
