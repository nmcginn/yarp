using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Proxy.Config.Yaml;

/// <summary>
/// Reads fields out of a YAML mapping, reporting every problem with a file and line number.
/// This is deliberately hand-rolled rather than a reflection-based deserializer: the validator's
/// output quality is what makes PR-based self-service tolerable (spec §4.4), and a deserializer
/// loses the line numbers we need for that.
/// </summary>
internal sealed class MappingReader
{
    private readonly YamlMappingNode _node;
    private readonly string _file;
    private readonly string _path;
    private readonly ErrorCollector _errors;

    public MappingReader(YamlMappingNode node, string file, string path, ErrorCollector errors)
    {
        _node = node;
        _file = file;
        _path = path;
        _errors = errors;
    }

    /// <summary>Line the mapping itself starts on.</summary>
    public int Line => LineNumber(_node);

    public string File => _file;

    /// <summary>Line of a key's value, or the mapping's line when the key is absent.</summary>
    public int LineOf(string key) => Find(key) is { } value ? LineNumber(value) : Line;

    /// <summary>Records an error against this file at the given line.</summary>
    public void Report(int line, string message) => _errors.Add(_file, line, message);

    /// <summary>
    /// Any key outside <paramref name="known"/> is an error. The schema is a safety boundary
    /// (ADR 0005): a misspelled or unsupported field must never be silently ignored.
    /// </summary>
    public void RejectUnknownKeys(params string[] known)
    {
        foreach (var (keyNode, _) in _node.Children)
        {
            var key = ScalarText(keyNode);
            if (key is null || !known.Contains(key, StringComparer.Ordinal))
            {
                var where = _path.Length == 0 ? "at the top level" : $"in {_path}";
                _errors.Add(_file, LineNumber(keyNode),
                    $"unknown field '{key ?? "?"}' {where}; allowed fields: {string.Join(", ", known)}");
            }
        }
    }

    public string? RequiredString(string key)
    {
        var node = Find(key);
        if (node is null)
        {
            _errors.Add(_file, Line, $"'{Describe(key)}' is required");
            return null;
        }

        return ReadScalar(node, key);
    }

    public string? OptionalString(string key)
    {
        var node = Find(key);
        return node is null ? null : ReadScalar(node, key);
    }

    public bool OptionalBool(string key, bool defaultValue)
    {
        var text = OptionalString(key);
        if (text is null)
        {
            return defaultValue;
        }

        if (bool.TryParse(text, out var value))
        {
            return value;
        }

        _errors.Add(_file, LineOf(key), $"'{Describe(key)}' must be true or false, got '{text}'");
        return defaultValue;
    }

