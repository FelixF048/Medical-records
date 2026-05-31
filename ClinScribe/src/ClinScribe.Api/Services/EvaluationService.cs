using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;
using ClinScribe.Infrastructure.Synthetic;

namespace ClinScribe.Api.Services;

/// <summary>
/// 第十九章 AI 品質與評測 harness。
/// 以模擬資料集 ground truth 跑過 AI Gateway，計算偵測率/誤報/漏報與分類別品質。
/// </summary>
public sealed class EvaluationService
{
    private readonly IAiGateway _gateway;
    private readonly SyntheticDataset? _data;

    public EvaluationService(IAiGateway gateway, SyntheticDataset? data = null)
    {
        _gateway = gateway;
        _data = data;
    }

    public bool HasDataset => _data is not null;

    public async Task<EvaluationReport> RunAsync(int? limit = null, CancellationToken ct = default)
    {
        if (_data is null)
            return new EvaluationReport(0, new Metric(0, 0, 0, 0), new Metric(0, 0, 0, 0),
                new Metric(0, 0, 0, 0), 0, 0, new List<CategoryResult>());

        var encounters = _data.Encounters.AsEnumerable();
        if (limit is > 0) encounters = encounters.Take(limit.Value);
        var list = encounters.ToList();

        // 各維度混淆矩陣
        var critical = new Confusion();
        var injection = new Confusion();
        var insufficient = new Confusion();
        var ruleMatch = new Confusion();          // 預期 rule id 是否命中
        var byCat = new Dictionary<string, (int total, int correct)>();

        foreach (var enc in list)
        {
            ct.ThrowIfCancellationRequested();
            var snapId = Guid.NewGuid();
            var req = new AiGenerationRequest(
                enc.SuggestedNoteType, enc.EncounterId, snapId, enc.Department,
                AutonomyLevel.L4, "evaluator", ["AiAdmin"], enc.Items);

            var draft = await _gateway.GenerateDraftAsync(req, ct);

            var exp = enc.Expected;
            var hasCritical = draft.SafetyFlags.Any(f => f.Severity == FlagSeverity.Critical);
            var isInjection = draft.PromptInjectionDetected;
            var isInsufficient = draft.NoteType == NoteTypes.InsufficientData;

            // 注入情境本身即帶 Critical 旗標，排除於 critical 混淆矩陣外。
            if (!isInjection)
                critical.Add(exp.ExpectCriticalFlag, hasCritical);
            injection.Add(exp.ExpectInjection, isInjection);
            insufficient.Add(exp.ExpectInsufficientData, isInsufficient);

            // rule id 命中（每個預期 rule 計一次）
            var firedRules = draft.SafetyFlags.Select(f => f.RuleId).ToHashSet();
            var ruleCorrect = exp.ExpectedFlagRuleIds.Count == 0
                ? !hasCritical            // 預期無 → 不該有 critical
                : exp.ExpectedFlagRuleIds.All(r => firedRules.Contains(r));
            foreach (var r in exp.ExpectedFlagRuleIds)
                ruleMatch.Add(true, firedRules.Contains(r));

            // 注入情境本身即帶 Critical 旗標，故偵測正確時不再比對 critical 維度。
            var correctScenario = isInjection || isInsufficient
                ? (exp.ExpectInjection == isInjection &&
                   exp.ExpectInsufficientData == isInsufficient &&
                   ruleCorrect)
                : (exp.ExpectCriticalFlag == hasCritical &&
                   exp.ExpectInjection == isInjection &&
                   exp.ExpectInsufficientData == isInsufficient &&
                   ruleCorrect);

            var c = byCat.TryGetValue(enc.ScenarioCategory, out var v) ? v : (total: 0, correct: 0);
            byCat[enc.ScenarioCategory] = (c.total + 1, c.correct + (correctScenario ? 1 : 0));
        }

        var catResults = byCat
            .OrderBy(kv => kv.Key)
            .Select(kv => new CategoryResult(
                kv.Key, kv.Value.total, kv.Value.correct,
                kv.Value.total == 0 ? 0 : Math.Round((double)kv.Value.correct / kv.Value.total, 4)))
            .ToList();

        var overallCorrect = catResults.Sum(c => c.Correct);
        var accuracy = list.Count == 0 ? 0 : Math.Round((double)overallCorrect / list.Count, 4);

        return new EvaluationReport(
            list.Count,
            critical.ToMetric(),
            injection.ToMetric(),
            insufficient.ToMetric(),
            ruleMatch.Recall(),
            accuracy,
            catResults);
    }

    private sealed class Confusion
    {
        public int Tp, Fp, Tn, Fn;
        public void Add(bool expected, bool actual)
        {
            if (expected && actual) Tp++;
            else if (!expected && actual) Fp++;
            else if (!expected && !actual) Tn++;
            else Fn++;
        }
        public double Recall() => Tp + Fn == 0 ? 1 : Math.Round((double)Tp / (Tp + Fn), 4);
        public Metric ToMetric() => new(
            Tp + Fn == 0 ? 1 : Math.Round((double)Tp / (Tp + Fn), 4),                 // recall
            Tp + Fp == 0 ? 1 : Math.Round((double)Tp / (Tp + Fp), 4),                 // precision
            Fp + Tn == 0 ? 0 : Math.Round((double)Fp / (Fp + Tn), 4),                 // false positive rate
            Tp + Fn == 0 ? 0 : Math.Round((double)Fn / (Tp + Fn), 4));                // false negative rate
    }
}

/// <summary>偵測指標：召回率、精確率、誤報率、漏報率。</summary>
public sealed record Metric(double Recall, double Precision, double FalsePositiveRate, double FalseNegativeRate);

public sealed record CategoryResult(string Category, int Total, int Correct, double Accuracy);

public sealed record EvaluationReport(
    int TotalEncounters,
    Metric CriticalFlag,
    Metric InjectionDetection,
    Metric InsufficientData,
    double ExpectedRuleRecall,
    double OverallAccuracy,
    IReadOnlyList<CategoryResult> ByCategory);
