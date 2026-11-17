using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>ArenaIngress.cpp's state machine over a fake host (task 4.3b): Begin's refusals and start-up, the formation phase (0.40 s re-issue,
/// the 30-unit arrival, the 4 s gather hold), the countdown barks and the hand-over to SparSession, the cancels, the town NPC limits (30 s
/// timeout, 20 s stall, 90 s cap, quarantine, refund), the Teams 1v1 walk-in (60 units, 0.40 s re-issue, 20 s fallback, off the stale sweep)
/// and AbandonWorldState.</summary>
public class ArenaIngressLogicTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB;
    const float Yaw = 0.7f;

    static (IngressFakeHost H, ArenaIngressLogic<int> L) Setup(int n = 4)
    {
        var h = new IngressFakeHost();
        h.AddSite(IngressFakeHost.Arena(1000f, 2000f, Yaw));
        for (int i = 0; i < n; i++) h.Add(i, -500f + 20f * i, 0f);
        h.FirstSquad = 0;
        return (h, new ArenaIngressLogic<int>(-1));
    }

    static bool BeginAvB(IngressFakeHost h, ArenaIngressLogic<int> l) => l.Begin(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);

    static Vec3[] Expected(IngressFakeHost h, MatchMode mode, MatchTeam[] teams, ArenaFormation.LocationMode loc = ArenaFormation.LocationMode.Arena) =>
        ArenaFormation.AssignSiteTargets(loc, false, mode, teams, teams.Length,
            new Vec3(h.Site!.Value.Position.X, h.Site.Value.Position.Y, h.Site.Value.Position.Z), Quat.FromYaw(h.Site.Value.Yaw));

    /// <summary>Puts every fighter on its formation point.</summary>
    static void Arrive(IngressFakeHost h, ArenaIngressLogic<int> l)
    {
        for (int i = 0; i < l.Fighters.Count; i++) h.Pos[l.Fighters[i]] = l.Targets[i];
    }

    static void Tick(IngressFakeHost h, ArenaIngressLogic<int> l, uint ms = 50) { h.Ms += ms; l.Tick(h); }

    // ---------- Begin ----------

    [Fact]
    public void Begin_Refusals_InTheCppOrder()
    {
        var (h, l) = Setup();
        h.Aftercare = true;
        Assert.False(BeginAvB(h, l)); Assert.Equal("Town medics are clearing the arena", l.GetStatus());
        h.Aftercare = false;
        Assert.False(l.Begin(h, MatchMode.TeamAvB, [0], [A], 1)); Assert.Equal("Need at least two fighters", l.GetStatus());
        Assert.False(l.Begin(h, MatchMode.TeamAvB, [0, -1], [A, B], 2)); Assert.Equal("Invalid fighter selection", l.GetStatus());
        h.SparActive = true;
        Assert.False(BeginAvB(h, l)); Assert.Equal("Already sparring", l.GetStatus());
        h.SparActive = false;
        h.SaveInFlightValue = true;
        Assert.False(BeginAvB(h, l)); Assert.Equal(SparSessionLogic<int>.SaveInFlightText, l.GetStatus());
        h.SaveInFlightValue = false;
        h.Site = null;
        Assert.False(BeginAvB(h, l)); Assert.Equal("No arena placed near registry", l.GetStatus());
        l.SetLocationMode(ArenaLocationMode.SmallArena);
        Assert.False(BeginAvB(h, l)); Assert.Equal("No small arena placed near registry", l.GetStatus());
        l.SetLocationMode(ArenaLocationMode.Banner);
        Assert.False(BeginAvB(h, l)); Assert.Equal("No banner placed near registry", l.GetStatus());
        Assert.False(l.IsPending());
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("remember", StringComparison.Ordinal));
    }

    [Fact]
    public void Begin_StartsTheFormation_InTheCppOrder()
    {
        var (h, l) = Setup();
        Assert.True(BeginAvB(h, l));
        Assert.True(l.IsPending());
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);
        Assert.Equal("Walking into arena...", l.GetStatus());
        Assert.Equal(["findsite:Arena", "remember:0,1,2,3", "results-cancel", "results-close", "ring",
            "move:0", "move:1", "move:2", "move:3", "cancel-npc-bout:Player match took priority"], h.Calls);
        Assert.Equal(h.Site!.Value.Ref, h.RingSite);
        Assert.Equal(h.Site.Value.Ref, l.BoundSite);
        var want = Expected(h, MatchMode.TeamAvB, [A, A, B, B]);
        Assert.Equal(want, l.Targets);
        Assert.Equal(want, h.Moves.Select(m => m.At));
        // a second Begin while walking
        Assert.False(BeginAvB(h, l));
        Assert.Equal("Already walking to fight site", l.GetStatus());
    }

    [Fact]
    public void Begin_AtABanner_UsesTheBannerFormation()
    {
        var (h, l) = Setup();
        h.AddSite(new ArenaBuilding(ArenaBuildingSource.Player, "2-b", ArenaIdentityLogic.BannerStringId, ArenaIdentityLogic.BannerFunctionStringId,
            true, new System.Numerics.Vector3(50f, 0f, 60f), 1.2f));
        l.SetLocationMode(ArenaLocationMode.Banner);
        Assert.True(BeginAvB(h, l));
        Assert.Equal("Walking to banner...", l.GetStatus());
        Assert.Equal(Expected(h, MatchMode.TeamAvB, [A, A, B, B], ArenaFormation.LocationMode.Banner), l.Targets);
    }

    [Fact]
    public void BindRegistry_ABanner_SetsTheBannerMode()
    {
        var (h, l) = Setup();
        var banner = new ArenaBuilding(ArenaBuildingSource.Player, "2-b", ArenaIdentityLogic.BannerStringId, ArenaIdentityLogic.BannerFunctionStringId,
            true, new System.Numerics.Vector3(50f, 0f, 60f), 0f);
        h.Buildings[banner.Ref] = banner;
        l.BindRegistry(IngressFakeHost.Arena(0, 0));   // not a match UI opener: ignored
        Assert.Null(l.BoundRegistryRef);
        l.BindRegistry(banner);
        Assert.Equal(ArenaLocationMode.Banner, l.GetLocationMode());
        Assert.Equal("Bound to banner", l.GetStatus());
        Assert.True(l.HasValidRegistry(h));
        Assert.Equal(banner, l.GetBoundRegistry(h));
        h.Buildings.Clear();   // gone
        Assert.False(l.HasValidRegistry(h));
        Assert.Null(l.GetBoundRegistry(h));
        l.ClearBind();
        Assert.Equal("Unbound", l.GetStatus());
        Assert.Null(l.BoundRegistryRef);
    }

    [Fact]
    public void HasLocationSite_FromTheSquadsFirstMember()
    {
        var (h, l) = Setup();
        Assert.True(l.HasLocationSite(h));
        h.Site = null;
        Assert.False(l.HasLocationSite(h));
        h.FirstSquad = -1;
        Assert.False(l.HasLocationSite(h));
    }

    // ---------- the formation phase ----------

    [Fact]
    public void Formation_ReissuesMovesEvery040s_OnlyToFightersNotAtTheirMark()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        h.Moves.Clear();
        Tick(h, l, 125);   // nextMoveIssueSec 0: the first tick re-issues at once (next at 0.525 s)
        Assert.Equal(4, h.Moves.Count);
        h.Pos[0] = l.Targets[0];   // fighter 0 arrived
        h.Moves.Clear();
        for (int i = 0; i < 3; i++) Tick(h, l, 125);   // 0.5 s: none
        Assert.Empty(h.Moves);
        Tick(h, l, 125);   // 0.625 s
        Assert.Equal([1, 2, 3], h.Moves.Select(m => m.Id));
    }

    [Fact]
    public void Formation_ParksAndFacesAFighterWithin30Units_NotBeyond()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        var t = l.Targets[0];
        h.Pos[0] = new Vec3(t.X + 29.9f, t.Y + 500f, t.Z);   // height ignored (ground distance)
        h.Pos[1] = new Vec3(l.Targets[1].X + 30.1f, t.Y, l.Targets[1].Z);
        Tick(h, l);
        Assert.Contains("park:0", h.Calls);
        Assert.DoesNotContain("park:1", h.Calls);
        var face = ArenaFormation.OpposingFacePoint(MatchMode.TeamAvB, [A, A, B, B], l.Targets.ToArray(), 0, h.Pos[0]);
        Assert.Equal((0, face!.Value), h.Faces.Single());
        Assert.Equal("Waiting for 3 fighters to reach formation...", l.GetStatus());
        h.Pos[1] = l.Targets[1]; h.Pos[2] = l.Targets[2];
        Tick(h, l);
        Assert.Equal("Waiting for 1 fighter to reach formation...", l.GetStatus());
    }

    [Fact]
    public void Formation_HoldsFourSecondsWithEveryoneIn_ThenStartsTheCountdown()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        Tick(h, l);
        Arrive(h, l);
        Tick(h, l, 125);   // hold 0.125 (exact steps)
        Assert.Equal("Fighters ready - crowd settling before the match", l.GetStatus());
        for (int i = 0; i < 30; i++) Tick(h, l, 125);   // 3.875 s
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);
        // one fighter steps out: the hold starts again
        var keep = h.Pos[3];
        h.Pos[3] = new Vec3(keep.X + 100f, keep.Y, keep.Z);
        Tick(h, l, 125);
        Assert.Equal(0f, l.SettleHoldSec);
        h.Pos[3] = keep;
        for (int i = 0; i < 31; i++) Tick(h, l, 125);   // 3.875 s
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);
        Assert.Empty(h.Says);
        Tick(h, l, 125);   // 4.0 s held
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Countdown, l.Kind);
        Assert.Equal("Fight starts in 3...", l.GetStatus());
        Assert.Equal(["3", "3", "3", "3"], h.Says.Select(s => s.Text));
    }

    [Fact]
    public void Formation_WaitsForTheBookie()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        Arrive(h, l);
        h.HoldBets = true;
        for (int i = 0; i < 60; i++) Tick(h, l, 125);
        Assert.Equal("NPC fighters ready - bookie accepting bets", l.GetStatus());
        Assert.Equal(ArenaIngressLogic<int>.PendingKind.Formation, l.Kind);
    }

    static void ToCountdown(IngressFakeHost h, ArenaIngressLogic<int> l)
    {
        BeginAvB(h, l);
        Arrive(h, l);
        while (l.Kind != ArenaIngressLogic<int>.PendingKind.Countdown) Tick(h, l, 100);
    }

    // ---------- the countdown ----------

    [Fact]
    public void Countdown_BarksAt0_06_12_18_ThenStartsTheMatchAt225()
    {
        var (h, l) = Setup();
        ToCountdown(h, l);
        uint t0 = h.Ms;
        h.Says.Clear();
        h.Calls.Clear();
        while (l.IsPending()) Tick(h, l, 10);
        var firsts = h.Says.Where(s => s.Id == 0).Select(s => (s.Text, Ms: s.Ms - t0)).ToList();
        Assert.Equal(["2", "1", "FIGHT!"], firsts.Select(f => f.Text));   // "3" was barked when the countdown began (t = 0)
        uint[] at = [600u, 1200u, 1800u];
        for (int i = 0; i < 3; i++) Assert.InRange(firsts[i].Ms, at[i], at[i] + 10u);   // the first 10 ms tick at or past the mark
        Assert.InRange(h.Ms - t0, 2250u, 2260u);   // FinishIngressAndStart at elapsed >= 2.25 s
        Assert.Contains("park:0", h.Calls);   // re-parked every countdown tick
        Assert.Equal(MatchMode.TeamAvB, h.Started!.Value.Mode);
        Assert.Equal([0, 1, 2, 3], h.Started.Value.Fighters);
        Assert.Equal([A, A, B, B], h.Started.Value.Teams);
        Assert.Equal("Match started", l.GetStatus());
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("disengage", StringComparison.Ordinal));   // ownership went to StartMatch
        int start = h.Calls.IndexOf("start");
        Assert.True(h.Calls.IndexOf("releaseparked") < start);
        Assert.Null(l.BoundSite);
    }

    [Fact]
    public void Countdown_StatusTexts()
    {
        var (h, l) = Setup();
        ToCountdown(h, l);
        Tick(h, l, 100); Assert.Equal("Fight starts in 3...", l.GetStatus());
        Tick(h, l, 600); Assert.Equal("Fight starts in 2...", l.GetStatus());
        Tick(h, l, 600); Assert.Equal("Fight starts in 1...", l.GetStatus());
        Tick(h, l, 600); Assert.Equal("Fight!", l.GetStatus());
    }

    [Fact]
    public void Countdown_ARefusedStart_DisengagesTheFighters()
    {
        var (h, l) = Setup();
        ToCountdown(h, l);
        h.StartOk = false; h.SparStatus = "Need at least two fighters";
        h.Calls.Clear();
        Tick(h, l, 2300);
        Assert.Equal(["start", "disengage:0,1,2,3"], h.Calls.Where(c => c is "start" || c.StartsWith("disengage", StringComparison.Ordinal)));
        Assert.Equal("Need at least two fighters", l.GetStatus());
        Assert.False(l.IsPending());
    }

    [Fact]
    public void Countdown_AFighterGone_Cancels()
    {
        var (h, l) = Setup();
        ToCountdown(h, l);
        h.Pos.Remove(2);
        Tick(h, l);
        Assert.Equal("Countdown cancelled: fighter or site invalid", l.GetStatus());
        Assert.False(l.IsPending());
        Assert.Contains("disengage:0,1,2,3", h.Calls);
    }

    [Fact]
    public void Formation_SiteGone_OrFighterGone()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        h.Buildings.Clear();
        Tick(h, l);
        Assert.Equal("Fight site destroyed during ingress", l.GetStatus());
        Assert.False(l.IsPending());

        (h, l) = Setup();
        BeginAvB(h, l);
        h.Pos.Remove(1);
        Tick(h, l);
        Assert.Equal("Fighter invalid during ingress", l.GetStatus());
        Assert.False(l.IsPending());
    }

    [Fact]
    public void Cancel_RestoresTheFighters_AndSaysIngressCancelled()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        h.Calls.Clear();
        l.Cancel(h);
        Assert.False(l.IsPending());
        Assert.Equal("Ingress cancelled", l.GetStatus());
        Assert.Equal(["removejob:0", "removejob:1", "removejob:2", "removejob:3", "releaseparked", "disengage:0,1,2,3", "announcer-release"], h.Calls);
        h.Calls.Clear();
        l.Cancel(h);   // nothing pending: nothing
        Assert.Empty(h.Calls);
    }

    [Fact]
    public void AbandonWorldState_ForgetsEverything()
    {
        var (h, l) = Setup();
        BeginAvB(h, l);
        l.BeginWalkIn(h, 0, A);
        l.AbandonWorldState(h);
        Assert.False(l.IsPending());
        Assert.False(l.IsWalkInPending());
        Assert.Equal(-1, l.GetWalkInFighter());
        Assert.Equal("Idle", l.GetStatus());
        Assert.Empty(l.Fighters);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("disengage", StringComparison.Ordinal));
    }

    // ---------- the town NPC limits (M7 inputs; the C++ timers) ----------

    /// <summary>A town NPC bout (not the player's); <paramref name="place"/> puts the fighters relative to their formation points first (the
    /// progress checkpoints start at Begin).</summary>
    static (IngressFakeHost H, ArenaIngressLogic<int> L) TownBout(Action<IngressFakeHost, Vec3[]> place)
    {
        var (h, l) = Setup();
        h.TownBusy = true;
        place(h, Expected2(h, l));
        BeginAvB(h, l);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("cancel-npc-bout", StringComparison.Ordinal));   // only when the town is not busy
        Assert.Equal(Expected2(h, l), l.Targets);
        return (h, l);
    }

    static Vec3[] Expected2(IngressFakeHost h, ArenaIngressLogic<int> l) =>
        ArenaFormation.AssignSiteTargets(ArenaFormation.LocationMode.Arena, true, MatchMode.TeamAvB, [A, A, B, B], 4,
            new Vec3(1000f, 0f, 2000f), Quat.FromYaw(Yaw));   // x1.0 while the town arena is busy

    [Fact]
    public void NpcFormation_ProgressExtendsPast30s()
    {
        var (h, l) = TownBout((h, t) => { for (int i = 0; i < 4; i++) h.Pos[i] = new Vec3(t[i].X + 2000f, 0f, t[i].Z); });
        // walk them 40 units closer each second: real progress
        for (int s = 0; s < 60; s++)
        {
            for (int i = 1; i < 4; i++) h.Pos[i] = l.Targets[i];
            h.Pos[0] = new Vec3(h.Pos[0].X - 32f, 0f, h.Pos[0].Z);
            Tick(h, l, 1000);
        }
        Assert.True(l.IsPending());   // 60 s, still progressing
        Assert.Empty(h.Quarantined);
    }

    [Fact]
    public void NpcFormation_A20sStallAfter30s_CancelsQuarantinesAndRefunds()
    {
        var (h, l) = TownBout((h, t) => { for (int i = 1; i < 4; i++) h.Pos[i] = t[i]; h.Pos[0] = new Vec3(t[0].X + 500f, 0f, t[0].Z); });   // 0 never moves
        h.PendingWager = true;
        for (int s = 0; s < 29; s++) Tick(h, l, 1000);
        Assert.True(l.IsPending());   // 29 s
        Tick(h, l, 1000);   // 30 s: stalled 30 s >= 20 s
        Assert.False(l.IsPending());
        Assert.Equal([0], h.Quarantined);
        Assert.Equal("NPC formation stalled; original lineup cancelled; wager refunded", l.GetStatus());
    }

    [Fact]
    public void NpcFormation_HardCapAt90s()
    {
        var (h, l) = TownBout((h, t) => { for (int i = 1; i < 4; i++) h.Pos[i] = t[i]; h.Pos[0] = new Vec3(t[0].X + 4000f, 0f, t[0].Z); });
        for (int s = 0; s < 89; s++)
        {
            h.Pos[0] = new Vec3(h.Pos[0].X - 31f, 0f, h.Pos[0].Z);   // progress 31 > 30 per second: never stalled
            Tick(h, l, 1000);
        }
        Assert.True(l.IsPending());
        h.Pos[0] = new Vec3(h.Pos[0].X - 31f, 0f, h.Pos[0].Z);
        Tick(h, l, 1000);
        Assert.False(l.IsPending());
        Assert.Empty(h.Quarantined);   // making progress: not stuck
        Assert.Equal("NPC formation hard cap reached; original lineup cancelled", l.GetStatus());
    }

    [Fact]
    public void NpcFormation_NotForAPlayerMatch()
    {
        var (h, l) = Setup();
        h.TownBusy = true; h.TownPlayerMatch = true;
        BeginAvB(h, l);
        for (int s = 0; s < 120; s++) Tick(h, l, 1000);
        Assert.True(l.IsPending());
    }

    // ---------- the Teams 1v1 walk-in ----------

    [Fact]
    public void WalkIn_ToThePitMarker_ReissuedEvery040s_ArrivesWithin60()
    {
        var (h, l) = Setup();
        Assert.True(l.BeginWalkIn(h, 2, B));
        Assert.Equal(["findsite:Arena", "leavesweep:2", "move:2"], h.Calls);
        var want = ArenaFormation.WalkInTarget(ArenaFormation.LocationMode.Arena, B, new Vec3(1000f, 0f, 2000f), Quat.FromYaw(Yaw));
        Assert.Equal(want, l.WalkInTarget);
        Assert.True(l.IsWalkInPending());
        Assert.Equal(2, l.GetWalkInFighter());
        Assert.False(l.BeginWalkIn(h, 3, A));   // one at a time
        h.Moves.Clear();
        h.Ms += 125; l.TickWalkIn(h);   // nextMove 0: at once (next at 0.525 s)
        Assert.Single(h.Moves);
        for (int i = 0; i < 3; i++) { h.Ms += 125; l.TickWalkIn(h); }
        Assert.Single(h.Moves);
        h.Ms += 125; l.TickWalkIn(h);   // 0.625 s
        Assert.Equal(2, h.Moves.Count);
        h.Pos[2] = new Vec3(want.X + 59.9f, 0f, want.Z);
        Assert.True(l.IsWalkInArrived(h));
        h.Pos[2] = new Vec3(want.X + 60.1f, 0f, want.Z);
        Assert.False(l.IsWalkInArrived(h));
        h.Calls.Clear();
        l.ClearWalkIn(h);
        Assert.Equal(["removejob:2"], h.Calls);
        Assert.False(l.IsWalkInPending());
        Assert.Equal(-1, l.GetWalkInFighter());
    }

    [Fact]
    public void WalkIn_After20s_ItsPositionBecomesTheTarget()
    {
        var (h, l) = Setup();
        l.BeginWalkIn(h, 1, A);
        h.Pos[1] = new Vec3(5000f, 0f, 5000f);
        for (int i = 0; i < 159; i++) { h.Ms += 125; l.TickWalkIn(h); }   // 19.875 s
        Assert.False(l.IsWalkInArrived(h));
        h.Ms += 125; l.TickWalkIn(h);   // 20.0 s
        Assert.True(l.IsWalkInArrived(h));
    }

    [Fact]
    public void WalkIn_Refusals_AndSmallArenaAndBannerTargets()
    {
        var (h, l) = Setup();
        Assert.False(l.BeginWalkIn(h, 9, A));   // no such fighter
        Assert.False(l.BeginWalkIn(h, 0, (MatchTeam)99));
        var site = h.Site;
        h.Site = null;
        Assert.False(l.BeginWalkIn(h, 0, A));
        h.Site = site;
        l.SetLocationMode(ArenaLocationMode.SmallArena);
        Assert.True(l.BeginWalkIn(h, 0, A));
        Assert.Equal(ArenaFormation.WalkInTarget(ArenaFormation.LocationMode.SmallArena, A, new Vec3(1000f, 0f, 2000f), Quat.FromYaw(Yaw)),
            l.WalkInTarget);
        l.ClearWalkIn(h);
        l.SetLocationMode(ArenaLocationMode.Banner);
        Assert.True(l.BeginWalkIn(h, 0, B));
        Assert.Equal(new Vec3(1020f, 0f, 2000f), l.WalkInTarget);   // banner +-20 world, no orientation
    }

    [Fact]
    public void WalkIn_FighterGone_ClearsItself()
    {
        var (h, l) = Setup();
        l.BeginWalkIn(h, 1, A);
        h.Pos.Remove(1);
        l.TickWalkIn(h);
        Assert.False(l.IsWalkInPending());
    }

    // ---------- review fixes: throw safety, clock wrap, escorts, short lists ----------

    [Fact]
    public void Begin_AThrowPartWay_RestoresEveryFighter_AndLeavesNothingPending()
    {
        var log = new List<string>();
        var (h, _) = Setup();
        var l = new ArenaIngressLogic<int>(-1, log.Add);
        h.ThrowMoveOn = 1;   // the 2nd fighter's move order throws
        Assert.False(BeginAvB(h, l));
        Assert.False(l.IsPending());
        Assert.Equal(ArenaIngressLogic<int>.BeginFailedText, l.GetStatus());
        Assert.Contains("remember:0,1,2,3", h.Calls);
        Assert.Contains("disengage:0,1,2,3", h.Calls);   // Jobs back on for all
        Assert.Empty(l.Fighters);
        Assert.Single(log);
        Assert.Equal(1, l.FailureCount);
    }

    [Fact]
    public void Cancel_ADisengageThatThrows_StillClearsTheStaging()
    {
        var log = new List<string>();
        var (h, _) = Setup();
        var l = new ArenaIngressLogic<int>(-1, log.Add);
        BeginAvB(h, l);
        h.ThrowDisengage = true;
        l.Cancel(h);
        Assert.False(l.IsPending());
        Assert.Equal("Ingress cancelled", l.GetStatus());
        Assert.Contains("announcer-release", h.Calls);   // the steps after the throw still ran
        Assert.Null(l.BoundSite);
        Assert.Single(log);
    }

    [Fact]
    public void Countdown_AStartThatThrows_IsARefusedStart_FightersReleased()
    {
        var (h, _) = Setup();
        var l = new ArenaIngressLogic<int>(-1);
        ToCountdown(h, l);
        h.ThrowStart = true;
        h.Calls.Clear();
        Tick(h, l, 2300);
        Assert.False(l.IsPending());
        Assert.Contains("disengage:0,1,2,3", h.Calls);
        Assert.Equal(ArenaIngressLogic<int>.BeginFailedText, l.GetStatus());
    }

    [Fact]
    public void HoldAndCountdown_AcrossTheClockWrap()
    {
        var (h, l) = Setup();
        h.Ms = uint.MaxValue - 300;
        BeginAvB(h, l);
        Tick(h, l, 125);
        Arrive(h, l);
        int ticks = 0;
        while (l.Kind != ArenaIngressLogic<int>.PendingKind.Countdown) { Tick(h, l, 125); ticks++; }
        Assert.Equal(32, ticks);   // 4.0 s of 0.125 s steps, the wrap in the middle
        h.Says.Clear();
        int steps = 0;
        while (l.IsPending()) { Tick(h, l, 125); steps++; }
        Assert.Equal(18, steps);   // 2.25 s
        Assert.Equal(["2", "1", "FIGHT!"], h.Says.Where(s => s.Id == 0).Select(s => s.Text));
        Assert.NotNull(h.Started);
    }

    [Fact]
    public void Begin_EscortsGetTheirMoves_InvalidOnesSkipped()
    {
        var (h, l) = Setup(6);
        Assert.True(l.Begin(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4, [4, 9, 5], 3));
        Assert.Contains("remember:4,9,5", h.Calls);   // the C++ remembers the escort array as given
        var site = h.Site!.Value;
        var gather = ArenaFormation.EscortGatherBase(ArenaFormation.LocationMode.Arena, new Vec3(site.Position.X, site.Position.Y, site.Position.Z),
            Quat.FromYaw(site.Yaw), null);
        // the filtered list (9 does not exist): 4 at index 0, 5 at index 1
        Assert.Equal([(4, ArenaFormation.EscortMoveTarget(gather, 0)), (5, ArenaFormation.EscortMoveTarget(gather, 1))],
            h.Moves.Where(m => m.Id >= 4).Select(m => (m.Id, m.At)));
        Assert.Equal("Walking into arena...", l.GetStatus());
        h.Calls.Clear();
        l.Cancel(h);
        Assert.Contains("removejob:4", h.Calls);
        Assert.Contains("disengage:0,1,2,3", h.Calls);   // kept quirk (C++ :803): escorts are not disengaged
    }

    [Fact]
    public void Begin_ListsShorterThanTheCount_AreRefused()
    {
        var (h, l) = Setup();
        Assert.False(l.Begin(h, MatchMode.TeamAvB, [0, 1, 2], [A, A, B, B], 4));
        Assert.Equal(ArenaIngressLogic<int>.ShortListText, l.GetStatus());
        Assert.False(l.Begin(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, B], 4));
        Assert.False(l.Begin(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4, [4], 2));   // escorts shorter than escortCount
        Assert.Equal(ArenaIngressLogic<int>.ShortListText, l.GetStatus());
        Assert.False(l.IsPending());
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("remember", StringComparison.Ordinal));
    }
}
