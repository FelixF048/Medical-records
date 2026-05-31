using ClinScribe.Web.Components;
using ClinScribe.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Scoped（per-circuit）使用者情境；禁止 Singleton 保存使用者/病人狀態。
builder.Services.AddScoped<CurrentUser>();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5185";
builder.Services.AddHttpClient<ApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
