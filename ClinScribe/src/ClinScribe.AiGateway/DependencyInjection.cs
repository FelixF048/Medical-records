using ClinScribe.AiGateway.Pipeline;
using ClinScribe.AiGateway.Providers;
using ClinScribe.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ClinScribe.AiGateway;

public static class DependencyInjection
{
    /// <summary>註冊 AI Gateway（第九章）。預設使用離線 Stub 供應商，可離線執行。</summary>
    public static IServiceCollection AddClinScribeAiGateway(this IServiceCollection services, bool useLiveProvider = false)
    {
        services.AddSingleton<RequestSanitizer>();
        services.AddSingleton<PromptInjectionDetector>();
        services.AddSingleton<SourceCitationEnforcer>();
        services.AddSingleton<SafetyGuardrail>();
        services.AddSingleton<SystemPromptRegistry>();

        if (useLiveProvider)
        {
            services.AddHttpClient<IModelProvider, GeminiModelProvider>();
        }
        else
        {
            services.AddSingleton<IModelProvider, StubModelProvider>();
        }

        services.AddSingleton<IAiGateway, AiGatewayService>();
        return services;
    }
}
