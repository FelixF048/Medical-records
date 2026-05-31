using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClinScribe.Tests;

/// <summary>端對端 API 測試：驗證 RBAC/ABAC 授權與核准→簽章→EMR 紅線。</summary>
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static HttpRequestMessage Msg(HttpMethod m, string url, string user = "dr.wang",
        string roles = "Physician", object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Add("X-User", user);
        r.Headers.Add("X-Roles", roles);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private static object NoteBody(string encounterId, string noteType) => new
    {
        encounterId,
        patientContextSnapshotId = Guid.Empty,
        noteType,
        department = (string?)null,
        maxAutonomy = 4
    };

    private static async Task<JsonElement> Json(HttpResponseMessage resp)
        => JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

    // ---- 授權 ----

    [Fact]
    public async Task Me_WithoutAuth_Returns401()
    {
        var resp = await Client().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Me_WithAuth_Returns200()
    {
        var resp = await Client().SendAsync(Msg(HttpMethod.Get, "/api/me"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Audit_AsPhysician_Forbidden_AsCompliance_Allowed()
    {
        var forbidden = await Client().SendAsync(Msg(HttpMethod.Get, "/api/audit"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var ok = await Client().SendAsync(Msg(HttpMethod.Get, "/api/audit", "auditor", "Compliance"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Json(ok);
        Assert.True(body.GetProperty("chainValid").GetBoolean());
    }

    [Fact]
    public async Task Prescription_AsNurse_Forbidden()
    {
        var resp = await Client().SendAsync(Msg(HttpMethod.Post, "/api/ai/draft-prescription",
            "nurse.lin", "Nurse", NoteBody("ENC-6006", "DraftPrescription")));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ---- 紅線：過敏 Critical 阻擋核准 ----

    [Fact]
    public async Task Enc6006_Prescription_CriticalFlag_BlocksApproval()
    {
        var c = Client();
        var gen = await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/draft-prescription",
            body: NoteBody("ENC-6006", "DraftPrescription")));
        Assert.Equal(HttpStatusCode.OK, gen.StatusCode);
        var draft = await Json(gen);
        var draftId = draft.GetProperty("draftId").GetGuid();
        Assert.True(draft.GetProperty("safetyFlags").GetArrayLength() > 0);

        var approve = await c.SendAsync(Msg(HttpMethod.Post, $"/api/approvals/{draftId}/approve"));
        Assert.Equal((HttpStatusCode)422, approve.StatusCode);
    }

    // ---- 紅線：核准 → 簽章 → 寫 EMR 順序 ----

    [Fact]
    public async Task Soap_EnforcesApproveThenSignThenEmrOrder()
    {
        var c = Client();
        var gen = await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/notes",
            body: NoteBody("ENC-6001", "SOAP")));
        var id = (await Json(gen)).GetProperty("draftId").GetGuid();

        // 未簽章寫 EMR → 422
        var emrEarly = await c.SendAsync(Msg(HttpMethod.Post, $"/api/emr/final/{id}"));
        Assert.Equal((HttpStatusCode)422, emrEarly.StatusCode);

        // 未核准簽章 → 422
        var signEarly = await c.SendAsync(Msg(HttpMethod.Post, $"/api/signatures/{id}"));
        Assert.Equal((HttpStatusCode)422, signEarly.StatusCode);

        // 依序
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Msg(HttpMethod.Post, $"/api/approvals/{id}/approve"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Msg(HttpMethod.Post, $"/api/signatures/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Msg(HttpMethod.Post, $"/api/emr/final/{id}"))).StatusCode);
    }

    // ---- 退回草稿 ----

    [Fact]
    public async Task RejectDraft_MovesToRejected_And_CannotApproveAfter()
    {
        var c = Client();
        var gen = await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/notes", body: NoteBody("ENC-6001", "SOAP")));
        var id = (await Json(gen)).GetProperty("draftId").GetGuid();

        var reject = await c.SendAsync(Msg(HttpMethod.Post, $"/api/drafts/{id}/reject", body: new { reason = "資料不足" }));
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        // 退回後不可再核准
        var approve = await c.SendAsync(Msg(HttpMethod.Post, $"/api/approvals/{id}/approve"));
        Assert.Equal((HttpStatusCode)422, approve.StatusCode);
    }

    // ---- 安全檢核端點 ----

    [Fact]
    public async Task SafetyCheck_Enc6006_ReturnsCriticalFlag()
    {
        var resp = await Client().SendAsync(Msg(HttpMethod.Post, "/api/safety/check",
            body: NoteBody("ENC-6006", "DraftPrescription")));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await Json(resp);
        Assert.True(body.GetProperty("safetyFlags").GetArrayLength() > 0);
    }

    // ---- 模型版本揭露 ----

    [Fact]
    public async Task ModelVersion_IsDisclosed()
    {
        var resp = await Client().SendAsync(Msg(HttpMethod.Get, "/api/models/version"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await Json(resp);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("modelVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("promptVersion").GetString()));
    }

    // ---- Kill-switch ----

    [Fact]
    public async Task KillSwitch_DisablesGeneration_ThenReenable()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.OK,
            (await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/disable?scope=*", "ai.admin", "AiAdmin"))).StatusCode);

        var blocked = await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/notes", body: NoteBody("ENC-6001", "SOAP")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/enable?scope=*", "ai.admin", "AiAdmin"))).StatusCode);

        var ok = await c.SendAsync(Msg(HttpMethod.Post, "/api/ai/notes", body: NoteBody("ENC-6001", "SOAP")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ---- 第十三章：病人/就醫目錄（模擬資料集） ----

    [Fact]
    public async Task Patients_List_RequiresReadPatient_AndReturnsData()
    {
        var forbidden = await Client().SendAsync(Msg(HttpMethod.Get, "/api/patients", "admin", "Admin"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var ok = await Client().SendAsync(Msg(HttpMethod.Get, "/api/patients?take=10"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Json(ok);
        Assert.True(body.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Encounters_List_FiltersBySetting()
    {
        var ok = await Client().SendAsync(Msg(HttpMethod.Get, "/api/encounters?setting=ER&take=200"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Json(ok);
        Assert.True(body.GetProperty("total").GetInt32() >= 260);
        foreach (var it in body.GetProperty("items").EnumerateArray())
            Assert.Equal("ER", it.GetProperty("status").GetString());
    }

    // ---- 第十九章：AI 品質評測端點 ----

    [Fact]
    public async Task EvalQuality_AsCompliance_MeetsThresholds()
    {
        var forbidden = await Client().SendAsync(Msg(HttpMethod.Get, "/api/eval/quality"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);   // Physician 無 ViewAudit

        var ok = await Client().SendAsync(Msg(HttpMethod.Get, "/api/eval/quality", "auditor", "Compliance"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Json(ok);
        Assert.Equal(1.0, body.GetProperty("injectionDetection").GetProperty("recall").GetDouble());
        Assert.Equal(1.0, body.GetProperty("criticalFlag").GetProperty("recall").GetDouble());
        Assert.True(body.GetProperty("overallAccuracy").GetDouble() >= 0.99);
    }
}
