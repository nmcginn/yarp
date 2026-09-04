using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Proxy.Config.Yaml;

internal static class YamlDocumentLoader
{
    private static int LineNumber(YamlNode node) => MappingReader.LineNumber(node);

    /// <summary>
    /// Loads a YAML file whose root must be a mapping. Returns null (and records an error) when the
    /// text is not valid YAML, is empty, or has a non-mapping root.
    /// </summary>
    public static MappingReader? LoadRootMapping(string file, string yaml, ErrorCollector errors)
    {
        // Two passes. The event parser reports most syntax errors with a line number; for the rest
        // (YamlDotNet's scanner throws InvalidOperationException on some malformed flow collections)
        // the line of the last event it managed to parse is the best available pointer.
        Parser? parser = null;
        try
        {
            using var reader = new StringReader(yaml);
            parser = new Parser(reader);
            while (parser.MoveNext())
            {
            }
        }
        catch (YamlException ex)
        {
            errors.Add(file, checked((int)ex.Start.Line), $"invalid YAML: {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            var line = parser?.Current is { } lastEvent ? checked((int)lastEvent.Start.Line) : 1;
            errors.Add(file, line, $"invalid YAML near this line: {ex.Message}");
            return null;
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            errors.Add(file, checked((int)ex.Start.Line), $"invalid YAML: {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(file, 1, $"invalid YAML: {ex.Message}");
            return null;
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is null)
        {
            errors.Add(file, 1, "file is empty");
            return null;
        }

        if (stream.Documents.Count > 1)
        {
            errors.Add(file, LineNumber(stream.Documents[1].RootNode), "file must contain exactly one YAML document");
            return null;
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            errors.Add(file, LineNumber(stream.Documents[0].RootNode), "top level must be a mapping of fields");
            return null;
        }

        return new MappingReader(root, file, string.Empty, errors);
    }
}
