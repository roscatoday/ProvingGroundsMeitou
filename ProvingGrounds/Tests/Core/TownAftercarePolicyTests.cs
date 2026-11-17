using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownAftercarePolicy;
using Act = ProvingGrounds.Core.TownAftercarePolicy.Action;   // System.Action is in scope too

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownAftercarePolicy.cs beside medics.json: the 32-bit poll clock (MSVC <c>unsigned long</c>) across
/// the wrap, and the CommandProgress default.</summary>
public class TownAftercarePolicyTests
{
    [Theory]
    [InlineData(0xFFFFFFFFu, 5u, true)]           // clock wrapped: now < last
    [InlineData(0xFFFFFC18u, 0xFFFFFFFFu, false)] // 999 ms before the wrap
    [InlineData(0xFFFFFC17u, 0xFFFFFFFFu, true)]  // 1000 ms
    [InlineData(0u, 0u, true)]                     // never polled
    [InlineData(1u, 1000u, false)]
    [InlineData(1u, 1001u, true)]
    public void Polls_use_the_32_bit_clock(uint last, uint now, bool due)
    {
        Assert.Equal(due, BedPollDue(last, now));
        Assert.Equal(due, MedicPollDue(last, now));
    }

    [Fact]
    public void Command_progress_default_is_the_cpp_reset()
    {
        var a = new CommandProgress();
        CommandProgress b = default;
        Assert.Equal(Act.Done, a.Action); Assert.False(a.Issued); Assert.Equal(0, a.StalledRetries);
        Assert.Equal(0f, a.BestDistance); Assert.Equal(0f, a.BestAid); Assert.Equal(0f, a.LastProgress); Assert.Equal(0f, a.LastObserved);
        Assert.Equal(a, b);
        Assert.True(a.Update(Act.Treat, 1, 5, .5f));   // first update always issues
        Assert.False(a.Update(Act.Treat, 2, 5, .5f));  // no progress, inside the 10 s window
        Assert.True(a.Update(Act.Treat, 12, 5, .5f));  // stalled retry
        Assert.Equal(1, a.StalledRetries);
    }
}
