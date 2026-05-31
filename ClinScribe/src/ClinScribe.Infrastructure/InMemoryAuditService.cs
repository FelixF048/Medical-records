using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;

namespace ClinScribe.Infrastructure.Audit;

/// <summary>
/// append-only 稽核服務（規格書第十二/十七章）。
/// 以雜湊鏈確保不可竄改：每筆 RowHash = SHA256(PrevHash || canonicalPayload)。
/// 骨架版本使用記憶體儲存；正式版本請改為 WORM/append-only 資料表並禁止 UPDATE/DELETE。
/// </summary>
public sealed class InMemoryAuditService : IAuditService
{
    private readonly List<AuditLogEntry> _entries = [];
    private readonly object _lock = new();
    private long _nextId = 1;
    private const string Genesis = "GENESIS";

    public Task<AuditLogEntry> AppendAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var prevHash = _entries.Count == 0 ? Genesis : _entries[^1].RowHash;
            var stamped = entry with
            {
                Id = _nextId++,
                At = entry.At == default ? DateTimeOffset.UtcNow : entry.At,
                PrevHash = prevHash
            };
            var rowHash = ComputeHash(stamped);
            stamped = stamped with { RowHash = rowHash };
            _entries.Add(stamped);
            return Task.FromResult(stamped);
        }
    }

    public Task<IReadOnlyList<AuditLogEntry>> QueryAsync(string? patientId = null, string? actor = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<AuditLogEntry> q = _entries;
            if (!string.IsNullOrWhiteSpace(patientId))
                q = q.Where(e => e.PatientId == patientId);
            if (!string.IsNullOrWhiteSpace(actor))
                q = q.Where(e => e.Actor == actor);
            return Task.FromResult<IReadOnlyList<AuditLogEntry>>(q.OrderByDescending(e => e.Id).ToList());
        }
    }

    public Task<bool> VerifyChainAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var prev = Genesis;
            foreach (var e in _entries)
            {
                if (e.PrevHash != prev) return Task.FromResult(false);
                var recomputed = ComputeHash(e with { RowHash = "" });
                if (recomputed != e.RowHash) return Task.FromResult(false);
                prev = e.RowHash;
            }
            return Task.FromResult(true);
        }
    }

    private static string ComputeHash(AuditLogEntry e)
    {
        // 以不含 RowHash 的標準化 JSON 作為 payload
        var payload = JsonSerializer.Serialize(e with { RowHash = "" });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(e.PrevHash + "|" + payload));
        return Convert.ToHexString(bytes);
    }
}
