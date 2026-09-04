using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Proxy.Config;
using Proxy.Config.Model;
using Serilog;
using Yarp.ReverseProxy.Transforms;

namespace Proxy.Core;

/// <summary>Wires the proxy data plane (port 8080) — YARP plus the middleware order of spec §6.1.</summary>
public static class DataPlane
{
    public static IServiceCollection AddProxyDataPlane(this IServiceCollection services, RouteSet routeSet)
    {
        var (routes, clusters) = YarpTranslator.Translate(routeSet);

        services.AddProblemDetails();
        services.AddReverseProxy()
            // YARP's built-in in-memory provider is sufficient (spec §4.3, ADR 0002). If a polling
            // reloader is ever needed, it calls InMemoryConfigProvider.Update — nothing here changes.
            .LoadFromMemory(routes, clusters)
            .AddTransforms<IdentityHeaderTransforms>()
            .AddTransforms(builderContext =>
            {
                // Forward the correlation id so upstream logs can be joined to ours (spec §6.1).
                builderContext.AddRequestTransform(transformContext =>
                {
                    transformContext.ProxyRequest.Headers.Remove(CorrelationIdMiddleware.HeaderName);
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                        CorrelationIdMiddleware.HeaderName, transformContext.HttpContext.TraceIdentifier);
                    return default;
                });
            });

        return services;
    }

    /// <summary>
    /// Middleware order is deliberate (spec §6.1); changing it changes the security properties.
    /// Authentication and authorization (steps 4–5) arrive in Phase 3, output caching (step 6) in
    /// Phase 6. They slot in between exception handling and MapReverseProxy.
    /// </summary>
    public static WebApplication UseProxyDataPlane(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();   // 1. correlation id
        app.UseSerilogRequestLogging();                 // 2. structured request logging scope
        app.UseExceptionHandler();                      // 3. exception handling (RFC 9457 problem details)
        app.MapReverseProxy();                          // 7. YARP
        return app;
    }
}
