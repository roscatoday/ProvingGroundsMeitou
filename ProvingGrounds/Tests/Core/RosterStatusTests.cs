using ProvingGrounds.Core;
using RS = ProvingGrounds.Core.RosterStatus;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/RosterStatus.cs, worked from RosterStatus.cpp:9-48.</summary>
public class RosterStatusTests
{
    static RS.Part P(float flesh, float stun, float max) => new RS.Part(flesh, stun, max);

    [Fact]
    public void Lowest_limb_is_the_smallest_flesh_minus_stun_over_max()
    {
        var parts = new List<RS.Part?> { P(100, 0, 100), P(60, 10, 100), P(80, 0, 100) };
        Assert.True(RS.Read(true, true, 200, 150, parts, out RS.Snapshot s, out string error));
        Assert.Equal("", error);
        Assert.Equal(0.5f, s.LowestLimb);
        Assert.Equal(0.75f, s.Blood);
        Assert.Equal(0.5f, s.Recovery);                       // min(limb, blood)
        Assert.Equal(RosterStatusPolicy.Band.Orange, s.Band);
    }

    [Fact]
    public void Lowest_limb_starts_at_one_and_blood_without_maximum_is_zero()
    {
        Assert.True(RS.Read(true, true, 0, 50, new List<RS.Part?> { P(150, 0, 100) }, out RS.Snapshot s, out _));
        Assert.Equal(1.0f, s.LowestLimb);                     // a part above full health never raises it
        Assert.Equal(0.0f, s.Blood);
        Assert.Equal(RosterStatusPolicy.Band.Red, s.Band);
    }

    [Fact]
    public void Failures_name_the_reason_and_leave_a_red_zero_snapshot()
    {
        Assert.False(RS.Read(false, true, 1, 1, new List<RS.Part?> { P(1, 0, 1) }, out RS.Snapshot s, out string e));
        Assert.Equal("health data unavailable", e);
        Assert.Equal(0f, s.LowestLimb); Assert.Equal(RosterStatusPolicy.Band.Red, s.Band);
        Assert.False(RS.Read(true, false, 1, 1, new List<RS.Part?> { P(1, 0, 1) }, out _, out e));
        Assert.Equal("medical system unavailable", e);
        Assert.False(RS.Read(true, true, 1, 1, new List<RS.Part?>(), out _, out e));
        Assert.Equal("anatomy is empty", e);
        Assert.False(RS.Read(true, true, 1, 1, null, out _, out e));
        Assert.Equal("anatomy is empty", e);
        // a null part, a zero and a NaN maximum all fail at their index; the blood was already written
        Assert.False(RS.Read(true, true, 10, 5, new List<RS.Part?> { P(1, 0, 1), null }, out s, out e));
        Assert.Equal("anatomy part 1/2 has no valid maximum health", e);
        Assert.Equal(0.5f, s.Blood); Assert.Equal(1.0f, s.LowestLimb); Assert.Equal(0f, s.Recovery);
        Assert.False(RS.Read(true, true, 1, 1, new List<RS.Part?> { P(1, 0, 0) }, out _, out e));
        Assert.Equal("anatomy part 0/1 has no valid maximum health", e);
        Assert.False(RS.Read(true, true, 1, 1, new List<RS.Part?> { P(1, 0, 1), P(1, 0, 1), P(1, 0, float.NaN) }, out _, out e));
        Assert.Equal("anatomy part 2/3 has no valid maximum health", e);
    }
}
