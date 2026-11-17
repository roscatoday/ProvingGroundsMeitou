using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaLedger;
using Snapshot = ProvingGrounds.Core.ArenaPersistence.Snapshot;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaLedger.cs (ArenaLedger.h:1-124), one per branch the random golden (Tests/Golden/LedgerGoldenTests.cs,
// 800 sequences against the real header) may hit rarely: the ~0ULL counter limits, every Cancel validation, the by-value copies.
public class ArenaLedgerTests
{
    static Snapshot World(params (string Id, int Marks)[] rows)
    {
        var s = new Snapshot { SaveKey = "k" };
        foreach (var r in rows) s.Fighters.Progression.Add(new LeaderboardData.Progression { Id = r.Id, Name = "N" + r.Id, Marks = r.Marks });
        return s;
    }

    static Ledger Active(params (string Id, int Marks)[] rows) { var l = new Ledger(); l.Activate(World(rows)); return l; }

    [Fact]
    public void Purchase_default_matches_the_cpp_constructor_and_default_struct_does_not()
    {
        var p = new Purchase();
        Assert.True(p.Cost == 0 && p.PreviousTier == -1 && p.NextTier == -1 && p.Reward == "" && p.Unlock == "" && !p.RequireNewUnlock);
        var q = new Purchase(7);
        Assert.True(q.Cost == 7 && q.PreviousTier == -1 && q.NextTier == -1);
        Purchase d = default;
        Assert.True(d.PreviousTier == 0 && d.Reward == null, "default(Purchase) is zeroed, unlike the C++ default (documented)");
        Reservation r = default;
        Assert.True(r.Owner == null && r.Epoch == 0 && r.Sequence == 0, "default(Reservation) is the C++ default");
    }

    [Fact]
    public void New_ledger_is_not_ready_and_refuses_everything()
    {
        var l = new Ledger();
        Snapshot o = new Snapshot { SaveKey = "pre" };
        Assert.False(l.Ready()); Assert.False(l.Complete()); Assert.False(l.Capture(o)); Assert.Equal("pre", o.SaveKey);
        Assert.Null(l.Find("a")); Assert.Equal(0, l.Marks("a")); Assert.Equal(-1, l.Tier("a", "r"));
        Assert.False(l.Spend("a", "", 0)); Assert.False(l.Refund("a", "", 0));
        var t = new Reservation();
        Assert.False(l.Reserve("a", "", new Purchase(), ref t)); Assert.Null(t.Owner);
    }

    [Fact]
    public void Activate_and_Capture_copy_the_snapshot_and_State_and_Find_do_not()
    {
        Snapshot src = World(("a", 5));
        var l = new Ledger(); l.Activate(src);
        src.Fighters.Progression[0].Marks = 99; src.Fighters.FactionUnlocks.Add("x");
        Assert.Equal(5, l.Marks("a")); Assert.Empty(l.State().Fighters.FactionUnlocks);

        Snapshot o = new Snapshot();
        Assert.True(l.Capture(o));
        o.Fighters.Progression[0].Marks = -1;
        Assert.Equal(5, l.Marks("a"));

        l.Find("a")!.Marks = 12; Assert.Equal(12, l.Marks("a"));
        l.State().Fighters.Progression[0].Marks = 13; Assert.Equal(13, l.Find("a")!.Marks);
        Assert.Same(l.State(), l.State());
    }

    [Fact]
    public void State_and_its_parts_keep_their_identity_across_Activate_Block_and_Capture()
    {
        // LeaderboardStore.cpp:30-34 binds g_data / g_townChallenges / g_bookieCredit / g_townDiagnostic to g_ledger.State() once
        var l = new Ledger();
        Snapshot state = l.State();
        LeaderboardData.Collections fighters = state.Fighters;
        TownChallengePolicy.Card challenges = state.Challenges;
        ArenaPersistence.PlannedNpcBout bout = state.PlannedNpc;
        List<LeaderboardData.Progression> rows = fighters.Progression;
        Snapshot src = World(("a", 5));
        src.BookieCredit = 9; src.Challenges.Division = 3; src.PlannedNpc.TeamA.Add("x");
        l.Activate(src);
        Assert.True(ReferenceEquals(state, l.State()) && ReferenceEquals(fighters, l.State().Fighters) && ReferenceEquals(challenges, l.State().Challenges)
            && ReferenceEquals(bout, l.State().PlannedNpc) && ReferenceEquals(rows, l.State().Fighters.Progression));
        Assert.True(fighters.Progression[0].Marks == 5 && challenges.Division == 3 && state.BookieCredit == 9 && bout.TeamA.SequenceEqual(new[] { "x" }));
        Assert.False(ReferenceEquals(src.Fighters.Progression[0], rows[0]), "rows are copies");
        l.Block();
        Assert.True(ReferenceEquals(state, l.State()) && ReferenceEquals(fighters, l.State().Fighters) && ReferenceEquals(challenges, l.State().Challenges)
            && ReferenceEquals(bout, l.State().PlannedNpc));
        Assert.True(rows.Count == 0 && challenges.Division == 0 && state.BookieCredit == 0 && bout.TeamA.Count == 0 && state.SaveKey == "");
        l.Activate(src);
        var output = new Snapshot { SaveKey = "pre" };
        LeaderboardData.Collections outFighters = output.Fighters;
        Assert.True(l.Capture(output));
        Assert.True(ReferenceEquals(outFighters, output.Fighters) && output.SaveKey == "k" && output.Fighters.Progression[0].Marks == 5);
        Assert.False(ReferenceEquals(output.Fighters.Progression[0], rows[0]));
    }

    [Fact]
    public void Find_takes_the_first_row_and_refuses_an_empty_id()
    {
        Ledger l = Active(("a", 1), ("a", 2), ("", 3));
        Assert.Equal(1, l.Marks("a")); Assert.Null(l.Find(""));
    }

    [Fact]
    public void Spend_and_Refund_edges()
    {
        Ledger l = Active(("a", int.MaxValue - 1));
        Assert.False(l.Spend("a", "x", -1)); Assert.False(l.Spend("", "x", 0)); Assert.False(l.Spend("a", "x", int.MaxValue));
        Assert.True(l.Refund("a", "Named", 1)); Assert.Equal(int.MaxValue, l.Marks("a")); Assert.Equal("Named", l.Find("a")!.Name);
        Assert.False(l.Refund("a", "", 1)); Assert.True(l.Refund("a", "", 0)); Assert.False(l.Refund("a", "", -1));
        Assert.True(l.Spend("b", "Bo", 0), "a zero spend on an unknown fighter creates the row, as in the C++");
        Assert.Equal("Bo", l.Find("b")!.Name);
        Assert.True(l.Refund("c", "", 4)); Assert.Equal(4, l.Marks("c"));
    }

    [Fact]
    public void BeginTier_creates_the_row_even_when_the_tier_is_refused()
    {
        Ledger l = Active();
        Assert.False(l.BeginTier("a", "Al", 0, "r1", 3, 4), "previous 3 on a missing tier is refused");
        Assert.NotNull(l.Find("a"));
        Assert.False(l.BeginTier("a", "Al", 0, "", -1, 0)); Assert.False(l.BeginTier("a", "Al", -1, "r1", -1, 0));
        Assert.True(l.BeginTier("a", "Al", 0, "r1", -1, 0)); Assert.Equal(0, l.Tier("a", "r1"));
    }

    [Fact]
    public void RollbackTier_refuses_a_lower_tier_and_an_overflowing_refund()
    {
        Ledger l = Active(("a", 10));
        Assert.True(l.BeginTier("a", "", 4, "r1", -1, 0));
        Assert.False(l.RollbackTier("a", 1, "r1", 0), "current tier 0 is not above previous 0");
        Assert.True(l.RollbackTier("a", 4, "r1", -1)); Assert.Equal(10, l.Marks("a")); Assert.Equal(-1, l.Tier("a", "r1"));
        l.Find("a")!.Marks = int.MaxValue;
        Assert.True(l.BeginTier("a", "", 0, "r1", -1, 0));
        Assert.False(l.RollbackTier("a", 1, "r1", -1));
        Assert.False(l.RollbackTier("x", 0, "r1", -1)); Assert.False(l.RollbackTier("a", -1, "r1", -1));
    }

    [Fact]
    public void Unlocks_are_global_and_refused_twice()
    {
        Ledger l = Active(("a", 3));
        Assert.False(l.BeginUnlock("a", "", 1, ""));
        Assert.True(l.BeginUnlock("a", "", 1, "u")); Assert.False(l.BeginUnlock("a", "", 1, "u"));
        Assert.False(l.RollbackUnlock("a", "", 1, "v"));
        Assert.True(l.RollbackUnlock("a", "", 1, "u")); Assert.Equal(3, l.Marks("a")); Assert.Empty(l.State().Fighters.FactionUnlocks);
    }

    [Fact]
    public void Reserve_then_Accept_keeps_the_change_and_refuses_nesting()
    {
        Ledger l = Active(("a", 10));
        var p = new Purchase(3) { Reward = "r1", PreviousTier = -1, NextTier = 0, Unlock = "u" };
        var t = new Reservation();
        Assert.True(l.Reserve("a", "", p, ref t));
        Assert.True(ReferenceEquals(t.Owner, l) && t.Epoch == 1 && t.Sequence == 1);
        Assert.True(l.Pending(t));
        var t2 = new Reservation();
        Assert.False(l.Reserve("a", "", new Purchase(0), ref t2), "nested reservations are refused");
        Snapshot o = new Snapshot { SaveKey = "pre" };
        Assert.False(l.Capture(o), "no capture while reserved"); Assert.Equal("pre", o.SaveKey);
        Assert.True(l.Accept(t)); Assert.False(l.Accept(t)); Assert.False(l.Pending(t));
        Assert.True(l.Marks("a") == 7 && l.Tier("a", "r1") == 0 && l.State().Fighters.FactionUnlocks.SequenceEqual(new[] { "u" }));
    }

    [Fact]
    public void Reserve_RequireNewUnlock_and_an_existing_unlock()
    {
        Ledger l = Active(("a", 10));
        l.State().Fighters.FactionUnlocks.Add("u");
        var t = new Reservation();
        Assert.False(l.Reserve("a", "", new Purchase(1) { RequireNewUnlock = true }, ref t), "required unlock empty");
        Assert.False(l.Reserve("a", "", new Purchase(1) { RequireNewUnlock = true, Unlock = "u" }, ref t), "required unlock already held");
        Assert.True(l.Reserve("a", "", new Purchase(1) { Unlock = "u" }, ref t), "an unlock already held is not required to be new");
        Assert.True(l.Cancel(t), "cancel does not remove an unlock the reservation did not add");
        Assert.True(l.Marks("a") == 10 && l.State().Fighters.FactionUnlocks.SequenceEqual(new[] { "u" }));
    }

    [Fact]
    public void Cancel_undoes_everything_once()
    {
        Ledger l = Active(("a", 10));
        l.Find("a")!.RewardTiers.Add(new RewardProgression.TierEntry("r1", 1));
        var p = new Purchase(4) { Reward = "r1", PreviousTier = 1, NextTier = 2, Unlock = "u" };
        var t = new Reservation();
        Assert.True(l.Reserve("a", "", p, ref t));
        Assert.True(l.Cancel(t)); Assert.False(l.Cancel(t));
        Assert.True(l.Marks("a") == 10 && l.Tier("a", "r1") == 1 && l.State().Fighters.FactionUnlocks.Count == 0);
    }

    [Theory]
    [InlineData("row removed")]
    [InlineData("marks overflow")]
    [InlineData("tier changed")]
    [InlineData("unlock removed")]
    public void Cancel_refuses_after_an_unrelated_edit_and_stays_pending(string edit)
    {
        Ledger l = Active(("a", 10));
        var p = new Purchase(4) { Reward = "r1", PreviousTier = -1, NextTier = 0, Unlock = "u" };
        var t = new Reservation();
        Assert.True(l.Reserve("a", "", p, ref t));
        switch (edit)
        {
            case "row removed": l.State().Fighters.Progression.Clear(); break;
            case "marks overflow": l.Find("a")!.Marks = int.MaxValue - 3; break;
            case "tier changed": RewardProgression.RestoreHighestTier(l.Find("a")!.RewardTiers, "r1", 5); break;
            case "unlock removed": l.State().Fighters.FactionUnlocks.Clear(); break;
        }
        Assert.False(l.Cancel(t), edit);
        Assert.True(l.Pending(t), edit + ": a refused cancel leaves the reservation pending");
        Assert.True(l.Accept(t));
    }

    [Fact]
    public void A_new_world_invalidates_tokens_and_a_foreign_token_never_matches()
    {
        Ledger l = Active(("a", 10)), other = Active(("a", 10));
        var t = new Reservation();
        Assert.True(l.Reserve("a", "", new Purchase(1), ref t));
        Assert.False(other.Pending(t)); Assert.False(other.Cancel(t)); Assert.False(other.Accept(t));
        l.Activate(World(("a", 10)));
        Assert.False(l.Pending(t), "epoch moved");
        Assert.Equal(2UL, l.EpochForTest); Assert.False(l.ReservedForTest);
        l.Block();
        Assert.False(l.Ready()); Assert.Equal(3UL, l.EpochForTest); Assert.Empty(l.State().Fighters.Progression);
        var t2 = new Reservation();
        Assert.False(l.Reserve("a", "", new Purchase(0), ref t2));
    }

    [Fact]
    public void Counters_stop_at_their_limit()
    {
        var l = new Ledger();
        l.SetCountersForTest(~0UL - 1, ~0UL - 1);
        l.Activate(World(("a", 10)));
        Assert.True(!l.Ready() && l.EpochForTest == ~0UL, "epoch reaches ~0 on this world, and that world is NOT ready");
        l.SetCountersForTest(~0UL - 2, ~0UL - 1);
        l.Activate(World(("a", 10)));
        Assert.True(l.Ready() && l.EpochForTest == ~0UL - 1);
        var t = new Reservation();
        Assert.True(l.Reserve("a", "", new Purchase(1), ref t)); Assert.Equal(~0UL, t.Sequence);
        Assert.True(l.Accept(t));
        var t2 = new Reservation();
        Assert.False(l.Reserve("a", "", new Purchase(1), ref t2), "sequence at ~0 refuses further reservations");
        l.Activate(World(("a", 10)));
        Assert.False(l.Ready(), "epoch now ~0: Activate leaves the ledger blocked");
        l.Activate(World(("a", 10)));
        Assert.True(l.EpochForTest == ~0UL && !l.Ready(), "EndWorld does not wrap the epoch");
    }
}
