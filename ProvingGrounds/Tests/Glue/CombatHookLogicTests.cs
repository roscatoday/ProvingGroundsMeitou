using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>CombatHooks.cpp's decisions (task 3.2b) in Core/Glue/CombatHookLogic.cs over fakes: the "sparring pair" (SparSession.cpp:515-540) and
/// "isolated pair" (CombatHooks.cpp:156-169, PA §2.8) predicates over the published match view, equal to SparSession's own predicate for the same
/// match; the blow classification by counters (spike S2); the damage numbers and their pairing with the blow; the stats feed in the C++'s order
/// (:225-247); the counter fallback (spike S2 offline) and SparStats' unpaired counts (E3).</summary>
public class CombatHookLogicTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB, N = MatchTeam.None;

    /// <summary>The view SparSession's state would publish (glue: src/Game/CombatHooks.cs Publish), built from the logic over the fake host.</summary>
    static CombatMatchView<int> ViewOf(SparFakeHost h, SparSessionLogic<int> s, int[]? town = null, int[]? prisoners = null)
    {
        var f = s.Fighters.ToArray();
        var teams = f.Select(s.GetTeam).ToArray();
        var elim = f.Select(c => s.IsEliminated(h, c)).ToArray();
        return new CombatMatchView<int>(-1, s.IsActive, s.IsActive ? s.GetMode() : MatchMode.TeamAvB, f, teams, elim, s.GetActiveA(-1), s.GetActiveB(-1),
            s.HasPendingWalkIn(h), town ?? [], prisoners ?? []);
    }

    static (SparFakeHost H, SparSessionLogic<int> S) New(int count = 6)
    {
        var h = new SparFakeHost();
        for (int i = 0; i < count; i++) h.Add(i, "F" + i, x: i * 100);
        return (h, new SparSessionLogic<int>(-1));
    }

    /// <summary>Every pair of ids in -1..7 (outsiders 6, 7 and none -1 included) gives the view's answer = SparSession's.</summary>
    static void AssertSameAsSession(SparFakeHost h, SparSessionLogic<int> s)
    {
        var v = ViewOf(h, s);
        for (int a = -1; a < 8; a++)
            for (int b = -1; b < 8; b++)
                Assert.True(s.IsSparringOpponent(h, a, b) == v.IsSparringOpponent(a, b), $"pair {a},{b}");
        for (int c = -1; c < 8; c++)
            Assert.Equal(s.IsParticipant(h, c), v.IsParticipant(c));
    }

    // ---------- the sparring pair over the view (SparSession.cpp:515-540) ----------

    [Fact]
    public void View_SparringPair_EqualsSession_InEveryMode()
    {
        var (h, s) = New();
        AssertSameAsSession(h, s);   // no match
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);
        AssertSameAsSession(h, s);
        h.C[2].Unconscious = true;   // AvB: a down fighter still counts (nobody is eliminated in AvB)
        AssertSameAsSession(h, s);
        Assert.True(ViewOf(h, s).IsSparringOpponent(0, 2));
        s.StopWithStatus(h, SparStopReason.Manual, -1, null);
        AssertSameAsSession(h, s);

        var (h2, s2) = New();
        s2.StartMatch(h2, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        AssertSameAsSession(h2, s2);
        s2.EliminateFighter(h2, 1);
        AssertSameAsSession(h2, s2);
        Assert.False(ViewOf(h2, s2).IsSparringOpponent(0, 1));   // eliminated
        Assert.True(ViewOf(h2, s2).IsSparringOpponent(0, 2));

        var (h3, s3) = New();
        s3.StartMatch(h3, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        AssertSameAsSession(h3, s3);
        Assert.True(ViewOf(h3, s3).IsSparringOpponent(0, 2));
        Assert.False(ViewOf(h3, s3).IsSparringOpponent(1, 2));   // benched
        h3.WalkInPending = true;                                  // never during a walk-in
        AssertSameAsSession(h3, s3);
        Assert.False(ViewOf(h3, s3).IsSparringOpponent(0, 2));
        h3.WalkInPending = false;
        h3.C[0].Unconscious = true;
        s3.TickTeams1v1Bouts(h3);                                 // 0 out for good, 1 promoted in place
        AssertSameAsSession(h3, s3);
        Assert.True(ViewOf(h3, s3).IsSparringOpponent(1, 2));
        Assert.False(ViewOf(h3, s3).IsSparringOpponent(0, 2));
    }

    // ---------- the isolated pair (CombatHooks.cpp:156-169; TownArenaRuntimePolicy.h:101-104, PrisonerMatchLogic.cpp:76-84) ----------

    [Fact]
    public void IsolatedPair_TownFighterOrMatchPrisoner_NotSparringOpponents()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        // 1 is a match prisoner, 3 a town bout fighter; 6 and 7 are outsiders
        var v = ViewOf(h, s, town: [3], prisoners: [1]);
        Assert.Equal(2, v.IsolatedCount);
        Assert.False(v.ShouldIsolatePair(0, 1));    // the prisoner's sparring opponent: never isolated (it must reach the prisoner)
        Assert.False(v.ShouldIsolatePair(1, 0));
        Assert.True(v.ShouldIsolatePair(6, 1));     // an outsider and the prisoner, both ways
        Assert.True(v.ShouldIsolatePair(1, 6));
        Assert.True(v.ShouldIsolatePair(6, 3));     // an outsider and a town fighter
        Assert.True(v.ShouldIsolatePair(3, 6));
        Assert.False(v.ShouldIsolatePair(6, 7));    // two outsiders: the game's own rules
        Assert.False(v.ShouldIsolatePair(0, 6));    // a plain player fighter and an outsider: not isolated (as the C++)
        Assert.False(v.ShouldIsolatePair(-1, 1));   // a null side
        Assert.False(v.ShouldIsolatePair(1, -1));
        // a town fighter and a match prisoner who are not sparring opponents: isolated from each other, both ways
        Assert.True(v.ShouldIsolatePair(3, 1));
        Assert.True(v.ShouldIsolatePair(1, 3));

        // no match running: the town fighter and the prisoner are still isolated from everyone (the C++ reads the town and prisoner lists alone)
        var idle = new CombatMatchView<int>(-1, false, MatchMode.TeamAvB, [], [], [], -1, -1, false, [3], [1]);
        Assert.True(idle.ShouldIsolatePair(0, 1));
        Assert.False(idle.ShouldIsolatePair(0, 6));
        Assert.False(CombatMatchView<int>.Idle(-1).ShouldIsolatePair(0, 1));
        Assert.Equal(0, CombatMatchView<int>.Idle(-1).IsolatedCount);
    }

    [Fact]
    public void View_CopiesItsInput_And_MatchesDetectsEveryChange()
    {
        int[] f = [4, 5];
        MatchTeam[] t = [A, B];
        bool[] e = [false, false];
        int[] town = [9];
        var v = new CombatMatchView<int>(-1, true, MatchMode.TeamAvB, f, t, e, -1, -1, false, town, []);
        f[0] = 7; town[0] = 8;                     // later writes to the builder's arrays never reach the view
        Assert.True(v.IsParticipant(4));
        Assert.False(v.IsParticipant(7));
        Assert.True(v.ShouldIsolatePair(1, 9));

        int[] f2 = [4, 5];
        Assert.True(v.Matches(true, MatchMode.TeamAvB, f2, t, e, -1, -1, false, [9], []));
        Assert.False(v.Matches(false, MatchMode.TeamAvB, f2, t, e, -1, -1, false, [9], []));
        Assert.False(v.Matches(true, MatchMode.LastStanding, f2, t, e, -1, -1, false, [9], []));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, [4], [A], [false], -1, -1, false, [9], []));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, [A, A], e, -1, -1, false, [9], []));            // only a team changed
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, t, [false, true], -1, -1, false, [9], []));    // only who is out changed
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, t, e, 4, -1, false, [9], []));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, t, e, -1, 5, false, [9], []));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, t, e, -1, -1, true, [9], []));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, t, e, -1, -1, false, [], []));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, f2, t, e, -1, -1, false, [9], [9]));
        Assert.Throws<ArgumentException>(() => new CombatMatchView<int>(-1, true, MatchMode.TeamAvB, [1, 2], [A], [false, false], -1, -1, false, [], []));
    }

    [Fact]
    public async Task View_IsReadSafelyFromManyThreads()
    {
        // the patches on the parallel think read one published view while the sim thread publishes new ones: a reader always sees one whole view
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        var withPrisoner = ViewOf(h, s, prisoners: [1]);
        var idle = CombatMatchView<int>.Idle(-1);
        CombatMatchView<int> published = withPrisoner;
        int bad = 0;
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 200_000; i++)
            {
                var v = Volatile.Read(ref published);
                bool iso = v.ShouldIsolatePair(6, 1);
                if (iso != (v.IsolatedCount > 0)) Interlocked.Exchange(ref bad, 1);
            }
        })).ToArray();
        for (int i = 0; i < 20_000; i++) Volatile.Write(ref published, (i & 1) == 0 ? idle : withPrisoner);
        await Task.WhenAll(readers);
        Assert.Equal(0, Volatile.Read(ref bad));
    }

    [Fact]
    public void PatchPredicates_DropsFoe_RefusesPickUp_SkipsSquadMemory()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1, 2], [A, B, B], 3);
        var none = ViewOf(h, s);                              // nobody isolated: the foe and pick-up patches never act
        Assert.False(none.DropsFoe(6, 1));
        Assert.False(none.RefusesPickUp(6, 1));
        var v = ViewOf(h, s, town: [4], prisoners: [1]);
        Assert.True(v.DropsFoe(6, 1));                        // an outsider would pick the prisoner: dropped
        Assert.True(v.DropsFoe(1, 6));                        // the prisoner would pick an outsider: dropped
        Assert.False(v.DropsFoe(0, 1));                       // its sparring opponent: kept
        Assert.True(v.DropsFoe(6, 4));                        // the town fighter
        Assert.False(v.DropsFoe(6, 7));
        Assert.True(v.RefusesPickUp(6, 1));                   // an outsider lifting the prisoner
        Assert.False(v.RefusesPickUp(0, 1));                  // its opponent
        Assert.False(v.RefusesPickUp(6, 2));                  // a plain player fighter: not protected (as the C++)
        Assert.False(v.RefusesPickUp(6, -1));
        Assert.True(v.SkipsSquadMemory(0, 1));                // two fighters of the match
        Assert.True(v.SkipsSquadMemory(1, 2));                // teammates too (any two participants)
        Assert.False(v.SkipsSquadMemory(0, 6));               // a fighter and an outsider: the game's own memory
        Assert.False(v.SkipsSquadMemory(6, 7));
        s.EliminateFighter(h, 2);
        Assert.True(ViewOf(h, s).SkipsSquadMemory(0, 2));     // an eliminated fighter is still a participant
        s.StopWithStatus(h, SparStopReason.Manual, -1, null);
        Assert.False(ViewOf(h, s).SkipsSquadMemory(0, 1));    // no match: nothing skipped
    }

    // ---------- the blow (CombatHooks.cpp:199-250; spike S2) ----------

    [Fact]
    public void Classify_ByWhichCounterMoved()
    {
        var b = new BlowCounters(5, 2, 1);
        Assert.Equal(BlowKind.Hit, CombatHookLogic.Classify(b, b with { Landed = 6 }, false));
        Assert.Equal(BlowKind.Blocked, CombatHookLogic.Classify(b, b with { Parried = 3 }, false));
        Assert.Equal(BlowKind.Dodged, CombatHookLogic.Classify(b, b with { Evaded = 2 }, false));
        Assert.Equal(BlowKind.None, CombatHookLogic.Classify(b, b, false));                       // dropped (no stats / body): nothing moved
        Assert.Equal(BlowKind.None, CombatHookLogic.Classify(b, b with { Landed = 6 }, true));   // a swing cancelled earlier this tick
    }

    [Fact]
    public void Damage_AppliedIsTheDeliveredTotal_IncomingAddsWhatTheArmourStopped()
    {
        var (applied, incoming) = CombatHookLogic.Damage(10, 5, 2.5f, 3, 1, 0.5f);
        Assert.Equal(17.5f, applied);
        Assert.Equal(22.0f, incoming);
        Assert.Equal(6f, CombatHookLogic.Total(1, 2, 3));
    }

    sealed class Sink : IBlowSink<int>
    {
        public readonly List<string> Calls = [];
        public void OnHit(int attacker, int defender, float applied, float incoming, bool isMiss) =>
            Calls.Add($"hit {attacker}>{defender} {applied}/{incoming}{(isMiss ? " miss" : "")}");
        public void OnBlock(int defender, int attacker) => Calls.Add($"block {defender}<{attacker}");
        public void React(int defender, int attacker) => Calls.Add($"react {defender}<{attacker}");
    }

    [Fact]
    public void Feed_InTheCppOrder_OnlyForSparringOpponents()
    {
        var k = new Sink();
        CombatHookLogic.Feed(k, true, BlowKind.Hit, 1, 2, true, 12.5f, 20f);
        CombatHookLogic.Feed(k, true, BlowKind.Blocked, 1, 2, false, 0, 0);
        CombatHookLogic.Feed(k, true, BlowKind.Dodged, 1, 2, true, 9f, 9f);    // a dodge: the attacker's miss, applied 0, and no reaction (:246)
        CombatHookLogic.Feed(k, true, BlowKind.Hit, 1, 2, false, 7f, 7f);      // a hit with no observed damage (no body part): 0 / 0
        CombatHookLogic.Feed(k, false, BlowKind.Hit, 1, 2, true, 5f, 5f);      // not sparring opponents: nothing (:208)
        CombatHookLogic.Feed(k, true, BlowKind.None, 1, 2, true, 5f, 5f);      // a dropped blow: nothing
        Assert.Equal(["hit 1>2 12.5/20", "react 2<1", "block 2<1", "react 2<1", "hit 1>2 0/0 miss", "hit 1>2 0/0", "react 2<1"], k.Calls);
    }

    [Fact]
    public void Pairing_TakesOnlyTheExpectedBlowsNumbers_Once()
    {
        var p = new HitPairing();
        Assert.False(p.Offer(1, 2, 7, 1, 100, 5, 6));      // nothing expected
        p.Expect(1, 2, 7, 1, 100);
        Assert.True(p.Expecting);
        Assert.False(p.Offer(3, 2, 7, 1, 100, 9, 9));      // another attacker
        Assert.False(p.Offer(1, 4, 7, 1, 100, 9, 9));      // another victim (a sweep reaching a second body)
        Assert.False(p.Offer(1, 2, 8, 1, 100, 9, 9));      // another attack
        Assert.False(p.Offer(1, 2, 7, 2, 100, 9, 9));      // the other blow of the attack
        Assert.False(p.Offer(1, 2, 7, 1, 101, 9, 9));      // another tick
        Assert.True(p.Offer(1, 2, 7, 1, 100, 5, 6));
        Assert.False(p.Offer(1, 2, 7, 1, 100, 1, 1));      // first offer wins
        Assert.True(p.Take(out float ap, out float inc));
        Assert.Equal((5f, 6f), (ap, inc));
        Assert.False(p.Expecting);
        Assert.False(p.Take(out ap, out inc));              // taken once
        Assert.Equal((0f, 0f), (ap, inc));
        p.Expect(1, 2, 7, 1, 100);
        p.Offer(1, 2, 7, 1, 100, 5, 6);
        p.Expect(1, 2, 9, 1, 100);                          // a new blow drops what the last one left
        Assert.False(p.Take(out _, out _));
    }

    [Fact]
    public void Pairing_TakeFor_IsIdempotent_AndAHitWithoutAnOfferFeedsZero()
    {
        var p = new HitPairing();
        p.Expect(1, 2, 7, 1, 100);
        p.Offer(1, 2, 7, 1, 100, 5, 6);
        Assert.False(p.TakeFor(1, 2, 7, 2, 100, out _, out _, out _));    // another blow: not this postfix's
        Assert.True(p.IsExpecting(1, 2, 7, 1, 100));
        Assert.True(p.TakeFor(1, 2, 7, 1, 100, out bool has, out float ap, out float inc));
        Assert.Equal((true, 5f, 6f), (has, ap, inc));
        // the patch applied twice (PgCheck hooks over a live game): the second copy of the postfix finds nothing to take and counts nothing
        Assert.False(p.TakeFor(1, 2, 7, 1, 100, out has, out ap, out inc));
        Assert.Equal((false, 0f, 0f), (has, ap, inc));

        // a hit the observer gave no numbers for (no body part chosen): taken once, no damage, fed as 0 / 0
        p.Expect(3, 4, 1, 1, 200);
        Assert.True(p.TakeFor(3, 4, 1, 1, 200, out has, out ap, out inc));
        Assert.False(has);
        var k = new Sink();
        CombatHookLogic.Feed(k, true, BlowKind.Hit, 3, 4, has, ap, inc);
        Assert.Equal(["hit 3>4 0/0", "react 4<3"], k.Calls);
    }

    // ---------- the counter fallback (spike S2 offline; SparStatsState.AddUnpairedCounts) ----------

    sealed class Counts : ICounterSink<int>
    {
        public readonly List<string> Calls = [];
        public void AddCounts(int fighter, int blocks, int misses, int dodges) => Calls.Add($"{fighter}: b{blocks} m{misses} d{dodges}");
    }

    [Fact]
    public void Fallback_DifferencesSinceTheBaseline_MissesAreEvaded()
    {
        var f = new CounterFallback<int>();
        var c = new Counts();
        f.Observe(1, blocks: 2, evaded: 1, dodges: 0, c);   // baseline only
        f.Observe(2, 0, 0, 3, c);
        Assert.Empty(c.Calls);
        f.Observe(1, 2, 1, 0, c);                            // nothing moved: no call
        f.Observe(1, 3, 3, 1, c);
        f.Observe(2, 1, 0, 3, c);
        f.Observe(2, 0, 0, 0, c);                            // a counter went down (slot reset): a new baseline, no call
        f.Observe(2, 0, 1, 0, c);
        Assert.Equal(["1: b1 m2 d1", "2: b1 m0 d0", "2: b0 m1 d0"], c.Calls);
        Assert.Equal(2, f.Count);
        f.Reset();
        f.Observe(1, 9, 9, 9, c);                            // after a reset: a baseline again
        Assert.Equal(3, c.Calls.Count);
        for (int i = 10; i < 30; i++) f.Observe(i, 0, 0, 0, c);   // grows past its first capacity
        Assert.Equal(21, f.Count);
    }

    sealed class Roster : ISparRoster<int>
    {
        public int ParticipantCount => 2;
        public int? Participant(int index) => index == 0 ? 10 : index == 1 ? 20 : null;
        public MatchTeam Team(int fighter) => fighter == 10 ? A : B;
        public bool IsSparringOpponent(int a, int b) => a != b && (a == 10 || a == 20) && (b == 10 || b == 20);
        public string? Name(int fighter) => "F" + fighter;
    }

    [Fact]
    public void UnpairedCounts_OnlyForParticipants_AddedToTheRow()
    {
        var s = new SparStatsState<int>();
        var r = new Roster();
        s.AddUnpairedCounts(r, 10, 1, 2, 3);
        s.AddUnpairedCounts(r, 10, 1, 0, 0);
        s.AddUnpairedCounts(r, 30, 5, 5, 5);   // not a participant
        s.AddUnpairedCounts(r, null, 5, 5, 5);
        Assert.Equal(1, s.LiveCount);
        var row = s.Live[0];
        Assert.Equal((10, 2, 2, 3), (row.Character, row.Blocks, row.Misses, row.Dodges));
        Assert.Equal(0, row.HitsLanded);
    }

    /// <summary>The fallback's sink as the glue's: each report becomes SparStats' unpaired counts for that fighter.</summary>
    readonly struct StatsSink(SparStatsState<int> stats, Roster roster) : ICounterSink<int>
    {
        public void AddCounts(int fighter, int blocks, int misses, int dodges) => stats.AddUnpairedCounts(roster, fighter, blocks, misses, dodges);
    }

    [Fact]
    public void FallbackFlow_CountersOverSteps_IntoSparStats()
    {
        var stats = new SparStatsState<int>();
        var roster = new Roster();
        var f = new CounterFallback<int>();
        var sink = new StatsSink(stats, roster);
        // match start (counters from earlier fights are the baseline), then three steps of the bout; an outsider is observed but never counted
        f.Observe(10, blocks: 4, evaded: 2, dodges: 1, sink);
        f.Observe(20, 0, 5, 0, sink);
        f.Observe(30, 0, 0, 0, sink);
        f.Observe(10, 5, 2, 1, sink);
        f.Observe(20, 0, 6, 2, sink);
        f.Observe(30, 3, 3, 3, sink);
        f.Observe(10, 5, 3, 3, sink);
        f.Observe(20, 1, 6, 2, sink);
        Assert.Equal(2, stats.LiveCount);
        var r10 = stats.Live.First(r => r.Character == 10);
        var r20 = stats.Live.First(r => r.Character == 20);
        Assert.Equal((1, 1, 2), (r10.Blocks, r10.Misses, r10.Dodges));
        Assert.Equal((1, 1, 2), (r20.Blocks, r20.Misses, r20.Dodges));
    }
}
