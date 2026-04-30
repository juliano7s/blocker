using Blocker.Game.Net;
using Blocker.Simulation.Maps;
using Blocker.Simulation.Net;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public static class GameLaunchData
{
	public static MapData? MapData { get; set; }
	public static List<SlotAssignment>? Assignments { get; set; }
	public static MultiplayerSessionState? MultiplayerSession { get; set; }
	public static bool ReturnToEditor { get; set; }
}
