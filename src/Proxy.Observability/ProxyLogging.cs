using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Proxy.Observability;

/// <summary>
/// Application logs: Serilog, JSON, stdout (spec §8.1). The cluster log pipeline takes it from
/// there. Compliance audit events are a separate stream and arrive in Phase 4; they are never
/// interleaved with these.
/// </summary>
public static class ProxyLogging
{
    public static ILogger CreateLogger(LogEventLevel minimumLevel = LogEventLevel.Information) =>
        new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Yarp", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RenderedCompactJsonFormatter())
            .CreateLogger();
}
