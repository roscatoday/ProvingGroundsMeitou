using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>FightStarter.cpp's orchestration (Core/Glue/FightStarterLogic.cs) over a fake world: who is disengaged how, what is remembered and
/// restored, the late stop, the in-match retarget, the hit reaction and the keep-alive (E3).</summary>
public class FightStarterLogicTests
{
    sealed class Ch
    {
        public bool Exists = true, Dead, Unconscious, Player, Jobs, Stance = true, InMelee;
        public float X;
        public int Target = -1;
        public bool InFight;
    }

    /// <summary>Ids are ints, -1 is none; the remembered orders are a string ("stance on/off").</summary>
    sealed class FakeWorld : IFightWorld<int, string>
    {
        public readonly Dictionary<int, Ch> C = [];
        public long Tick { get; set; } = 10;
        public readonly List<int> EndTasks = [], Restores = [], JobsOn = [];
        public readonly List<(int A, int T, bool Player, bool Queued)> Attacks = [];
        public readonly List<(int[] Ids, int Ahead)> Stops = [];
        public Func<int, bool>? ThrowOnAttack, ThrowOnRestore;

        public Ch Add(int id, float x = 0, bool player = true, bool jobs = false)
        {
            var c = new Ch { X = x, Player = player, Jobs = jobs };
            C[id] = c;
            return c;
        }

        Ch? Get(int id) => C.TryGetValue(id, out var c) && c.Exists ? c : null;
        public int None => -1;
        public bool IsNone(int c) => c < 0;
        public bool Exists(int c) => Get(c) is not null;
        public bool IsDead(int c) => Get(c) is { Dead: true };
        public bool IsUnconscious(int c) => Get(c) is { Unconscious: true };
        public bool IsPlayer(int c) => Get(c) is { Player: true };
        public bool TryGetPosition(int c, out float x, out float y, out float z)
        {
            y = z = 0; x = Get(c)?.X ?? 0;
            return Get(c) is not null;
        }
        public bool TryGetOrders(int c, out string orders)
        {
            orders = Get(c) is { } ch ? (ch.Stance ? "stance on" : "stance off") : "";
            return Get(c) is not null;
        }
        public void SetCombatStance(int c) { if (Get(c) is { } ch) ch.Stance = false; }
        public bool SetJobsOff(int c)
        {
            if (Get(c) is not { Player: true, Jobs: true } ch) return false;
            ch.Jobs = false;
            return true;
        }
        public void SetJobsOn(int c) { JobsOn.Add(c); if (Get(c) is { } ch) ch.Jobs = true; }
        public void RestoreStance(int c, string saved)
        {
            if (ThrowOnRestore?.Invoke(c) == true) throw new InvalidOperationException("restore failed");
            Restores.Add(c);
            if (Get(c) is { } ch) ch.Stance = saved == "stance on";
        }
        public void Attack(int attacker, int target, bool player, bool queued)
        {
            if (ThrowOnAttack?.Invoke(attacker) == true) throw new InvalidOperationException("attack failed");
            Attacks.Add((attacker, target, player, queued));
        }
        public void EndTask(int c) => EndTasks.Add(c);
        public void Stop(IReadOnlyList<int> fighters, int ticksAhead) => Stops.Add(([.. fighters], ticksAhead));
        public int AttackTarget(int c) => Get(c)?.Target ?? -1;
        public bool InMeleeCombatMode(int c) => Get(c) is { InMelee: true };
        public bool InFight(int c) => Get(c) is { InFight: true };
    }

    sealed class FakeSession(MatchMode mode) : IFightSession<int>
    {
        public bool IsActive => true;
        public MatchMode Mode => mode;
        public bool IsEliminated(int c) => false;
        public bool IsSparringOpponent(int a, int b) => a != b;
    }

    static FightStarterLogic<int, string> New(List<string>? log = null) => new(log is null ? null : log.Add);

    // ---------- Meitou-only: the stale-target sweep (task 3.5) ----------

    static (FakeWorld W, FightStarterLogic<int, string> L) Disengaged()
    {
        var w = new FakeWorld();
        w.Add(1); w.Add(2);
        var l = new FightStarterLogic<int, string>();
        l.EngageMatch(w, [1, 2], [MatchTeam.TeamA, MatchTeam.TeamB], 2, MatchMode.TeamAvB);
        l.DisengageMatch(w, [1, 2], 2);
        w.Stops.Clear();
        return (w, l);
    }

