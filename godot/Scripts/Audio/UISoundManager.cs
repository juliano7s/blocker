using Godot;

namespace Blocker.Game.Audio;

/// <summary>
/// Autoload singleton for UI sounds that works across all scenes.
/// Shares the same AudioConfig resource as AudioManager.
/// </summary>
public partial class UISoundManager : Node
{
    public static UISoundManager? Instance { get; private set; }

    [Export] public AudioConfig? Config { get; set; }

    private const int PoolSize = 4;
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
    private int _poolIndex;

    public override void _Ready()
    {
        Instance = this;

        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer { Bus = "Master" };
            AddChild(player);
            _pool[i] = player;
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void PlayClick() => Play(Config?.UIClick);
    public void PlayHover() => Play(Config?.UIHover);
    public void PlayStartGame() => Play(Config?.UIStartGame);
    public void PlayToggleOn() => Play(Config?.UIToggleOn);
    public void PlayToggleOff() => Play(Config?.UIToggleOff);
    public void PlayCommandClick() => Play(Config?.UICommandClick);
    public void PlayBlueprintClick() => Play(Config?.UIBlueprintClick);
    public void PlayChatSend() => Play(Config?.UIChatSend);
    public void PlayChatReceive() => Play(Config?.UIChatReceive);
    public void PlayError() => Play(Config?.UIError);
    public void PlayReadyUp() => Play(Config?.UIReadyUp);
    public void PlayUnready() => Play(Config?.UIUnready);
    public void PlaySurrender() => Play(Config?.UISurrender);

    private void Play(AudioStream? stream)
    {
        if (stream == null) return;

        var player = _pool[_poolIndex];
        _poolIndex = (_poolIndex + 1) % PoolSize;

        player.Stream = stream;
        player.VolumeDb = Mathf.LinearToDb(Config?.MasterVolume ?? 1.0f);
        player.Play();
    }
}
