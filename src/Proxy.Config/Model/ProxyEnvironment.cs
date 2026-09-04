namespace Proxy.Config.Model;

/// <summary>
/// One parsed <c>config/environments/*.yaml</c> file. Environment files carry settings that differ
/// between environments; they never carry routes.
/// </summary>
public sealed record ProxyEnvironment(string Path, string Name, DestinationAllowlist DestinationAllowlist);
