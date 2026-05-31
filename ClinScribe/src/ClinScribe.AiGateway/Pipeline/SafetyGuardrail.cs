using System.Text.RegularExpressions;
using ClinScribe.Domain;
using ClinScribe.Domain.Abstractions;

namespace ClinScribe.AiGateway.Pipeline;

/// <summary>
/// 第十五章臨床安全 Guardrails（資料驅動）。
/// 依快照項目的結構化 Tags 進行：過敏交叉比對、檢驗危急值、異常生命徵象、
/// 急症紅旗、特殊族群、多重用藥、矛盾偵測；另含禁語偵測與改寫。
/// </summary>
public sealed class SafetyGuardrail
{
    private static readonly Regex BannedClaims = new(
        @"(確定是|確診為|已開立|已下醫囑|已簽章|已寫入病歷)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<SafetyFlag> Evaluate(
        string noteType,
        IReadOnlyList<DraftSection> sections,
        IReadOnlyList<SnapshotItem> snapshot)
    {
        var flags = new List<SafetyFlag>();

        EvaluateAllergy(snapshot, flags);
        EvaluateCriticalLab(snapshot, flags);
        EvaluateVitals(snapshot, flags);
        EvaluateSpecialPopulation(snapshot, flags);
        EvaluatePolypharmacy(snapshot, flags);
        EvaluateContradictions(snapshot, flags);
        EvaluateBannedLanguage(sections, flags);

        // 去重（同 RuleId 取最嚴重）
        return flags
            .GroupBy(f => f.RuleId)
            .Select(g => g.OrderByDescending(f => f.Severity).First())
            .ToList();
    }

    // ---- R-ALG-001 過敏 × 處方（含交叉反應） ----
    private static void EvaluateAllergy(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        var allergens = snap
            .Where(s => Tag(s, ClinicalTags.Allergen) is not null)
            .Select(s => Tag(s, ClinicalTags.Allergen)!)
            .ToList();

        var meds = snap.Where(s => Tag(s, ClinicalTags.Drug) is not null).ToList();

        if (allergens.Count > 0 && meds.Count > 0)
        {
            foreach (var med in meds)
            {
                var drugClass = Tag(med, ClinicalTags.DrugClass) ?? Tag(med, ClinicalTags.Drug)!;
                foreach (var allergen in allergens)
                {
                    if (CrossReactivity.Conflicts(allergen, drugClass) ||
                        string.Equals(allergen, drugClass, StringComparison.OrdinalIgnoreCase))
                    {
                        flags.Add(new SafetyFlag(
                            SafetyRuleIds.Allergy,
                            $"病人對 {allergen} 過敏，與處方藥（{Tag(med, ClinicalTags.Drug)}／類別 {drugClass}）有過敏或交叉過敏風險。",
                            FlagSeverity.Critical, true,
                            "請改用替代藥並由藥師覆核，禁止自動定稿。"));
                        return;
                    }
                }
            }
        }

        // 後備：無結構化 Tags 時，以內容關鍵字偵測（向後相容）。
        EvaluateAllergyByText(snap, flags);
    }

    // 已知過敏原關鍵字 → drugClass
    private static readonly (string keyword, string drugClass)[] AllergenKeywords =
    {
        ("penicillin", "penicillin"), ("盤尼西林", "penicillin"), ("青黴素", "penicillin"),
        ("cephalosporin", "cephalosporin"), ("頭孢", "cephalosporin"),
        ("sulfa", "sulfa"), ("磺胺", "sulfa"),
        ("aspirin", "aspirin"), ("阿斯匹靈", "aspirin"),
        ("nsaid", "nsaid"),
    };

    // 已知藥物關鍵字 → drugClass
    private static readonly (string keyword, string drugClass)[] DrugKeywords =
    {
        ("amoxicillin", "penicillin"), ("ampicillin", "penicillin"), ("penicillin", "penicillin"),
        ("cephalexin", "cephalosporin"), ("ceftriaxone", "cephalosporin"), ("cefazolin", "cephalosporin"),
        ("sulfamethoxazole", "sulfa"), ("cotrimoxazole", "sulfa"),
        ("ibuprofen", "nsaid"), ("naproxen", "nsaid"), ("ketorolac", "nsaid"), ("aspirin", "aspirin"),
    };

