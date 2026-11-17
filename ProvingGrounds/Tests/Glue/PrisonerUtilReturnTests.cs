using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The task 6.3 half of the fake world: every engine write of ringside aid, the return and the lock check is one line in
/// <see cref="Calls"/>; Imprison locks the cage it fills (Meitou's Fixtures.Imprison, spike S11).</summary>
sealed partial class PrisonerFake
{
    public readonly HashSet<int> Unconscious = [], Eliminated = [], NeedsAid = [], Carrying = [], Carried = [], NoAI = [];
    public readonly Dictionary<int, string> Names = [];
    public readonly HashSet<string> Destroyed = [], Broken = [], NoLock = [], LockWontEngage = [], ImprisonRefused = [];
    public readonly HashSet<string> Locked = [];
    public readonly List<string> Messages = [];
    public bool Stabilize = true, Verifier = true, RejectLockOrder;

    public bool IsUnconscious(int c) => Unconscious.Contains(c);
    public string Name(int c) => Names.TryGetValue(c, out var n) ? n : "char" + c;
    public bool IsEliminated(int c) => Eliminated.Contains(c);
    public bool ShouldStabilizePrisoner() => Stabilize;
    public bool NeedsStabilization(int c) => NeedsAid.Contains(c);
    public void ShowPlayerMessage(string text) => Messages.Add(text);
    public void Imprison(int prisoner, string cage)
    {
        Calls.Add($"imprison {prisoner} {cage}");
        if (ImprisonRefused.Contains(cage)) return;
        Buildings[cage].Occupant = prisoner;
        Imprisoned.Add(prisoner);
        Held[prisoner] = cage;
        Locked.Add(cage);   // Imprison locks the cage
    }
    public void SetChainedMode(int prisoner) => Calls.Add($"chain {prisoner}");
    public bool IsDestroyed(string cage) => Destroyed.Contains(cage);
    public bool IsBroken(string cage) => Broken.Contains(cage);
    public bool HasLock(string cage) => !NoLock.Contains(cage);
    public bool LockVerifierAvailable() => Verifier;
    public bool HasAI(int c) => Valid(c) && !NoAI.Contains(c);
    public bool IsDoorLocked(string cage) => Locked.Contains(cage) && !LockWontEngage.Contains(cage);
    public bool LockOrderRejected(int handler, string cage) { Calls.Add($"checklock {handler} {cage}"); return RejectLockOrder; }
    public void IssueLockOrder(int handler, string cage) => Calls.Add($"lockorder {handler} {cage}");
    public void ClearAIGoals(int c) => Calls.Add($"clearai {c}");
    public bool IsCarryingSomething(int c) => Carrying.Contains(c);
    public bool IsBeingCarried(int c) => Carried.Contains(c);
    public void DropCarriedObject(int c) { Calls.Add($"drop {c}"); Carrying.Remove(c); }
    public void LiftPerson(int handler, int prisoner) => Calls.Add($"lift {handler} {prisoner}");
    public void IssueStabilizeOrder(int medic, int target) => Calls.Add($"treat {medic} {target}");
}

/// <summary>PrisonerUtil task 6.3 (Core/Glue/PrisonerUtilLogic.Return.cs): ringside aid (3 s retries, only an eliminated prisoner that needs it,
/// its own handler, the park on recovery), the cage choice (own cage free and in range, else the nearest free one, none), walk / carry / dead,
/// the treatment before the return (3 s retries, 20 s cap), the carry pick-up retry (2.5 s), arrival 36 / timeout 60, the lock check (secured,
/// every failure text, the 30 s wait), ReturnAllToCages, and the forced return before a save (SaveSafety over the real logic).</summary>
public class PrisonerUtilReturnTests
{
    const int Prisoner = 7, Handler = 3, Prisoner2 = 8, Handler2 = 5;

    /// <summary>A released match prisoner after its fight: its own cage "c1" (owned, empty: it left it), the bound Registry, the prisoner at
    /// registry + (px, pz), its handler at registry + (hx, hz).</summary>
    static (PrisonerUtilLogic<int> Logic, PrisonerFake Fake) Fought(float px = 300, float pz = 0, float hx = 330, float hz = 0,
        bool released = true)
    {
        var f = new PrisonerFake { Bound = PrisonerFake.Registry };
        f.Cage("c1", 200, 0);
        f.Char(Prisoner, px, pz);
        f.Char(Handler, hx, hz);
        f.Names[Prisoner] = "Dust Bandit";
        var logic = new PrisonerUtilLogic<int>();
        var e = new MatchEntry<int> { Prisoner = Prisoner, CageUseable = "c1", CageHand = "c1", Handler = Handler, PrisonerHand = Prisoner,
            HandlerHand = Handler, Released = released, OriginalCageIndex = 0 };
        if (!released) { f.Buildings["c1"].Occupant = Prisoner; f.Imprisoned.Add(Prisoner); f.Locked.Add("c1"); }
        logic.State.MatchEntries.Add(e);
        logic.State.MatchHandlers.Add(Handler);
        logic.State.MatchHasPrisoner = true;
        logic.State.HandlerStatesRemembered = true;
        return (logic, f);
    }

