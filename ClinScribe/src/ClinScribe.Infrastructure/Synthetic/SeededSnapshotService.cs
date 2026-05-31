using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;
using ClinScribe.Infrastructure.Snapshots;

namespace ClinScribe.Infrastructure.Synthetic;

/// <summary>
/// 以模擬資料集為後端的快照服務（第十三章）。
/// BuildAsync 依 encounterId 取出對應就醫的真實快照；未知 ID 回退為一般門診樣本。
/// 同時實作 IPatientDirectory 供工作清單/總覽查詢。
/// </summary>
public sealed class SeededSnapshotService : ISnapshotService, IPatientDirectory
{
    private readonly SyntheticDataset _data;
    private readonly Dictionary<Guid, IReadOnlyList<SnapshotItem>> _snapshots = new();
    private readonly object _lock = new();

    public SeededSnapshotService(SyntheticDataset data) => _data = data;

    public Task<(Guid snapshotId, IReadOnlyList<SnapshotItem> items)> BuildAsync(
        string encounterId, CancellationToken ct = default)
    {
        var enc = _data.FindEncounter(encounterId);
        IReadOnlyList<SnapshotItem> items = enc?.Items ?? FallbackItems(encounterId);

        var id = Guid.NewGuid();
        lock (_lock) _snapshots[id] = items;
        return Task.FromResult((id, items));
    }

    public Task<IReadOnlyList<SnapshotItem>> GetItemsAsync(Guid snapshotId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_snapshots.TryGetValue(snapshotId, out var v) ? v : []);
    }

    public Task<IReadOnlyList<PatientSummaryDto>> ListPatientsAsync(
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var byPatient = _data.Encounters
            .GroupBy(e => e.PatientId)
            .ToDictionary(g => g.Key, g => g.First());

        var list = _data.Patients
            .Skip(skip).Take(take)
            .Select(p =>
            {
                byPatient.TryGetValue(p.PatientId, out var enc);
                var highlights = new List<string> { $"{p.AgeYears} 歲 / {p.Sex}" };
                if (enc is not null) highlights.Add($"{enc.Department}・{enc.ScenarioCategory}");
                return new PatientSummaryDto(
                    p.PatientId, p.DisplayName, highlights,
                    enc?.Items.Select(i => new SourceReference(
                        i.SourceRefId, i.SourceType, i.SourceSystem, i.ResourceId, null, i.RecordedAt)).ToList()
                        ?? [],
                    []);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<PatientSummaryDto>>(list);
    }

    public Task<IReadOnlyList<WorkItem>> ListEncountersAsync(
        string? department = null, string? setting = null, int take = 100, CancellationToken ct = default)
    {
        var q = _data.Encounters.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(department))
            q = q.Where(e => e.Department == department);
        if (!string.IsNullOrWhiteSpace(setting))
            q = q.Where(e => e.Setting == setting);

        var list = q.Take(take).Select(e => new WorkItem(
            e.EncounterId, e.SuggestedNoteType, e.PatientId, e.EncounterId,
            e.Setting, e.Expected.ExpectCriticalFlag ? FlagSeverity.Critical : null)).ToList();
        return Task.FromResult<IReadOnlyList<WorkItem>>(list);
    }

    public Task<int> CountEncountersAsync(CancellationToken ct = default)
        => Task.FromResult(_data.Encounters.Count);

    private static IReadOnlyList<SnapshotItem> FallbackItems(string encounterId) => encounterId switch
    {
        // 保留既有示範案例（向後相容既有測試/Demo）
        "ENC-6006" =>
        [
            new SnapshotItem("a1", "AllergyIntolerance", "FHIR", "Allergy-PCN",
                "病人對 Penicillin 過敏（嚴重）。", DateTimeOffset.UtcNow.AddYears(-2),
                new Dictionary<string,string>{[ClinicalTags.Category]="allergy",[ClinicalTags.Allergen]="penicillin"}),
            new SnapshotItem("m1", "MedicationRequest", "FHIR", "Med-Amox",
                "醫師考慮處方 Amoxicillin 500mg。", DateTimeOffset.UtcNow.AddMinutes(-5),
                new Dictionary<string,string>{[ClinicalTags.Category]="med",[ClinicalTags.Drug]="Amoxicillin",[ClinicalTags.DrugClass]="penicillin"}),
        ],
        _ =>
        [
            new SnapshotItem($"{encounterId}-s1", "Encounter.note", "HIS", $"{encounterId}#hpi",
                "病人主訴咳嗽3天，無發燒。", DateTimeOffset.UtcNow.AddMinutes(-20)),
            new SnapshotItem($"{encounterId}-s2", "Observation", "FHIR", $"{encounterId}#vs",
                "體溫 36.8°C，SpO2 98%，BP 120/78。", DateTimeOffset.UtcNow.AddMinutes(-15),
                new Dictionary<string,string>{[ClinicalTags.Category]="vital",[ClinicalTags.Code]="Temp",[ClinicalTags.Value]="36.8"}),
        ],
    };
}
