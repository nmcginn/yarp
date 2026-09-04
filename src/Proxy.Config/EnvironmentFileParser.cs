using System.Net;
using Proxy.Config.Model;
using Proxy.Config.Yaml;

namespace Proxy.Config;

/// <summary>
/// Parses one <c>config/environments/*.yaml</c> file. Only the sections that exist so far are
/// accepted; each later phase adds its section here, and nowhere else.
/// </summary>
public static class EnvironmentFileParser
{
    public static ProxyEnvironment Load(string path)
    {
        var errors = new ErrorCollector();
        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            errors.Add(path, 1, $"cannot read environment file: {ex.Message}");
            throw new ConfigException(errors.Sorted());
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add(path, 1, $"cannot read environment file: {ex.Message}");
            throw new ConfigException(errors.Sorted());
        }

        var environment = Parse(path, yaml, errors);
        if (errors.HasErrors || environment is null)
        {
            throw new ConfigException(errors.Sorted());
        }

        return environment;
    }

    public static ProxyEnvironment? Parse(string file, string yaml, ErrorCollector errors)
    {
        var root = YamlDocumentLoader.LoadRootMapping(file, yaml, errors);
        if (root is null)
        {
            return null;
        }

        root.RejectUnknownKeys("environment", "destinationAllowlist");

        var name = root.RequiredString("environment");
        var allowlist = ParseAllowlist(root.RequiredMapping("destinationAllowlist"));

        if (name is null || allowlist is null)
        {
            return null;
        }

        return new ProxyEnvironment(file, name, allowlist);
    }

    private static DestinationAllowlist? ParseAllowlist(MappingReader? node)
    {
        if (node is null)
        {
            return null;
        }

        node.RejectUnknownKeys("dnsSuffixes", "cidrs");

        var suffixes = node.OptionalStringList("dnsSuffixes");
        foreach (var suffix in suffixes)
        {
            if (suffix.Contains("://", StringComparison.Ordinal) || suffix.Contains('/', StringComparison.Ordinal) || suffix.Contains(' ', StringComparison.Ordinal))
            {
                node.Report(node.LineOf("dnsSuffixes"),
                    $"dnsSuffixes entry '{suffix}' must be a bare DNS suffix such as .svc.cluster.local or an exact host such as localhost");
            }
        }

        var cidrs = new List<IPNetwork>();
        foreach (var text in node.OptionalStringList("cidrs"))
        {
            if (IPNetwork.TryParse(text, out var network))
            {
                cidrs.Add(network);
            }
            else
            {
                node.Report(node.LineOf("cidrs"), $"cidrs entry '{text}' is not a valid CIDR such as 10.0.0.0/8");
            }
        }

        return new DestinationAllowlist(suffixes, cidrs);
    }
}
