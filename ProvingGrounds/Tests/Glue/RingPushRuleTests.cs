using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>TEST ONLY <c>pg ring push</c> (task 4.8): with a bearing, the guard's return spot on it must be walkable only when the push lands
/// where the guard acts (the near band or outside, ClassifyRing), so a ring check exercises a real return; a push inside the near line (an
/// ingress walk-in check) is never returned or stepped by the guard and needs no return spot (Core/Glue/RingPushRule.cs).</summary>
public class RingPushRuleTests
{
    // the shipped radii x scale: full arena (x1.0: 145 / 147.5) and the small ring ring.sh uses (x0.6: 87 / 88.5)
    [Theory]
    [InlineData(100f, 1f, 145f, 147.5f, false)]   // arena-ingress (b): inside the near line
    [InlineData(120f, 1f, 145f, 147.5f, false)]
    [InlineData(144.9f, 1f, 145f, 147.5f, false)]
    [InlineData(145f, 1f, 145f, 147.5f, true)]    // ClassifyRing: >= near is the near band
    [InlineData(146f, 1f, 145f, 147.5f, true)]
    [InlineData(147.5f, 1f, 145f, 147.5f, true)]  // >= out is outside
    [InlineData(200f, 1f, 145f, 147.5f, true)]
    [InlineData(120f, 0.6f, 87f, 88.5f, true)]    // ring.sh (1): beyond out
    [InlineData(87.2f, 0.6f, 87f, 88.5f, true)]   // ring.sh (1): the near band
    [InlineData(88.4f, 0.6f, 87f, 88.5f, true)]   // just below the small ring's out radius: still the near band
    [InlineData(86.9f, 0.6f, 87f, 88.5f, false)]
    [InlineData(150f, 0.6f, 87f, 88.5f, true)]
    public void Needs_a_return_spot_only_where_the_guard_acts(float distance, float ringScale, float near, float outR, bool expected) =>
        Assert.Equal(expected, RingPushRule.NeedsWalkableReturn(bearingGiven: true, ringScale, distance, near, outR));

    [Fact]
    public void No_bearing_never_needs_one() =>
        Assert.False(RingPushRule.NeedsWalkableReturn(bearingGiven: false, ringScale: 1f, 200f, 145f, 147.5f));

    [Fact]
    public void An_inert_ring_never_needs_one() =>   // ring.sh (2): scale 0, the push 150 / 120 with a bearing
        Assert.False(RingPushRule.NeedsWalkableReturn(bearingGiven: true, ringScale: 0f, 150f, 0f, 0f));

    // the push's floor hints, in order: the site centre's height first (the ring's floor for a site set around fighters: ring.sh's
    // behaviour since 3.4), then the character's own (a building's origin can lie under its floor: Scratch's town Arena, 25-48 units)
    [Fact]
    public void Floor_hints_are_the_centre_then_the_character() =>
        Assert.Equal(new[] { 1052.7f, 1088.6f }, RingPushRule.FloorHints(1052.7f, 1088.6f));

    [Fact]
    public void One_hint_when_they_are_the_same_height() =>
        Assert.Equal(new[] { 1009.4f }, RingPushRule.FloorHints(1009.4f, 1009.4f));

    [Fact]
    public void The_centre_comes_first_even_when_it_is_above_the_character() =>
        Assert.Equal(new[] { 1100.5f, 1088.6f }, RingPushRule.FloorHints(1100.5f, 1088.6f));

    [Fact]
    public void Minus_zero_and_zero_are_one_hint() =>
        Assert.Single(RingPushRule.FloorHints(-0f, 0f));

    // the push's choice (RingPushRule.ChoosePushFloor): the first hint the pushed spot stands at, then, when the guard acts there, the return
    // spot from the pushed height (the ground found there, not the hint)
    static RingPushRule.StandAt Stands(params (float hint, float ground)[] ok) => (float h, out float y) =>
    {
        foreach (var (hint, ground) in ok) if (hint == h) { y = ground; return true; }
        y = float.NaN;
        return false;
    };

    [Fact]
    public void The_centre_height_wins_when_the_spot_stands_there()
    {
        var tried = new List<float>();
        RingPushRule.StandAt push = (float h, out float y) => { tried.Add(h); return Stands((1052.7f, 1053f), (1088.6f, 1089f))(h, out y); };
        Assert.Equal(RingPushRule.PushRefusal.None,
            RingPushRule.ChoosePushFloor(1052.7f, 1088.6f, needsReturn: false, push, Stands(), out float hint));
        Assert.Equal(1052.7f, hint);
        Assert.Equal(new[] { 1052.7f }, tried);
    }

    [Fact]
    public void The_character_height_when_nothing_stands_at_the_centre()   // Scratch's town Arena 183, task 4.8
    {
        Assert.Equal(RingPushRule.PushRefusal.None,
            RingPushRule.ChoosePushFloor(1052.7f, 1088.6f, needsReturn: false, Stands((1088.6f, 1089.2f)), Stands(), out float hint));
        Assert.Equal(1088.6f, hint);
    }

    [Fact]
    public void No_hint_stands_refuses_the_spot() =>
        Assert.Equal(RingPushRule.PushRefusal.SpotNotWalkable,
            RingPushRule.ChoosePushFloor(1052.7f, 1088.6f, needsReturn: true, Stands(), Stands((1089.2f, 1089f)), out _));

    [Fact]
    public void The_return_spot_is_tried_from_the_pushed_ground_height()
    {
        var asked = new List<float>();
        RingPushRule.StandAt ret = (float h, out float y) => { asked.Add(h); y = h; return true; };
        Assert.Equal(RingPushRule.PushRefusal.None,
            RingPushRule.ChoosePushFloor(1052.7f, 1088.6f, needsReturn: true, Stands((1088.6f, 1091.3f)), ret, out float hint));
        Assert.Equal(1088.6f, hint);
        Assert.Equal(new[] { 1091.3f }, asked);
    }

    [Fact]
    public void A_return_spot_that_does_not_stand_refuses_the_push() =>
        Assert.Equal(RingPushRule.PushRefusal.ReturnSpotNotWalkable,
            RingPushRule.ChoosePushFloor(1052.7f, 1088.6f, needsReturn: true, Stands((1052.7f, 1053f)), Stands(), out _));

    [Fact]
    public void No_return_spot_asked_where_the_guard_does_not_act()
    {
        bool asked = false;
        RingPushRule.StandAt ret = (float h, out float y) => { asked = true; y = 0; return false; };
        Assert.Equal(RingPushRule.PushRefusal.None,
            RingPushRule.ChoosePushFloor(1052.7f, 1088.6f, needsReturn: false, Stands((1052.7f, 1053f)), ret, out _));
        Assert.False(asked);
    }
}
