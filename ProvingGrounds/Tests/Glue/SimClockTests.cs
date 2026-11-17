using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The port's GetTickCount() stand-in (src/Engine/PgClock.cs delegates its arithmetic to Core/Glue/SimClock.cs).</summary>
public class SimClockTests
{
    [Theory]
    [InlineData(0L, 0u)]
    [InlineData(1L, 33u)]          // 1000/30 = 33.3 ms, truncated like integer division
    [InlineData(2L, 66u)]
    [InlineData(3L, 100u)]
    [InlineData(30L, 1000u)]       // one second of simulation
    [InlineData(31L, 1033u)]
    [InlineData(108_000L, 3_600_000u)]   // one hour at 30 ticks per second
    public void MillisecondsFromTicks(long ticks, uint expected) => Assert.Equal(expected, SimClock.MsAt(ticks));

    [Fact]
    public void WrapsAt2Pow32MillisecondsLikeADword()
    {
        // GetTickCount() is a DWORD: it wraps to 0 after 2^32 ms (49.7 days). 2^32 ms = 128,849,018.88 ticks.
        const long wrapTicks = 128_849_019L;   // first tick at or past 2^32 ms: 4,294,967,300 ms
        Assert.Equal(4u, SimClock.MsAt(wrapTicks));
        Assert.Equal(4_294_967_266u, SimClock.MsAt(wrapTicks - 1));   // 4,294,967,266 ms: just before the wrap
        // Unsigned subtraction across the wrap still gives the elapsed time, as the C++ throttles rely on.
        Assert.Equal(34u, unchecked(SimClock.MsAt(wrapTicks) - SimClock.MsAt(wrapTicks - 1)));
    }

    [Fact]
    public void AFreshWorldStartsAnHourIn()
    {
        Assert.Equal(3_600_000u, SimClock.NowMsAfter(0));
        Assert.NotEqual(0u, SimClock.NowMsAfter(0));
        Assert.True(SimClock.NowMsAfter(0) >= SimClock.LargestOriginalIntervalMs);
        // A throttle against a never-set stamp of 0 fires on the first tick, as with the original's uptime clock.
        Assert.True(SimClock.ElapsedMs(SimClock.NowMsAfter(1), 0u) >= SimClock.LargestOriginalIntervalMs);
        Assert.Equal(3_600_033u, SimClock.NowMsAfter(1));
    }

    [Fact]
    public void AWorldClockWrapsAfterAbout49Days()
    {
        // The absolute wrap tick (128,849,019) less the start offset: 128,741,019 ticks of this world.
        const long worldWrap = 128_849_019L - SimClock.StartTicks;
        Assert.Equal(128_741_019L, worldWrap);
        Assert.Equal(4u, SimClock.NowMsAfter(worldWrap));
        Assert.Equal(4_294_967_266u, SimClock.NowMsAfter(worldWrap - 1));
    }

    [Fact]
    public void TickIsOneThirtiethOfASecond() => Assert.Equal(1f / 30f, SimClock.TickSeconds);

    [Fact]
    public void ElapsedMsUsesUnsignedDifference() => Assert.Equal(10u, SimClock.ElapsedMs(5u, uint.MaxValue - 4));
}
