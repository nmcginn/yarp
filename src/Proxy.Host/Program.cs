using Proxy.Config;
using Proxy.Host;
using Proxy.Observability;
using Serilog;

// Composition root (spec §3). Read settings, load and validate configuration, start the three
// listeners. Invalid configuration exits non-zero before anything listens (spec §4.4): the pod
// never becomes ready, and the rollout stalls with old pods still serving.

Log.Logger = ProxyLogging.CreateLogger();

try
{
    var settings = ProxySettings.Load(args);
    await using var proxy = ProxyProcess.Create(settings);
    await proxy.RunAsync();
    return 0;
}
catch (ConfigException ex)
{
    foreach (var error in ex.Errors)
    {
        Log.Fatal("Invalid configuration at {File}:{Line}: {Message}", error.Location.File, error.Location.Line, error.Message);
    }

    Log.Fatal("Refusing to start with invalid configuration ({ErrorCount} errors)", ex.Errors.Count);
    return 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Proxy terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
