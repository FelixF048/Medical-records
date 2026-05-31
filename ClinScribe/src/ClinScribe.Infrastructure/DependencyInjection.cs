using ClinScribe.Domain.Abstractions;
using ClinScribe.Infrastructure.Audit;
using ClinScribe.Infrastructure.Repositories;
using ClinScribe.Infrastructure.Snapshots;
using ClinScribe.Infrastructure.Synthetic;
using Microsoft.Extensions.DependencyInjection;

namespace ClinScribe.Infrastructure;

public static class DependencyInjection
{
    /// <summary>註冊 Infrastructure 服務（骨架皆為單例記憶體實作）。</summary>
    /// <param name="syntheticPerCategory">每種情境模擬筆數；&gt;0 時改用 seeded 大量資料集。</param>
    public static IServiceCollection AddClinScribeInfrastructure(
        this IServiceCollection services, int syntheticPerCategory = 20)
    {
        services.AddSingleton<IAuditService, InMemoryAuditService>();
        services.AddSingleton<IDraftRepository, InMemoryDraftRepository>();
        services.AddSingleton<IIncidentService, InMemoryIncidentService>();
        services.AddSingleton<IAiKillSwitch, InMemoryAiKillSwitch>();

        if (syntheticPerCategory > 0)
        {
            var dataset = SyntheticDataGenerator.Generate(syntheticPerCategory);
            services.AddSingleton(dataset);
            services.AddSingleton<SeededSnapshotService>();
            services.AddSingleton<ISnapshotService>(sp => sp.GetRequiredService<SeededSnapshotService>());
            services.AddSingleton<IPatientDirectory>(sp => sp.GetRequiredService<SeededSnapshotService>());
        }
        else
        {
            services.AddSingleton<ISnapshotService, DemoSnapshotService>();
        }

        return services;
    }
}