    private static void EvaluateAllergyByText(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        if (flags.Any(f => f.RuleId == SafetyRuleIds.Allergy)) return;

        var allergyText = string.Join(" ",
            snap.Where(s => s.SourceType.Contains("Allergy", StringComparison.OrdinalIgnoreCase)
                            || s.Content.Contains("過敏"))
                .Select(s => s.Content)).ToLowerInvariant();
        var medText = string.Join(" ",
            snap.Where(s => s.SourceType.Contains("Medication", StringComparison.OrdinalIgnoreCase)
                            || s.Content.Contains("處方") || s.Content.Contains("醫囑"))
                .Select(s => s.Content)).ToLowerInvariant();
        if (allergyText.Length == 0 || medText.Length == 0) return;

        var allergenClasses = AllergenKeywords
            .Where(a => allergyText.Contains(a.keyword)).Select(a => a.drugClass).ToHashSet();
        var medClasses = DrugKeywords
            .Where(d => medText.Contains(d.keyword)).Select(d => d.drugClass).ToList();
        if (allergenClasses.Count == 0 || medClasses.Count == 0) return;

        foreach (var mc in medClasses)
        {
            var conflict = allergenClasses.Contains(mc) ||
                           allergenClasses.Any(ac => CrossReactivity.Conflicts(ac, mc));
            if (conflict)
            {
                flags.Add(new SafetyFlag(
                    SafetyRuleIds.Allergy,
                    "病人過敏史與處方藥有過敏或交叉過敏風險。",
                    FlagSeverity.Critical, true,
                    "請改用替代藥並由藥師覆核，禁止自動定稿。"));
                return;
            }
        }
    }

    // ---- R-LAB-008 檢驗危急值 ----
    private static void EvaluateCriticalLab(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        var crit = snap.Where(s => Cat(s) == "lab" && Tag(s, ClinicalTags.Critical) == "true").ToList();
        if (crit.Count == 0) return;
        var codes = string.Join("、", crit.Select(s => Tag(s, ClinicalTags.Code) ?? "?"));
        flags.Add(new SafetyFlag(
            SafetyRuleIds.CriticalLab,
            $"檢驗危急值（{codes}）需依院內危急值流程立即通知負責醫師。",
            FlagSeverity.Critical, true,
            "立即通知醫事人員並記錄回覆。"));
    }

    // ---- R-VS-005 異常生命徵象 / R-ER-006 急症紅旗 ----
    private static void EvaluateVitals(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        var vitals = snap.Where(s => Cat(s) == "vital").ToList();
        if (vitals.Count == 0) return;

        var criticalVital = vitals.Any(s => Tag(s, ClinicalTags.Critical) == "true");
        var abnormalVital = vitals.Any(s => Tag(s, ClinicalTags.Abnormal) == "true");

        if (criticalVital)
        {
            flags.Add(new SafetyFlag(
                SafetyRuleIds.EmergencyRedFlag,
                "偵測到危急生命徵象（如嚴重低血氧／休克），屬急症紅旗。",
                FlagSeverity.Critical, true,
                "停止一般流程，立即啟動人工急救處置。"));
        }

        if (abnormalVital)
        {
            flags.Add(new SafetyFlag(
                SafetyRuleIds.AbnormalVitals,
                "偵測到異常生命徵象，需醫事人員確認與處置。",
                FlagSeverity.High, true,
                "通知負責醫師評估。"));
        }
    }

    // ---- R-POP-009 特殊族群 ----
    private static void EvaluateSpecialPopulation(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        var pops = snap.Select(s => Tag(s, ClinicalTags.Population))
                       .Where(p => p is not null).Select(p => p!).Distinct().ToList();
        if (pops.Count == 0) return;

        var label = string.Join("、", pops.Select(p => p switch
        {
            "pregnancy" => "懷孕",
            "pediatric" => "兒童",
            "geriatric" => "高齡",
            "renal" => "腎功能不全",
            "hepatic" => "肝功能不全",
            _ => p
        }));
        flags.Add(new SafetyFlag(
            SafetyRuleIds.SpecialPopulation,
            $"病人屬特殊族群（{label}），用藥與劑量須提高風險等級並由醫師評估。",
            FlagSeverity.High, true,
            "套用該族群之劑量與禁忌檢核。"));
    }

