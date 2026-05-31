using ClinScribe.Api.Auth;
using ClinScribe.Api.Endpoints;
using ClinScribe.Api.Services;
using ClinScribe.AiGateway;
using ClinScribe.Domain;
using ClinScribe.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// ----- 身分驗證（骨架：Header；正式請換 SSO+MFA）-----
builder.Services.AddAuthentication(DevHeaderAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthHandler>(DevHeaderAuthHandler.SchemeName, _ => { });

// ----- 授權（RBAC + ABAC）第二章 -----
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.ReadPatient, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Physician, ClinicalRoles.Nurse, ClinicalRoles.Pharmacist,
            ClinicalRoles.CaseManager, ClinicalRoles.LabTech, ClinicalRoles.Radiographer)
        && AbacAllows(ctx, abac => abac.IsCareTeamMember || abac.IsEmergency)));

    options.AddPolicy(AuthPolicies.GenerateNote, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Physician, ClinicalRoles.Nurse)
        && AbacAllows(ctx, abac => abac.IsCareTeamMember && abac.HasPatientConsent)));

    options.AddPolicy(AuthPolicies.GeneratePrescription, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Physician)
        && AbacAllows(ctx, abac => abac.IsAttendingForEncounter && abac.IsOnShift && abac.HasPatientConsent)));

    options.AddPolicy(AuthPolicies.ApproveClinical, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Physician, ClinicalRoles.Pharmacist, ClinicalRoles.Nurse)));

    options.AddPolicy(AuthPolicies.SignRecord, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Physician, ClinicalRoles.Nurse, ClinicalRoles.Pharmacist)));

    options.AddPolicy(AuthPolicies.WriteFinalEmr, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Physician, ClinicalRoles.Nurse)));

    options.AddPolicy(AuthPolicies.ViewAudit, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.Security, ClinicalRoles.Compliance, ClinicalRoles.AiAdmin, ClinicalRoles.SysAdmin)));

    options.AddPolicy(AuthPolicies.ManageAi, p => p.RequireAssertion(ctx =>
        HasAnyRole(ctx, ClinicalRoles.AiAdmin, ClinicalRoles.Security)));
});

// ----- 應用服務 -----
var useLive = builder.Configuration.GetValue<bool>("AiGateway:UseLiveProvider");
builder.Services.Configure<AiGatewayOptions>(builder.Configuration.GetSection(AiGatewayOptions.SectionName));
builder.Services.AddClinScribeInfrastructure();
builder.Services.AddClinScribeAiGateway(useLive);
builder.Services.AddScoped<ClinicalDraftService>();
builder.Services.AddScoped<EvaluationService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("https://localhost:7150", "http://localhost:5062")
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "ClinScribe.Api", status = "ok" }));
app.MapClinScribeEndpoints();

app.Run();

static bool HasAnyRole(AuthorizationHandlerContext ctx, params string[] roles)
    => roles.Any(r => ctx.User.IsInRole(r));

static bool AbacAllows(AuthorizationHandlerContext ctx, Func<AbacContext, bool> predicate)
{
    if (ctx.Resource is HttpContext http)
        return predicate(AbacContext.FromHeaders(http.Request.Headers));
    return false;
}

public partial class Program { }