    [Fact]
    public void Sweep_StopsATargetLeftWithoutAFight_WithinTheWindow()
    {
        var (w, l) = Disengaged();
        Assert.Equal(2, l.StaleSweepCount);
        w.Tick += 5;
        w.C[2].Target = 1;          // an initiative yield turned the stopped fighter on its opponent: a target, no fight task
        l.SweepStaleTargets(w);
        Assert.Equal([2], StopsAt(w, 1));
        w.Stops.Clear();
        w.C[2].Target = -1;         // the stop dropped it
        l.SweepStaleTargets(w);
        Assert.Empty(w.Stops);
    }

    [Fact]
    public void Sweep_NeverStopsARealFight_NorADownedFighter()
    {
        var (w, l) = Disengaged();
        w.C[1].Target = 2; w.C[1].InFight = true;          // a new fight (a player's order after the match)
        w.C[2].Target = 1; w.C[2].Unconscious = true;      // down: the combat step drops its target itself
        l.SweepStaleTargets(w);
        Assert.Empty(w.Stops);
    }

    [Fact]
    public void Sweep_EndsAfterTheWindow_AndForAFighterEngagedAgain()
    {
        var (w, l) = Disengaged();
        l.EngageMatch(w, [1, 2], [MatchTeam.TeamA, MatchTeam.TeamB], 2, MatchMode.TeamAvB);   // a new match: off the sweep
        Assert.Equal(0, l.StaleSweepCount);
        l.DisengageMatch(w, [1, 2], 2);
        w.Stops.Clear();
        w.Tick += FightStarterLogic<int, string>.StaleSweepTicks + 1;
        w.C[1].Target = 2;
        l.SweepStaleTargets(w);
        Assert.Empty(w.Stops);
        Assert.Equal(0, l.StaleSweepCount);
    }

    [Fact]
    public void Sweep_AWalkInFighterLeavesIt_SoItsMoveIsNotStopped()
    {
        var (w, l) = Disengaged();
        l.LeaveStaleSweep(2);   // ArenaIngress.BeginWalkIn (task 4.3b), before the walk-in's move order
        Assert.Equal(1, l.StaleSweepCount);
        w.Tick += 5;
        w.C[2].Target = 1;      // still a target left, walking (no fight)
        l.SweepStaleTargets(w);
        Assert.Empty(w.Stops);  // no StopCommand: the walk goes on
    }

    [Fact]
    public void Sweep_ForgottenByAbandonWorldState()
    {
        var (w, l) = Disengaged();
        l.AbandonWorldState();
        Assert.Equal(0, l.StaleSweepCount);
    }

    static int[] StopsAt(FakeWorld w, int ahead) => [.. w.Stops.Where(s => s.Ahead == ahead).SelectMany(s => s.Ids)];

    [Fact]
    public void DisengageEndsAndRestoresALivingFighterButOnlyStopsADeadOne()
    {
        // C++ :370-420: a living fighter gets the cleanup, the stance and Jobs back; a dead one only endCombatMode (the stop)
        var w = new FakeWorld();
        w.Add(1, jobs: true); w.Add(2, jobs: true);
        var fs = New();
        fs.Engage(w, 1, 2);
        w.C[2].Dead = true;
        w.Tick++;
        fs.Disengage(w, 1, 2);

        Assert.Equal([1], w.EndTasks);
        Assert.Equal([1], w.Restores);
        Assert.Equal([1], w.JobsOn);
        Assert.Equal([1, 2], StopsAt(w, 1));
        Assert.True(w.C[1].Stance);
        Assert.Equal(0, fs.RememberedCount);   // the dead fighter's state is erased too (:422-426)
    }

    [Fact]
    public void DisengageErasesTheRememberedStateOfAStaleId()
    {
        var w = new FakeWorld();
        w.Add(1); w.Add(2);
        var fs = New();
        fs.Engage(w, 1, 2);
        Assert.Equal(2, fs.RememberedCount);
        w.C[2].Exists = false;   // gone between engage and disengage
        w.Tick++;
        fs.Disengage(w, 1, 2);

        Assert.Equal(0, fs.RememberedCount);
        Assert.False(fs.TryGetRemembered(2, out _));
        Assert.False(fs.TryGetAssignedTarget(2, out _));
        Assert.Equal([1], StopsAt(w, 1));      // only a valid fighter is stopped
        Assert.DoesNotContain(2, w.EndTasks);
    }

