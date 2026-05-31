using System.Net.Http.Json;
using System.Text.Json;
using ClinScribe.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinScribe.AiGateway.Providers;

/// <summary>
/// 真實 Gemini 供應商（僅在 AiGateway:UseLiveProvider=true 時啟用）。
/// 金鑰只從環境變數讀取（預設 CLINSCRIBE_GEMINI_API_KEY），嚴禁寫入原始碼或組態檔。
/// 注意：正式環境應改由 Key Vault 取得，並加上 region lock 與 NoTrain 合約控管（第十七/十八章）。
/// </summary>
public sealed class GeminiModelProvider : IModelProvider
{
    private readonly HttpClient _http;
    private readonly AiGatewayOptions _opt;

    public GeminiModelProvider(HttpClient http, IOptions<AiGatewayOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public string ModelVersion => _opt.ModelVersion;

    public async Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(_opt.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"找不到環境變數 {_opt.ApiKeyEnvVar}。請於後端設定金鑰，切勿寫入原始碼。");

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = userContent } } } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _opt.GeminiEndpoint);
        req.Headers.Add("X-goog-api-key", apiKey);
        req.Content = JsonContent.Create(payload);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return doc.GetProperty("candidates")[0].GetProperty("content")
                  .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
    }
}
