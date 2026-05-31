namespace ClinScribe.AiGateway;

/// <summary>AI Gateway 組態（第九章版本鎖定）。金鑰一律由環境變數/User Secrets 提供，嚴禁寫入原始碼。</summary>
public sealed class AiGatewayOptions
{
    public const string SectionName = "AiGateway";

    /// <summary>啟用真實模型供應商；預設 false 使用 Stub，可離線執行與測試。</summary>
    public bool UseLiveProvider { get; set; }

    /// <summary>鎖定之模型版本（VersionLock）。</summary>
    public string ModelVersion { get; set; } = "stub-deterministic-v1";

    /// <summary>System Prompt 版本（Registry）。</summary>
    public string SystemPromptVersion { get; set; } = "agent-runtime-v1";

    /// <summary>Prompt 範本版本。</summary>
    public string PromptVersion { get; set; } = "base-v1";

    /// <summary>知識庫版本。</summary>
    public string KbVersion { get; set; } = "kb-v1";

    // ----- 僅在 UseLiveProvider=true 時使用 -----
    public string GeminiEndpoint { get; set; } =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent";

    /// <summary>環境變數名稱（值不存在組態檔，由 OS/KeyVault 提供）。</summary>
    public string ApiKeyEnvVar { get; set; } = "CLINSCRIBE_GEMINI_API_KEY";
}