    [Fact]
    public void JobsComeBackOnlyWhereTheModTurnedThemOff()
    {
        // C++ :70-104 SetJobsEnabled(c, false) changes only a player character with Jobs on; :413-414 restores only those
        var w = new FakeWorld();
        w.Add(1, jobs: true);                  // player, Jobs on: turned off, restored
        w.Add(2, jobs: false);                 // player, Jobs already off: left off
        w.Add(3, x: 1, player: false, jobs: true);   // NPC: never touched
        var fs = New();
        fs.EngageMatch(w, [1, 2, 3], [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB], 3, MatchMode.TeamAvB);
        Assert.False(w.C[1].Jobs);
        Assert.True(fs.TryGetRemembered(1, out var s1) && s1.JobsToggledOff);
        Assert.True(fs.TryGetRemembered(2, out var s2) && !s2.JobsToggledOff);
        Assert.True(w.C[3].Jobs);
        w.Tick++;
        fs.DisengageMatch(w, [1, 2, 3], 3);

        Assert.Equal([1], w.JobsOn);
        Assert.True(w.C[1].Jobs);
        Assert.False(w.C[2].Jobs);
    }

    [Fact]
    public void TheLateStopGoesOnlyToFightersOrderedInTheDisengagesTick()
    {
        var w = new FakeWorld();
        w.Add(1); w.Add(2);
        var fs = New();
        fs.Engage(w, 1, 2);          // both ordered at tick 10
        fs.Disengage(w, 1, 2);       // same tick: the stop and a second stop at tick+2

        Assert.Equal([1, 2], StopsAt(w, 1));
        Assert.Equal([1, 2], StopsAt(w, 2));

        var w2 = new FakeWorld();
        w2.Add(1); w2.Add(2);
        var fs2 = New();
        fs2.Engage(w2, 1, 2);
        w2.Tick++;                   // a later tick: no late stop
        fs2.Disengage(w2, 1, 2);
        Assert.Equal([1, 2], StopsAt(w2, 1));
        Assert.Empty(StopsAt(w2, 2));
    }

    [Fact]
    public void RetargetKeepsTheRememberedStateAndJobs()
    {
        // C++ :430-453, :560-563: the stale order ends, the standing orders and Jobs stay until the full disengage
        var w = new FakeWorld();
        w.Add(1, jobs: true); w.Add(2, x: 1, jobs: true);
        var fs = New();
        fs.Engage(w, 1, 2);
        Assert.True(fs.TryGetAssignedTarget(1, out _));
        w.Tick++;
        fs.RetargetMatchAfterElimination(w, [1, 2], 2, null, null, 0, MatchMode.TeamAvB);

        Assert.Equal([1, 2], w.EndTasks);
        Assert.Equal(2, fs.RememberedCount);
        Assert.Empty(w.JobsOn);
        Assert.Empty(w.Restores);
        Assert.False(w.C[1].Jobs);
        Assert.False(w.C[1].Stance);
        Assert.False(fs.TryGetAssignedTarget(1, out _));
        Assert.Empty(w.Stops);
    }

    [Fact]
    public void AHitIsAnsweredOncePerTickBecauseTheFirstAnswerBecomesTheAssignedTarget()
    {
        // C++ :573-589: an opponent that is not the assigned target turns the defender; a second hit from it is from the assigned target
        var w = new FakeWorld();
        w.Add(1, x: 0); w.Add(2, x: 1); w.Add(3, x: 5);
        var fs = New();
        fs.EngageMatch(w, [1, 2, 3], [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB], 3, MatchMode.LastStanding);
        Assert.True(fs.TryGetAssignedTarget(1, out int t) && t == 2);   // nearest
        w.Attacks.Clear();
        w.Tick++;

        fs.RecordIncomingAttack(1, 2);   // the assigned target hitting: no change
        fs.RecordIncomingAttack(1, 3);
        fs.RecordIncomingAttack(1, 3);
        fs.ApplyIncomingAttacks(w, new FakeSession(MatchMode.LastStanding));

        Assert.Equal([(1, 3, true, false)], w.Attacks);
        Assert.True(fs.TryGetAssignedTarget(1, out t) && t == 3);
    }

    [Fact]
    public void NoHitReactionInTeams1v1OrWithoutASession()
    {
        var w = new FakeWorld();
        w.Add(1); w.Add(2); w.Add(3);
        var fs = New();
        fs.RecordIncomingAttack(1, 3);
        fs.ApplyIncomingAttacks(w, null);
        fs.RecordIncomingAttack(1, 3);
        fs.ApplyIncomingAttacks(w, new FakeSession(MatchMode.Teams1v1));
        Assert.Empty(w.Attacks);
    }

