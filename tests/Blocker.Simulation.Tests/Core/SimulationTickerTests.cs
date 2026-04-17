using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests.Core;

public class SimulationTickerTests
{
    [Fact]
    public void ProcessFrame_ClearsVisualEventsExactlyOnce()
    {
        // Arrange
        var state = new GameState(new Grid(10, 10));
        state.VisualEvents.Add(new VisualEvent(VisualEventType.BlockMoved, new GridPos(0, 0)));
        int tryAdvanceCount = 0;

        var ticker = new SimulationTicker(
            state,
            tickRate: 12,
            maxAdvancePerFrame: 5,
            tryAdvance: (delta) =>
            {
                tryAdvanceCount++;
                // Simulate a tick adding a new event
                state.VisualEvents.Add(new VisualEvent(VisualEventType.BlockDied, new GridPos(0, 0)));
                return true;
            }
        );

        // Act
        // Process a frame with enough delta for 2 ticks
        ticker.ProcessFrame(2.0 / 12.0);

        // Assert
        Assert.Equal(2, tryAdvanceCount);
        
        // VisualEvents from the PREVIOUS frame should be gone.
        // VisualEvents from the TWO ticks in the current frame should remain.
        Assert.Equal(2, state.VisualEvents.Count);
        Assert.All(state.VisualEvents, e => Assert.Equal(VisualEventType.BlockDied, e.Type));
    }

    [Fact]
    public void ProcessFrame_LimitsAdvancesToMaxAdvancePerFrame()
    {
        // Arrange
        var state = new GameState(new Grid(10, 10));
        int advanceCount = 0;
        
        var ticker = new SimulationTicker(
            state,
            tickRate: 10,
            maxAdvancePerFrame: 3,
            tryAdvance: (_) =>
            {
                advanceCount++;
                return true;
            }
        );

        // Act
        // Supply 1 full second of delta (enough for 10 ticks)
        ticker.ProcessFrame(1.0);

        // Assert
        Assert.Equal(3, advanceCount); // Capped by MaxAdvancePerFrame
    }

    [Fact]
    public void ProcessFrame_AccumulatesSmallDeltasUntilTickInterval()
    {
        // Arrange
        var state = new GameState(new Grid(10, 10));
        int advanceCount = 0;
        
        var ticker = new SimulationTicker(
            state,
            tickRate: 10, // 0.1s interval
            maxAdvancePerFrame: 5,
            tryAdvance: (_) => { advanceCount++; return true; }
        );

        // Act & Assert
        ticker.ProcessFrame(0.04);
        Assert.Equal(0, advanceCount);
        Assert.Equal(0.04, ticker.Accumulator, 5);

        ticker.ProcessFrame(0.04);
        Assert.Equal(0, advanceCount);
        Assert.Equal(0.08, ticker.Accumulator, 5);

        // This pushes it over 0.1s
        ticker.ProcessFrame(0.04);
        Assert.Equal(1, advanceCount);
        Assert.Equal(0.02, ticker.Accumulator, 5); // 0.12 - 0.10
    }

    [Fact]
    public void ProcessFrame_CapsAccumulatorDrift()
    {
        // Arrange
        var state = new GameState(new Grid(10, 10));
        
        var ticker = new SimulationTicker(
            state,
            tickRate: 10, // 0.1s interval
            maxAdvancePerFrame: 5,
            tryAdvance: (_) => false // Simulate network stall — zero ticks advance
        );

        // Act
        // Supply a massive lag spike (1 full second)
        ticker.ProcessFrame(1.0);

        // Assert
        // Accumulator should be capped at 2 * TickInterval (0.2s)
        Assert.Equal(0.2, ticker.Accumulator, 5);
    }
}
