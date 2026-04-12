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
    private static readonly Color BgColor = new(0.05f, 0.05f, 0.08f, 0.85f);
    private static readonly Color TextColor = new(0.85f, 0.85f, 0.85f);
    private static readonly Color DimTextColor = new(0.5f, 0.5f, 0.55f);
    private static readonly Color BarBgColor = new(0.15f, 0.15f, 0.18f);

    private Control? _drawControl;
    private Button? _exitBtn;
    private Button? _surrenderBtn;
    private Action? _surrenderHandler;
    private bool _surrendered;

    public override void _Ready()
    {
        // CanvasLayer renders on top of everything
        Layer = 10;

        // Add a Control node for drawing
        _drawControl = new HudDrawControl(this);
        _drawControl.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _drawControl.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_drawControl);

        // Exit button — anchored to top-right, inside the top bar
        _exitBtn = new Button
        {
            Text = "✕",
            Size = new Vector2(24, 24),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _exitBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _exitBtn.OffsetLeft = -34;
        _exitBtn.OffsetTop = 4;
        _exitBtn.OffsetRight = -10;
        _exitBtn.OffsetBottom = 28;
        _exitBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        AddChild(_exitBtn);

        // Surrender button — to the left of the exit button
        _surrenderBtn = new Button
        {
            Text = "Surrender",
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "Concede the match",
        };
        _surrenderBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _surrenderBtn.OffsetLeft = -120;
        _surrenderBtn.OffsetTop = 4;
        _surrenderBtn.OffsetRight = -40;
        _surrenderBtn.OffsetBottom = 28;
        _surrenderBtn.Pressed += OnSurrenderPressed;
        AddChild(_surrenderBtn);
    }

    /// <summary>
    /// Provide the callback that submits a Surrender command for the local player.
    /// GameManager wires this to SelectionManager.SubmitSurrender so the command
    /// flows through the same lockstep / SP-pending pipeline as anything else.
    /// </summary>
    public void SetSurrenderHandler(Action handler) => _surrenderHandler = handler;

    private void OnSurrenderPressed()
    {
        if (_surrendered) return;
        _surrendered = true;
        if (_surrenderBtn != null)
        {
            _surrenderBtn.Disabled = true;
            _surrenderBtn.Text = "Surrendered";
        }
        _surrenderHandler?.Invoke();
    }

    public void SetGameState(GameState state) => _gameState = state;
    public void SetConfig(GameConfig config) => _config = config;
    public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

    public GameState? GetGameState() => _gameState;
    public int GetControllingPlayer() => _controllingPlayer;

    /// <summary>Inner Control that handles the actual drawing.</summary>
    private partial class HudDrawControl : Control
    {
        private readonly HudOverlay _hud;
        private int _frameCounter;
        private float _fps;
        private float _fpsAccum;
        private int _fpsFrames;
        private float _fpsTimer;

        public HudDrawControl(HudOverlay hud) => _hud = hud;

        public override void _Process(double delta)
        {
            // FPS tracking — update reading every 0.5s for stability
            _fpsAccum += (float)delta;
            _fpsFrames++;
            _fpsTimer += (float)delta;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / _fpsAccum;
                _fpsAccum = 0;
                _fpsFrames = 0;
                _fpsTimer = 0;
            }

            // Throttle full HUD redraw to every 3 frames
            if (++_frameCounter % 3 == 0)
                QueueRedraw();
        }

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

            // FPS counter (right side, color-coded)
            string fpsText = $"{_fps:F0} FPS";
            var fpsColor = _fps >= 55 ? new Color(0.4f, 0.9f, 0.4f) :
                           _fps >= 30 ? new Color(0.9f, 0.9f, 0.3f) :
                           new Color(0.9f, 0.3f, 0.3f);
            float fpsWidth = font.GetStringSize(fpsText, HorizontalAlignment.Left, -1, fontSize).X;
            DrawString(font, new Vector2(viewport.X - fpsWidth - 44, 22), fpsText,
                HorizontalAlignment.Left, -1, fontSize, fpsColor);

            // Block count ratio bar (bottom of top bar)
            DrawBlockRatioBar(state, new Rect2(0, barHeight, viewport.X, ratioBarHeight));
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
