namespace Blocker.Game.Net;

public static class RelayClientConfig
{
    public const string DefaultUrl = "wss://julianoschroeder.com/blocker/ws-relay";

    public static string ResolvedUrl =>
        System.Environment.GetEnvironmentVariable("BLOCKER_RELAY_URL") ?? DefaultUrl;

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HelloTimeout   = TimeSpan.FromSeconds(5);
}
