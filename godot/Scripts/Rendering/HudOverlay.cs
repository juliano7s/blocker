using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// HUD overlay: player info, time display, population, menu button.
/// Game bible Section 16.15.
/// </summary>
public partial class HudOverlay : CanvasLayer
{
    private GameState? _gameState;
    private GameConfig _config = GameConfig.CreateDefault();
    private int _controllingPlayer;
    private static readonly Color DividerColor = new(0.176f, 0.216f, 0.282f); // #2d3748

    private SpawnToggles _spawnToggles = null!;
    private Control? _drawControl;
    private Button? _menuBtn;
    private PopupMenu? _menuPopup;
    private Action? _surrenderHandler;
    private bool _surrendered;
    private bool _showDebugFps = false;

    public void SetShowDebugFps(bool show) => _showDebugFps = show;

    [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType);

    public override void _Ready()
    {
        // CanvasLayer renders on top of everything
        Layer = 10;

        // Add a Control node for drawing
        _drawControl = new HudDrawControl(this);
        _drawControl.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _drawControl.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_drawControl);

        // Menu button (right side)
        _menuBtn = new Button
        {
            Text = "☰ Menu",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _menuBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _menuBtn.OffsetLeft = -90;
        _menuBtn.OffsetTop = 8;
        _menuBtn.OffsetRight = -14;
        _menuBtn.OffsetBottom = 34;
        _menuBtn.Pressed += OnMenuPressed;
        AddChild(_menuBtn);

        // Popup menu
        _menuPopup = new PopupMenu();
        _menuPopup.AddItem("Surrender", 0);
        _menuPopup.AddItem("Exit to Menu", 1);
        _menuPopup.IdPressed += OnMenuItemSelected;
        AddChild(_menuPopup);

        // Spawn toggle buttons — centered in top bar
        _spawnToggles = new SpawnToggles();
        _spawnToggles.AnchorLeft = 0.5f;
        _spawnToggles.AnchorRight = 0.5f;
        _spawnToggles.AnchorTop = 0f;
        _spawnToggles.AnchorBottom = 0f;
        _spawnToggles.OffsetLeft = -SpawnToggles.TotalWidth / 2f;
        _spawnToggles.OffsetRight = SpawnToggles.TotalWidth / 2f;
        _spawnToggles.OffsetTop = (HudStyles.TopBarHeight - SpawnToggles.ButtonSize) / 2f;
        _spawnToggles.OffsetBottom = _spawnToggles.OffsetTop + SpawnToggles.ButtonSize;
        _spawnToggles.SpawnToggleChanged += type => EmitSignal(SignalName.SpawnToggleChanged, type);
        AddChild(_spawnToggles);
    }

    /// <summary>
    /// Provide the callback that submits a Surrender command for the local player.
    /// GameManager wires this to SelectionManager.SubmitSurrender so the command
    /// flows through the same lockstep / SP-pending pipeline as anything else.
    /// </summary>
    public void SetSurrenderHandler(Action handler) => _surrenderHandler = handler;

    private void OnMenuPressed()
    {
        if (_menuPopup == null || _menuBtn == null) return;
        var btnRect = _menuBtn.GetGlobalRect();
        _menuPopup.Position = new Vector2I((int)btnRect.Position.X, (int)(btnRect.Position.Y + btnRect.Size.Y));
        _menuPopup.Popup();
    }

    private void OnMenuItemSelected(long id)
    {
        switch (id)
        {
            case 0: // Surrender
                if (!_surrendered)
                {
                    _surrendered = true;
                    _surrenderHandler?.Invoke();
                }
                break;
            case 1: // Exit
                GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
                break;
        }
    }

    public void SetGameState(GameState state)
    {
        _gameState = state;
        _spawnToggles?.SetGameState(state);
    }
    public void SetConfig(GameConfig config) => _config = config;
    public void SetControllingPlayer(int playerId)
    {
        _controllingPlayer = playerId;
        _spawnToggles?.SetControllingPlayer(playerId);
    }

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
            var font = ThemeDB.FallbackFont;

            // Draw top bar background with gradient
            HudStyles.DrawPanelBackground(this, new Rect2(0, 0, viewport.X, HudStyles.TopBarHeight));

            // Player info (left side)
            int pid = _hud.GetControllingPlayer();
            var playerColor = _hud._config.GetPalette(pid).Base;
            var lighterBorder = playerColor.Lightened(0.3f);

            float x = 14f;
            float centerY = HudStyles.TopBarHeight / 2f;

            // Player color square
            var colorRect = new Rect2(x, centerY - 8, 16, 16);
            DrawRect(colorRect, playerColor);
            DrawRect(colorRect, lighterBorder, false, 1f);
            x += 24;

            // Player name
            string playerName = $"Player {pid}";
            DrawString(font, new Vector2(x, centerY + 5), playerName,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextPrimary);
            x += font.GetStringSize(playerName, HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal).X + 14;

            // Divider
            DrawLine(new Vector2(x, centerY - 11), new Vector2(x, centerY + 11), DividerColor, 1f);
            x += 14;

            // Game time (convert ticks to hh:mm:ss)
            int totalSeconds = state.TickNumber / 12; // Assuming 12 tps
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            string timeText = hours > 0 ? $"{hours}:{minutes:D2}:{seconds:D2}" : $"{minutes:D2}:{seconds:D2}";
            DrawString(font, new Vector2(x, centerY + 5), timeText,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextSecondary);
            x += font.GetStringSize(timeText, HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal).X + 14;

            // Divider
            DrawLine(new Vector2(x, centerY - 11), new Vector2(x, centerY + 11), DividerColor, 1f);
            x += 14;

            // Population
            if (pid < state.Players.Count)
            {
                var player = state.Players.Find(p => p.Id == pid);
                if (player != null)
                {
                    int currentPop = state.GetPopulation(pid);
                    string popText = $"Pop: {currentPop} / {player.MaxPopulation}";
                    DrawString(font, new Vector2(x, centerY + 5), popText,
                        HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextPrimary);
                }
            }

            // Debug FPS (right side, only if enabled)
            if (_hud._showDebugFps)
            {
                string fpsText = $"{_fps:F0} FPS";
                var fpsColor = _fps >= 55 ? new Color(0.4f, 0.9f, 0.4f) :
                               _fps >= 30 ? new Color(0.9f, 0.9f, 0.3f) :
                               new Color(0.9f, 0.3f, 0.3f);
                float fpsWidth = font.GetStringSize(fpsText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall).X;
                // Position below top bar
                DrawString(font, new Vector2(viewport.X - fpsWidth - 14, HudStyles.TopBarHeight + 16),
                    fpsText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, fpsColor);
            }
        }
    }
}
