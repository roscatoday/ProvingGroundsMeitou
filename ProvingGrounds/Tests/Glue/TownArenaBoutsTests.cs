using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaMedicalPolicy;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.3 (TownArena.cpp:433-465, 591-697, 1074-1145, 1464-1779, 1890-2066): planning, cancelling, beginning, the fight, the result, the
/// aftercare (an immediate Finish until M8) and the saved bout's restore, over <see cref="TownFake"/>.</summary>
public class TownArenaBoutsTests
{
    const string Polearm = "219-Proving Grounds.mod";
    static readonly Vector3 ArenaPos = TownFake.RegistryPos + new Vector3(600, 0, 0);
    static readonly PlacedBuilding Arena = TownFake.Building("10.34:40000", "183-Proving Grounds.mod", pos: ArenaPos);

    /// <summary>Scratch with the Town Registry, a finished Rust Crucible 600 away, one player (id 0), six Polearm fighters (ids 10-15) and four medics
    /// (ids 30-33).</summary>
    static (TownArenaLogic<int> L, TownFake F) Town(int fighters = 6, int medics = 4)
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Buildings.Add(Arena);
        f.Player(0);   // the first player character is id 0, as Meitou's default(CharacterId)
        for (int i = 0; i < fighters; ++i) f.Npc(10 + i, Polearm, dx: 30 + i);
        for (int i = 0; i < medics; ++i) f.Npc(30 + i, TownFake.Medic, dx: 60 + i);
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        f.Logic = l;
        return (l, f);
    }

    static void TickAt(TownArenaLogic<int> l, TownFake f, uint now) { f.Now = now; l.Tick(f); }

    /// <summary>The subsystem calls every Tick makes (TownArena.cpp:1883-1888, task 7.1) and the standby of an event's ticks.</summary>
    static readonly HashSet<string> PerTick = ["spectators tick", "announcer tick", "corpse tick", "aftercare bed tick", "aftercare standby"];

    /// <summary>Binds the registry (first check), starts the settle clock (+1 s), and runs the schedule check after the 10 s settle (+12 s): the
    /// bout is planned there.</summary>
    static void SettleAndPlan(TownArenaLogic<int> l, TownFake f)
    {
        uint t0 = f.Now;
        TickAt(l, f, t0);
        TickAt(l, f, t0 + 1000);
        TickAt(l, f, t0 + 12000);
    }

    /// <summary>A planned bout, due now, begun by the next schedule check.</summary>
    static (TownArenaLogic<int> L, TownFake F) Begun()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        Assert.True(l.State.PlannedNpc.Valid);
        f.Hours = l.State.NextNpcHours;
        TickAt(l, f, f.Now + 5000);
        Assert.Equal(TownPhase.Preparing, l.State.Phase);
        return (l, f);
    }

    /// <summary>A begun bout whose match the spar session now runs, after the tick that saw it start.</summary>
    static (TownArenaLogic<int> L, TownFake F) Fighting()
    {
        var (l, f) = Begun();
        f.Active = true;
        for (int i = 0; i < l.State.FighterCount; ++i) f.Participants.Add(l.State.Fighters[i]);
        f.Pending = false;
        TickAt(l, f, f.Now + 16);
        Assert.Equal(TownPhase.Fighting, l.State.Phase);
        return (l, f);
    }

    static SparPodium.Snapshot Result(SparPodium.OutcomeKind outcome) => new() { Outcome = outcome };

    // ---------- planning (TW §2.2 steps 4-6) ----------

    [Fact]
    public void BoutIsPlannedAfterTheRosterSettlesWithTheAnnouncedStartTime()
    {
        var (l, f) = Town();
        uint seedBefore = f.Card.AmbientSeed;
        SettleAndPlan(l, f);
        var st = l.State;
        Assert.True(st.PlannedNpc.Valid);
        Assert.True(st.PlannedNpc.A.Count >= 1 && st.PlannedNpc.B.Count >= 1);
        Assert.True(st.PlannedNpc.A.Count + st.PlannedNpc.B.Count <= 4);   // capacity = min(8, 4 medics)
        Assert.NotEqual(seedBefore, f.Card.AmbientSeed);                    // the saved ambient stream advanced once
        // TW §2.2 step 6: ceil((now + 1.0 + delay/60 x 1.0) x 60) / 60, delay = FNV(ambientSeed, "next NPC start") % 61 (after the selection)
        uint delay = Hashing.Fnv1a32(f.Card.AmbientSeed, "next NPC start") % 61;
        Assert.Equal(Math.Ceiling((100.0 + 1.0 + delay / 60.0 * 1.0) * 60.0) / 60.0, st.NextNpcHours);
        Assert.Equal(st.NextNpcHours, l.GetPlannedNpcStartHours());
        Assert.True(l.HasPlannedNpcBout());
        Assert.Equal(TownFake.TownRegistry().Building.Ref, st.PlannedRegistry);
        Assert.Equal(st.PlannedNpc.A.Count + st.PlannedNpc.B.Count, st.PlannedFighters.Count);
        Assert.Contains("bookie begin", f.Calls);
        Assert.Equal(st.PlannedFighters, f.BookieBout!.Value.Fighters);
        Assert.Equal(TownArena.Text.NpcLineupAnnounced, l.GetStatus());
    }

    [Fact]
    public void NothingIsPlannedBeforeTheRosterSettles()
    {
        var (l, f) = Town();
        TickAt(l, f, f.Now);
        TickAt(l, f, f.Now + 1000);
        TickAt(l, f, f.Now + 5000);   // a schedule check 6 s after arrival
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.DoesNotContain("bookie begin", f.Calls);
    }

    [Fact]
    public void NoBoutWithoutTwoEligibleFightersOrMedicalCapacityOrAnActiveSave()
    {
        var (l1, f1) = Town(fighters: 1);
        SettleAndPlan(l1, f1);
        Assert.False(l1.State.PlannedNpc.Valid);
        var (l2, f2) = Town(medics: 1);          // capacity min(8, 1) < 2
        SettleAndPlan(l2, f2);
        Assert.False(l2.State.PlannedNpc.Valid);
        var (l3, f3) = Town();
        f3.ActiveSave = false;                    // LeaderboardStore::HasActiveSave (7.1 note (b))
        SettleAndPlan(l3, f3);
        Assert.False(l3.State.PlannedNpc.Valid);
        var (l4, f4) = Town();
        l4.State.BookingPending = true;
        SettleAndPlan(l4, f4);
        Assert.False(l4.State.PlannedNpc.Valid);
    }

    [Fact]
    public void BeginTrialPlansOrSaysWhy()
    {
        var (l, f) = Town();
        Assert.False(l.BeginTrial(f));
        Assert.Equal(TownArena.Text.WaitingToAnnounce, l.GetStatus());
        l.State.BookingPending = true;
        Assert.False(l.BeginTrial(f));
        Assert.Equal(TownArena.Text.PlayerHoldsSlot, l.GetStatus());
        l.State.BookingPending = false;
        TickAt(l, f, f.Now); TickAt(l, f, f.Now + 1000); f.Now += 11000;   // settled, no schedule check yet
        Assert.True(l.BeginTrial(f));
        Assert.Equal(TownArena.Text.NpcLineupAnnounced, l.GetStatus());
        Assert.True(l.BeginTrial(f));   // already planned
        var (lb, fb) = Begun();
        Assert.False(lb.BeginTrial(fb));
        Assert.Equal(TownArena.Text.BoutUnderway, lb.GetStatus());
    }

    // ---------- cancel and refund on every listed reason ----------

    static void AssertCancelled(TownArenaLogic<int> l, TownFake f, string reason)
    {
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.Empty(l.State.PlannedFighters);
        Assert.Null(l.State.PlannedRegistry);
        Assert.Equal(0.0, l.State.NextNpcHours);
        Assert.Contains("bookie cancel: " + reason, f.Calls);   // the refund (TownBookie::CancelNpcBout -> Settle(-1))
        Assert.Contains("diagnostic cancel", f.Calls);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("hurt")]
    [InlineData("ko")]
    [InlineData("aftercare")]
    [InlineData("limbshop")]
    [InlineData("carried")]
    [InlineData("gone")]
    [InlineData("registry")]
    [InlineData("preparation")]
    public void AnnouncedBoutIsCancelledAndRefundedWhenItsFighterOrRegistryIsUnavailable(string reason)
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        int fighter = l.State.PlannedFighters[0];
        f.Calls.Clear();
        switch (reason)
        {
            case "left": f.TownOf[fighter] = TownFake.Elsewhere; break;
            case "hurt": f.Health[fighter] = (0.5f, 1f); break;
            case "ko": f.KoSet.Add(fighter); break;
            case "aftercare": f.Aftercare.Add(fighter); break;
            case "limbshop": f.InLimbShop.Add(fighter); break;
            case "carried": f.CarriedSet.Add(fighter); break;
            case "gone": f.Invalid.Add(fighter); break;
            case "registry": f.Buildings[0] = TownFake.TownRegistry(finished: false); break;   // a gone registry hits the stop rule first (:1877)
            case "preparation": f.PreparationOk = false; break;
        }
        TickAt(l, f, f.Now + 16);
        AssertCancelled(l, f, TownArena.Text.AnnouncedUnavailable);
    }

    [Fact]
    public void ReadyAnnouncedBoutIsKeptAndNotCheckedWhilePaused()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        TickAt(l, f, f.Now + 16);
        Assert.True(l.State.PlannedNpc.Valid);
        f.Paused = true;
        f.Health[l.State.PlannedFighters[0]] = (0.5f, 1f);
        TickAt(l, f, f.Now + 16);
        Assert.True(l.State.PlannedNpc.Valid);
    }

    [Fact]
    public void EveryPlayerOutOfScratchCancelsTheAnnouncedBout()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        f.Calls.Clear();
        f.TownOf[0] = TownFake.Elsewhere;
        TickAt(l, f, f.Now + 16);
        AssertCancelled(l, f, TownArena.Text.ScheduledNoPlayers);
    }

    // ---------- beginning (TW §2.2 steps 7-9) ----------

    [Fact]
    public void DueBoutBeginsInTheNearestArenaAsTeamAvBInArenaMode()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        var planned = l.State.PlannedNpc;
        var fighters = l.State.PlannedFighters.ToArray();
        f.Buildings.Add(TownFake.Building("10.34:40001", "183-Proving Grounds.mod", pos: ArenaPos + new Vector3(900, 0, 0)));   // farther
        f.Buildings.Add(TownFake.Building("10.34:40002", "183-Proving Grounds.mod", finished: false, pos: TownFake.RegistryPos));   // unfinished
        f.Buildings.Add(TownFake.Building("10.34:40003", "183-Proving Grounds.mod", town: TownFake.Elsewhere, pos: TownFake.RegistryPos));
        f.Hours = l.State.NextNpcHours - 0.01;
        TickAt(l, f, f.Now + 5000);
        Assert.Null(f.Begun);   // not yet due
        f.Hours = l.State.NextNpcHours;
        f.Calls.Clear();
        TickAt(l, f, f.Now + 5000);
        var begun = f.Begun!.Value;
        Assert.Equal(MatchRules.MatchMode.TeamAvB, begun.Mode);
        Assert.Equal(Arena.Building.Ref, begun.Arena);
        Assert.Equal(ArenaLocationMode.Arena, begun.Location);          // set for the call ...
        Assert.Equal(ArenaLocationMode.SmallArena, f.Location);         // ... and the window's mode put back
        Assert.Equal(fighters, begun.Fighters);
        for (int i = 0; i < begun.Count; ++i)
            Assert.Equal(i < planned.A.Count ? MatchRules.MatchTeam.TeamA : MatchRules.MatchTeam.TeamB, begun.Teams[i]);
        var st = l.State;
        Assert.Equal(TownPhase.Preparing, st.Phase);
        Assert.False(st.PlayerMatch);
        Assert.Equal(fighters.Length, st.FighterCount);
        Assert.Equal(Protocol.RingsideAid, f.Medical);
        Assert.Equal(Protocol.Bloodsport, st.PreviousProtocol);
        Assert.Equal(Arena.Building.Ref, f.Ring);
        Assert.Equal(fighters.Length, f.Doctors!.Count);                // one medic per participant
        Assert.False(st.PlannedNpc.Valid);                               // the announcement is now the event
        Assert.Equal(TownFake.TownRegistry().Building.Ref, st.ActiveRegistry);
        Assert.Equal(TownArena.AssemblingMessage(MatchRules.MatchMode.TeamAvB, false, 0, planned.A.Count, fighters.Length), l.GetStatus());
        Assert.Equal(["ring", "aftercare begin", "corpse begin", "protocol RingsideAid", "announcer begin", "bookie assembling", "spectators begin",
            "location Arena", "ingress begin", "location SmallArena"], f.Calls.Where(c => !PerTick.Contains(c)).ToList());
        Assert.All(f.AnnouncedRoles!, r => Assert.Equal(Polearm, r));
        Assert.True(l.IsFighter(f, fighters[0]));
    }

    [Fact]
    public void DueNpcBoutBeginsAsAnAmbientEventWhileTheFirstPlayerCharacterIsInTown()
    {
        // C++ TownArena.cpp:1985-1991: an NPC event passes BeginEvent(NULL, ...). Player 0 is the fake's first player character, as Meitou's
        // default(CharacterId) (slot 0): passing default instead of the host's None made BeginEvent take the player branch and begin nothing.
        var (l, f) = Town();
        Assert.Contains(0, f.Players!);
        SettleAndPlan(l, f);
        f.Hours = l.State.NextNpcHours;
        TickAt(l, f, f.Now + 5000);
        Assert.NotNull(f.Begun);
        Assert.Equal(TownPhase.Preparing, l.State.Phase);
        Assert.False(l.State.PlayerMatch);
        Assert.Equal(0, l.State.PlayerCount);
        Assert.DoesNotContain(0, f.Begun!.Value.Fighters);
        Assert.All(Enumerable.Range(l.State.FighterCount, TownArenaState<int>.MaxFighters - l.State.FighterCount),
            i => Assert.Equal(TownFake.NoCharacter, l.State.Fighters[i]));   // the unused slots stay NULL, never character 0
    }

    [Fact]
    public void NoArenaInTheTownKeepsTheBoutAnnounced()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        f.Buildings.Remove(Arena);
        f.Hours = l.State.NextNpcHours;
        TickAt(l, f, f.Now + 5000);
        Assert.Equal(TownArena.Text.NoCrucible, l.GetStatus());
        Assert.True(l.State.PlannedNpc.Valid);
        Assert.False(l.IsBusy());
    }

    [Fact]
    public void TooFewMedicsAtStagingWaits()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        int count = l.State.PlannedFighters.Count;
        for (int i = 30; i < 34; ++i) if (i - 30 >= count - 1) f.KoSet.Add(i);   // count - 1 medics left
        f.Hours = l.State.NextNpcHours;
        TickAt(l, f, f.Now + 5000);
        Assert.Equal(TownArena.MedicsWaitingMessage(count - 1, count), l.GetStatus());
        Assert.False(l.IsBusy());
        Assert.True(l.State.PlannedNpc.Valid);
    }

    [Fact]
    public void IngressRefusalFinishesWithItsStatusAndPutsTheProtocolBack()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        f.Hours = l.State.NextNpcHours;
        f.BeginOk = false;
        f.IngressStatus = "Already sparring";
        TickAt(l, f, f.Now + 5000);
        Assert.False(l.IsBusy());
        Assert.Equal("Town staging failed: Already sparring", l.GetStatus());
        Assert.Equal(Protocol.Bloodsport, f.Medical);
        Assert.Equal(1, f.HoldReleases);
        Assert.Equal(ArenaLocationMode.SmallArena, f.Location);
    }

    [Fact]
    public void BusyArenaOperationOrSaveInFlightBeginsNothing()
    {
        var (l, f) = Town();
        SettleAndPlan(l, f);
        f.Hours = l.State.NextNpcHours;
        f.OpBusy = true;
        TickAt(l, f, f.Now + 5000);
        Assert.Null(f.Begun);
        f.OpBusy = false;
        f.InFlight = true;
        TickAt(l, f, f.Now + 5000);
        Assert.Null(f.Begun);
        f.InFlight = false;
        TickAt(l, f, f.Now + 5000);
        Assert.NotNull(f.Begun);
    }

    // ---------- the preparation (TownArena.cpp:433-465) ----------

    [Fact]
    public void PreparationWhoseLockedFighterIsNoLongerReadyIsCancelled()
    {
        var (l, f) = Begun();
        f.PreparationOk = false;
        f.Calls.Clear();
        TickAt(l, f, f.Now + 16);
        Assert.False(l.IsBusy());
        Assert.Equal("NPC bout cancelled: locked fighter unavailable or recovering", l.GetStatus());
        Assert.Contains("ingress cancel", f.Calls);
        Assert.Contains("diagnostic retry", f.Calls);
        Assert.Equal(Protocol.Bloodsport, f.Medical);
    }

    [Fact]
    public void FormationFailureFinishesWithTheIngressStatus()
    {
        var (l, f) = Begun();
        f.PreparationOk = false;
        f.Pending = false;
        f.IngressStatus = "NPC formation stalled; original lineup cancelled";
        TickAt(l, f, f.Now + 16);
        Assert.Equal("NPC formation stalled; original lineup cancelled", l.GetStatus());
        Assert.True(l.ValidatePreparation(f));   // idle: nothing to guard
    }

    [Fact]
    public void StagingThatEndsWithoutAMatchFinishes()
    {
        var (l, f) = Begun();
        f.Pending = false;
        f.IngressStatus = "Ingress failed: no fighters";
        TickAt(l, f, f.Now + 16);
        Assert.False(l.IsBusy());
        Assert.Equal("Town staging ended: Ingress failed: no fighters", l.GetStatus());
    }

    [Fact]
    public void QuarantinedFighterIsNotReadyAndIsLoggedOnce()
    {
        var (l, f) = Town();
        Assert.True(l.IsNpcReadyForPreparation(f, 10));
        l.QuarantineIngressFighter(f, 10);
        l.QuarantineIngressFighter(f, 10);
        Assert.Single(l.State.IngressQuarantine);
        Assert.Single(f.Debugs, d => d.StartsWith("Proving Grounds: ingress fighter quarantined for this world session name=Npc 10", StringComparison.Ordinal));
        Assert.False(l.IsNpcReadyForPreparation(f, 10));
        f.CarryingSet.Add(11);
        Assert.False(l.IsNpcReadyForPreparation(f, 11));
        Assert.False(l.State.QuietDiscovery);
    }

    // ---------- the fight and the result (TW §2.2 steps 10-12) ----------

    [Fact]
    public void MatchStartTurnsTheEventIntoTheFight()
    {
        var (l, f) = Begun();
        f.Wager = true;
        f.Active = true;
        for (int i = 0; i < l.State.FighterCount; ++i) f.Participants.Add(l.State.Fighters[i]);
        f.Calls.Clear();
        l.OnMatchStarted(f);   // SparSession::StartMatch calls it (SparSession.cpp:669)
        Assert.Equal(TownPhase.Fighting, l.State.Phase);
        Assert.False(l.State.SequentialCare);
        Assert.Equal(["rules bout started", "bookie combat started", "aftercare standby"], f.Calls);
        Assert.Equal([TownArena.Text.WagerStarting], f.Messages);
        Assert.Equal("Town 1v1 underway", l.GetStatus());
        f.Calls.Clear();
        l.OnMatchStarted(f);   // no second start
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void ResultMarksWinnersRecordsAmbientHistoryAndSettlesTheBookie()
    {
        var (l, f) = Fighting();
        var st = l.State;
        f.Calls.Clear();
        l.OnMatchResult(f, Result(SparPodium.OutcomeKind.TeamBWins));
        var teamB = Enumerable.Range(0, st.FighterCount).Where(i => st.FighterTeams[i] == MatchRules.MatchTeam.TeamB).Select(i => st.Fighters[i]);
        Assert.Equal(teamB, f.Winners);
        Assert.Single(f.Card.AmbientHistory);
        Assert.Equal(Enumerable.Range(0, st.FighterCount).Select(i => "fid-" + st.Fighters[i]).Order(StringComparer.Ordinal), f.Card.AmbientHistory[0]);
        Assert.Equal(["spectators react", "spectators release", "bookie settle 1", "diagnostic complete"], f.Calls);
        Assert.True(st.ResultRecorded);
        f.Calls.Clear();
        l.OnMatchResult(f, Result(SparPodium.OutcomeKind.TeamAWins));   // recorded once
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void DrawSettlesTheBookieAsNoWinner()
    {
        var (l, f) = Fighting();
        l.OnMatchResult(f, Result(SparPodium.OutcomeKind.Draw));
        Assert.Contains("bookie settle -1", f.Calls);
        Assert.Empty(f.Winners);
    }

    [Fact]
    public void AbortedNpcMatchAbortsTheDiagnosticOnly()
    {
        var (l, f) = Fighting();
        f.Calls.Clear();
        l.OnMatchAborted(f);
        Assert.Equal(["diagnostic abort"], f.Calls);
    }

    [Fact]
    public void FinishedFightEndsInFinishAtOnceUntilM8AndPlansTheNextBout()
    {
        var (l, f) = Fighting();
        int fighters = l.State.FighterCount;
        l.OnMatchResult(f, Result(SparPodium.OutcomeKind.TeamAWins));
        f.Active = false;   // the session ended
        f.Participants.Clear();
        f.Calls.Clear();
        TickAt(l, f, f.Now + 5000);
        Assert.False(l.IsBusy());
        Assert.Equal("Arena care complete; medics returning to hospital", l.GetStatus());
        Assert.Contains("Proving Grounds: town trial - " + TownArena.Text.FightFinished, f.Debugs);
        Assert.Equal(Protocol.Bloodsport, f.Medical);   // restored at the bout's end (M4 note)
        Assert.Equal(1, f.HoldReleases);
        Assert.Equal(["bookie settle -1", "spectators release", "corpse seal", "limbshop begin " + fighters], f.Calls.Where(c => !PerTick.Contains(c)).Take(4));
        Assert.Contains("treat all", f.Calls);
        Assert.Contains("corpse admit", f.Calls);
        Assert.True(l.State.PlannedNpc.Valid);          // the next bout was planned in aftercare (capacity counts the bout's fighters)
    }

    [Fact]
    public void AftercareThatStillTreatsWaitsForTheNextMedicPoll()
    {
        var (l, f) = Fighting();
        f.TreatRounds = 1;
        f.Active = false;
        f.Participants.Clear();
        TickAt(l, f, f.Now + 16);
        Assert.Equal(TownPhase.Aftercare, l.State.Phase);
        TickAt(l, f, f.Now + 500);   // within 1 s of the last poll
        Assert.Equal(TownPhase.Aftercare, l.State.Phase);
        TickAt(l, f, f.Now + 600);
        Assert.False(l.IsBusy());
    }

    [Fact]
    public void PendingCorpseCleanupHoldsTheAftercareWithItsStatus()
    {
        var (l, f) = Fighting();
        f.CorpsePending = true;
        f.Active = false;
        f.Participants.Clear();
        TickAt(l, f, f.Now + 16);
        Assert.Equal(TownPhase.Aftercare, l.State.Phase);
        Assert.Equal("corpse status", l.GetStatus());
        f.CorpsePending = false;
        TickAt(l, f, f.Now + 1000);
        Assert.False(l.IsBusy());
    }

    // ---------- the save and the restore (TW §2.2 step 13) ----------

    [Fact]
    public void CaptureWritesTheAnnouncedBoutOrNothing()
    {
        var (l, f) = Town();
        var output = new ArenaPersistence.PlannedNpcBout { Active = true, RegistryId = "stale" };
        Assert.True(l.CapturePlannedNpcBout(f, output));
        Assert.False(output.Active);
        Assert.Equal("", output.RegistryId);
        f.Wager = true;
        Assert.False(l.CapturePlannedNpcBout(f, output));   // a wager without a bout: not saved
        f.Wager = false;
        SettleAndPlan(l, f);
        Assert.True(l.CapturePlannedNpcBout(f, output));
        Assert.True(output.Active);
        Assert.Equal("uid:" + TownFake.TownRegistry().Building.Key, output.RegistryId);
        Assert.Equal(l.State.NextNpcHours, output.StartHours);
        Assert.Equal(l.State.PlannedNpc.A, output.TeamA);
        Assert.Equal(l.State.PlannedNpc.B, output.TeamB);
        Assert.Contains("bookie capture", f.Calls);
        f.Buildings.RemoveAt(0);
        Assert.False(l.CapturePlannedNpcBout(f, output));   // the registry has no instance: the arena part is not written
    }

    static ArenaPersistence.PlannedNpcBout Saved(TownArenaLogic<int> source, TownFake f)
    {
        var saved = new ArenaPersistence.PlannedNpcBout();
        Assert.True(source.CapturePlannedNpcBout(f, saved));
        return saved;
    }

    /// <summary>A save made with an announced bout, loaded into a fresh world with the same town.</summary>
    static (TownArenaLogic<int> L, TownFake F, ArenaPersistence.PlannedNpcBout Saved) Loaded()
    {
        var (l0, f0) = Town();
        SettleAndPlan(l0, f0);
        var saved = Saved(l0, f0);
        var (l, f) = Town();
        f.Card = f0.Card;
        l.RestorePlannedNpcBout(f, saved);
        return (l, f, saved);
    }

    [Fact]
    public void RestoredBoutWaitsForItsRegistryAndFightersThenIsAnnouncedAgain()
    {
        var (l, f, saved) = Loaded();
        Assert.True(l.State.RestoredNpcPending);
        Assert.True(l.State.PlannedNpc.Valid);
        Assert.Equal(saved.StartHours, l.State.NextNpcHours);
        Assert.Equal(TownArena.Text.SavedLineupWaiting, l.GetStatus());
        f.Calls.Clear();
        TickAt(l, f, f.Now);   // binds the registry and finds the fighters
        Assert.False(l.State.RestoredNpcPending);
        Assert.Equal(TownArena.Text.SavedLineupRestored, l.GetStatus());
        Assert.Equal(TownFake.TownRegistry().Building.Ref, l.State.PlannedRegistry);
        Assert.Equal(saved.TeamA.Count + saved.TeamB.Count, l.State.PlannedFighters.Count);
        Assert.Contains("bookie begin", f.Calls);
        Assert.Contains("bookie restore", f.Calls);
        Assert.Equal(0, l.State.ScheduleChecks);   // the restore took the schedule clock's check
    }

    [Fact]
    public void RestoredBoutStillPendingReturnsFromTickAndExpiresAfterOneGameHourInScratch()
    {
        var (l, f, saved) = Loaded();
        int fighter = int.Parse(saved.TeamA[0].Substring(4));
        f.TownOf[fighter] = TownFake.Elsewhere;
        TickAt(l, f, f.Now);
        Assert.True(l.State.RestoredNpcPending);
        Assert.Equal(0, l.State.ScheduleChecks);           // nothing past the restored-bout wait ran (:1939)
        Assert.Equal(100.0, l.State.RestoreObservedHours);
        f.Hours = 100.99;
        TickAt(l, f, f.Now + 5000);
        Assert.True(l.State.RestoredNpcPending);
        f.Calls.Clear();
        f.Hours = 101.0;
        TickAt(l, f, f.Now + 5000);
        Assert.False(l.State.RestoredNpcPending);
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.Contains("bookie refund unrestored", f.Calls);
        Assert.Contains("bookie cancel: " + TownArena.Text.SavedLineupExpired, f.Calls);
    }

    [Fact]
    public void RestoredBoutClockStartsOnlyWithAPlayerInTheRegistrysTown()
    {
        var (l, f, saved) = Loaded();
        f.TownOf[0] = TownFake.Elsewhere;
        TickAt(l, f, f.Now);
        Assert.Equal(0.0, l.State.RestoreObservedHours);
        Assert.True(l.State.RestoredNpcPending);   // the unobserved-bout cancel skips a restored bout (:1877)
        f.Hours = 105;
        TickAt(l, f, f.Now + 5000);
        Assert.True(l.State.RestoredNpcPending);
    }

    [Fact]
    public void RestoredBoutWhoseMarketCannotReopenIsCancelled()
    {
        var (l, f, _) = Loaded();
        f.RestoreOk = false;
        TickAt(l, f, f.Now);
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.Contains("bookie cancel: " + TownArena.Text.SavedLineupNoMarket, f.Calls);
    }

    [Fact]
    public void RestoredBoutIsSavedAgainAsItWasLoaded()
    {
        var (l, f, saved) = Loaded();
        var output = new ArenaPersistence.PlannedNpcBout();
        Assert.True(l.CapturePlannedNpcBout(f, output));
        Assert.Equal(saved.RegistryId, output.RegistryId);
        Assert.Equal(saved.TeamA, output.TeamA);
        Assert.Equal(saved.StartHours, output.StartHours);
        l.RestorePlannedNpcBout(f, new ArenaPersistence.PlannedNpcBout());   // an inactive saved bout changes nothing
        Assert.True(l.State.RestoredNpcPending);
    }
}
