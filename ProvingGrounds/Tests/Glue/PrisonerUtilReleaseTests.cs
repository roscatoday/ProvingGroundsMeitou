using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The task 6.2 half of the fake world: every engine write the release and protection make is one line in <see cref="Calls"/>.</summary>
sealed partial class PrisonerFake
{
    public uint Now = 1000;
    public readonly List<string> Calls = [];
    public readonly HashSet<int> Chained = [];
    public readonly Dictionary<int, List<int>> AttackersOfC = [];
    public readonly Dictionary<int, int> Targets = [];
    public readonly HashSet<(int, int)> Opponents = [];
    public bool SparActive;
    public uint RandState = Hashing.CRandSeed;
    public readonly List<int> Remembered = [];

    public uint NowMs => Now;
    public void SetMoveStance(int c) => Calls.Add($"stance {c}");
    public void MoveTo(int c, Vector3 l) => Calls.Add(FormattableString.Invariant($"move {c} {l.X} {l.Y} {l.Z}"));
    public void SetRunSpeed(int c) => Calls.Add($"run {c}");
    public void ClearOrderQueue(int c) => Calls.Add($"clearq {c}");
    public void IssueReleaseOrder(int handler, string cage, int prisoner) => Calls.Add($"releaseorder {handler} {cage} {prisoner}");
    public bool IsChainedMode(int c) => Chained.Contains(c);
    public bool LeaveRefused;   // task 9.3d: the port's release refused (the prisoner stays held)
    public bool ReleaseAvailable = true;   // task 9.3d: Cages.ReleaseAvailable
    public void LeavePrison(int p) { Calls.Add($"leave {p}"); if (!LeaveRefused) Imprisoned.Remove(p); }
    public bool CageReleaseAvailable() => ReleaseAvailable;
    public void ClearChainedMode(int p) => Calls.Add($"unchain {p}");
    public void Say(int c, string text) => Calls.Add($"say {c} {text}");
    public int Rand() => Hashing.CRand(ref RandState);
    public void RememberMatchFighters(IReadOnlyList<int> handlers) { Calls.Add("remember " + string.Join(",", handlers)); Remembered.AddRange(handlers); }
    public void ParkInPlace(int c) => Calls.Add($"park {c}");
    public void ReassertPark(int c) => Calls.Add($"reassert {c}");
    public bool SparIsActive() => SparActive;
    public bool IsSparringOpponent(int a, int b) => Opponents.Contains((a, b)) || Opponents.Contains((b, a));
    public void AllAttackers(int c, List<int> into) { if (AttackersOfC.TryGetValue(c, out var l)) into.AddRange(l); }
    public int AttackTarget(int c) => Targets.TryGetValue(c, out var t) ? t : 0;
    public void ClearTempEnemyStatus(int a, int b) => Calls.Add($"cleartemp {a} {b}");
    public void RemoveMeleeAttackJobs(int c) => Calls.Add($"removejobs {c}");
    public void EndCombatMode(int c) => Calls.Add($"endcombat {c}");
    public void RememberTemporaryAlly(int a, int b) => Calls.Add($"ally {a} {b}");
}

/// <summary>PrisonerUtil task 6.2 (Core/Glue/PrisonerUtilLogic.Release.cs / .Protection.cs): the handler walk and release timing (near, far with
/// the 45 s timeout, the 0.45 s hold, released once, ConsumeNewlyReleased once), the release line (the CRT rand of the C++), the pair targets
/// (28 to the side, 8 short, the C++'s frame), the parking and its 0.25 s re-assert, the 0.10 s protection, and nothing done without a prisoner.</summary>
public class PrisonerUtilReleaseTests
{
    const int Prisoner = 7, Handler = 3, Opponent = 4, Outsider = 9;

