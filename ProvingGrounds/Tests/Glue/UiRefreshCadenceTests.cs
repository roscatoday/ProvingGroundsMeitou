using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The snapshot publish cadence: ArenaUI.cpp's 0.20 s idle refresh (kIdleUiRefreshIntervalSec, :213-230, :3172-3185)
/// driven by FrameCadencePolicy with the clock's per-tick dt.</summary>
public class UiRefreshCadenceTests
{
    [Fact]
    public void IntervalIsTheOriginals() => Assert.Equal(0.20f, UiRefreshCadence.IntervalSec);

    [Fact]
    public void FirstCallFiresAtOnce()
    {
        // The C++ starts elapsed at the interval and its first dt is 0 (g_uiRefreshLastTick == 0): the first frame refreshes.
        var c = new UiRefreshCadence();
        Assert.True(c.Advance(SimClock.TickSeconds));
    }

    [Fact]
    public void FiresEverySixTicksAtThirtyTicksPerSecond()
    {
        var c = new UiRefreshCadence();
        Assert.True(c.Advance(SimClock.TickSeconds));   // first: dt 0, elapsed 0.20 -> fires, remainder 0
        var fired = new List<int>();
        for (int tick = 1; tick <= 60; tick++)
            if (c.Advance(SimClock.TickSeconds)) fired.Add(tick);
        // Same arithmetic as the C++ (float sum, fmodf remainder): one refresh per 0.20 s of simulation time.
        float elapsed = 0f;
        var expected = new List<int>();
        for (int tick = 1; tick <= 60; tick++)
            if (ProvingGrounds.Core.FrameCadencePolicy.Advance(ref elapsed, 1f / 30f, 0.20f)) expected.Add(tick);
        Assert.Equal(expected, fired);
        // Pinned: with float 1/30 steps the remainder never drifts far enough to slip a tick in the first two seconds.
        Assert.Equal(new[] { 6, 12, 18, 24, 30, 36, 42, 48, 54, 60 }, fired);
        Assert.InRange(fired.Count, 9, 10);
        Assert.All(fired.Zip(fired.Skip(1), (a, b) => b - a), gap => Assert.InRange(gap, 6, 7));
    }

    [Fact]
    public void DtIsCappedAtOneSecond()
    {
        // AdvanceUiRefreshClock returns at most 1.0f (ArenaUI.cpp:223).
        var c = new UiRefreshCadence();
        c.Advance(0f);
        Assert.True(c.Advance(5f));
        float expected = 0f;
        ProvingGrounds.Core.FrameCadencePolicy.Advance(ref expected, 1.0f, 0.20f);
        Assert.Equal(expected, c.ElapsedSec);
    }

    [Fact]
    public void ResetFiresOnTheNextCall()
    {
        var c = new UiRefreshCadence();
        c.Advance(0f);
        Assert.False(c.Advance(0.05f));
        c.Reset();   // ResetUiRefreshClock: elapsed back to the interval, dt 0 next
        Assert.True(c.Advance(0.05f));
        Assert.False(c.Advance(0.05f));
    }
}
