using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaPerformance.cs (task 1.6); values read off ArenaPerformance.h/.cpp. Small enough that a golden adds nothing.
public class ArenaPerformanceTests
{
    [Fact]   // ArenaPerformance.h:7-12, ArenaPerformance.cpp:5-8
    public void Profiles_and_validity()
    {
        Assert.Equal(0, (int)ArenaPerformance.Profile.High);
        Assert.Equal(1, (int)ArenaPerformance.Profile.Normal);
        Assert.Equal(2, (int)ArenaPerformance.Profile.Potato);
        Assert.False(ArenaPerformance.IsValid(-1));
        Assert.True(ArenaPerformance.IsValid(0));
        Assert.True(ArenaPerformance.IsValid(2));
        Assert.False(ArenaPerformance.IsValid(3));
    }

    [Fact]   // ArenaPerformance.cpp:10-28: anything else reads as High
    public void Names()
    {
        Assert.Equal("High", ArenaPerformance.Name(ArenaPerformance.Profile.High));
        Assert.Equal("Normal", ArenaPerformance.Name(ArenaPerformance.Profile.Normal));
        Assert.Equal("Potato", ArenaPerformance.Name(ArenaPerformance.Profile.Potato));
        Assert.Equal("High", ArenaPerformance.Name((ArenaPerformance.Profile)7));
        Assert.Equal("high", ArenaPerformance.PersistedName(ArenaPerformance.Profile.High));
        Assert.Equal("normal", ArenaPerformance.PersistedName(ArenaPerformance.Profile.Normal));
        Assert.Equal("potato", ArenaPerformance.PersistedName(ArenaPerformance.Profile.Potato));
        Assert.Equal("high", ArenaPerformance.PersistedName((ArenaPerformance.Profile)(-1)));
    }

    [Fact]   // ArenaPerformance.cpp:30-37: exact, case-sensitive; the output is untouched on failure
    public void Parse()
    {
        var p = ArenaPerformance.Profile.Normal;
        Assert.True(ArenaPerformance.Parse("potato", ref p));
        Assert.Equal(ArenaPerformance.Profile.Potato, p);
        Assert.True(ArenaPerformance.Parse("high", ref p));
        Assert.Equal(ArenaPerformance.Profile.High, p);
        Assert.True(ArenaPerformance.Parse("normal", ref p));
        Assert.Equal(ArenaPerformance.Profile.Normal, p);
        foreach (string bad in new[] { "High", "POTATO", " normal", "", "low" })
        {
            p = ArenaPerformance.Profile.Potato;
            Assert.False(ArenaPerformance.Parse(bad, ref p));
            Assert.Equal(ArenaPerformance.Profile.Potato, p);
        }
    }

    [Fact]   // ArenaPerformance.cpp:39-47 and the default arguments of ArenaPerformance.h:23-29
    public void Policies()
    {
        var high = ArenaPerformance.PolicyFor(ArenaPerformance.Profile.High);
        Assert.Equal((120, 0u, 4.0f, 8.0f, 5, true),
            (high.SpectatorCap, high.MaintenanceMs, high.ChatterMinSeconds, high.ChatterMaxSeconds, high.MaxChatterVoices, high.ChatterEnabled));
        var normal = ArenaPerformance.PolicyFor(ArenaPerformance.Profile.Normal);
        Assert.Equal((60, 100u, 8.0f, 12.0f, 2, true),
            (normal.SpectatorCap, normal.MaintenanceMs, normal.ChatterMinSeconds, normal.ChatterMaxSeconds, normal.MaxChatterVoices, normal.ChatterEnabled));
        var potato = ArenaPerformance.PolicyFor(ArenaPerformance.Profile.Potato);
        Assert.Equal((20, 250u, 0.0f, 0.0f, 0, false),
            (potato.SpectatorCap, potato.MaintenanceMs, potato.ChatterMinSeconds, potato.ChatterMaxSeconds, potato.MaxChatterVoices, potato.ChatterEnabled));
        Assert.Equal(high, ArenaPerformance.PolicyFor((ArenaPerformance.Profile)9));
        Assert.Equal(high, new ArenaPerformance.Policy());
        var partial = new ArenaPerformance.Policy(30);
        Assert.Equal((30, 0u, 4.0f, 8.0f, 5, true),
            (partial.SpectatorCap, partial.MaintenanceMs, partial.ChatterMinSeconds, partial.ChatterMaxSeconds, partial.MaxChatterVoices, partial.ChatterEnabled));
    }
}
