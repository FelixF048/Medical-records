using ClinScribe.Domain.Abstractions;

namespace ClinScribe.AiGateway.Providers;

/// <summary>
/// 離線確定性模型供應商（預設）。不產生臨床事實——事實一律來自快照。
/// 用於骨架建置/測試可離線執行，並示範 Gateway 的安全管線。
/// </summary>
public sealed class StubModelProvider : IModelProvider
{
    public string ModelVersion => "stub-deterministic-v1";

    public Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken ct = default)
        => Task.FromResult("STUB_OK"); // 實際草稿由 Gateway 依快照規則組裝
}