    static MatchEntry<int> Entry(PrisonerUtilLogic<int> logic) => logic.State.MatchEntries[0];
    static void Return(PrisonerUtilLogic<int> logic, PrisonerFake f, uint ms) { f.Now += ms; logic.TickReturn(f); }
    static void Aid(PrisonerUtilLogic<int> logic, PrisonerFake f, uint ms) { f.Now += ms; logic.TickRingsideAid(f); }
    static string Move(int c, Vector3 v) => FormattableString.Invariant($"move {c} {v.X} {v.Y} {v.Z}");
    static Vector3 Cage1 => PrisonerFake.Registry.Position + new Vector3(200, 0, 0);

    /// <summary>The state after the C++ ClearMatchState: nothing left over (no entry, no handler, no return, no protection, no activity).</summary>
    static void AssertCleared(PrisonerUtilLogic<int> logic, PrisonerFake f)
    {
        Assert.Empty(logic.State.MatchEntries);
        Assert.Empty(logic.State.MatchHandlers);
        Assert.False(logic.State.MatchHasPrisoner);
        Assert.False(logic.State.Returning);
        Assert.False(logic.HasRuntimeActivity());
        Assert.False(logic.HasPendingCageLocks());
        Assert.Contains([Handler], f.Disengaged);   // FightStarter::DisengageMatch: the handler's orders and jobs restored
    }

    // ---------- ringside aid ----------

    static (PrisonerUtilLogic<int>, PrisonerFake) Eliminated()
    {
        var (logic, f) = Fought();
        f.SparActive = true;
        f.Eliminated.Add(Prisoner);
        f.NeedsAid.Add(Prisoner);
        return (logic, f);
    }

    [Fact]
    public void RingsideAidTreatsAnEliminatedPrisonerAndRetriesEvery3s()
    {
        var (logic, f) = Eliminated();
        logic.TickRingsideAid(f);   // first tick: dt 0, the treatment starts
        Assert.Equal([$"clearai {Handler}", $"stance {Handler}", $"treat {Handler} {Prisoner}"], f.Calls);
        Assert.True(Entry(logic).RingsideTreating);
        f.Calls.Clear();
        for (int i = 0; i < 5; i++) Aid(logic, f, 500);    // 2.5 s: no retry yet
        Assert.Empty(f.Calls);
        Aid(logic, f, 500);                                 // 3.0 s
        Assert.Equal([$"stance {Handler}", $"treat {Handler} {Prisoner}"], f.Calls);
        f.Calls.Clear();
        for (int i = 0; i < 3; i++) Aid(logic, f, 1000);    // 6.0 s: one more
        Assert.Equal([$"stance {Handler}", $"treat {Handler} {Prisoner}"], f.Calls);
    }

    [Fact]
    public void RingsideAidClampsATickToOneSecond()
    {
        var (logic, f) = Eliminated();
        logic.TickRingsideAid(f);
        f.Calls.Clear();
        Aid(logic, f, 10000);   // a 10 s gap counts 1 s
        Aid(logic, f, 1500);    // 2 s
        Assert.Empty(f.Calls);
        Aid(logic, f, 1000);    // 3 s
        Assert.Equal(2, f.Calls.Count);
    }

    [Fact]
    public void RingsideAidOnlyForAnEliminatedLivingPrisonerThatNeedsIt()
    {
        foreach (var setup in new Action<PrisonerFake>[]
        {
            p => p.Eliminated.Remove(Prisoner),          // still in the fight
            p => p.NeedsAid.Remove(Prisoner),            // nothing to treat
            p => p.Dead.Add(Prisoner),
            p => p.Invalid.Add(Prisoner),
            p => p.Dead.Add(Handler),                    // no living handler
        })
        {
            var (logic, f) = Eliminated();
            setup(f);
            logic.TickRingsideAid(f);
            Aid(logic, f, 1000);
            Assert.Empty(f.Calls);
            Assert.False(Entry(logic).RingsideTreating);
        }
    }

    [Fact]
    public void RingsideAidNeedsARunningSparAPrisonerAndTheRingsideProtocol()
    {
        foreach (var setup in new Action<PrisonerFake, PrisonerUtilLogic<int>>[]
        {
            (p, l) => p.SparActive = false,
            (p, l) => l.State.MatchHasPrisoner = false,
            (p, l) => p.Stabilize = false,                // Bloodsport
        })
        {
            var (logic, f) = Eliminated();
            logic.TickRingsideAid(f);
            Assert.NotEqual(0u, logic.State.RingsideAidLastTick);
            setup(f, logic);
            Aid(logic, f, 1000);
            Assert.Equal(0u, logic.State.RingsideAidLastTick);   // the clock is reset
            Assert.DoesNotContain($"treat {Handler} {Prisoner}", f.Calls.Skip(3));
        }
    }

