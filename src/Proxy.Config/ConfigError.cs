using Proxy.Config.Model;

namespace Proxy.Config;

/// <summary>
/// One validation problem, with the file and line it was found at. The validator is the primary
/// feedback surface for application teams (spec §4.4), so the message must make sense to someone
/// who has never read the proxy's source.
/// </summary>
public sealed record ConfigError(SourceLocation Location, string Message)
{
    public override string ToString() => $"{Location}: {Message}";
}

/// <summary>Collects errors so a run reports every problem, not just the first.</summary>
public sealed class ErrorCollector
{
    private readonly List<ConfigError> _errors = [];

    public IReadOnlyList<ConfigError> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    public void Add(SourceLocation location, string message) => _errors.Add(new ConfigError(location, message));

    public void Add(string file, int line, string message) => Add(new SourceLocation(file, line), message);

    public void AddRange(IEnumerable<ConfigError> errors) => _errors.AddRange(errors);

    /// <summary>Errors ordered by file then line, for stable output.</summary>
    public IReadOnlyList<ConfigError> Sorted() =>
        _errors.OrderBy(e => e.Location.File, StringComparer.Ordinal).ThenBy(e => e.Location.Line).ToArray();
}

/// <summary>
/// Thrown when configuration is invalid. At startup this is fatal (spec §4.4): the pod must not
/// start with invalid config, and readiness must fail.
/// </summary>
public sealed class ConfigException : Exception
{
    public IReadOnlyList<ConfigError> Errors { get; }

    public ConfigException(IReadOnlyList<ConfigError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<ConfigError> errors)
    {
        var noun = errors.Count == 1 ? "error" : "errors";
        return $"Configuration is invalid ({errors.Count} {noun}):{Environment.NewLine}" +
               string.Join(Environment.NewLine, errors.Select(e => "  " + e));
    }
}
