namespace Blocker.Relay;

public record RelayOptions(
    string ListenUrl,
    int RateLimitMsgPerSec,
    int MaxMessageBytes,
    int MaxRoomsPerIp,
    int MaxConnections,
    TimeSpan LobbyTimeout,
    TimeSpan GameTimeout,
    TimeSpan HelloTimeout)
{
    public static RelayOptions FromEnvironment()
    {
        string port = Environment.GetEnvironmentVariable("PORT") ?? "3002";
        return new RelayOptions(
            ListenUrl: $"http://127.0.0.1:{port}/",
            RateLimitMsgPerSec: Int("BLOCKER_RELAY_RATE_LIMIT", 60),
            MaxMessageBytes: Int("BLOCKER_RELAY_MAX_MSG", 64 * 1024),
            MaxRoomsPerIp: Int("BLOCKER_RELAY_ROOMS_PER_IP", 3),
            MaxConnections: Int("BLOCKER_RELAY_MAX_CONNS", 500),
            LobbyTimeout: TimeSpan.FromMinutes(Int("BLOCKER_RELAY_LOBBY_TIMEOUT_MIN", 10)),
            GameTimeout:  TimeSpan.FromMinutes(Int("BLOCKER_RELAY_GAME_TIMEOUT_MIN", 60)),
            HelloTimeout: TimeSpan.FromSeconds(Int("BLOCKER_RELAY_HELLO_TIMEOUT_SEC", 5))
        );
    }

    private static int Int(string name, int def) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : def;
}