    [Fact]
    public void RingsideAidOnlyItsOwnHandlerTreatsItAndParksAgainWhenTheNeedEnds()
    {
        var (logic, f) = Eliminated();
        f.Char(Prisoner2, 400, 0);
        f.Char(Handler2, 430, 0);
        logic.State.MatchEntries.Add(new MatchEntry<int> { Prisoner = Prisoner2, CageUseable = "c1", CageHand = "c1", Handler = Handler2,
            PrisonerHand = Prisoner2, HandlerHand = Handler2, Released = true });
        logic.TickRingsideAid(f);   // only prisoner 7 is eliminated and needs aid
        Assert.Equal([$"clearai {Handler}", $"stance {Handler}", $"treat {Handler} {Prisoner}"], f.Calls);
        f.Calls.Clear();
        f.NeedsAid.Remove(Prisoner);   // stabilised
        Aid(logic, f, 500);
        Assert.Equal([$"park {Handler}"], f.Calls);   // ApplyParkOrders
        Assert.False(Entry(logic).RingsideTreating);
        Assert.Equal(0.0f, Entry(logic).RingsideTreatElapsed);
    }

    // ---------- the cage choice ----------

    [Fact]
    public void TheOwnCageFreeAndInRangeIsChosenEvenWhenAnotherIsNearer()
    {
        var (logic, f) = Fought(px: 900, hx: 930);
        f.Cage("near", 880, 0);   // nearer to the prisoner
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("c1", Entry(logic).ReturnCageHand);
        Assert.Equal("Returning prisoners to cages...", status);
        Assert.True(logic.State.Returning);
        Assert.Equal(f.Now, logic.State.ReturnLastTick);
    }

    [Fact]
    public void AnOccupiedOwnCageGivesTheNearestFreeOne()
    {
        var (logic, f) = Fought(px: 900, hx: 930);
        f.Buildings["c1"].Occupant = 42; f.Pos[42] = Vector3.Zero;   // someone else in it
        f.Cage("far", -1500, 0);
        f.Cage("near", 880, 0);
        logic.BeginReturnToCages(f, out _);
        Assert.Equal("near", Entry(logic).ReturnCageHand);
    }

    [Fact]
    public void AnOwnCageOutOfTheRegistrysRangeIsNotChosen()
    {
        var (logic, f) = Fought(px: 900, hx: 930);
        f.Buildings["c1"].Pos = PrisonerFake.Registry.Position + new Vector3(2600, 0, 0);   // out of 2,500: not a nearby cage
        f.Cage("near", 1000, 0);
        logic.BeginReturnToCages(f, out _);
        Assert.Equal("near", Entry(logic).ReturnCageHand);
    }

    [Fact]
    public void NoFreeCageIsAFailureWithTheCppText()
    {
        var (logic, f) = Fought();
        f.Buildings["c1"].Occupant = 42; f.Pos[42] = Vector3.Zero;
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("Could not secure Dust Bandit: no free destination cage or pole is available", status);
        Assert.Equal(["Could not secure Dust Bandit: no free destination cage or pole is available"], f.Messages);
        Assert.Contains("Proving Grounds: Could not secure Dust Bandit: no free destination cage or pole is available", f.Debugs);
        Assert.True(logic.HasCageReturnFailure());
        AssertCleared(logic, f);   // nothing pending: the match state is cleared at once
    }

    [Fact]
    public void NoCageAtAllIsAFailureForEveryPrisoner()
    {
        var (logic, f) = Fought();
        f.Buildings.Remove("c1"); f.OwnedCages.Clear();
        f.Invalid.Add(Prisoner);   // the C++ names it "prisoner" when it is not valid
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("Could not secure prisoner: no destination cage or pole is available", status);
        AssertCleared(logic, f);
    }

    [Fact]
    public void ADeadPrisonerIsSkippedWithAFailure()
    {
        var (logic, f) = Fought();
        f.Dead.Add(Prisoner);
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("Could not secure Dust Bandit: prisoner is missing or dead", status);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        AssertCleared(logic, f);
    }

