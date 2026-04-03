using Blocker.Game.Input;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Drives the simulation at a fixed tick rate from Godot's _Process loop.
/// Collects commands from SelectionManager and passes them to Tick().
/// </summary>
public partial class TickRunner : Node
{
	[Export] public int TickRate = 12;

	private GameState? _gameState;
	private SelectionManager? _selectionManager;
	private double _accumulator;

	public double TickInterval => 1.0 / TickRate;

	public float InterpolationFactor =>
		TickInterval > 0 ? Mathf.Clamp((float)(_accumulator / TickInterval), 0f, 1f) : 1f;

	public void SetGameState(GameState state)
	{
		_gameState = state;
	}

	public void SetSelectionManager(SelectionManager sm)
	{
		_selectionManager = sm;
	}

	public override void _Process(double delta)
	{
		if (_gameState == null) return;

		_accumulator += delta;
		while (_accumulator >= TickInterval)
		{
			var commands = _selectionManager?.FlushCommands();
			_gameState.Tick(commands);
			_accumulator -= TickInterval;
		}
	}
}
