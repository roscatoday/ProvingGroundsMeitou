using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The Registry / Banner / Leaderboard approach of ArenaIngress.cpp (task 4.2) over the fake host: the refusals, the bind, the targets
/// (position marker or the local use node pushed outward), the 0.35 s first move and 0.40 s re-issue, the three open conditions (within 60;
/// within 120 and slower than 0.35 for 0.8 s; 20 s), open at once without binding while a match / ingress / walk-in runs, the cancels.</summary>
public class ArenaApproachTests
{
    const float Yaw = 0.4f;

    static ArenaBuilding Building(string record, float x, float z, bool finished = true, string key = "1-reg") =>
        new(ArenaBuildingSource.Player, key, record, null, finished, new Vector3(x, 0f, z), Yaw);

    static ArenaBuilding Registry(bool finished = true, string key = "1-reg") => Building(PgRecords.Registry, 1000f, 2000f, finished, key);
    static ArenaBuilding Banner() => Building(PgRecords.Banner, 1000f, 2000f, true, "2-banner");
    static ArenaBuilding Board(string record = PgRecords.Leaderboard) => Building(record, 1000f, 2000f, true, "3-board");

    static (IngressFakeHost H, ArenaIngressLogic<int> L) Setup(params ArenaBuilding[] buildings)
    {
        var h = new IngressFakeHost();
        foreach (var b in buildings) h.Buildings[b.Ref] = b;
        h.Add(0, 0f, 0f);
        h.Add(1, 20f, 0f);
        h.Selected = [0, 1];
        return (h, new ArenaIngressLogic<int>(-1));
    }

    static void Tick(IngressFakeHost h, ArenaIngressLogic<int> l, uint ms) { h.Ms += ms; l.Tick(h); }

    static Vec3 Fallback(ArenaBuilding b) =>
        ArenaFormation.RegistryApproachPoint(new Vec3(b.Position.X, b.Position.Y, b.Position.Z), Quat.FromYaw(b.Yaw), null);

    static void ArriveAll(IngressFakeHost h, ArenaIngressLogic<int> l)
    {
        for (int i = 0; i < l.Fighters.Count; i++) h.Pos[l.Fighters[i]] = l.Targets[i];
    }

    // ---------- BeginApproachForUI ----------

    [Fact]
    public void Begin_RefusesAnUnfinishedBuilding()
    {
        var reg = Registry(finished: false);
        var (h, l) = Setup(reg);
        Assert.False(l.BeginApproachForUI(h, reg));
        Assert.Equal("Interact building not finished", l.GetStatus());
        Assert.False(l.IsPending());
        Assert.Null(l.BoundRegistryRef);
        Assert.False(l.BeginApproachForUI(h, null));
        Assert.Equal("Interact building not finished", l.GetStatus());
    }

