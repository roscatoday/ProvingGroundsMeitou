using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.4a: the world's one owner-counted hold registry (Core/Glue/HoldRegistry.cs): two owners, release order, world change, a stale id.</summary>
public class HoldRegistryTests
{
    /// <summary>The engine half: who is Engaged now, every call in order, and which ids are stale.</summary>
    sealed class FakeEngine : IHoldEngine<int>
    {
        public readonly HashSet<int> Engaged = [];
        public readonly HashSet<int> Stale = [];
        public readonly List<string> Calls = [];
        public bool ThrowOnRelease;
        public bool Hold(int id)
        {
            Calls.Add("hold " + id);
            if (Stale.Contains(id)) return false;
            Engaged.Add(id);
            return true;
        }
        public bool Reassert(int id)
        {
            Calls.Add("reassert " + id);
            if (Stale.Contains(id)) return false;
            Engaged.Add(id);
            return true;
        }
        public void Release(int id)
        {
            Calls.Add("release " + id);
            if (ThrowOnRelease) throw new InvalidOperationException("boom");
            Engaged.Remove(id);
        }
    }

    [Fact]
    public void First_owner_holds_by_the_recipe_a_second_joins_with_a_reassert()
    {
        var e = new FakeEngine();
        var r = new HoldRegistry<int>();
        Assert.True(r.Hold(e, 7, HoldOwner.TownFighter));
        Assert.True(r.Hold(e, 7, HoldOwner.AftercarePatient));
        Assert.Equal(["hold 7", "reassert 7"], e.Calls);
        Assert.Equal(HoldOwner.TownFighter | HoldOwner.AftercarePatient, r.OwnersOf(7));
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void Released_only_when_the_last_owner_lets_go_in_either_order()
    {
        foreach (bool fighterFirst in new[] { true, false })
        {
            var e = new FakeEngine();
            var r = new HoldRegistry<int>();
            r.Hold(e, 7, HoldOwner.TownFighter);
            r.Hold(e, 7, HoldOwner.AftercareMedic);
            r.Release(e, 7, fighterFirst ? HoldOwner.TownFighter : HoldOwner.AftercareMedic);
            Assert.Contains(7, e.Engaged);
            Assert.True(r.IsHeld(7));
            Assert.DoesNotContain("release 7", e.Calls);
            r.Release(e, 7, fighterFirst ? HoldOwner.AftercareMedic : HoldOwner.TownFighter);
            Assert.DoesNotContain(7, e.Engaged);
            Assert.False(r.IsHeld(7));
            Assert.Single(e.Calls, c => c == "release 7");
        }
    }

    [Fact]
    public void An_owner_that_does_not_hold_releases_nothing()
    {
        var e = new FakeEngine();
        var r = new HoldRegistry<int>();
        r.Hold(e, 7, HoldOwner.Bench);
        r.Release(e, 7, HoldOwner.TownFighter);
        r.Release(e, 8, HoldOwner.Bench);
        Assert.Contains(7, e.Engaged);
        Assert.Equal(["hold 7"], e.Calls);
    }

    [Fact]
    public void ReleaseAll_lets_go_of_one_owner_only()
    {
        var e = new FakeEngine();
        var r = new HoldRegistry<int>();
        r.Hold(e, 1, HoldOwner.TownFighter);
        r.Hold(e, 2, HoldOwner.TownFighter);
        r.Hold(e, 2, HoldOwner.AftercarePatient);
        r.Hold(e, 3, HoldOwner.AftercareMedic);
        r.ReleaseAll(e, HoldOwner.TownFighter);
        Assert.Equal([2, 3], e.Engaged.Order());
        Assert.Equal(HoldOwner.AftercarePatient, r.OwnersOf(2));
        Assert.False(r.IsHeld(1));
        Assert.True(r.IsHeldBy(3, HoldOwner.AftercareMedic));
    }

    [Fact]
    public void ReassertAll_reasserts_everyone_and_drops_a_stale_id_without_a_release()
    {
        var e = new FakeEngine();
        var r = new HoldRegistry<int>();
        r.Hold(e, 1, HoldOwner.Bench);
        r.Hold(e, 2, HoldOwner.External);
        e.Engaged.Clear();   // a save cleared Engaged (CAP 19)
        e.Stale.Add(2);      // and 2 is gone
        r.ReassertAll(e);
        Assert.Equal([1], e.Engaged);
        Assert.True(r.IsHeld(1));
        Assert.False(r.IsHeld(2));
        Assert.DoesNotContain("release 2", e.Calls);
    }

    [Fact]
    public void A_stale_first_hold_records_nothing()
    {
        var e = new FakeEngine();
        e.Stale.Add(5);
        var r = new HoldRegistry<int>();
        Assert.False(r.Hold(e, 5, HoldOwner.Spectator));
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void World_change_forgets_everything_without_touching_an_actor()
    {
        var e = new FakeEngine();
        var r = new HoldRegistry<int>();
        r.Hold(e, 1, HoldOwner.TownFighter);
        r.Hold(e, 2, HoldOwner.Bench);
        e.Calls.Clear();
        r.Clear();
        Assert.Equal(0, r.Count);
        Assert.Empty(e.Calls);
        r.ReassertAll(e);
        Assert.Empty(e.Calls);   // the new world never re-asserts the old world's holds
    }

    [Fact]
    public void A_release_that_throws_is_logged_once_and_leaves_no_ownerless_entry()
    {
        var e = new FakeEngine { ThrowOnRelease = true };
        var logs = new List<string>();
        var r = new HoldRegistry<int> { FailureLog = logs.Add };
        r.Hold(e, 1, HoldOwner.TownFighter);
        r.Hold(e, 2, HoldOwner.TownFighter);
        r.ReleaseAll(e, HoldOwner.TownFighter);
        Assert.Equal(0, r.Count);
        Assert.Single(logs);
        e.Calls.Clear();
        r.ReassertAll(e);
        Assert.Empty(e.Calls);
    }

    [Fact]
    public void A_second_owner_on_a_stale_id_is_refused_and_the_entry_dropped_without_a_release()
    {
        var e = new FakeEngine();
        var r = new HoldRegistry<int>();
        Assert.True(r.Hold(e, 7, HoldOwner.TownFighter));
        e.Stale.Add(7);
        Assert.False(r.Hold(e, 7, HoldOwner.AftercarePatient));
        Assert.Equal(0, r.Count);
        Assert.DoesNotContain("release 7", e.Calls);
    }

    [Fact]
    public void A_first_hold_that_throws_is_refused_and_logged_once_and_Clear_lets_the_next_world_log_again()
    {
        var e = new ThrowingEngine();
        var logs = new List<string>();
        var r = new HoldRegistry<int> { FailureLog = logs.Add };
        Assert.False(r.Hold(e, 1, HoldOwner.Bench));
        Assert.False(r.Hold(e, 2, HoldOwner.Bench));
        Assert.Equal(0, r.Count);
        Assert.Single(logs);
        r.Clear();
        Assert.False(r.Hold(e, 3, HoldOwner.Bench));
        Assert.Equal(2, logs.Count);
    }

    sealed class ThrowingEngine : IHoldEngine<int>
    {
        public bool Hold(int id) => throw new MissingMemberException("Engaged");
        public bool Reassert(int id) => throw new MissingMemberException("Engaged");
        public void Release(int id) => throw new MissingMemberException("Engaged");
    }
}
