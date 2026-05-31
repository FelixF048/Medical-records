using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;

namespace ClinScribe.Infrastructure.Snapshots;

/// <summary>
/// 資料快照服務（第十三章）。建立版本化、唯讀的 EncounterDataSnapshot；
/// AI 只能引用快照內的 ResourceId。骨架版本提供示範資料（含一個過敏案例）。
/// </summary>
public interface ISnapshotService
{
    Task<(Guid snapshotId, IReadOnlyList<SnapshotItem> items)> BuildAsync(string encounterId, CancellationToken ct = default);
    Task<IReadOnlyList<SnapshotItem>> GetItemsAsync(Guid snapshotId, CancellationToken ct = default);
}

public sealed class DemoSnapshotService : ISnapshotService
{
    private readonly Dictionary<Guid, IReadOnlyList<SnapshotItem>> _snapshots = new();
    private readonly object _lock = new();

    public Task<(Guid, IReadOnlyList<SnapshotItem>)> BuildAsync(string encounterId, CancellationToken ct = default)
    {
        IReadOnlyList<SnapshotItem> items = encounterId switch
        {
            "ENC-6006" =>
            [
                new SnapshotItem("a1", "AllergyIntolerance", "FHIR", "Allergy-PCN", "病人對 Penicillin 過敏（嚴重）。", DateTimeOffset.UtcNow.AddYears(-2)),
                new SnapshotItem("m1", "MedicationRequest", "FHIR", "Med-Amox", "醫師考慮處方 Amoxicillin 500mg。", DateTimeOffset.UtcNow.AddMinutes(-5)),
            ],
            _ =>
            [
                new SnapshotItem("s1", "Encounter.note", "HIS", $"{encounterId}#hpi", "病人主訴咳嗽3天，無發燒。", DateTimeOffset.UtcNow.AddMinutes(-20)),
                new SnapshotItem("s2", "Observation", "FHIR", "Obs-VS-22", "體溫 36.8°C，SpO2 98%，BP 120/78。", DateTimeOffset.UtcNow.AddMinutes(-15)),
            ]
        };

        var id = Guid.NewGuid();
        lock (_lock) _snapshots[id] = items;
        return Task.FromResult<(Guid, IReadOnlyList<SnapshotItem>)>((id, items));
    }

    public Task<IReadOnlyList<SnapshotItem>> GetItemsAsync(Guid snapshotId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_snapshots.TryGetValue(snapshotId, out var v) ? v : []);
    }
}