    public TimeSpan? OptionalDuration(string key)
    {
        var text = OptionalString(key);
        if (text is null)
        {
            return null;
        }

        if (TimeSpan.TryParseExact(text, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        _errors.Add(_file, LineOf(key), $"'{Describe(key)}' is not a valid duration; expected HH:MM:SS, got '{text}'");
        return null;
    }

    /// <summary>Parses a lowercase enum name into <typeparamref name="T"/>, naming the valid values on failure.</summary>
    public T? OptionalEnum<T>(string key, IReadOnlyDictionary<string, T> values, T? defaultValue)
        where T : struct, Enum
    {
        var text = OptionalString(key);
        if (text is null)
        {
            return defaultValue;
        }

        return ParseEnum(text, key, LineOf(key), values);
    }

    public T? RequiredEnum<T>(string key, IReadOnlyDictionary<string, T> values)
        where T : struct, Enum
    {
        var text = RequiredString(key);
        return text is null ? null : ParseEnum(text, key, LineOf(key), values);
    }

    public IReadOnlyList<string>? RequiredStringList(string key)
    {
        var node = Find(key);
        if (node is null)
        {
            _errors.Add(_file, Line, $"'{Describe(key)}' is required");
            return null;
        }

        return ReadStringList(node, key);
    }

    public IReadOnlyList<string> OptionalStringList(string key)
    {
        var node = Find(key);
        return node is null ? [] : ReadStringList(node, key) ?? [];
    }

    /// <summary>Each item of a list of enum names, with the line each invalid item is on.</summary>
    public IReadOnlyList<T> OptionalEnumList<T>(string key, IReadOnlyDictionary<string, T> values)
        where T : struct, Enum
    {
        var node = Find(key);
        if (node is null)
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence)
        {
            _errors.Add(_file, LineNumber(node), $"'{Describe(key)}' must be a list");
            return [];
        }

        var result = new List<T>();
        foreach (var item in sequence.Children)
        {
            var text = ScalarText(item);
            if (text is null)
            {
                _errors.Add(_file, LineNumber(item), $"items of '{Describe(key)}' must be plain values");
                continue;
            }

            if (ParseEnum(text, key, LineNumber(item), values) is { } parsed)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    public MappingReader? RequiredMapping(string key)
    {
        var node = Find(key);
        if (node is null)
        {
            _errors.Add(_file, Line, $"'{Describe(key)}' is required");
            return null;
        }

        return AsMapping(node, Describe(key));
    }

    public MappingReader? OptionalMapping(string key)
    {
        var node = Find(key);
        return node is null ? null : AsMapping(node, Describe(key));
    }

    /// <summary>A list of mappings, such as <c>routes:</c> or <c>clusters:</c>.</summary>
    public IReadOnlyList<MappingReader>? RequiredMappingList(string key)
    {
        var node = Find(key);
        if (node is null)
        {
            _errors.Add(_file, Line, $"'{Describe(key)}' is required");
            return null;
        }

        if (node is not YamlSequenceNode sequence)
        {
            _errors.Add(_file, LineNumber(node), $"'{Describe(key)}' must be a list");
            return null;
        }

        var result = new List<MappingReader>();
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            var itemPath = $"{Describe(key)}[{i}]";
            if (AsMapping(sequence.Children[i], itemPath) is { } mapping)
            {
                result.Add(mapping);
            }
        }

        return result;
    }

    /// <summary>The entries of a mapping keyed by user-chosen names, such as <c>destinations:</c>.</summary>
    public IReadOnlyList<(string Name, MappingReader Value)> Entries()
    {
        var result = new List<(string, MappingReader)>();
        foreach (var (keyNode, valueNode) in _node.Children)
        {
            var name = ScalarText(keyNode);
            if (string.IsNullOrWhiteSpace(name))
            {
                _errors.Add(_file, LineNumber(keyNode), $"keys of '{_path}' must be non-empty names");
                continue;
            }

            if (AsMapping(valueNode, $"{_path}.{name}") is { } mapping)
            {
                result.Add((name, mapping));
            }
        }

        return result;
    }

    private MappingReader? AsMapping(YamlNode node, string path)
    {
        if (node is YamlMappingNode mapping)
        {
            return new MappingReader(mapping, _file, path, _errors);
        }

        _errors.Add(_file, LineNumber(node), $"'{path}' must be a mapping of fields");
        return null;
    }

    private List<string>? ReadStringList(YamlNode node, string key)
    {
        if (node is not YamlSequenceNode sequence)
        {
            _errors.Add(_file, LineNumber(node), $"'{Describe(key)}' must be a list");
            return null;
        }

        var result = new List<string>();
        foreach (var item in sequence.Children)
        {
            var text = ScalarText(item);
            if (string.IsNullOrWhiteSpace(text))
            {
                _errors.Add(_file, LineNumber(item), $"items of '{Describe(key)}' must be non-empty values");
                continue;
            }

            result.Add(text);
        }

        return result;
    }

    private string? ReadScalar(YamlNode node, string key)
    {
        var text = ScalarText(node);
        if (string.IsNullOrWhiteSpace(text))
        {
            _errors.Add(_file, LineNumber(node), $"'{Describe(key)}' must be a non-empty value");
            return null;
        }

        return text;
    }

    private T? ParseEnum<T>(string text, string key, int line, IReadOnlyDictionary<string, T> values)
        where T : struct, Enum
    {
        if (values.TryGetValue(text, out var value))
        {
            return value;
        }

        _errors.Add(_file, line,
            $"unknown {Describe(key)} '{text}'; expected one of: {string.Join(", ", values.Keys)}");
        return null;
    }

    private YamlNode? Find(string key)
    {
        foreach (var (keyNode, valueNode) in _node.Children)
        {
            if (ScalarText(keyNode) == key)
            {
                return valueNode;
            }
        }

        return null;
    }

    private string Describe(string key) => _path.Length == 0 ? key : $"{_path}.{key}";

    private static string? ScalarText(YamlNode node) => node is YamlScalarNode scalar ? scalar.Value : null;

    /// <summary>YamlDotNet marks are 1-based and 64-bit; our locations are int.</summary>
    internal static int LineNumber(YamlNode node) => checked((int)node.Start.Line);
}
