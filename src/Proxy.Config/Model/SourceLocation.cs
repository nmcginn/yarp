namespace Proxy.Config.Model;

/// <summary>Where something was defined, for error messages. Lines are 1-based.</summary>
public readonly record struct SourceLocation(string File, int Line)
{
    public override string ToString() => $"{File}:{Line}";
}
