using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class VisibilityTests
{
    [Fact]
    public void VisibilityMap_SetVisible_MarksExplored()
    {
        var vm = new VisibilityMap(10, 10);
        vm.SetVisible(3, 4);

        Assert.True(vm.IsVisible(3, 4));
        Assert.True(vm.IsExplored(3, 4));
    }

    [Fact]
    public void VisibilityMap_ClearVisible_PreservesExplored()
    {
        var vm = new VisibilityMap(10, 10);
        vm.SetVisible(3, 4);
        vm.ClearVisible();

        Assert.False(vm.IsVisible(3, 4));
        Assert.True(vm.IsExplored(3, 4));
    }

    [Fact]
    public void VisibilityMap_UnsetCells_NotVisible()
    {
        var vm = new VisibilityMap(10, 10);
        Assert.False(vm.IsVisible(0, 0));
        Assert.False(vm.IsExplored(0, 0));
    }
}