    [Fact]
    public void Begin_NeedsASelection()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        h.Selected = [];
        Assert.False(l.BeginApproachForUI(h, reg));
        Assert.Equal("No characters selected", l.GetStatus());
        Assert.False(l.IsPending());
        Assert.Null(l.BoundRegistryRef);   // the C++ binds after the selection test (:1197-1207)
    }

    [Fact]
    public void Begin_Registry_BindsAndWalksTheSelection()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.True(l.IsPending());
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.OpenUI, l.Kind);
        Assert.Equal("Walking to registry...", l.GetStatus());
        Assert.Equal(reg.Ref, l.BoundRegistryRef);
        Assert.Null(l.BoundLeaderboardRef);
        Assert.Equal(ArenaLocationMode.Arena, l.GetLocationMode());
        Assert.Equal([0, 1], l.Fighters);
        Assert.Equal(0, l.GetUiOpener(h));
        var want = Fallback(reg);
        Assert.Equal([want, want], l.Targets);
        Assert.Empty(h.Moves);   // no move on the click's frame (:1220-1221)
    }

    [Fact]
    public void Begin_UsesThePositionMarker_WhenAwayFromTheOrigin()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        h.Markers[reg.Ref] = new Vec3(1010f, 0f, 2030f);
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.Equal(new Vec3(1010f, 0f, 2030f), l.Targets[0]);
        // a marker within 1 unit of the origin is "stuck at the building origin": the use node fallback
        h.Markers[reg.Ref] = new Vec3(1000.5f, 0f, 2000f);
        l.AbandonWorldState(h);
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.Equal(Fallback(reg), l.Targets[0]);
    }

    [Fact]
    public void Begin_SkipsDeadAndMissingSelected()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        h.Dead.Add(0);
        h.Selected = [0, 5, 1];
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.Equal([1], l.Fighters);
        Assert.Equal(1, l.GetUiOpener(h));
    }

    [Fact]
    public void Begin_Banner_SwitchesTheLocationToBanner()
    {
        var banner = Banner();
        var (h, l) = Setup(banner);
        Assert.True(l.BeginApproachForUI(h, banner));
        Assert.Equal("Walking to banner...", l.GetStatus());
        Assert.Equal(ArenaLocationMode.Banner, l.GetLocationMode());
        Assert.Equal(banner.Ref, l.BoundRegistryRef);
    }

    [Fact]
    public void Begin_Leaderboard_BindsTheBoardNotTheRegistry()
    {
        var reg = Registry();
        var board = Board();
        var (h, l) = Setup(reg, board);
        l.BindRegistry(reg);
        Assert.True(l.BeginApproachForUI(h, board));
        Assert.Equal("Walking to leaderboard...", l.GetStatus());
        Assert.Equal(board.Ref, l.BoundLeaderboardRef);
        Assert.Null(l.BoundRegistryRef);   // BindLeaderboardBuilding clears the Registry (:545)
    }

    [Fact]
    public void Begin_AlreadyThere_OpensAtOnce()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        var at = Fallback(reg);
        h.Pos[0] = at; h.Pos[1] = new Vec3(at.X + 50f, at.Y, at.Z);
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.False(l.IsPending());
        Assert.Equal([(reg.Ref, true)], h.ArenaShown);
    }

    [Fact]
    public void Begin_SameBuildingAgain_KeepsTheApproach_AnotherRestarts()
    {
        var reg = Registry();
        var other = Registry(key: "9-other");
        var (h, l) = Setup(reg, other);
        Assert.True(l.BeginApproachForUI(h, reg));
        Tick(h, l, 500);
        int moves = h.Moves.Count;
        Assert.True(l.BeginApproachForUI(h, reg));   // a multi-select "use" calls once per character (:1186)
        Assert.Equal(0.5f, l.ElapsedSec, 3);
        Assert.Equal(moves, h.Moves.Count);
        Assert.True(l.BeginApproachForUI(h, other));
        Assert.Equal(other.Ref, l.BoundRegistryRef);
        Assert.Equal(0f, l.ElapsedSec);
        Assert.Contains("removejob:0", h.Calls);   // ClearPendingState of the first approach
    }

    [Fact]
    public void Begin_WhileAMatchOrIngressOrWalkInRuns_OpensAtOnceWithoutBinding()
    {
        var reg = Registry();
        var board = Board(PgRecords.TownLeaderboard);
        var (h, l) = Setup(reg, board);
        h.SparActive = true;
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.Equal([(reg.Ref, false)], h.ArenaShown);
        Assert.False(l.IsPending());
        Assert.Null(l.BoundRegistryRef);
        Assert.True(l.BeginApproachForUI(h, board));
        Assert.Equal([(LeaderboardData.Kind.Town, board.Ref)], h.BoardShown);
        Assert.Null(l.BoundLeaderboardRef);

        // a staged fight (formation) pending
        h.SparActive = false;
        h.ArenaShown.Clear();
        h.AddSite(IngressFakeHost.Arena(3000f, 0f));
        h.Add(2, 0f, 50f); h.Add(3, 0f, 70f);
        Assert.True(l.Begin(h, MatchMode.TeamAvB, [2, 3], [MatchTeam.TeamA, MatchTeam.TeamB], 2));
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.Equal([(reg.Ref, false)], h.ArenaShown);
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);   // the staging goes on
        l.Cancel(h);

        // a walk-in pending
        h.ArenaShown.Clear();
        Assert.True(l.BeginWalkIn(h, 2, MatchTeam.TeamA));
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.Equal([(reg.Ref, false)], h.ArenaShown);
        Assert.False(l.IsPending());
    }

    // ---------- the approach step of Tick ----------

    [Fact]
    public void Tick_FirstMoveAfter035s_ThenEvery040s()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        Tick(h, l, 340);
        Assert.Empty(h.Moves);
        Tick(h, l, 20);   // 0.36 s
        Assert.Equal([0, 1], h.Moves.Select(m => m.Id));
        Assert.All(h.Moves, m => Assert.Equal(Fallback(reg), m.At));
        Tick(h, l, 390);  // 0.75 s
        Assert.Equal(2, h.Moves.Count);
        Tick(h, l, 20);   // 0.77 s >= 0.36 + 0.40
        Assert.Equal(4, h.Moves.Count);
        Assert.True(l.IsPending());
    }

    [Fact]
    public void Tick_OpensWhenEveryWalkerIsWithin60()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        var t = l.Targets[0];
        h.Pos[0] = new Vec3(t.X + 59f, t.Y, t.Z);
        h.Pos[1] = new Vec3(t.X + 61f, t.Y, t.Z);
        Tick(h, l, 100);
        Assert.True(l.IsPending());
        h.Pos[1] = new Vec3(t.X, t.Y + 500f, t.Z + 59f);   // on the ground only (height ignored)
        Tick(h, l, 100);
        Assert.False(l.IsPending());
        Assert.Equal([(reg.Ref, true)], h.ArenaShown);
        Assert.Contains("removejob:0", h.Calls);
    }

    [Fact]
    public void Tick_OpensWhenWithin120AndSettledFor08s()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        var t = l.Targets[0];
        h.Pos[0] = new Vec3(t.X + 100f, t.Y, t.Z);
        h.Pos[1] = new Vec3(t.X - 100f, t.Y, t.Z);
        h.Speeds[1] = 0.36f;
        Tick(h, l, 500);
        Tick(h, l, 500);
        Assert.Equal(0f, l.SettleHoldSec);   // one walker still faster than 0.35
        h.Speeds[1] = 0.35f;
        Tick(h, l, 500);
        Assert.Equal(0.5f, l.SettleHoldSec, 3);
        Assert.True(l.IsPending());
        h.Pos[1] = new Vec3(t.X - 121f, t.Y, t.Z);   // out of 120: the hold restarts
        Tick(h, l, 100);
        Assert.Equal(0f, l.SettleHoldSec);
        h.Pos[1] = new Vec3(t.X - 100f, t.Y, t.Z);
        Tick(h, l, 500);
        Assert.True(l.IsPending());
        Tick(h, l, 350);   // 0.85 s >= 0.8
        Assert.False(l.IsPending());
        Assert.Equal([(reg.Ref, true)], h.ArenaShown);
    }

    [Fact]
    public void Tick_OpensAfter20s()
    {
        var board = Board();
        var (h, l) = Setup(board);
        Assert.True(l.BeginApproachForUI(h, board));
        for (int i = 0; i < 19; i++) Tick(h, l, 1000);
        Assert.True(l.IsPending());
        Tick(h, l, 1000);
        Assert.False(l.IsPending());
        Assert.Equal([(LeaderboardData.Kind.Player, board.Ref)], h.BoardShown);
        Assert.Equal("Player leaderboard open", l.GetStatus());
    }

    [Fact]
    public void Tick_TownLeaderboard_OpensTheTownBoard()
    {
        var board = Board(PgRecords.TownLeaderboard);
        var (h, l) = Setup(board);
        Assert.True(l.BeginApproachForUI(h, board));
        ArriveAll(h, l);
        Tick(h, l, 100);
        Assert.Equal([(LeaderboardData.Kind.Town, board.Ref)], h.BoardShown);
        Assert.Equal("Town leaderboard open", l.GetStatus());
    }

    [Fact]
    public void Tick_BuildingGone_CancelsTheApproach()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        h.Buildings.Remove(reg.Ref);
        Tick(h, l, 100);
        Assert.False(l.IsPending());
        Assert.Equal("Interact building invalid during approach", l.GetStatus());
        Assert.Empty(h.ArenaShown);
    }

    [Fact]
    public void Tick_BuildingNoLongerFinished_CancelsTheApproach()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        h.Buildings[reg.Ref] = reg with { Finished = false };
        Tick(h, l, 100);
        Assert.Equal("Interact building invalid during approach", l.GetStatus());
    }

    [Fact]
    public void Tick_WalkerGone_CancelsTheApproach()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        h.Pos.Remove(1);
        Tick(h, l, 100);
        Assert.False(l.IsPending());
        Assert.Equal("Selection invalid during approach", l.GetStatus());
        Assert.Equal(0, l.GetUiOpener(h));   // g_uiOpener outlives the approach (ClearPendingState leaves it, :784-822)
    }

    // ---------- NotifyExternalOrder ----------

    [Fact]
    public void NotifyExternalOrder_AnyMenu_OrAWalkersOrder_Cancels()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        h.Add(7, 0f, 0f);
        Assert.True(l.BeginApproachForUI(h, reg));
        l.NotifyExternalOrder(h, 7);   // not a walker: ignored
        Assert.True(l.IsPending());
        l.NotifyExternalOrder(h, 1);
        Assert.False(l.IsPending());
        Assert.Equal("Approach cancelled by new order", l.GetStatus());
        Assert.Contains("removejob:1", h.Calls);

        Assert.True(l.BeginApproachForUI(h, reg));
        l.NotifyExternalOrder(h, -1);   // the C++ NULL: another right-click menu
        Assert.False(l.IsPending());
        Assert.Equal("Approach cancelled by new order", l.GetStatus());
    }

    [Fact]
    public void NotifyExternalOrder_IgnoredOutsideTheApproach()
    {
        var (h, l) = Setup();
        h.AddSite(IngressFakeHost.Arena(3000f, 0f));
        Assert.True(l.Begin(h, MatchMode.TeamAvB, [0, 1], [MatchTeam.TeamA, MatchTeam.TeamB], 2));
        l.NotifyExternalOrder(h, 0);
        l.NotifyExternalOrder(h, -1);
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);
    }

    [Fact]
    public void OwnApproachMove_RecognisedOnce_LeftoverAfterACancelIsStale()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.False(l.ConsumeOwnApproachMove(0, Fallback(reg), h.Ms, out _));
        Assert.True(l.BeginApproachForUI(h, reg));
        var t = l.Targets[0];
        Assert.False(l.ConsumeOwnApproachMove(0, t, h.Ms, out _));   // nothing given yet (the first move at 0.35 s)
        Tick(h, l, 400);
        Assert.Equal(2, l.OwnApproachMovesInFlight);
        h.Ms += 33;   // the next tick: the deferred orders arrive
        Assert.False(l.ConsumeOwnApproachMove(0, new Vec3(t.X + 1.5f, t.Y, t.Z), h.Ms, out _));   // another point: the player's
        Assert.False(l.ConsumeOwnApproachMove(5, t, h.Ms, out _));                               // another character
        Assert.True(l.ConsumeOwnApproachMove(0, new Vec3(t.X + 0.9f, t.Y + 40f, t.Z), h.Ms, out bool stale));
        Assert.False(stale);
        Assert.False(l.ConsumeOwnApproachMove(0, t, h.Ms, out _));   // used up
        // walker 1's order is still in flight when the approach is cancelled: it arrives stale (the hook drops it)
        l.NotifyExternalOrder(h, -1);
        Assert.True(l.ConsumeOwnApproachMove(1, t, h.Ms, out stale));   // both walkers share the fallback target
        Assert.True(stale);
        Assert.Equal(0, l.OwnApproachMovesInFlight);
    }

    [Fact]
    public void OwnApproachMove_LeftoverOfAReplacedApproachIsStale()
    {
        // right press on Registry 1, its first move given, then a right press on Registry 2 before that move arrived: the leftover is the
        // replaced approach's (an older generation), dropped by the hook; Registry 2's own moves are ours
        var reg1 = Registry();
        var reg2 = Building(PgRecords.Registry, 3000f, 2000f, true, "4-reg");
        var (h, l) = Setup(reg1, reg2);
        Assert.True(l.BeginApproachForUI(h, reg1));
        var t1 = l.Targets[0];
        Tick(h, l, 400);
        Assert.Equal(2, l.OwnApproachMovesInFlight);
        Assert.True(l.BeginApproachForUI(h, reg2));
        var t2 = l.Targets[0];
        Assert.True(ArenaFormation.HorizontalDistSq(t1, t2) > 1.0f);
        h.Ms += 33;
        Assert.True(l.ConsumeOwnApproachMove(0, t1, h.Ms, out bool stale));
        Assert.True(stale);
        Tick(h, l, 400);
        h.Ms += 33;
        Assert.True(l.ConsumeOwnApproachMove(0, t2, h.Ms, out stale));
        Assert.False(stale);
    }

    [Fact]
    public void OwnApproachMove_ForgottenAfter2s()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        Assert.True(l.BeginApproachForUI(h, reg));
        var t = l.Targets[0];
        Tick(h, l, 400);
        h.Ms += 2001;
        Assert.False(l.ConsumeOwnApproachMove(0, t, h.Ms, out _));
        Assert.Equal(0, l.OwnApproachMovesInFlight);
    }

    [Fact]
    public void FightBegin_CancelsARunningApproach()
    {
        var reg = Registry();
        var (h, l) = Setup(reg);
        h.AddSite(IngressFakeHost.Arena(3000f, 0f));
        Assert.True(l.BeginApproachForUI(h, reg));
        Assert.True(l.Begin(h, MatchMode.TeamAvB, [0, 1], [MatchTeam.TeamA, MatchTeam.TeamB], 2));
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);
    }
}