    [Fact]
    public void ASecondBeginWhileReturningChangesNothingAndGivesTheStatus()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        f.Calls.Clear();
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("", status);   // g_cageReturnStatus, empty
        Assert.Empty(f.Calls);
        Assert.True(logic.State.Returning);
    }

    [Fact]
    public void NoEntriesClearsTheMatchState()
    {
        var logic = new PrisonerUtilLogic<int>();
        var f = new PrisonerFake();
        logic.State.Unlocking = true;
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("", status);
        Assert.False(logic.HasRuntimeActivity());
    }

    // ---------- walk ----------

    [Fact]
    public void AConsciousPrisonerRunsBackWithItsHandlerAndIsImprisonedWithin36()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        var hp = HandlerTarget();
        Assert.Equal([$"stance {Handler}", $"clearai {Prisoner}", $"clearai {Handler}",
            $"stance {Prisoner}", Move(Prisoner, Cage1), $"run {Prisoner}", $"stance {Handler}", Move(Handler, hp), $"run {Handler}"], f.Calls);
        Assert.True(Entry(logic).ReturnEscorting);
        f.Calls.Clear();

        f.Pos[Prisoner] = Cage1 + new Vector3(36.5f, 0, 0);   // not yet
        Return(logic, f, 500);
        Assert.Contains(Move(Prisoner, Cage1), f.Calls);
        f.Calls.Clear();
        f.Pos[Prisoner] = Cage1 + new Vector3(36f, 50, 0);    // within 36 on x/z (height not measured)
        Return(logic, f, 500);
        Assert.Equal([$"clearq {Handler}", $"imprison {Prisoner} c1", $"chain {Prisoner}"], f.Calls);
        Assert.Equal(Prisoner, f.Buildings["c1"].Occupant);
        AssertCleared(logic, f);   // secured at once: Imprison locked the cage
        Assert.False(logic.HasCageReturnFailure());
    }

    /// <summary>HandlerTransitTarget toward cage c1 for entry 0 (the C++ passes index 0 from BeginReturnMovement), origin = the original cage.</summary>
    static Vector3 HandlerTarget() => Cage1 + new Vector3(1, 0, 0) * PrisonerUtilLogic<int>.HandlerSideOffset;

    [Fact]
    public void AWalkTimesOutAt60sAndThePrisonerIsPlacedAnyway()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        for (int i = 0; i < 59; i++) Return(logic, f, 1000);
        Assert.True(logic.State.Returning);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        Return(logic, f, 1000);   // 60 s
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void APrisonerDyingOnTheWayIsAFailure()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        f.Dead.Add(Prisoner);
        Return(logic, f, 500);
        Assert.Equal("Could not secure Dust Bandit: prisoner is missing or dead", logic.GetCageReturnStatus());
        AssertCleared(logic, f);
    }

    // ---------- treat, then carry ----------

    [Fact]
    public void AnUnconsciousPrisonerIsTreatedUpTo20sThenCarriedWithA25sPickUpRetry()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        f.NeedsAid.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);
        Assert.Equal([$"stance {Handler}", $"treat {Handler} {Prisoner}"], f.Calls);
        Assert.True(Entry(logic).ReturnTreating);
        Assert.True(Entry(logic).ReturnCarrying);
        f.Calls.Clear();
        for (int i = 0; i < 2; i++) Return(logic, f, 1000);   // 2 s
        Assert.Empty(f.Calls);
        Return(logic, f, 1000);                               // 3 s: retried
        Assert.Equal([$"treat {Handler} {Prisoner}"], f.Calls);
        f.Calls.Clear();
        for (int i = 0; i < 16; i++) Return(logic, f, 1000);  // 19 s: retries at 6, 9, 12, 15, 18
        Assert.Equal(5, f.Calls.Count(c => c == $"treat {Handler} {Prisoner}"));
        f.Calls.Clear();
        Return(logic, f, 1000);                               // 20 s: the cap, the carry starts
        Assert.Equal([$"clearai {Prisoner}", $"clearai {Handler}", $"stance {Handler}", $"lift {Handler} {Prisoner}"], f.Calls);
        Assert.False(Entry(logic).ReturnTreating);
        Assert.Equal(0.0f, Entry(logic).ReturnElapsed);
        f.Calls.Clear();

        for (int i = 0; i < 4; i++) Return(logic, f, 500);    // 2.0 s: not yet
        Assert.Empty(f.Calls);
        Return(logic, f, 500);                                // 2.5 s: the lift asked again
        Assert.Equal([$"stance {Handler}", $"lift {Handler} {Prisoner}"], f.Calls);
        f.Calls.Clear();

        f.Carrying.Add(Handler); f.Carried.Add(Prisoner);     // picked up
        Return(logic, f, 500);
        Assert.Equal([$"stance {Handler}", Move(Handler, Cage1)], f.Calls);
        Assert.True(Entry(logic).ReturnPickedUp);
        f.Calls.Clear();
        f.Pos[Handler] = Cage1 + new Vector3(0, 0, 30);       // the carrier within 36
        Return(logic, f, 500);
        Assert.Equal([$"drop {Handler}", $"clearq {Handler}", $"imprison {Prisoner} c1", $"chain {Prisoner}"], f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void TheTreatmentEndsAsSoonAsThePrisonerIsStable()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        f.NeedsAid.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);
        f.Calls.Clear();
        f.NeedsAid.Remove(Prisoner);
        Return(logic, f, 500);
        Assert.Contains($"lift {Handler} {Prisoner}", f.Calls);
    }

    [Fact]
    public void AConsciousPrisonerThatNeedsAidIsTreatedThenWalks()
    {
        var (logic, f) = Fought();
        f.NeedsAid.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);
        Assert.True(Entry(logic).ReturnTreating && Entry(logic).ReturnEscorting);
        f.NeedsAid.Remove(Prisoner);
        f.Calls.Clear();
        Return(logic, f, 500);
        Assert.Contains(Move(Prisoner, Cage1), f.Calls);
    }

    [Fact]
    public void ACarryNeverPickedUpIsPlacedAt60s()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);   // no aid needed: the lift at once
        Assert.Contains($"lift {Handler} {Prisoner}", f.Calls);
        for (int i = 0; i < 59; i++) Return(logic, f, 1000);
        Assert.Equal(1 + 23, f.Calls.Count(c => c == $"lift {Handler} {Prisoner}"));   // the first + every 2.5 s up to 59 s
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        Return(logic, f, 1000);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void ACarriedPrisonerIsPlacedWhenTheCarryTimesOut()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);
        f.Carrying.Add(Handler);
        Return(logic, f, 500);   // picked up
        for (int i = 0; i < 60; i++) Return(logic, f, 1000);
        Assert.Contains($"drop {Handler}", f.Calls);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void WithoutALivingHandlerAnUnconsciousPrisonerIsPlacedAtOnce()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        f.Dead.Add(Handler);
        f.NeedsAid.Add(Prisoner);   // no one can treat it
        logic.BeginReturnToCages(f, out _);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("treat") || c.StartsWith("lift"));
        AssertCleared(logic, f);
    }

    // ---------- clocks and edge cases (review fixes) ----------

    [Fact]
    public void AReturnTickClampsToOneSecondTowardThe60sTimeout()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        Return(logic, f, 10000);   // a 10 s gap counts 1 s
        Assert.Equal(1.0f, Entry(logic).ReturnElapsed);
        for (int i = 0; i < 58; i++) Return(logic, f, 10000);   // 59 s
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        Return(logic, f, 10000);   // 60 s
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void AClampedGapCrossesAtMostOneLiftRetrySlot()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);
        f.Calls.Clear();
        Return(logic, f, 10000);   // 1 s: slot 0
        Return(logic, f, 10000);   // 2 s
        Assert.Empty(f.Calls);
        Return(logic, f, 10000);   // 3 s: slot 1, one retry
        Assert.Equal([$"stance {Handler}", $"lift {Handler} {Prisoner}"], f.Calls);
    }

    [Fact]
    public void TheReturnClockCrossesThe32BitWrap()
    {
        var (logic, f) = Fought();
        f.Now = uint.MaxValue - 500;
        logic.BeginReturnToCages(f, out _);
        Return(logic, f, 1000);   // wraps: 1 s
        Assert.Equal(1.0f, Entry(logic).ReturnElapsed);
    }

    [Fact]
    public void ASecondBeginWhileOnlyALockIsPendingChangesNothing()
    {
        var (logic, f) = Fought();
        f.LockWontEngage.Add("c1");
        logic.ReturnAllToCages(f, out _);
        Assert.True(logic.HasPendingCageLocks());
        logic.State.Returning = false;   // only the pending lock holds it (the HasPendingCageLocks half of :1412)
        uint started = Entry(logic).LockStartedAt;
        f.Calls.Clear();
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("", status);
        Assert.Empty(f.Calls);
        Assert.True(Entry(logic).LockPending);
        Assert.Equal(started, Entry(logic).LockStartedAt);   // the timeout is not restarted
    }

    static (PrisonerUtilLogic<int>, PrisonerFake) PickedUp()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        logic.BeginReturnToCages(f, out _);
        f.Carrying.Add(Handler);
        Return(logic, f, 500);
        Assert.True(Entry(logic).ReturnPickedUp);
        f.Calls.Clear();
        return (logic, f);
    }

    [Fact]
    public void AHandlerDyingAfterThePickUpEndsAtTheTimeout()
    {
        var (logic, f) = PickedUp();
        f.Dead.Add(Handler);
        for (int i = 0; i < 59; i++) Return(logic, f, 1000);   // 59.5 s: walking on (IssueMove does not check death, as the C++)
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        Return(logic, f, 1000);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void AnInvalidHandlerAfterThePickUpIsNotMovedAndTheTimeoutPlacesThePrisoner()
    {
        var (logic, f) = PickedUp();
        f.Invalid.Add(Handler);
        Return(logic, f, 1000);
        Assert.Empty(f.Calls);
        for (int i = 0; i < 59; i++) Return(logic, f, 1000);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        Assert.False(logic.HasRuntimeActivity());
    }

    [Fact]
    public void APrisonerWakingWhileCarriedIsStillCarriedToTheCage()
    {
        var (logic, f) = PickedUp();
        f.Unconscious.Remove(Prisoner);
        Return(logic, f, 500);
        Assert.Contains(Move(Handler, Cage1), f.Calls);   // still the carry path
        f.Pos[Handler] = Cage1;
        Return(logic, f, 500);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void AHostRefusingTheImprisonStillEndsTheReturn()
    {
        // the glue reads a throwing engine write as refused (PrisonerUtil.Return.cs Guarded): the logic then ends on its own failure
        var (logic, f) = Fought();
        f.ImprisonRefused.Add("c1");
        logic.BeginReturnToCages(f, out _);
        for (int i = 0; i < 60; i++) Return(logic, f, 1000);   // the walk's 60 s timeout, then the placement refused
        Assert.Equal("Could not secure Dust Bandit: destination does not contain the expected prisoner", logic.GetCageReturnStatus());
        Assert.False(logic.HasRuntimeActivity());
        Assert.False(logic.State.Returning);
        logic.PrepareMatch(f, [], [], out string prep);   // a next match is not refused
        Assert.Equal("", prep);
    }

    [Fact]
    public void RefusedTreatAndLiftOrdersStillEndTheReturnAtTheTimeouts()
    {
        var (logic, f) = Fought();
        f.Unconscious.Add(Prisoner);
        f.NeedsAid.Add(Prisoner);   // the treat order does nothing in the fake: the need stays
        logic.BeginReturnToCages(f, out _);
        for (int i = 0; i < 20; i++) Return(logic, f, 1000);   // the 20 s treatment cap
        Assert.False(Entry(logic).ReturnTreating);
        for (int i = 0; i < 60; i++) Return(logic, f, 1000);   // never picked up: the 60 s carry timeout
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    // ---------- the lock check ----------

    [Fact]
    public void AnUnreleasedPrisonerIsCheckedInItsOwnCageWithoutMoving()
    {
        var (logic, f) = Fought(released: false);
        logic.BeginReturnToCages(f, out _);
        Assert.Equal([$"clearq {Handler}", $"imprison {Prisoner} c1", $"chain {Prisoner}"], f.Calls);   // no walk, no carry: placed and checked
        AssertCleared(logic, f);
    }

    [Fact]
    public void ARingsideTickOf100msAddsUpAsTheCppFloatDoes()
    {
        // 30 steps of 0.1f sum to 2.9999998f, under the 3 s slot, as the C++'s float does: the retry comes one step later
        var (logic, f) = Eliminated();
        logic.TickRingsideAid(f);
        f.Calls.Clear();
        for (int i = 0; i < 30; i++) Aid(logic, f, 100);
        Assert.Empty(f.Calls);
        Aid(logic, f, 100);
        Assert.Equal(2, f.Calls.Count);
    }

    static void PlaceAndFail(Action<PrisonerFake> setup, string reason, bool cleared = true)
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        setup(f);
        f.Pos[Prisoner] = Cage1;
        Return(logic, f, 500);
        Assert.Equal("Could not secure Dust Bandit: " + reason, logic.GetCageReturnStatus());
        Assert.Equal(["Could not secure Dust Bandit: " + reason], f.Messages);
        if (cleared) AssertCleared(logic, f);
    }

    [Fact]
    public void EachLockFailureHasTheCppText()
    {
        PlaceAndFail(p => p.ImprisonRefused.Add("c1"), "destination does not contain the expected prisoner");
        PlaceAndFail(p => p.NoLock.Add("c1"), "destination has no operable lock");
        PlaceAndFail(p => p.Verifier = false, "native cage lock verification is unavailable");
        PlaceAndFail(p => { p.LockWontEngage.Add("c1"); p.Unconscious.Add(Handler); }, "no conscious handler can lock the destination");
        PlaceAndFail(p => { p.LockWontEngage.Add("c1"); p.RejectLockOrder = true; }, "native cage lock order was rejected");
    }

    [Fact]
    public void ABrokenCageIsNotFilledAndFailsAsBroken()
    {
        // isBroken at the arrival: FinishCageEntry places nobody (canPlace false), the check says "broken"
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        f.Broken.Add("c1");
        Return(logic, f, 500);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        Assert.Equal("Could not secure Dust Bandit: destination cage or pole is broken", logic.GetCageReturnStatus());
        AssertCleared(logic, f);
    }

    [Fact]
    public void ADestroyedCageIsAMissingDestination()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        f.Destroyed.Add("c1");
        Return(logic, f, 500);   // ResolveReturnReferences drops it: FinishCageEntry at once, nothing placed
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("imprison"));
        Assert.Equal("Could not secure Dust Bandit: destination cage or pole is missing or destroyed", logic.GetCageReturnStatus());
        AssertCleared(logic, f);
    }

    [Fact]
    public void ALockThatDoesNotEngageWaits30sThenTimesOut()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        f.LockWontEngage.Add("c1");   // placed, but the lock does not count as locked (broken or level 0)
        f.Pos[Prisoner] = Cage1;
        Return(logic, f, 500);
        Assert.Contains($"checklock {Handler} c1", f.Calls);
        Assert.Contains($"lockorder {Handler} c1", f.Calls);
        Assert.True(logic.HasPendingCageLocks());
        Assert.True(Entry(logic).LockOrderIssued);
        uint started = Entry(logic).LockStartedAt;
        f.Calls.Clear();
        // the lock is verified every tick; ClearMatchState refuses while a lock is pending
        logic.ClearMatchState(f);
        Assert.Single(logic.State.MatchEntries);
        f.Now = started + 29999;
        logic.TickReturn(f);
        Assert.True(logic.HasPendingCageLocks());
        Assert.Empty(f.Messages);
        f.Now = started + 30000;
        logic.TickReturn(f);
        Assert.Equal("Could not secure Dust Bandit: native lock did not engage before timeout (blocked or broken lock)", logic.GetCageReturnStatus());
        Assert.Contains($"clearq {Handler}", f.Calls);   // the issued order's queue cleared
        AssertCleared(logic, f);
    }

    [Fact]
    public void ALockThatEngagesLateIsSecured()
    {
        var (logic, f) = Fought();
        logic.BeginReturnToCages(f, out _);
        f.LockWontEngage.Add("c1");
        f.Pos[Prisoner] = Cage1;
        Return(logic, f, 500);
        Assert.True(logic.HasPendingCageLocks());
        f.LockWontEngage.Remove("c1");
        Return(logic, f, 5000);
        Assert.False(logic.HasCageReturnFailure());
        AssertCleared(logic, f);
    }

    [Fact]
    public void FailureTextsAreJoinedByNewlinesAndClearedByTheNextPrepare()
    {
        var (logic, f) = Fought();
        f.Char(Prisoner2, 400, 0);
        f.Names[Prisoner2] = "Hungry Bandit";
        logic.State.MatchEntries.Add(new MatchEntry<int> { Prisoner = Prisoner2, CageUseable = "c1", CageHand = "c1", Handler = Handler2,
            PrisonerHand = Prisoner2, HandlerHand = Handler2, Released = true });
        f.Dead.Add(Prisoner); f.Dead.Add(Prisoner2);
        logic.BeginReturnToCages(f, out string status);
        Assert.Equal("Could not secure Dust Bandit: prisoner is missing or dead\nCould not secure Hungry Bandit: prisoner is missing or dead", status);
        // PrepareMatch (task 6.1) clears it for the next match
        logic.PrepareMatch(f, [], [], out _);
        Assert.False(logic.HasCageReturnFailure());
        Assert.Equal("", logic.GetCageReturnStatus());
    }

    [Fact]
    public void TwoPrisonersReserveDifferentCages()
    {
        var (logic, f) = Fought(px: 900, hx: 930);
        f.Cage("c2", 950, 0);
        f.Char(Prisoner2, 905, 0); f.Char(Handler2, 935, 0);
        logic.State.MatchEntries.Add(new MatchEntry<int> { Prisoner = Prisoner2, CageUseable = "c1", CageHand = "c1", Handler = Handler2,
            PrisonerHand = Prisoner2, HandlerHand = Handler2, Released = true });
        logic.BeginReturnToCages(f, out _);
        Assert.Equal("c1", logic.State.MatchEntries[0].ReturnCageHand);   // its own
        Assert.Equal("c2", logic.State.MatchEntries[1].ReturnCageHand);   // c1 reserved: the nearest other free one
    }

    // ---------- ReturnAllToCages ----------

    [Fact]
    public void ReturnAllToCagesPlacesEveryoneAtOnceAndClears()
    {
        var (logic, f) = Fought(px: 1500, hx: 1530);   // far from the cage
        logic.ReturnAllToCages(f, out string status);
        Assert.Equal("", status);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void ReturnAllToCagesAfterABeginForcesThePendingReturn()
    {
        var (logic, f) = Fought(px: 1500, hx: 1530);
        logic.BeginReturnToCages(f, out _);   // SparSession's stop
        Assert.True(logic.State.Returning);
        logic.ReturnAllToCages(f, out string status);   // then the save
        Assert.Equal("", status);
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        AssertCleared(logic, f);
    }

    [Fact]
    public void ReturnAllToCagesWaitsForALockThatDidNotEngage()
    {
        var (logic, f) = Fought();
        f.LockWontEngage.Add("c1");
        logic.ReturnAllToCages(f, out string status);
        Assert.Equal("Waiting for native cage locks before return completes...", status);
        Assert.True(logic.State.Returning);
        Assert.True(logic.HasPendingCageLocks());
    }

    [Fact]
    public void ReturnAllToCagesGivesTheFailureText()
    {
        var (logic, f) = Fought();
        f.NoLock.Add("c1");
        logic.ReturnAllToCages(f, out string status);
        Assert.Equal("Could not secure Dust Bandit: destination has no operable lock", status);
        AssertCleared(logic, f);
    }

    [Fact]
    public void TheLockClockCrossesThe32BitWrap()
    {
        var (logic, f) = Fought();
        f.Now = uint.MaxValue - 1000;
        f.LockWontEngage.Add("c1");
        logic.ReturnAllToCages(f, out _);
        f.Now = unchecked(f.Now + 29000);
        logic.TickPendingCageLocks(f);
        Assert.True(logic.HasPendingCageLocks());
        f.Now = unchecked(f.Now + 1000);
        logic.TickPendingCageLocks(f);
        Assert.False(logic.HasPendingCageLocks());
        Assert.True(logic.HasCageReturnFailure());
    }

    // ---------- the save (task 2.5's SaveSafety over the real return) ----------

    /// <summary>ISaveSafetyWorld whose prisoner members are the real PrisonerUtilLogic over the fake; a running match (SparSession's abort runs
    /// BeginReturnToCages, as SparSession.cpp:740 in StopWithStatus).</summary>
    sealed class PrisonerSaveWorld(PrisonerUtilLogic<int> logic, PrisonerFake f) : ISaveSafetyWorld
    {
        public bool Match = true;
        public readonly List<string> Shown = [], Logged = [];
        public bool IngressPending => false;
        public bool WalkInPending => false;
        public bool MatchActive => Match;
        public bool PrisonerRuntimeActivity => logic.HasRuntimeActivity();
        public bool PendingCageLocks => logic.HasPendingCageLocks();
        public bool CageReturnFailure => logic.HasCageReturnFailure();
        public bool TownBusy => false;
        public bool TownHasBooking => false;
        public bool LimbShopVisitActive => false;
        public bool LimbShopPhaseActive => false;
        public string CageReturnStatus() => logic.GetCageReturnStatus();
        public void LimbShopSuspendVisit() { }
        public void TownCancel() { }
        public void TownCancelForSave() { }
        public void IngressCancel() { }
        public void SparAbortForSave() { Match = false; logic.BeginReturnToCages(f, out _); }
        public string PrisonersReturnAllToCages() { logic.ReturnAllToCages(f, out string s); return s; }
        public void ResultsClose() { }
        public void SparStatsAbandon() { }
        public void IngressAbandon() { }
        public void SparSessionAbandon() { }
        public void PrisonerAbandon() => logic.AbandonWorldState();
        public void FightStarterAbandon() { }
        public void ArenaUiAbandon() { }
        public void DebugMenuAbandon() { }
        public void Message(string text) => Shown.Add(text);
        public void Log(string text) => Logged.Add(text);
    }

    [Fact]
    public void AManualSaveDuringAPrisonerMatchForcesThePrisonerBackAndSavesAfterTheLock()
    {
        var (logic, f) = Fought(px: 1500, hx: 1530);
        var w = new PrisonerSaveWorld(logic, f);
        var s = new SaveSafety(w);
        var r = s.ManualSave("pgtest", false);
        Assert.True(r.Requested);   // the lock is verified in the same call: saved
        Assert.Contains($"imprison {Prisoner} c1", f.Calls);
        Assert.Equal(Prisoner, f.Buildings["c1"].Occupant);
        Assert.False(logic.HasRuntimeActivity());
        Assert.Equal(SaveSafety.MatchCancelledText, w.Shown[0]);
    }

    [Fact]
    public void AManualSaveWaitsForALockAndIsCancelledWhenItTimesOut()
    {
        var (logic, f) = Fought();
        f.LockWontEngage.Add("c1");
        var w = new PrisonerSaveWorld(logic, f);
        var s = new SaveSafety(w);
        Assert.False(s.ManualSave("pgtest", false).Requested);
        Assert.True(s.ManualSaveWaiting);
        Assert.Equal("Manual save waiting for cage locks. Unpause so the handler can finish locking; a timeout will cancel this save.", w.Shown[^1]);
        f.Now += LockWait;
        logic.TickReturn(f);   // the frame loop's TickReturn verifies the lock: timed out
        Assert.False(s.Tick().Requested);
        Assert.Equal("Manual save CANCELLED: prisoner return was not secured: Could not secure Dust Bandit: native lock did not engage before timeout "
            + "(blocked or broken lock). Check the prisoner/cage. A later manual save can save the current unsecured world.", w.Shown[^1]);
    }

    const uint LockWait = PrisonerUtilLogic<int>.LockTimeoutMs;

    [Fact]
    public void TheConstantsAreTheCpps()
    {
        Assert.Equal(36.0f, PrisonerUtilLogic<int>.ReturnArriveRadius);
        Assert.Equal(60.0f, PrisonerUtilLogic<int>.ReturnTimeoutSec);
        Assert.Equal(30000u, PrisonerUtilLogic<int>.LockTimeoutMs);
        Assert.Equal(2.5f, PrisonerUtilLogic<int>.CarryPickupRetrySec);
        Assert.Equal(20.0f, PrisonerUtilLogic<int>.TreatmentTimeoutSec);
        Assert.Equal(3.0f, PrisonerUtilLogic<int>.TreatmentRetrySec);
    }
}

/// <summary>The view's own-carrier exemption (task 6.3): a match prisoner's handler may lift it; any other carrier still may not.</summary>
public class OwnCarrierTests
{
    [Fact]
    public void TheHandlerMayLiftItsPrisonerAndNobodyElse()
    {
        var view = new CombatMatchView<int>(-1, false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7, 8], [3, -1]);
        Assert.False(view.RefusesPickUp(3, 7));   // its own handler
        Assert.True(view.RefusesPickUp(9, 7));    // an outsider
        Assert.True(view.RefusesPickUp(3, 8));    // another prisoner's body
        var noHandlers = new CombatMatchView<int>(-1, false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7]);
        Assert.True(noHandlers.RefusesPickUp(3, 7));
        Assert.Throws<ArgumentException>(() => new CombatMatchView<int>(-1, false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7], [3, 4]));
    }

    [Fact]
    public void TheHandlersAreComparedByMatches()
    {
        var view = new CombatMatchView<int>(-1, false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7], [3]);
        Assert.True(view.Matches(false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7], [3]));
        Assert.False(view.Matches(false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7], [4]));
        Assert.False(view.Matches(false, MatchRules.MatchMode.TeamAvB, [], [], [], -1, -1, false, [], [7]));
    }
}
