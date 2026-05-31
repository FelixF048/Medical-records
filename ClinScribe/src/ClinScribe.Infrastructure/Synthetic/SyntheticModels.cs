using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;

namespace ClinScribe.Infrastructure.Synthetic;

/// <summary>模擬病人。</summary>
public sealed record SyntheticPatient(
    string PatientId,
    string DisplayName,
    int AgeYears,
    string Sex);

/// <summary>每筆模擬就醫的預期結果（評測 ground truth）。</summary>
public sealed record ExpectedOutcome(
    bool ExpectCriticalFlag,
    bool ExpectInjection,
    bool ExpectInsufficientData,
    IReadOnlyList<string> ExpectedFlagRuleIds);

/// <summary>模擬就醫（含快照項與 ground truth）。</summary>
public sealed record SyntheticEncounter(
    string EncounterId,
    string PatientId,
    string PatientName,
    int AgeYears,
    string Sex,
    string Department,
    string Setting,          // OPD | ER | IPD
    string ScenarioCategory,
    string SuggestedNoteType,
    IReadOnlyList<SnapshotItem> Items,
    ExpectedOutcome Expected);

/// <summary>整個模擬資料集。</summary>
public sealed class SyntheticDataset
{
    public required IReadOnlyList<SyntheticPatient> Patients { get; init; }
    public required IReadOnlyList<SyntheticEncounter> Encounters { get; init; }

    private IReadOnlyDictionary<string, SyntheticEncounter>? _byEncounter;
    public SyntheticEncounter? FindEncounter(string encounterId)
    {
        _byEncounter ??= Encounters.ToDictionary(e => e.EncounterId, StringComparer.OrdinalIgnoreCase);
        return _byEncounter.TryGetValue(encounterId, out var e) ? e : null;
    }
}
