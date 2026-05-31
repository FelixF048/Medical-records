using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;

namespace ClinScribe.AiGateway.Pipeline;

/// <summary>
/// 第九/十五章 R-SRC-002 來源引用強制器。
/// 任一 sourceRefId 必須存在於快照集合；不存在者剔除並改列 uncertainty。
/// </summary>
public sealed class SourceCitationEnforcer
{
    public (List<DraftSection> sections, List<string> addedUncertainties) Enforce(
        IReadOnlyList<DraftSection> sections,
        IReadOnlyList<SnapshotItem> snapshot)
    {
        var validIds = snapshot.Select(s => s.SourceRefId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<DraftSection>();
        var uncertainties = new List<string>();

        foreach (var sec in sections)
        {
            var kept = sec.SourceRefIds.Where(id => validIds.Contains(id)).ToList();
            var dropped = sec.SourceRefIds.Where(id => !validIds.Contains(id)).ToList();
            if (dropped.Count > 0)
                uncertainties.Add($"段落「{sec.Key}」引用了不存在的來源並已移除：{string.Join(",", dropped)}");
            result.Add(sec with { SourceRefIds = kept });
        }
        return (result, uncertainties);
    }
}