    // ---- R-POLY-007 多重用藥 ----
    private static void EvaluatePolypharmacy(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        var medCount = snap.Count(s => Cat(s) == "med" || Tag(s, ClinicalTags.Drug) is not null);
        if (medCount >= 5)
        {
            flags.Add(new SafetyFlag(
                SafetyRuleIds.Polypharmacy,
                $"病人同時使用 {medCount} 種藥物（多重用藥），交互作用風險升高。",
                FlagSeverity.High, true,
                "請藥師進行用藥整合與交互作用評估。"));
        }
    }

    // ---- R-CON-010 資料矛盾 ----
    private static void EvaluateContradictions(IReadOnlyList<SnapshotItem> snap, List<SafetyFlag> flags)
    {
        foreach (var c in ContradictionList(snap))
        {
            flags.Add(new SafetyFlag(
                SafetyRuleIds.Contradiction,
                $"資料來源不一致（{c}）；已列出衝突，須由醫事人員釐清，不得自行選邊。",
                FlagSeverity.High, true,
                "請醫事人員確認正確資料來源。"));
        }
    }

    // ---- R-LANG-011 禁語 ----
    private static void EvaluateBannedLanguage(IReadOnlyList<DraftSection> sections, List<SafetyFlag> flags)
    {
        foreach (var sec in sections)
        {
            if (BannedClaims.IsMatch(sec.Content))
            {
                flags.Add(new SafetyFlag(
                    SafetyRuleIds.BannedLanguage,
                    $"段落「{sec.Key}」出現禁止語氣（確定診斷/已執行臨床行動），須改為待核准建議。",
                    FlagSeverity.High, true,
                    "改寫為待醫師覆核之建議。"));
            }
        }
    }

    /// <summary>缺漏資料偵測（SOAP 應有客觀生命徵象/檢驗）。</summary>
    public IReadOnlyList<string> DetectMissing(string noteType, IReadOnlyList<SnapshotItem> snap)
    {
        var missing = new List<string>();
        if (noteType == NoteTypes.Soap)
        {
            var hasObjective = snap.Any(s => Cat(s) is "vital" or "lab" || s.SourceType == "Observation");
            if (!hasObjective) missing.Add("缺少客觀資料（生命徵象／檢驗）");
            var hasHpi = snap.Any(s => s.SourceType == "Encounter.note");
            if (!hasHpi) missing.Add("缺少主訴/病史");
        }
        return missing;
    }

    /// <summary>矛盾清單（供 gateway 填入 Contradictions 欄位）。</summary>
    public IReadOnlyList<string> DetectContradictions(IReadOnlyList<SnapshotItem> snap)
        => ContradictionList(snap);

    private static IReadOnlyList<string> ContradictionList(IReadOnlyList<SnapshotItem> snap)
    {
        var result = new List<string>();
        var groups = snap
            .Where(s => Tag(s, ClinicalTags.ConflictKey) is not null)
            .GroupBy(s => Tag(s, ClinicalTags.ConflictKey)!);
        foreach (var g in groups)
        {
            var values = g.Select(s => Tag(s, ClinicalTags.Value)).Where(v => v is not null).Distinct().ToList();
            if (values.Count > 1)
                result.Add($"{g.Key}: {string.Join(" vs ", values)}");
        }
        return result;
    }

    /// <summary>禁語改寫：把確定性語氣降級為待覆核。</summary>
    public string Soften(string content) =>
        BannedClaims.Replace(content, m => m.Value switch
        {
            "確定是" or "確診為" => "疑似（待醫師確認）",
            "已開立" => "建議開立（待核准）",
            "已下醫囑" => "建議醫囑（待核准）",
            "已簽章" => "待簽章",
            "已寫入病歷" => "待寫入病歷草稿區",
            _ => m.Value
        });

    private static string? Tag(SnapshotItem s, string key)
        => s.Tags is not null && s.Tags.TryGetValue(key, out var v) ? v : null;

    private static string? Cat(SnapshotItem s) => Tag(s, ClinicalTags.Category);
}
