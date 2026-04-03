using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// HUD overlay: player info, tick counter, population, block count ratio bar.
/// Game bible Section 16.15.
/// </summary>
public partial class HudOverlay : CanvasLayer
{
	private GameState? _gameState;
	private GameConfig _config = GameConfig.CreateDefault();
	private int _controllingPlayer;
	private IReadOnlyList<Block>? _selectedBlocks;

	private static readonly Color BgColor = new(0.05f, 0.05f, 0.08f, 0.85f);
	private static readonly Color TextColor = new(0.85f, 0.85f, 0.85f);
	private static readonly Color DimTextColor = new(0.5f, 0.5f, 0.55f);
	private static readonly Color BarBgColor = new(0.15f, 0.15f, 0.18f);

	private Control? _drawControl;

	public override void _Ready()
	{
		// CanvasLayer renders on top of everything
		Layer = 10;

		// Add a Control node for drawing
		_drawControl = new HudDrawControl(this);
		_drawControl.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
		_drawControl.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_drawControl);
	}

	public void SetGameState(GameState state) => _gameState = state;
	public void SetConfig(GameConfig config) => _config = config;
	public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;
	public void SetSelectedBlocks(IReadOnlyList<Block> blocks) => _selectedBlocks = blocks;

	public GameState? GetGameState() => _gameState;
	public int GetControllingPlayer() => _controllingPlayer;
	public IReadOnlyList<Block>? GetSelectedBlocks() => _selectedBlocks;

	/// <summary>Inner Control that handles the actual drawing.</summary>
	private partial class HudDrawControl : Control
	{
		private readonly HudOverlay _hud;

		public HudDrawControl(HudOverlay hud) => _hud = hud;

		public override void _Process(double delta) => QueueRedraw();

		public override void _Draw()
		{
			var state = _hud.GetGameState();
			if (state == null) return;

			var viewport = GetViewportRect().Size;
			const float barHeight = 32f;
			const float ratioBarHeight = 6f;
			const float padding = 10f;

			// Top bar background
			DrawRect(new Rect2(0, 0, viewport.X, barHeight + ratioBarHeight), BgColor);

			var font = ThemeDB.FallbackFont;
			int fontSize = 14;

			// Player info (left side)
			int pid = _hud.GetControllingPlayer();
			var playerColor = _hud._config.GetPalette(pid).Base;
			string playerName = $"Player {pid}";

			// Player color indicator
			float x = padding;
			DrawRect(new Rect2(x, 8, 16, 16), playerColor);
			x += 22;
			DrawString(font, new Vector2(x, 22), playerName, HorizontalAlignment.Left, -1, fontSize, TextColor);
			x += font.GetStringSize(playerName, HorizontalAlignment.Left, -1, fontSize).X + 20;

			// Tick counter
			string tickText = $"Tick {state.TickNumber}";
			DrawString(font, new Vector2(x, 22), tickText, HorizontalAlignment.Left, -1, fontSize, DimTextColor);
			x += font.GetStringSize(tickText, HorizontalAlignment.Left, -1, fontSize).X + 20;

			// Population display
			if (pid < state.Players.Count)
			{
				var player = state.Players.Find(p => p.Id == pid);
				if (player != null)
				{
					int currentPop = state.GetPopulation(pid);
					string popText = $"Pop: {currentPop} / {player.MaxPopulation}";
					DrawString(font, new Vector2(x, 22), popText, HorizontalAlignment.Left, -1, fontSize, TextColor);
				}
			}

			// Block count ratio bar (bottom of top bar)
			DrawBlockRatioBar(state, new Rect2(0, barHeight, viewport.X, ratioBarHeight));

			// Selection info and keybind hints (bottom-left)
			DrawSelectionInfo(state, viewport, font, fontSize);
		}

		private void DrawSelectionInfo(GameState state, Vector2 viewport, Font font, int fontSize)
		{
			var selected = _hud.GetSelectedBlocks();
			if (selected == null || selected.Count == 0) return;

			const float padding = 10f;
			float y = viewport.Y - 80f;

			// Background panel
			DrawRect(new Rect2(0, y - 5, 200, 85), BgColor);

			// Count by type
			var typeCounts = new Dictionary<BlockType, int>();
			foreach (var block in selected)
			{
				typeCounts.TryGetValue(block.Type, out int count);
				typeCounts[block.Type] = count + 1;
			}

			string selText = string.Join(", ", typeCounts.Select(kv => $"{kv.Value} {kv.Key}"));
			DrawString(font, new Vector2(padding, y + 12), selText, HorizontalAlignment.Left, -1, fontSize, TextColor);

			// Keybind hints based on selected types
			var hints = new List<string>();
			if (typeCounts.ContainsKey(BlockType.Builder))
			{
				hints.Add("F:Root  W:Wall  B:Blueprint");
				hints.Add("G:Push");
			}
			if (typeCounts.ContainsKey(BlockType.Soldier))
				hints.Add("F:Root  D:Self-Destruct  T:Tower");
			if (typeCounts.ContainsKey(BlockType.Stunner))
				hints.Add("F:Root  S:Stun  D:Explode  T:Tower");
			if (typeCounts.ContainsKey(BlockType.Warden))
				hints.Add("F:Root  D:Magnet Pull");
			if (typeCounts.ContainsKey(BlockType.Jumper))
				hints.Add("F:Jump");

			hints.Add("A:Attack-Move  Shift:Queue");

			float hintY = y + 28;
			int smallFont = 11;
			foreach (var hint in hints.Take(3)) // Max 3 lines
			{
				DrawString(font, new Vector2(padding, hintY), hint, HorizontalAlignment.Left, -1, smallFont, DimTextColor);
				hintY += 16;
			}
		}

		private void DrawBlockRatioBar(GameState state, Rect2 rect)
		{
			DrawRect(rect, BarBgColor);

			if (state.Players.Count == 0) return;

			// Count non-wall blocks per player
			var counts = new Dictionary<int, int>();
			int total = 0;
			foreach (var block in state.Blocks)
			{
				if (block.Type == BlockType.Wall) continue;
				counts.TryGetValue(block.PlayerId, out int count);
				counts[block.PlayerId] = count + 1;
				total++;
			}

			if (total == 0) return;

			float x = rect.Position.X;
			foreach (var player in state.Players)
			{
				if (!counts.TryGetValue(player.Id, out int count)) continue;
				float width = rect.Size.X * count / total;
				var color = _hud._config.GetPalette(player.Id).Base;
				DrawRect(new Rect2(x, rect.Position.Y, width, rect.Size.Y), color);
				x += width;
			}
		}
	}
}