    [Fact]
    public void KeepAliveReordersOnlyAFighterWithoutALivingOpponentAsTarget()
    {
        // C++ :600-656
        var w = new FakeWorld();
        w.Add(1).Target = 2;                         // player on a living opponent: fine
        w.Add(2, x: 1).Target = -1;                  // no target: re-ordered at 1
        w.Add(3, x: 2, player: false).Target = 4;    // NPC on a living opponent but not in melee: re-ordered
        w.Add(4, x: 3, player: false).Target = 3;
        w.C[4].InMelee = true;                       // NPC on a living opponent in melee: fine
        var fs = New();
        int[] ids = [1, 2, 3, 4];
        MatchTeam[] teams = [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamA, MatchTeam.TeamB];
        fs.KeepEngagedMatch(w, ids, teams, 4, MatchMode.TeamAvB);

        Assert.Equal([2, 3], w.Attacks.Select(a => a.A));
        Assert.All(w.Attacks, a => Assert.False(a.Queued));

        // a target that went down is no longer a living opponent: re-ordered at the next one
        w.Attacks.Clear();
        w.C[2].Unconscious = true;
        fs.KeepEngagedMatch(w, ids, teams, 4, MatchMode.TeamAvB);
        Assert.Contains(w.Attacks, a => a.A == 1 && a.T == 4);
    }

    [Fact]
    public void AThrowDuringEngageUndoesTheFightersAlreadyTouchedAndRethrows()
    {
        var w = new FakeWorld();
        w.Add(1, jobs: true); w.Add(2, jobs: true);
        w.ThrowOnAttack = a => a == 2;
        var fs = New();

        Assert.Throws<InvalidOperationException>(() => fs.Engage(w, 1, 2));

        Assert.Equal(0, fs.RememberedCount);
        Assert.True(w.C[1].Jobs && w.C[2].Jobs);
        Assert.True(w.C[1].Stance && w.C[2].Stance);
        Assert.Equal([1, 2], StopsAt(w, 1));
        Assert.Equal([1], StopsAt(w, 2));   // fighter 1 was ordered in this tick
    }

    [Fact]
    public void AThrowingFighterIsStillStoppedWithJobsBackAndTheOthersAreDisengagedLoggedOnce()
    {
        var log = new List<string>();
        var w = new FakeWorld();
        w.Add(1, jobs: true); w.Add(2, jobs: true);
        w.ThrowOnRestore = c => c == 1;
        var fs = New(log);
        fs.Engage(w, 1, 2);
        fs.Disengage(w, 1, 2);

        Assert.Equal([1, 2], StopsAt(w, 1));          // queued before the engine writes that threw
        Assert.Equal([1, 2], StopsAt(w, 2));          // both were ordered in this tick
        Assert.Equal([1, 2], w.JobsOn);               // Jobs back in the finally
        Assert.True(w.C[1].Jobs && w.C[2].Jobs);
        Assert.Contains(1, w.EndTasks);
        Assert.True(fs.TryGetRemembered(1, out _));   // kept, so a later disengage can restore the stance
        Assert.False(fs.TryGetRemembered(2, out _));
        Assert.Single(log);

        w.Tick++;
        fs.Disengage(w, 1, 2);
        Assert.Single(log);   // once per world
    }

    [Fact]
    public void AThrowingStopDoesNotReplaceTheEngageException()
    {
        var log = new List<string>();
        var w = new StopThrowingWorld();
        w.Add(1); w.Add(2);
        w.ThrowOnAttack = a => a == 2;
        var fs = New(log);

        var e = Assert.Throws<InvalidOperationException>(() => fs.Engage(w, 1, 2));
        Assert.Equal("attack failed", e.Message);
        Assert.Equal(2, log.Count);                   // once per step name (task 3.3 review Q4): the stop and the late stop
        Assert.Contains("FightStarter stop failed", log[0]);
        Assert.Contains("FightStarter late stop failed", log[1]);
        Assert.Equal(2, fs.DisengageFailureCount);
    }

