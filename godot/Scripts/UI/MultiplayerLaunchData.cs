using Blocker.Game.Net;
using Blocker.Simulation.Net;

namespace Blocker.Game.UI;

public enum MultiplayerIntent { None, Host, Join }

public static class MultiplayerLaunchData
{
    public static MultiplayerIntent Intent = MultiplayerIntent.None;
    public static string JoinCode = "";
    public static RelayClient? Relay;
    public static bool RematchReattach;
    public static RoomStatePayload? PendingRoomState;
    public static string LobbyName = "";
    public static string PlayerName = "Player";
}
