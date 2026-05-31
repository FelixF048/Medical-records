using ClinScribe.AiGateway;
using ClinScribe.AiGateway.Pipeline;
using ClinScribe.AiGateway.Providers;
using ClinScribe.Api.Services;
using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;
using ClinScribe.Infrastructure.Synthetic;
using Microsoft.Extensions.Options;

namespace ClinScribe.Tests;

/// <summary>第十九章：以模擬資料集驗證安全引擎偵測率/誤報/漏報。</summary>
public class EvaluationTests
{
    private static AiGatewayService Gateway()
    {
        var opt = Options.Create(new AiGatewayOptions());
        return new AiGatewayService(
            new RequestSanitizer(),
            new PromptInjectionDetector(),
            new SourceCitationEnforcer(),
            new SafetyGuardrail(),
            new SystemPromptRegistry(),
            new StubModelProvider(),
            opt);
    }

    private static AiGenerationRequest Req(SyntheticEncounter enc) =>
        new(enc.SuggestedNoteType, enc.EncounterId, Guid.NewGuid(), enc.Department,
            AutonomyLevel.L4, "tester", ["Physician"], enc.Items);

    [Fact]
    public void Dataset_IsDeterministicAndLarge()
    {
        var a = SyntheticDataGenerator.Generate(20);
        var b = SyntheticDataGenerator.Generate(20);
        Assert.Equal(260, a.Encounters.Count);                 // 13 類別 × 20
        Assert.Equal(a.Encounters.Count, b.Encounters.Count);
        Assert.Equal(
            a.Encounters.Select(e => e.EncounterId),
            b.Encounters.Select(e => e.EncounterId));          // 同 seed → 同序列
    }

    [Theory]
    [InlineData(ScenarioCategories.Allergy, SafetyRuleIds.Allergy, true)]
    [InlineData(ScenarioCategories.CriticalLab, SafetyRuleIds.CriticalLab, true)]
    [InlineData(ScenarioCategories.AbnormalVitals, SafetyRuleIds.AbnormalVitals, false)]
    [InlineData(ScenarioCategories.EmergencyRedFlag, SafetyRuleIds.EmergencyRedFlag, true)]
    [InlineData(ScenarioCategories.Pregnancy, SafetyRuleIds.SpecialPopulation, false)]
    [InlineData(ScenarioCategories.Polypharmacy, SafetyRuleIds.Polypharmacy, false)]
    [InlineData(ScenarioCategories.Contradiction, SafetyRuleIds.Contradiction, false)]
    public async Task EachScenario_FiresExpectedRule(string category, string ruleId, bool expectCritical)
    {
        var gw = Gateway();
        var data = SyntheticDataGenerator.Generate(5);
        foreach (var enc in data.Encounters.Where(e => e.ScenarioCategory == category))
        {
            var draft = await gw.GenerateDraftAsync(Req(enc));
            Assert.Contains(draft.SafetyFlags, f => f.RuleId == ruleId);
            Assert.Equal(expectCritical, draft.SafetyFlags.Any(f => f.Severity == FlagSeverity.Critical));
        }
    }

    [Fact]
    public async Task NormalScenario_HasNoSafetyFlags()
    {
        var gw = Gateway();
        var data = SyntheticDataGenerator.Generate(5);
        foreach (var enc in data.Encounters.Where(e => e.ScenarioCategory == ScenarioCategories.Normal))
        {
            var draft = await gw.GenerateDraftAsync(Req(enc));
            Assert.DoesNotContain(draft.SafetyFlags, f => f.Severity == FlagSeverity.Critical);
        }
    }

    [Fact]
    public async Task MissingData_ReportsMissingButNoCritical()
    {
        var gw = Gateway();
        var data = SyntheticDataGenerator.Generate(5);
        foreach (var enc in data.Encounters.Where(e => e.ScenarioCategory == ScenarioCategories.MissingData))
        {
            var draft = await gw.GenerateDraftAsync(Req(enc));
            Assert.NotEqual(NoteTypes.InsufficientData, draft.NoteType);  // 有主訴，非全空
            Assert.NotEmpty(draft.MissingInformation);
            Assert.DoesNotContain(draft.SafetyFlags, f => f.Severity == FlagSeverity.Critical);
        }
    }

    [Fact]
    public async Task Injection_IsAlwaysDetected()
    {
        var gw = Gateway();
        var data = SyntheticDataGenerator.Generate(5);
        foreach (var enc in data.Encounters.Where(e => e.ScenarioCategory == ScenarioCategories.Injection))
        {
            var draft = await gw.GenerateDraftAsync(Req(enc));
            Assert.True(draft.PromptInjectionDetected);
            Assert.Equal(NoteTypes.InjectionDetected, draft.NoteType);
        }
    }

    [Fact]
    public async Task EvaluationHarness_MeetsQualityThresholds()
    {
        var eval = new EvaluationService(Gateway(), SyntheticDataGenerator.Generate(20));
        var report = await eval.RunAsync();

        Assert.Equal(260, report.TotalEncounters);
        // 安全關鍵：危急旗標與注入偵測召回率必須 100%、零漏報。
        Assert.Equal(1.0, report.CriticalFlag.Recall);
        Assert.Equal(0.0, report.CriticalFlag.FalseNegativeRate);
        Assert.Equal(1.0, report.InjectionDetection.Recall);
        Assert.Equal(1.0, report.ExpectedRuleRecall);
        // 一般情境不得誤報 critical。
        Assert.Equal(0.0, report.CriticalFlag.FalsePositiveRate);
        // 整體分類正確率門檿。
        Assert.True(report.OverallAccuracy >= 0.99, $"accuracy={report.OverallAccuracy}");
    }
}