    /// <summary>A prepared match (as PrepareMatch leaves it): one entry, the prisoner caged in "c1" at the origin, the handler at (dx, 0, dz).</summary>
    static (PrisonerUtilLogic<int> Logic, PrisonerFake Fake) Prepared(float hx = 500, float hz = 0, int handler = Handler)
    {
        var f = new PrisonerFake();
        f.Buildings["c1"] = new PrisonerFake.B { Pos = new Vector3(0, 10, 0), Cage = true, Useable = "c1", Occupant = Prisoner };
        f.Pos[Prisoner] = new Vector3(0, 10, 0);
        f.Imprisoned.Add(Prisoner);
        f.Pos[handler] = new Vector3(hx, 10, hz);
        var logic = new PrisonerUtilLogic<int>();
        var entry = new MatchEntry<int> { Prisoner = Prisoner, CageUseable = "c1", CageHand = "c1", Handler = handler, PrisonerHand = Prisoner, HandlerHand = handler };
        logic.State.MatchEntries.Add(entry);
        logic.State.MatchHandlers.Add(handler);
        logic.State.MatchHasPrisoner = true;
        return (logic, f);
    }

    static void Step(PrisonerUtilLogic<int> logic, PrisonerFake f, uint ms) { f.Now += ms; logic.TickHandlerUnlocks(f); }

    // ---------- task 9.3d: a refused release ----------

    [Fact]
    public void ARefusedReleaseLeavesThePrisonerUnreleasedAndPendingAndTriesAgain()
    {
        var (logic, f) = Prepared(hx: 120, hz: 0);
        f.LeaveRefused = true;
        logic.BeginHandlerUnlocks(f);
        Step(logic, f, 100);
        Step(logic, f, 400);                          // hold 0.5: the release is tried and refused
        var e = logic.State.MatchEntries[0];
        Assert.False(e.Released);
        Assert.True(e.UnlockPending);
        Assert.False(e.NewlyReleased);
        Assert.False(logic.AllMatchPrisonersReleased());
        Assert.False(logic.ConsumeNewlyReleased(f, Prisoner));
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("say ", StringComparison.Ordinal));
        Assert.Equal(1, f.Calls.Count(c => c == "leave 7"));

