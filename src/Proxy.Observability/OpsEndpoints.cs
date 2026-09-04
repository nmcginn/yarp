using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Proxy.Observability;

/// <summary>Health and readiness on port 8082 (spec §8.3). Prometheus metrics join them in Phase 4.</summary>
public static class OpsEndpoints
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// Readiness reports the loaded route configuration. Invalid configuration never gets this far —
    /// startup is fatal (spec §4.4) — so a pod that is listening has valid config by construction.
    /// The check exists so readiness states that explicitly rather than by implication.
    /// </summary>
    public static IServiceCollection AddOpsHealthChecks(this IServiceCollection services, int routesLoaded, int clustersLoaded)
    {
        services.AddHealthChecks()
            .AddCheck(
                "config",
                () => HealthCheckResult.Healthy($"{routesLoaded} routes and {clustersLoaded} clusters loaded"),
                tags: [ReadyTag]);
        return services;
    }

    public static WebApplication MapOpsEndpoints(this WebApplication app)
    {
        // Security control (spec §8.3): liveness never checks dependencies. A liveness probe that
        // fails during a Redis blip makes Kubernetes restart every pod at once. Predicate = false
        // runs no checks at all and reports Healthy while the process can answer HTTP.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
        });

        return app;
    }
}
