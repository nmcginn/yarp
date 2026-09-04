using Proxy.Config.Model;

namespace Proxy.Config;

/// <summary>
/// Reads every route file in a directory, validates the merged set, and returns it. Used by the
/// proxy at startup and by <c>tools/ConfigValidator</c> in CI — the same code, by design (spec §4.4).
/// </summary>
public static class RouteConfigLoader
{
    /// <summary>Loads and validates, throwing <see cref="ConfigException"/> with every error found.</summary>
    public static RouteSet Load(string routesDirectory, DestinationAllowlist allowlist)
    {
        var errors = new ErrorCollector();
        var files = ReadDirectory(routesDirectory, errors);
        if (errors.HasErrors)
        {
            throw new ConfigException(errors.Sorted());
        }

        RouteSetValidator.Validate(files, allowlist, errors);
        if (errors.HasErrors)
        {
            throw new ConfigException(errors.Sorted());
        }

        return Merge(files);
    }

    /// <summary>
    /// Parses every <c>*.yaml</c> file in the directory. Non-YAML files (such as a README) are
    /// ignored. Parse errors are recorded; the returned files are only trustworthy when
    /// <paramref name="errors"/> is empty.
    /// </summary>
    public static IReadOnlyList<RouteFile> ReadDirectory(string routesDirectory, ErrorCollector errors)
    {
        if (!Directory.Exists(routesDirectory))
        {
            errors.Add(routesDirectory, 1, "routes directory does not exist");
            return [];
        }

        var paths = Directory.EnumerateFiles(routesDirectory)
            .Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var files = new List<RouteFile>();
        foreach (var path in paths)
        {
            string yaml;
            try
            {
                yaml = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                errors.Add(path, 1, $"cannot read file: {ex.Message}");
                continue;
            }

            if (RouteFileParser.Parse(path, yaml, errors) is { } file)
            {
                files.Add(file);
            }
        }

        return files;
    }

    public static RouteSet Merge(IReadOnlyList<RouteFile> files) =>
        new(files, files.SelectMany(f => f.Routes).ToArray(), files.SelectMany(f => f.Clusters).ToArray());
}