        f.LeaveRefused = false;                       // the next tick tries again and frees it
        Step(logic, f, 100);
        Assert.True(e.Released);
        Assert.True(e.NewlyReleased);
        Assert.Equal(2, f.Calls.Count(c => c == "leave 7"));
    }

    // ---------- BeginHandlerUnlocks ----------

    [Fact]
    public void BeginWithNoEntriesDoesNothing()
    {
        var f = new PrisonerFake();
        var logic = new PrisonerUtilLogic<int>();
        logic.State.Unlocking = true;
        logic.BeginHandlerUnlocks(f);
        Assert.False(logic.State.Unlocking);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void BeginRemembersTheHandlersOnceAndWalksThemToTheCage()
    {
        var (logic, f) = Prepared();
        logic.BeginHandlerUnlocks(f);
        Assert.True(logic.State.Unlocking);
        Assert.Equal(1000u, logic.State.UnlockLastTick);
        Assert.True(logic.State.HandlerStatesRemembered);
        Assert.Equal(["remember 3", "stance 3", "move 3 0 10 0"], f.Calls);
        var e = logic.State.MatchEntries[0];
        Assert.True(e.UnlockPending);
        Assert.False(e.Released);

        f.Calls.Clear();
        logic.BeginHandlerUnlocks(f);   // the states are remembered once (g_handlerStatesRemembered)
        Assert.Equal(["stance 3", "move 3 0 10 0"], f.Calls);
    }

    [Fact]
    public void ADeadHandlerIsNotWalked()
    {
        var (logic, f) = Prepared();
        f.Dead.Add(Handler);
        logic.BeginHandlerUnlocks(f);
        Assert.Equal(["remember 3"], f.Calls);
    }

    [Fact]
    public void ClearMatchStateDisengagesTheRememberedHandlers()
    {
        var (logic, f) = Prepared();
        logic.BeginHandlerUnlocks(f);
        logic.ClearMatchState(f);
        Assert.Equal([new[] { Handler }], f.Disengaged);
        Assert.False(logic.State.HandlerStatesRemembered);
        Assert.False(logic.State.Unlocking);
    }

    // ---------- TickHandlerUnlocks: near ----------

    [Fact]
    public void AHandlerWithin120ReleasesAfterTheHoldOf045s()
    {
        var (logic, f) = Prepared(hx: 120, hz: 0);   // exactly 120 on x/z: near (<=)
        f.Pos[Handler] = new Vector3(120, 900, 0);    // height is not measured
        logic.BeginHandlerUnlocks(f);
        f.Calls.Clear();

        Step(logic, f, 100);                          // arrives: the fake unlock order, hold 0.1
        Assert.Equal(["releaseorder 3 c1 7"], f.Calls);
        var e = logic.State.MatchEntries[0];
        Assert.True(e.UnlockArrived);
        Assert.False(e.Released);

        Step(logic, f, 300);                          // hold 0.4
        Assert.False(e.Released);
        Assert.Single(f.Calls);

        Step(logic, f, 100);                          // hold 0.5 >= 0.45: released
        Assert.True(e.Released);
        Assert.False(e.UnlockPending);
        Assert.True(e.NewlyReleased);
        Assert.True(logic.State.ProtectionImmediate);
        Assert.Equal(["releaseorder 3 c1 7", "clearq 3", "leave 7", "say 7 Thought you'd never ask."], f.Calls);   // CRand(1) = 41, 41 % 8 = 1
        Assert.True(logic.State.Unlocking);           // this tick still had a pending entry

        Step(logic, f, 100);
        Assert.False(logic.State.Unlocking);          // nothing pending any more
        Assert.Equal(4, f.Calls.Count);               // released once
    }

    [Fact]
    public void TheHoldIsNotCountedBeforeTheHandlerArrives()
    {
        var (logic, f) = Prepared(hx: 400);
        logic.BeginHandlerUnlocks(f);
        Step(logic, f, 1000);
        Step(logic, f, 1000);
        var e = logic.State.MatchEntries[0];
        Assert.False(e.UnlockArrived);
        Assert.Equal(0.0f, e.UnlockHoldElapsed);
        f.Pos[Handler] = new Vector3(100, 10, 50);
        Step(logic, f, 200);                          // arrives with this tick's 0.2
        Assert.Equal(0.2f, e.UnlockHoldElapsed);
        Step(logic, f, 200);
        Assert.False(e.Released);
        Step(logic, f, 100);                          // 0.5
        Assert.True(e.Released);
    }

    [Fact]
    public void ConsumeNewlyReleasedAnswersOnce()
    {
        var (logic, f) = Prepared(hx: 0);
        Assert.False(logic.ConsumeNewlyReleased(f, Prisoner));
        logic.BeginHandlerUnlocks(f);
        Step(logic, f, 1000);                         // arrives, hold 1.0 (dt clamped to 1): released at once
        Assert.True(logic.State.MatchEntries[0].Released);
        Assert.False(logic.ConsumeNewlyReleased(f, 0));
        Assert.False(logic.ConsumeNewlyReleased(f, 99));
        Assert.True(logic.ConsumeNewlyReleased(f, Prisoner));
        Assert.False(logic.ConsumeNewlyReleased(f, Prisoner));
    }

    // ---------- TickHandlerUnlocks: far, the timeout, dt ----------

    [Fact]
    public void AFarHandlerIsWalkedAgainEachTickAndThePrisonerIsReleasedAt45s()
    {
        var (logic, f) = Prepared(hx: 120.01f);
        logic.BeginHandlerUnlocks(f);
        f.Calls.Clear();
        for (int i = 0; i < 44; i++) Step(logic, f, 5000);   // dt clamped to 1 s each tick: 44 s
        var e = logic.State.MatchEntries[0];
        Assert.False(e.Released);
        Assert.Equal(44.0f, e.UnlockElapsed);
        Assert.Equal(88, f.Calls.Count);
        Assert.All(f.Calls, c => Assert.True(c is "stance 3" or "move 3 0 10 0"));
        f.Calls.Clear();
        Step(logic, f, 1000);                                // 45 s: released though not near, no fake unlock order
        Assert.True(e.Released);
        Assert.False(e.UnlockArrived);
        Assert.Equal(["clearq 3", "leave 7", "say 7 Thought you'd never ask."], f.Calls);
    }

    [Fact]
    public void ADeadHandlerTimesOutToo()
    {
        var (logic, f) = Prepared(hx: 0);
        logic.BeginHandlerUnlocks(f);
        f.Dead.Add(Handler);
        f.Calls.Clear();
        for (int i = 0; i < 44; i++) Step(logic, f, 1000);
        Assert.Empty(f.Calls);                               // never near, never walked
        Step(logic, f, 1000);
        Assert.True(logic.State.MatchEntries[0].Released);
    }

    [Fact]
    public void TheFirstTickAfterBeginCountsFromBegin()
    {
        var (logic, f) = Prepared(hx: 400);
        logic.BeginHandlerUnlocks(f);
        Step(logic, f, 250);
        Assert.Equal(0.25f, logic.State.MatchEntries[0].UnlockElapsed);
        Step(logic, f, 0);
        Assert.Equal(0.25f, logic.State.MatchEntries[0].UnlockElapsed);
    }

    [Fact]
    public void TheUnlockClockCrossesThe32BitWrap()
    {
        var (logic, f) = Prepared(hx: 400);
        logic.BeginHandlerUnlocks(f);
        logic.State.UnlockLastTick = 0xFFFFFF00u;
        f.Now = 0x100u;                                      // DWORD now - last = 0x200 = 512 ms
        logic.TickHandlerUnlocks(f);
        Assert.Equal(512u / 1000.0f, logic.State.MatchEntries[0].UnlockElapsed);
        Assert.Equal(0.512f, logic.State.MatchEntries[0].UnlockElapsed);
        Assert.Equal(0x100u, logic.State.UnlockLastTick);
    }

    [Fact]
    public void AZeroLastTickGivesNoTime()
    {
        var (logic, f) = Prepared(hx: 400);
        logic.BeginHandlerUnlocks(f);
        logic.State.UnlockLastTick = 0;                      // C++: if (g_unlockLastTick != 0) dt = ...
        f.Now += 700;
        logic.TickHandlerUnlocks(f);
        Assert.Equal(0.0f, logic.State.MatchEntries[0].UnlockElapsed);
        Assert.Equal(f.Now, logic.State.UnlockLastTick);
        Step(logic, f, 700);
        Assert.Equal(0.7f, logic.State.MatchEntries[0].UnlockElapsed);
    }

    [Fact]
    public void ANearHandlerStillHoldingIsReleasedByTheTimeout()
    {
        var (logic, f) = Prepared(hx: 400);
        logic.BeginHandlerUnlocks(f);
        for (int i = 0; i < 44; i++) Step(logic, f, 1000);  // far: 44 s
        Step(logic, f, 750);                                 // 44.75 s
        var e = logic.State.MatchEntries[0];
        Assert.False(e.UnlockArrived);
        f.Pos[Handler] = new Vector3(0, 10, 0);
        f.Calls.Clear();
        Step(logic, f, 250);                                 // 45.0 s, near: arrives, hold 0.25 < 0.45, released through "|| timedOut"
        Assert.Equal(45.0f, e.UnlockElapsed);
        Assert.Equal(0.25f, e.UnlockHoldElapsed);
        Assert.True(e.UnlockArrived);
        Assert.True(e.Released);
        Assert.Equal(["releaseorder 3 c1 7", "clearq 3", "leave 7", "say 7 Thought you'd never ask."], f.Calls);
    }

    [Fact]
    public void ThreeSteps015sReachTheHoldAsTheCppFloatDoes()
    {
        // 150 / 1000.0f = 0.15000000596f; three float sums = 0.45000001788f >= 0.45f (0.44999998808f): released on the third step
        var (logic, f) = Prepared(hx: 0);
        logic.BeginHandlerUnlocks(f);
        var e = logic.State.MatchEntries[0];
        Step(logic, f, 150);
        Step(logic, f, 150);
        Assert.False(e.Released);
        Step(logic, f, 150);
        float expected = 0.0f;
        for (int i = 0; i < 3; i++) expected += 150 / 1000.0f;
        Assert.Equal(expected, e.UnlockHoldElapsed);
        Assert.True(expected >= PrisonerUtilLogic<int>.UnlockHoldSec);
        Assert.True(e.Released);
    }

    [Fact]
    public void AChainedPrisonerIsUnchainedAfterLeavingThePrison()
    {
        var (logic, f) = Prepared(hx: 0);
        f.Chained.Add(Prisoner);
        logic.BeginHandlerUnlocks(f);
        f.Calls.Clear();
        Step(logic, f, 1000);
        Assert.True(logic.State.MatchEntries[0].WasChained);
        Assert.Equal(["releaseorder 3 c1 7", "clearq 3", "leave 7", "unchain 7", "say 7 Thought you'd never ask."], f.Calls);
    }

    [Fact]
    public void AnInvalidPrisonerIsNotReleased()
    {
        var (logic, f) = Prepared(hx: 0);
        f.Invalid.Add(Prisoner);
        logic.BeginHandlerUnlocks(f);
        Step(logic, f, 1000);
        Assert.False(logic.State.MatchEntries[0].Released);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("leave"));
    }

    [Fact]
    public void TheReleaseLineIsTheCrtRandsPickAmongTheEightLines()
    {
        // the MSVC rand() sequence from an unseeded state: 41, 18467, 6334, 26500, 19169, 15724, 11478, 29358
        uint s = Hashing.CRandSeed;
        Assert.Equal([41, 18467, 6334, 26500, 19169, 15724, 11478, 29358], Enumerable.Range(0, 8).Select(_ => Hashing.CRand(ref s)));

        var said = new List<string>();
        var f = new PrisonerFake();
        for (int i = 0; i < 4; i++)
        {
            var (logic, g) = Prepared(hx: 0);
            g.RandState = f.RandState;
            logic.BeginHandlerUnlocks(g);
            Step(logic, g, 1000);
            f.RandState = g.RandState;
            said.Add(g.Calls.Single(c => c.StartsWith("say ")));
        }
        // 41 % 8 = 1, 18467 % 8 = 3, 6334 % 8 = 6, 26500 % 8 = 4
        Assert.Equal(["say 7 Thought you'd never ask.", "say 7 Open the way. I'm ready.", "say 7 Let's get this over with.", "say 7 Try not to blink."], said);
        Assert.Equal(8, PrisonerUtilLogic<int>.PrisonerFightLines.Length);
    }

    // ---------- SendPairToArena ----------

    [Fact]
    public void AnUnreleasedPrisonerIsNotSent()
    {
        var (logic, f) = Prepared();
        logic.SendPairToArena(f, Prisoner, new Vector3(100, 10, 0));
        Assert.Empty(f.Calls);
    }

    static (PrisonerUtilLogic<int>, PrisonerFake) Released()
    {
        var (logic, f) = Prepared(hx: 0);
        logic.BeginHandlerUnlocks(f);
        Step(logic, f, 1000);
        f.Calls.Clear();
        return (logic, f);
    }

    [Fact]
    public void ThePrisonerRunsToItsMarkAndTheHandler28ToTheSideAnd8Short()
    {
        var (logic, f) = Released();
        logic.SendPairToArena(f, Prisoner, new Vector3(300, 20, 400));   // from the cage (0, 10, 0): direction (0.6, 0, 0.8), side (-0.8, 0, 0.6)
        Assert.Equal(6, f.Calls.Count);
        Assert.Equal(["stance 7", "move 7 300 20 400", "run 7", "stance 3"], f.Calls.Take(4));
        Assert.Equal("run 3", f.Calls[5]);
        var p = Parse(f.Calls[4]);
        Assert.Equal(300 - 0.6f * 8 + (-0.8f) * 28, p.X, 3);
        Assert.Equal(20f, p.Y);
        Assert.Equal(400 - 0.8f * 8 + 0.6f * 28, p.Z, 3);
    }

    [Fact]
    public void AnOddEntryHasItsHandlerOnTheOtherSide()
    {
        var (logic, f) = Released();
        var first = logic.State.MatchEntries[0];
        logic.State.MatchEntries.Insert(0, new MatchEntry<int> { Prisoner = 50 });   // the prisoner's entry is now index 1
        logic.SendPairToArena(f, Prisoner, new Vector3(300, 20, 400));
        var p = Parse(f.Calls[4]);
        Assert.Equal(300 - 0.6f * 8 + 0.8f * 28, p.X, 3);
        Assert.Equal(400 - 0.8f * 8 - 0.6f * 28, p.Z, 3);
        Assert.Same(first, logic.State.MatchEntries[1]);
    }

    [Fact]
    public void AMarkOnTheCageGivesTheXAxisSide()
    {
        var (logic, f) = Released();
        logic.SendPairToArena(f, Prisoner, new Vector3(0.5f, 10, 0.5f));   // lenSq 0.5 <= 1: direction kept as is, side (1, 0, 0)
        var p = Parse(f.Calls[4]);
        Assert.Equal(0.5f - 0.5f * 8 + 28, p.X, 4);
        Assert.Equal(0.5f - 0.5f * 8, p.Z, 4);
    }

    [Fact]
    public void WithoutItsCageTheHandlersFrameStartsAtThePrisoner()
    {
        var (logic, f) = Released();
        f.Buildings["c1"].Valid = false;
        f.Pos[Prisoner] = new Vector3(300, 0, 0);
        logic.SendPairToArena(f, Prisoner, new Vector3(300, 0, 100));     // direction (0, 0, 1), side (-1, 0, 0)
        var p = Parse(f.Calls[4]);
        Assert.Equal(300 - 28f, p.X, 4);
        Assert.Equal(100 - 8f, p.Z, 4);
    }

    [Fact]
    public void ADeadHandlerStaysWhileThePrisonerRuns()
    {
        var (logic, f) = Released();
        f.Dead.Add(Handler);
        logic.SendPairToArena(f, Prisoner, new Vector3(300, 20, 400));
        Assert.Equal(["stance 7", "move 7 300 20 400", "run 7"], f.Calls);
    }

    static Vector3 Parse(string move)
    {
        var w = move.Split(' ');
        Assert.Equal("move", w[0]);
        return new Vector3(float.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(w[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(w[4], System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------- parking ----------

    [Fact]
    public void ParkingAndReassertingCoverEveryLivingHandler()
    {
        var (logic, f) = Prepared();
        logic.State.MatchHandlers.Add(5);
        logic.State.MatchHandlers.Add(6);
        logic.State.MatchHandlers.Add(0);
        f.Dead.Add(5);
        f.Invalid.Add(6);
        logic.ParkMatchHandlers(f);
        logic.ReinforceParkedHandlers(f);
        Assert.Equal(["park 3", "reassert 3"], f.Calls);
    }

    // ---------- the upkeep cadence ----------

    static (PrisonerUtilLogic<int>, PrisonerFake) Upkeep()
    {
        var (logic, f) = Prepared(hx: 0);
        logic.State.MatchEntries[0].Released = true;
        f.Targets[Prisoner] = Outsider;   // a non-opponent target: each protection pass breaks it off
        f.Calls.Clear();
        return (logic, f);
    }

    static int Protections(PrisonerFake f) => f.Calls.Count(c => c == "removejobs 7");

    [Fact]
    public void ProtectionRunsEvery010s()
    {
        var (logic, f) = Upkeep();
        logic.TickMatchUpkeep(f);              // first tick: dt 0
        Assert.Equal(0, Protections(f));
        for (int i = 0; i < 10; i++) { f.Now += 50; logic.TickMatchUpkeep(f); }
        Assert.Equal(5, Protections(f));       // every second 0.05 s tick
    }

    [Fact]
    public void AReleaseProtectsAtOnceWithoutAdvancingTheCadence()
    {
        var (logic, f) = Upkeep();
        logic.TickMatchUpkeep(f);
        f.Now += 50; logic.TickMatchUpkeep(f);             // elapsed 0.05
        logic.State.ProtectionImmediate = true;
        f.Now += 30; logic.TickMatchUpkeep(f);             // at once; the cadence's elapsed stays 0.05
        Assert.Equal(1, Protections(f));
        Assert.False(logic.State.ProtectionImmediate);
        Assert.Equal(0.05f, logic.State.ProtectionElapsedSec);
        f.Now += 50; logic.TickMatchUpkeep(f);             // 0.10: the cadence fires
        Assert.Equal(2, Protections(f));
    }

    [Fact]
    public void HandlersAreReassertedEvery025sOnlyWhileTheSparRuns()
    {
        var (logic, f) = Upkeep();
        logic.TickMatchUpkeep(f);
        for (int i = 0; i < 10; i++) { f.Now += 100; logic.TickMatchUpkeep(f); }
        Assert.DoesNotContain("reassert 3", f.Calls);
        Assert.Equal(0.0f, logic.State.HandlerElapsedSec);   // not even counted while no spar runs (&& short-circuits)
        f.SparActive = true;
        f.Calls.Clear();
        for (int i = 0; i < 8; i++) { f.Now += 125; logic.TickMatchUpkeep(f); }
        Assert.Equal(4, f.Calls.Count(c => c == "reassert 3"));   // 1.0 s of 0.125 s ticks (exact in float): every second tick
    }

    [Fact]
    public void TheUpkeepClockCrossesThe32BitWrap()
    {
        var (logic, f) = Upkeep();
        f.SparActive = true;
        logic.State.MatchUpkeepLastTick = 0xFFFFFF00u;
        f.Now = 0x100u;                                      // dt = 512 ms
        logic.TickMatchUpkeep(f);
        Assert.Equal(1, Protections(f));
        Assert.Equal(0.512f % 0.10f, logic.State.ProtectionElapsedSec);
        Assert.Equal(0.512f % 0.25f, logic.State.HandlerElapsedSec);
        Assert.Contains("reassert 3", f.Calls);
        Assert.Equal(0x100u, logic.State.MatchUpkeepLastTick);
    }

    [Fact]
    public void TheUpkeepClockClampsAtOneSecondAndResetsWithoutAPrisoner()
    {
        var (logic, f) = Upkeep();
        logic.TickMatchUpkeep(f);
        f.Now += 10_000; logic.TickMatchUpkeep(f);
        Assert.Equal(1, Protections(f));
        Assert.Equal(1.0f % 0.10f, logic.State.ProtectionElapsedSec);
        logic.State.MatchHasPrisoner = false;
        logic.State.ProtectionImmediate = true;
        f.Now += 50; logic.TickMatchUpkeep(f);
        Assert.Equal(0u, logic.State.MatchUpkeepLastTick);
        Assert.Equal(0.0f, logic.State.ProtectionElapsedSec);
        Assert.False(logic.State.ProtectionImmediate);
        Assert.Equal(1, Protections(f));
    }

    // ---------- protection ----------

    [Fact]
    public void AnOutsiderAttackingThePrisonerIsBrokenOffAndTheOpponentKept()
    {
        var (logic, f) = Upkeep();
        f.Targets.Remove(Prisoner);
        f.Opponents.Add((Opponent, Prisoner));
        f.AttackersOfC[Prisoner] = [Opponent, Outsider, 0];
        f.Targets[Outsider] = Prisoner;
        f.Targets[Opponent] = Prisoner;
        logic.ProtectMatchPrisoners(f);
        Assert.Equal(["cleartemp 9 7", "cleartemp 7 9", "removejobs 9", "endcombat 9", "ally 3 7", "ally 7 3"], f.Calls);
    }

    [Fact]
    public void AnOutsiderWhoseTargetMovedOnLosesItsJobsButKeepsItsFight()
    {
        var (logic, f) = Upkeep();
        f.Targets.Remove(Prisoner);
        f.AttackersOfC[Prisoner] = [Outsider];
        f.Targets[Outsider] = 12;
        logic.ProtectMatchPrisoners(f);
        Assert.Equal(["cleartemp 9 7", "cleartemp 7 9", "removejobs 9", "ally 3 7", "ally 7 3"], f.Calls);
    }

    [Fact]
    public void APrisonerChasingANonOpponentIsBrokenOffAndItsOpponentKept()
    {
        var (logic, f) = Upkeep();
        logic.ProtectMatchPrisoners(f);
        Assert.Equal(["cleartemp 7 9", "removejobs 7", "endcombat 7", "ally 3 7", "ally 7 3"], f.Calls);
        f.Calls.Clear();
        f.Opponents.Add((Prisoner, Opponent));
        f.Targets[Prisoner] = Opponent;
        logic.ProtectMatchPrisoners(f);
        Assert.Equal(["ally 3 7", "ally 7 3"], f.Calls);
    }

    [Fact]
    public void AnUnreleasedDeadOrInvalidPrisonerIsNotProtected()
    {
        var (logic, f) = Upkeep();
        f.AttackersOfC[Prisoner] = [Outsider];
        logic.State.MatchEntries[0].Released = false;
        logic.ProtectMatchPrisoners(f);
        Assert.Empty(f.Calls);
        logic.State.MatchEntries[0].Released = true;
        f.Dead.Add(Prisoner);
        logic.ProtectMatchPrisoners(f);
        Assert.Empty(f.Calls);
        f.Dead.Clear();
        f.Invalid.Add(Prisoner);
        logic.ProtectMatchPrisoners(f);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void AnInvalidHandlerIsNotMadeAnAlly()
    {
        var (logic, f) = Upkeep();
        f.Targets.Remove(Prisoner);
        f.Invalid.Add(Handler);
        logic.ProtectMatchPrisoners(f);
        Assert.Empty(f.Calls);
    }

    // ---------- no prisoner in the match ----------

    [Fact]
    public void NothingIsDoneWhenTheMatchHasNoPrisoner()
    {
        var f = new PrisonerFake();
        f.AttackersOfC[Prisoner] = [Outsider];
        f.Targets[Prisoner] = Outsider;
        f.SparActive = true;
        var logic = new PrisonerUtilLogic<int>();
        logic.BeginHandlerUnlocks(f);
        logic.TickHandlerUnlocks(f);
        for (int i = 0; i < 5; i++) { f.Now += 200; logic.TickMatchUpkeep(f); }
        logic.ProtectMatchPrisoners(f);
        logic.ParkMatchHandlers(f);
        logic.ReinforceParkedHandlers(f);
        logic.SendPairToArena(f, Prisoner, Vector3.Zero);
        Assert.False(logic.ConsumeNewlyReleased(f, Prisoner));
        Assert.Empty(f.Calls);
        Assert.False(logic.State.Unlocking);
        Assert.False(logic.HasRuntimeActivity());
    }
}
