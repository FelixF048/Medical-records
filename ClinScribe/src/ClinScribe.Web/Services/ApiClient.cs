using System.Net.Http.Json;
using ClinScribe.Domain;

namespace ClinScribe.Web.Services;

/// <summary>
/// 後端 API 用戶端（Blazor Server 端呼叫；前端永不直呼 LLM，第五章）。
/// 注入目前使用者情境作為 Dev 驗證/ABAC 標頭。
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly CurrentUser _user;

    public ApiClient(HttpClient http, CurrentUser user)
    {
        _http = http;
        _user = user;
    }

    private HttpRequestMessage Build(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-User", _user.UserId);
        req.Headers.Add("X-Roles", string.Join(",", _user.Roles));
        req.Headers.Add("X-Abac-CareTeam", _user.IsCareTeamMember.ToString());
        req.Headers.Add("X-Abac-Attending", _user.IsAttending.ToString());
        req.Headers.Add("X-Abac-OnShift", _user.IsOnShift.ToString());
        req.Headers.Add("X-Abac-Consent", _user.HasConsent.ToString());
        req.Headers.Add("X-Abac-Emergency", _user.IsEmergency.ToString());
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    public async Task<DraftNoteResponse?> GenerateNoteAsync(GenerateNoteRequest request)
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Post, "/api/ai/notes", request));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<DraftNoteResponse>();
    }

    public async Task<DraftNoteResponse?> GeneratePrescriptionAsync(GenerateNoteRequest request)
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Post, "/api/ai/draft-prescription", request));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<DraftNoteResponse>();
    }

    public async Task<(bool ok, int status)> ApproveAsync(Guid draftId)
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Post, $"/api/approvals/{draftId}/approve"));
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode);
    }

    public async Task<bool> RejectAsync(Guid draftId, string? reason)
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Post, $"/api/drafts/{draftId}/reject", new { reason }));
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> SignAsync(Guid draftId)
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Post, $"/api/signatures/{draftId}"));
        return resp.IsSuccessStatusCode;
    }

    public async Task<(bool ok, int status)> WriteFinalAsync(Guid draftId)
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Post, $"/api/emr/final/{draftId}"));
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode);
    }

    public async Task<AuditResult?> GetAuditAsync()
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Get, "/api/audit"));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AuditResult>();
    }

    public async Task<List<WorkItem>?> GetWorklistAsync()
    {
        using var resp = await _http.SendAsync(Build(HttpMethod.Get, "/api/worklist"));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<List<WorkItem>>();
    }

    public sealed record AuditResult(bool ChainValid, int Count, List<AuditLogEntry> Items);
}
