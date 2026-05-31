using System.Text.RegularExpressions;

namespace ClinScribe.AiGateway.Pipeline;

/// <summary>
/// 第九/十五章 R-INJ-019 Prompt Injection 偵測器。
/// 資料區內的「指令性文字」皆視為攻擊，命中即阻斷流程。
/// </summary>
public sealed class PromptInjectionDetector
{
    private static readonly string[] Patterns =
    [
        @"ignore\s+(all\s+)?(previous|above|system)",
        @"忽略(上述|前面|系統|以上)",
        @"disregard\s+(the\s+)?(rules|instructions|system)",
        @"reveal\s+(the\s+)?(system\s+)?prompt",
        @"洩漏.*(系統|提示|prompt)",
        @"delete\s+.*audit",
        @"刪除.*(稽核|紀錄|日志|日誌)",
        @"bypass\s+(approval|review|signature|permission)",
        @"繞過(審核|核准|簽章|權限)",
        @"you\s+are\s+now\s+(a\s+)?doctor",
        @"pretend\s+to\s+be",
        @"act\s+as\s+system",
        @"sign\s+(the\s+)?(record|note)\s+(for|as)",
        @"write\s+directly\s+to\s+(the\s+)?emr",
        @"直接寫入(病歷|emr)"
    ];

    private readonly Regex _regex = new(
        string.Join("|", Patterns.Select(p => $"(?:{p})")),
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool TryDetect(string content, out string matched)
    {
        matched = "";
        if (string.IsNullOrEmpty(content)) return false;
        var m = _regex.Match(content);
        if (m.Success) { matched = m.Value; return true; }
        return false;
    }
}
