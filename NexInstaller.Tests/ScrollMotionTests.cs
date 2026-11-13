using NexInstaller.App.Behaviors;
namespace NexInstaller.Tests;
public sealed class ScrollMotionTests
{
    [Theory]
    [InlineData(100, 200, false, -120, 3, 148)]
    [InlineData(100, 200, true, -120, 3, 248)]
    [InlineData(100, 200, true, 120, 3, 52)]
    [InlineData(10, 10, false, 120, 3, 0)]
    [InlineData(990, 990, false, -120, 3, 1000)]
    [InlineData(100, 100, false, -60, 3, 124)]
    [InlineData(100, 100, false, -120, 0, 100)]
    [InlineData(100, 100, false, -120, -1, 400)]
    public void WheelHonorsBoundsAccumulationReversalAndWindowsSettings(double current, double previous,
        bool moving, int delta, int lines, double expected) =>
        Assert.Equal(expected, ScrollMotion.Target(current, previous, moving, delta, lines, 300, 1000));
}
