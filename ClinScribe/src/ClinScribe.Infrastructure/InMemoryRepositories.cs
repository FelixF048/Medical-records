using System.Collections.Concurrent;
using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;

namespace ClinScribe.Infrastructure.Repositories;

/// <summary>記憶體草稿儲存庫（骨架）。正式版本改為 EF Core + 版本化資料表。</summary>
public sealed class InMemoryDraftRepository : IDraftRepository
{
    private readonly ConcurrentDictionary<Guid, DraftRecord> _store = new();

    private sealed record DraftRecord(DraftNoteResponse Draft, DraftStatus Status);

    public Task<DraftNoteResponse> SaveAsync(DraftNoteResponse draft, CancellationToken ct = default)
    {
        _store[draft.DraftId] = new DraftRecord(draft, DraftStatus.Draft);
        return Task.FromResult(draft);
    }

    public Task<DraftNoteResponse?> GetAsync(Guid draftId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(draftId, out var r) ? r.Draft : null);

    public Task<DraftStatus?> GetStatusAsync(Guid draftId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(draftId, out var r) ? r.Status : (DraftStatus?)null);

    public Task<IReadOnlyList<DraftNoteResponse>> ListPendingAsync(CancellationToken ct = default)
    {
        var list = _store.Values
            .Where(r => r.Status is DraftStatus.Draft or DraftStatus.PendingReview or DraftStatus.PendingApproval)
            .Select(r => r.Draft)
            .ToList();
        return Task.FromResult<IReadOnlyList<DraftNoteResponse>>(list);
    }

    public Task<bool> SetStatusAsync(Guid draftId, DraftStatus status, string actor, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(draftId, out var r)) return Task.FromResult(false);
        // 紅線：不可由非簽章流程直接跳到 WrittenToEmr（由 Api 服務層把關核准+簽章）
        _store[draftId] = r with { Status = status };
        return Task.FromResult(true);
    }
}

/// <summary>事件通報（骨架）。</summary>
public sealed class InMemoryIncidentService : IIncidentService
{
    private readonly List<IncidentReport> _items = [];
    private readonly object _lock = new();
    private long _id = 1;

    public Task<IncidentReport> ReportAsync(string type, FlagSeverity severity, string detail, string reportedBy, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var r = new IncidentReport(_id++, type, severity, detail, "Open", reportedBy, DateTimeOffset.UtcNow);
            _items.Add(r);
            return Task.FromResult(r);
        }
    }

    public Task<IReadOnlyList<IncidentReport>> ListAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<IncidentReport>>(_items.OrderByDescending(i => i.Id).ToList());
    }
}

/// <summary>AI 停用開關（骨架）。支援全域與分 scope 停用。</summary>
public sealed class InMemoryAiKillSwitch : IAiKillSwitch
{
    private readonly ConcurrentDictionary<string, bool> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private const string Global = "*";

    public bool IsEnabled(string? scope = null)
    {
        if (_disabled.TryGetValue(Global, out var g) && g) return false;
        if (scope is not null && _disabled.TryGetValue(scope, out var s) && s) return false;
        return true;
    }

    public void Disable(string scope, string actor, string reason) => _disabled[scope] = true;
    public void Enable(string scope, string actor) => _disabled[scope] = false;
}
