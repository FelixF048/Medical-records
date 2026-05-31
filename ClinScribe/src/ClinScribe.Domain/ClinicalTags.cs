namespace ClinScribe.Domain;

/// <summary>SnapshotItem.Tags 的標準鍵（資料驅動安全引擎用）。</summary>
public static class ClinicalTags
{
    public const string Category = "category";        // vital | lab | allergy | med | condition | patient
    public const string Code = "code";                // K, Na, SpO2, SBP, HR, Temp, Troponin...
    public const string Value = "value";              // 數值字串
    public const string Unit = "unit";
    public const string Abnormal = "abnormal";        // true/false
    public const string Critical = "critical";        // true/false（危急值）
    public const string Allergen = "allergen";        // penicillin, sulfa, nsaid, cephalosporin...
    public const string Drug = "drug";                // amoxicillin...
    public const string DrugClass = "drugClass";      // penicillin, sulfa, nsaid, cephalosporin
    public const string Population = "population";     // pregnancy | pediatric | geriatric | renal | hepatic
    public const string ConflictKey = "conflictKey";  // 用於矛盾偵測之分組鍵
}

/// <summary>第十五章安全規則 ID（集中管理）。</summary>
public static class SafetyRuleIds
{
    public const string Allergy = "R-ALG-001";            // 過敏 × 處方（含交叉反應）
    public const string AbnormalVitals = "R-VS-005";      // 異常生命徵象 → 通知
    public const string EmergencyRedFlag = "R-ER-006";    // 急症紅旗 → 停止一般流程
    public const string Polypharmacy = "R-POLY-007";      // 多重用藥 → 藥師覆核
    public const string CriticalLab = "R-LAB-008";        // 檢驗危急值 → 通知
    public const string SpecialPopulation = "R-POP-009";  // 特殊族群 → 提高風險
    public const string Contradiction = "R-CON-010";      // 資料矛盾 → 列出不選邊
    public const string BannedLanguage = "R-LANG-011";    // 禁語
}

/// <summary>第十五章情境分類（模擬資料 ground truth）。</summary>
public static class ScenarioCategories
{
    public const string Normal = "Normal";
    public const string Allergy = "Allergy";
    public const string CriticalLab = "CriticalLab";
    public const string AbnormalVitals = "AbnormalVitals";
    public const string EmergencyRedFlag = "EmergencyRedFlag";
    public const string Pregnancy = "Pregnancy";
    public const string Pediatric = "Pediatric";
    public const string Geriatric = "Geriatric";
    public const string RenalImpairment = "RenalImpairment";
    public const string Polypharmacy = "Polypharmacy";
    public const string Contradiction = "Contradiction";
    public const string MissingData = "MissingData";
    public const string Injection = "Injection";
}

/// <summary>過敏交叉反應對應表（藥物類別 → 受影響成分），供 Guardrail 比對。</summary>
public static class CrossReactivity
{
    /// <summary>allergen（過敏原類別） → 視為同類而禁用的 drugClass 集合。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Map =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["penicillin"] = ["penicillin", "cephalosporin"], // 盤尼西林與頭孢菌素部分交叉
            ["cephalosporin"] = ["cephalosporin", "penicillin"],
            ["sulfa"] = ["sulfa"],
            ["nsaid"] = ["nsaid"],
            ["aspirin"] = ["nsaid", "aspirin"],
        };

    /// <summary>判定某過敏原是否與某藥物類別衝突。</summary>
    public static bool Conflicts(string allergen, string drugClass)
        => Map.TryGetValue(allergen, out var classes) &&
           classes.Contains(drugClass, StringComparer.OrdinalIgnoreCase);
}