    sealed class StopThrowingWorld : IFightWorld<int, string>
    {
        readonly FakeWorld inner = new();
        public Func<int, bool>? ThrowOnAttack { set => inner.ThrowOnAttack = value; }
        public void Add(int id) => inner.Add(id);
        public long Tick => inner.Tick;
        public int None => -1;
        public bool IsNone(int c) => inner.IsNone(c);
        public bool Exists(int c) => inner.Exists(c);
        public bool IsDead(int c) => inner.IsDead(c);
        public bool IsUnconscious(int c) => inner.IsUnconscious(c);
        public bool IsPlayer(int c) => inner.IsPlayer(c);
        public bool TryGetPosition(int c, out float x, out float y, out float z) => inner.TryGetPosition(c, out x, out y, out z);
        public bool TryGetOrders(int c, out string orders) => inner.TryGetOrders(c, out orders);
        public void SetCombatStance(int c) => inner.SetCombatStance(c);
        public bool SetJobsOff(int c) => inner.SetJobsOff(c);
        public void SetJobsOn(int c) => inner.SetJobsOn(c);
        public void RestoreStance(int c, string saved) => inner.RestoreStance(c, saved);
        public void Attack(int attacker, int target, bool player, bool queued) => inner.Attack(attacker, target, player, queued);
        public void EndTask(int c) => inner.EndTask(c);
        public void Stop(IReadOnlyList<int> fighters, int ticksAhead) => throw new InvalidOperationException("stop failed");
        public int AttackTarget(int c) => inner.AttackTarget(c);
        public bool InMeleeCombatMode(int c) => inner.InMeleeCombatMode(c);
        public bool InFight(int c) => inner.InFight(c);
    }

    [Fact]
    public void AThrowDuringARetargetLeavesTheSurvivorsRememberedStateAlone()
    {
        // C++ :563-565: the retarget keeps the remembered state and Jobs; a throw in its re-engage must not undo that
        var w = new FakeWorld();
        w.Add(1, x: 0, jobs: true); w.Add(2, x: 1, jobs: true); w.Add(3, x: 2, jobs: true);
        var fs = New();
        int[] all = [1, 2, 3];
        fs.EngageMatch(w, all, [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB], 3, MatchMode.TeamAvB);
        w.C[3].Unconscious = true;
        w.Tick++;
        w.Restores.Clear(); w.JobsOn.Clear(); w.Stops.Clear();
        w.ThrowOnAttack = a => a == 2;

        Assert.Throws<InvalidOperationException>(() =>
            fs.RetargetMatchAfterElimination(w, all, 3, [1, 2], [MatchTeam.TeamA, MatchTeam.TeamB], 2, MatchMode.TeamAvB));

        Assert.Equal(3, fs.RememberedCount);
        Assert.Empty(w.Restores);
        Assert.Empty(w.JobsOn);
        Assert.Empty(StopsAt(w, 1));
        Assert.False(w.C[1].Jobs || w.C[2].Jobs);
    }

    [Fact]
    public void FighterZeroIsARealFighter()
    {
        // default(TId) is a real id here: 0 must be remembered, targeted and disengaged like any other
        var w = new FakeWorld();
        w.Add(0, jobs: true); w.Add(1, x: 1, jobs: true);
        var fs = New();
        fs.Engage(w, 0, 1);

        Assert.Equal(2, fs.RememberedCount);
        Assert.True(fs.TryGetAssignedTarget(1, out int t) && t == 0);
        Assert.True(fs.TryGetAssignedTarget(0, out t) && t == 1);
        Assert.Contains(w.Attacks, a => a.A == 0 && a.T == 1);
        Assert.Contains(w.Attacks, a => a.A == 1 && a.T == 0);

        // a hit from 0 on 1 is from 1's assigned target: no reaction
        w.Attacks.Clear();
        w.Tick++;
        fs.RecordIncomingAttack(1, 0);
        fs.ApplyIncomingAttacks(w, new FakeSession(MatchMode.TeamAvB));
        Assert.Empty(w.Attacks);

        fs.Disengage(w, 0, 1);
        Assert.Equal(0, fs.RememberedCount);
        Assert.Equal([0, 1], StopsAt(w, 1));
        Assert.Equal([0, 1], w.JobsOn);
    }

    [Fact]
    public void DroppedHitsAreLoggedOnce()
    {
        var log = new List<string>();
        var w = new FakeWorld();
        var fs = New(log);
        for (int i = 0; i < FightStarterLogic<int, string>.IncomingCapacity + 3; i++)
            fs.RecordIncomingAttack(1, 2);
        Assert.Equal(3, fs.DroppedHits);
        fs.ApplyIncomingAttacks(w, null);
        for (int i = 0; i < FightStarterLogic<int, string>.IncomingCapacity + 1; i++)
            fs.RecordIncomingAttack(1, 2);
        fs.ApplyIncomingAttacks(w, null);
        Assert.Single(log);
        Assert.Contains("3 hit(s) dropped", log[0]);
    }
}
