using Proxy.Config;
using Proxy.Config.Model;

// CLI wrapper over Proxy.Config (spec §4.4). CI runs this on every PR touching config/; the proxy
// runs the same RouteConfigLoader / RouteSetValidator code at startup. Nothing is validated here
// that is not validated there, and vice versa.

const int ExitOk = 0;
const int ExitInvalidConfig = 1;
const int ExitUsage = 2;

var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(Options.Usage);
    return ExitUsage;
}

if (options.ShowHelp)
{
    Console.WriteLine(Options.Usage);
    return ExitOk;
}

var environmentPaths = ResolveEnvironmentFiles(options.EnvironmentsPath);
if (environmentPaths.Count == 0)
{
    Console.Error.WriteLine($"error: no environment files found at '{options.EnvironmentsPath}'; the destination allowlist comes from them");
    return ExitUsage;
}

var errors = new ErrorCollector();

var environments = new List<ProxyEnvironment>();
foreach (var path in environmentPaths)
{
    try
    {
        environments.Add(EnvironmentFileParser.Load(path));
    }
    catch (ConfigException ex)
    {
        errors.AddRange(ex.Errors);
    }
}

var files = RouteConfigLoader.ReadDirectory(options.RoutesDirectory, errors);

if (!errors.HasErrors)
{
    // Routes are identical across environments, so they must satisfy every environment's allowlist.
    foreach (var environment in environments)
    {
        var environmentErrors = new ErrorCollector();
        RouteSetValidator.Validate(files, environment.DestinationAllowlist, environmentErrors);
        foreach (var error in environmentErrors.Errors)
        {
            errors.Add(error.Location, $"[{environment.Name}] {error.Message}");
        }
    }
}

if (errors.HasErrors)
{
    foreach (var error in errors.Sorted().Distinct())
    {
        Console.Error.WriteLine(error);
    }

    var noun = errors.Errors.Count == 1 ? "error" : "errors";
    Console.Error.WriteLine($"FAILED: {errors.Errors.Count} {noun} in {options.RoutesDirectory}");
    return ExitInvalidConfig;
}

var set = RouteConfigLoader.Merge(files);
Console.WriteLine(
    $"OK: {set.Routes.Count} routes and {set.Clusters.Count} clusters in {files.Count} files under {options.RoutesDirectory}, " +
    $"validated against environments: {string.Join(", ", environments.Select(e => e.Name))}");
return ExitOk;

static IReadOnlyList<string> ResolveEnvironmentFiles(string path)
{
    if (Directory.Exists(path))
    {
        return Directory.EnumerateFiles(path)
            .Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    return File.Exists(path) ? [path] : [];
}

internal sealed record Options(string RoutesDirectory, string EnvironmentsPath, bool ShowHelp)
{
    public const string Usage = """
        Usage: ConfigValidator [<routes-directory>] [--environments <directory-or-file>]

        Validates every route file against the rules of spec §4.4, including the destination
        allowlist of each environment file. Exit code 0 when valid, 1 when not, 2 on usage error.

        Defaults (relative to the current directory):
          <routes-directory>   config/routes
          --environments       config/environments
        """;

    public static Options? Parse(string[] args)
    {
        var routes = "config/routes";
        var environments = "config/environments";
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    help = true;
                    break;
                case "--environments" or "-e":
                    if (i + 1 >= args.Length)
                    {
                        return null;
                    }

                    environments = args[++i];
                    break;
                case var flag when flag.StartsWith('-'):
                    return null;
                default:
                    routes = args[i];
                    break;
            }
        }

        return new Options(routes, environments, help);
    }
}
