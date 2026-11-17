using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>SparStats.cpp (task 3.2a) in Core/Glue/SparStatsState.cs: the counting rules of OnHit / OnBlock / OnDodge (:98-150), the elimination
/// order (:152-157), Freeze building the snapshot and the podium fighters (:159-232), the in-place clears (:87-96, :249-256) and the 63-byte
/// name cut (:178-182).</summary>
public class SparStatsTests
{
    /// <summary>A match roster: participant slots (null = a NULL slot), teams, names, and who spars with whom (every pair of distinct
    /// participants, unless <see cref="Opponents"/> is set).</summary>
    sealed class FakeRoster : ISparRoster<int>
    {
        public int?[] Slots = [];
        public Dictionary<int, MatchRules.MatchTeam> Teams = [];
        public Dictionary<int, string?> Names = [];
        public Func<int, int, bool>? Opponents;
        public int ParticipantCount => Slots.Length;
        public int? Participant(int index) => index >= 0 && index < Slots.Length ? Slots[index] : null;
        public MatchRules.MatchTeam Team(int fighter) => Teams.TryGetValue(fighter, out var t) ? t : MatchRules.MatchTeam.None;
        public bool IsSparringOpponent(int a, int b) => Opponents?.Invoke(a, b) ?? (a != b && Slots.Contains(a) && Slots.Contains(b));
        public string? Name(int fighter) => Names.TryGetValue(fighter, out var n) ? n : null;
    }

    static FakeRoster TwoFighters() => new()
    {
        Slots = [10, 20],
        Teams = { [10] = MatchRules.MatchTeam.TeamA, [20] = MatchRules.MatchTeam.TeamB },
        Names = { [10] = "Alpha", [20] = "Beta" },
    };

    [Fact]
    public void HitCountsDamageAndMitigation()
    {
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 12.5f, 20.0f, false);
        s.OnHit(r, 10, 20, 3.0f, 1.0f, false);   // incoming below applied: raised to applied (C++ :121-122), so no mitigation
        Assert.Equal(2, s.LiveCount);
        var atk = s.Live[0]; var def = s.Live[1];
        Assert.Equal(10, atk.Character);
        Assert.Equal(15.5f, atk.DamageDealt);
        Assert.Equal(2, atk.HitsLanded);
        Assert.Equal(15.5f, def.DamageTaken);
        Assert.Equal(7.5f, def.DamageMitigated);
        Assert.Equal(0, atk.Misses);
    }

    [Fact]
    public void NegativeAppliedDamageIsZero()
    {
        // C++ :119-120: appliedDamage < 0 -> 0, and the hit still counts
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, -4.0f, 5.0f, false);
        Assert.Equal(0.0f, s.Live[0].DamageDealt);
        Assert.Equal(1, s.Live[0].HitsLanded);
        Assert.Equal(5.0f, s.Live[1].DamageMitigated);
    }

    [Fact]
    public void MissIsTheAttackersMissAndTheDefendersDodgeOnly()
    {
        // C++ :112-117 and :141-150: both paths count attacker miss + defender dodge, nothing else
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 50.0f, 50.0f, true);
        s.OnDodge(r, 10, 20);
        var atk = s.Live[0]; var def = s.Live[1];
        Assert.Equal(2, atk.Misses);
        Assert.Equal(2, def.Dodges);
        Assert.Equal(0.0f, atk.DamageDealt);
        Assert.Equal(0.0f, def.DamageTaken);
        Assert.Equal(0, atk.HitsLanded);
    }

    [Fact]
    public void BlockCountsForTheDefenderAndAddsTheAttackersRow()
    {
        // C++ :136-138: defender's row first, then the attacker's (empty) row
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnBlock(r, 20, 10);
        Assert.Equal(2, s.LiveCount);
        Assert.Equal(20, s.Live[0].Character);
        Assert.Equal(1, s.Live[0].Blocks);
        Assert.Equal(10, s.Live[1].Character);
        Assert.Equal(0, s.Live[1].Blocks);
    }

    [Fact]
    public void NothingCountsBetweenNonOpponentsOrWithoutAFighter()
    {
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 99, 5.0f, 5.0f, false);     // 99 is not in the match
        s.OnHit(r, null, 20, 5.0f, 5.0f, false);
        s.OnHit(r, 10, null, 5.0f, 5.0f, true);
        s.OnBlock(r, 20, null);
        s.OnDodge(r, null, 20);
        s.OnDodge(r, 10, 10);                      // nobody spars with themselves
        r.Opponents = (_, _) => false;             // teammates / a session that says no
        s.OnHit(r, 10, 20, 5.0f, 5.0f, false);
        s.OnBlock(r, 20, 10);
        s.OnDodge(r, 10, 20);
        Assert.Equal(0, s.LiveCount);
    }

    [Fact]
    public void OpponentCheckIsAskedAttackerFirst()
    {
        // C++ :103, :134, :145: IsSparringOpponent(attacker, defender) on every path, even OnBlock(defender, attacker)
        var asked = new List<(int, int)>();
        var r = TwoFighters();
        r.Opponents = (a, b) => { asked.Add((a, b)); return true; };
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 1, 1, false);
        s.OnBlock(r, 20, 10);
        s.OnDodge(r, 10, 20);
        Assert.Equal([(10, 20), (10, 20), (10, 20)], asked);
    }

    [Fact]
    public void FirstBlowOfAMatchKeepsTheAttackersCounts()
    {
        // The C++'s atk pointer dangles when adding the defender's row reallocates g_live (the EnsureLive calls at :110-111); the port keeps both rows' counts
        // (docs/divergences.md "SparStats live rows").
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 8.0f, 8.0f, false);
        Assert.Equal(8.0f, s.Live[0].DamageDealt);
        Assert.Equal(1, s.Live[0].HitsLanded);
    }

    [Fact]
    public void EliminationOrderKeepsTheFirstKoOnly()
    {
        var s = new SparStatsState<int>();
        s.OnEliminated(20);
        s.OnEliminated(10);
        s.OnEliminated(20);
        s.OnEliminated(null);
        Assert.Equal([20, 10], s.EliminationOrder);
    }

    [Fact]
    public void FreezeBuildsRowsByParticipantIndexAndThePodium()
    {
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 30.0f, 40.0f, false);
        s.OnBlock(r, 20, 10);
        s.OnDodge(r, 20, 10);
        s.OnEliminated(20);
        s.Freeze(r, SparPodium.OutcomeKind.TeamAWins, MatchRules.MatchMode.TeamAvB, null);

        Assert.True(s.HasSnapshot());
        var snap = s.GetSnapshot();
        Assert.Same(snap, s.GetMutableSnapshot());
        Assert.Equal(SparPodium.OutcomeKind.TeamAWins, snap.Outcome);
        Assert.Equal(MatchRules.MatchMode.TeamAvB, snap.Mode);
        Assert.Equal(-1, snap.LastStandingId);
        Assert.Equal(2, snap.FighterCount);
        var a = snap.Fighters[0]; var b = snap.Fighters[1];
        Assert.Equal((0, MatchRules.MatchTeam.TeamA, "Alpha", -1), (a.Id, a.Team, a.Name, a.EliminationIndex));
        Assert.Equal((1, MatchRules.MatchTeam.TeamB, "Beta", 0), (b.Id, b.Team, b.Name, b.EliminationIndex));
        Assert.Equal((30.0f, 0.0f, 0.0f, 1, 0, 0, 1), (a.DamageDealt, a.DamageTaken, a.DamageMitigated, a.HitsLanded, a.Blocks, a.Misses, a.Dodges));
        Assert.Equal((0.0f, 30.0f, 10.0f, 0, 1, 1, 0), (b.DamageDealt, b.DamageTaken, b.DamageMitigated, b.HitsLanded, b.Blocks, b.Misses, b.Dodges));
        Assert.Equal(1, snap.EliminationCount);
        Assert.Equal(1, snap.EliminationIds[0]);
        Assert.Equal("Team A wins", snap.Header);
        Assert.Equal(1, snap.PodiumCount);   // team A only
        Assert.Equal(10, s.GetPodiumCharacter(0));
        Assert.Null(s.GetPodiumCharacter(1));
        Assert.Null(s.GetPodiumCharacter(3));
        Assert.Null(s.GetPodiumCharacter(-1));
    }

    [Fact]
    public void FreezeSkipsNullSlotsButKeepsTheirIndexAsId()
    {
        // C++ :167-174: row.id = i, a NULL participant is skipped; a fighter with no live row has zero counts
        var r = new FakeRoster
        {
            Slots = [10, null, 30],
            Names = { [10] = "A", [30] = "C" },
        };
        var s = new SparStatsState<int>();
        s.OnEliminated(30);
        s.OnEliminated(99);   // not a participant: ParticipantId -1, skipped (C++ :205-207)
        s.OnEliminated(10);
        s.Freeze(r, SparPodium.OutcomeKind.LastStanding, MatchRules.MatchMode.LastStanding, 10);
        var snap = s.GetSnapshot();
        Assert.Equal(2, snap.FighterCount);
        Assert.Equal(0, snap.Fighters[0].Id);
        Assert.Equal(2, snap.Fighters[1].Id);
        Assert.Equal(0.0f, snap.Fighters[1].DamageDealt);
        Assert.Equal(2, snap.EliminationCount);
        Assert.Equal([2, 0], snap.EliminationIds.Take(2));
        Assert.Equal(1, snap.Fighters[0].EliminationIndex);
        Assert.Equal(0, snap.Fighters[1].EliminationIndex);
        Assert.Equal(0, snap.LastStandingId);
        Assert.Equal("A last standing", snap.Header);
        Assert.Equal(2, snap.PodiumCount);
        Assert.Equal(10, s.GetPodiumCharacter(0));
        Assert.Equal(30, s.GetPodiumCharacter(1));
        Assert.Null(s.GetPodiumCharacter(2));
    }

    [Fact]
    public void FreezeCapsAtSixteenFightersAndEliminations()
    {
        var r = new FakeRoster { Slots = Enumerable.Range(0, 20).Select(i => (int?)i).ToArray() };
        var s = new SparStatsState<int>();
        for (int i = 0; i < 20; i++) s.OnEliminated(i);
        s.Freeze(r, SparPodium.OutcomeKind.Stopped, MatchRules.MatchMode.LastStanding, 19);
        var snap = s.GetSnapshot();
        Assert.Equal(16, snap.FighterCount);
        Assert.Equal(16, snap.EliminationCount);
        Assert.Equal(15, snap.EliminationIds[15]);
        Assert.Equal(19, snap.LastStandingId);   // C++ :218-219 looks the id up among all participants, not only the rows
        Assert.Equal("", snap.Fighters[0].Name);   // no name: a zeroed C array reads ""
    }

    [Fact]
    public void EliminationsPastTheSixteenRowCapKeepTheirIdsWithoutARow()
    {
        // C++ :200-214: the elimination loop looks ids up among ALL participants and writes EliminationIds even when no row (only 16 rows exist)
        // carries that id; the fighter rows then have no EliminationIndex for it
        var r = new FakeRoster { Slots = Enumerable.Range(0, 20).Select(i => (int?)i).ToArray() };
        var s = new SparStatsState<int>();
        s.OnEliminated(17);
        s.OnEliminated(3);
        s.OnEliminated(19);
        s.Freeze(r, SparPodium.OutcomeKind.Stopped, MatchRules.MatchMode.LastStanding, null);
        var snap = s.GetSnapshot();
        Assert.Equal(16, snap.FighterCount);
        Assert.Equal(3, snap.EliminationCount);
        Assert.Equal([17, 3, 19], snap.EliminationIds.Take(3));
        Assert.Equal(1, snap.Fighters[3].EliminationIndex);   // participant 3 has a row and is the 2nd out
        for (int f = 0; f < snap.FighterCount; f++)
        {
            if (f != 3) Assert.Equal(-1, snap.Fighters[f].EliminationIndex);
        }
    }

    sealed class FakeFighters(params int[] alive) : ISparFighters<int>
    {
        public bool Exists(int fighter) => alive.Contains(fighter);
    }

    [Fact]
    public void PodiumCharacterThatNoLongerExistsIsNone()
    {
        // the glue's GetPodiumCharacter(world, i): the id captured at freeze time, null once it no longer resolves (C++ :258-263 returned the pointer)
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnEliminated(20);
        s.Freeze(r, SparPodium.OutcomeKind.TeamAWins, MatchRules.MatchMode.TeamAvB, null);
        Assert.Equal(10, s.GetPodiumCharacter(new FakeFighters(10, 20), 0));
        Assert.Null(s.GetPodiumCharacter(new FakeFighters(20), 0));   // stale
        Assert.Null(s.GetPodiumCharacter(new FakeFighters(10, 20), 1));   // no slot: never asks the table
        s.ClearSnapshot();
        Assert.Null(s.GetPodiumCharacter(new FakeFighters(10, 20), 0));
    }

    [Fact]
    public void PooledRowsStartFromZeroInTheNextMatch()
    {
        // ResetForMatch keeps the row objects (rows pooled) but a reused row counts exactly as a new one: every field reset
        var r = new FakeRoster { Slots = [10, 20, 30] };
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 5.0f, 9.0f, false);
        s.OnBlock(r, 30, 10);
        s.OnDodge(r, 20, 30);
        var first = s.Live[0];
        Assert.Equal(3, s.LiveCount);

        s.ResetForMatch();
        Assert.Equal(0, s.LiveCount);
        Assert.Empty(s.Live);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Live[0]);

        r.Slots = [40, 50];
        s.OnHit(r, 40, 50, 1.0f, 1.0f, true);
        Assert.Equal(2, s.LiveCount);
        Assert.Same(first, s.Live[0]);   // reused, not reallocated
        var atk = s.Live[0]; var def = s.Live[1];
        Assert.Equal(40, atk.Character);
        Assert.Equal((0.0f, 0.0f, 0.0f, 0, 0, 1, 0), (atk.DamageDealt, atk.DamageTaken, atk.DamageMitigated, atk.HitsLanded, atk.Blocks, atk.Misses, atk.Dodges));
        Assert.Equal(50, def.Character);
        Assert.Equal((0.0f, 0.0f, 0.0f, 0, 0, 0, 1), (def.DamageDealt, def.DamageTaken, def.DamageMitigated, def.HitsLanded, def.Blocks, def.Misses, def.Dodges));
        Assert.Equal([40, 50], s.Live.Select(l => l.Character));

        s.OnBlock(r, 50, 40);   // existing rows found among the live ones only, not the pooled third
        Assert.Equal(2, s.LiveCount);
        Assert.Equal(1, def.Blocks);
    }

    [Fact]
    public void FreezeClearsTheLastSnapshotInPlace()
    {
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnEliminated(20);
        s.Freeze(r, SparPodium.OutcomeKind.TeamAWins, MatchRules.MatchMode.TeamAvB, null);
        var snap = s.GetSnapshot();
        snap.Fighters[0].RatingDelta = 12.0f;   // LeaderboardStore::ApplyFromSnapshot writes these
        r.Slots = [10];
        s.ResetForMatch();
        Assert.False(s.HasSnapshot());
        s.Freeze(r, SparPodium.OutcomeKind.Draw, MatchRules.MatchMode.TeamAvB, null);
        Assert.Same(snap, s.GetSnapshot());
        Assert.Equal(1, snap.FighterCount);
        Assert.Equal(0.0f, snap.Fighters[0].RatingDelta);
        Assert.Null(snap.Fighters[1].Name);
        Assert.Equal(0, snap.EliminationCount);
        Assert.Equal(0, snap.EliminationIds[0]);
        Assert.Equal("Draw", snap.Header);
        Assert.Equal(0, snap.PodiumCount);
        Assert.Null(s.GetPodiumCharacter(0));
    }

    [Fact]
    public void ResetAndClearSnapshotClearEverythingTheyShould()
    {
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        s.OnHit(r, 10, 20, 1, 1, false);
        s.OnEliminated(20);
        s.Freeze(r, SparPodium.OutcomeKind.TeamAWins, MatchRules.MatchMode.TeamAvB, null);
        var snap = s.GetSnapshot();

        s.ClearSnapshot();   // C++ :249-256: the snapshot and podium only; live rows and the elimination order stay
        Assert.False(s.HasSnapshot());
        Assert.Null(s.GetPodiumCharacter(0));
        Assert.Equal(0, snap.FighterCount);
        Assert.Equal("", snap.Header);
        Assert.Equal(SparPodium.OutcomeKind.TeamAWins, snap.Outcome);   // memset: 0 = TeamAWins
        Assert.Equal(0, snap.LastStandingId);
        Assert.Equal(2, s.LiveCount);
        Assert.Single(s.EliminationOrder);

        s.Freeze(r, SparPodium.OutcomeKind.TeamAWins, MatchRules.MatchMode.TeamAvB, null);
        s.AbandonWorldState();   // C++ :82-85 = ResetForMatch (:87-96)
        Assert.False(s.HasSnapshot());
        Assert.Equal(0, s.LiveCount);
        Assert.Empty(s.EliminationOrder);
        Assert.Equal(0, snap.FighterCount);
        Assert.Null(s.GetPodiumCharacter(0));
    }

    [Fact]
    public void DamageAccumulatesAsFloatNotDouble()
    {
        // float accumulation, never double: 0.1f added ten times as floats is 1.0000001f; added as doubles and narrowed it would be 1.0f
        var r = TwoFighters();
        var s = new SparStatsState<int>();
        float expected = 0.0f;
        for (int i = 0; i < 10; i++) { s.OnHit(r, 10, 20, 0.1f, 0.3f, false); expected += 0.1f; }
        Assert.Equal(expected, s.Live[0].DamageDealt);
        double asDouble = 0.0;
        for (int i = 0; i < 10; i++) asDouble += 0.1f;
        Assert.NotEqual((float)asDouble, s.Live[0].DamageDealt);   // the test can tell the two apart
        float mit = 0.0f;
        float hi = 0.3f, lo = 0.1f;
        for (int i = 0; i < 10; i++) mit += hi - lo;
        Assert.Equal(mit, s.Live[1].DamageMitigated);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Beep", "Beep")]
    [InlineData("Be\0ep", "Be")]   // strncpy stops at the first NUL
    public void CutNameBasics(string? name, string expected) => Assert.Equal(expected, SparStatsState<int>.CutName(name));

    [Fact]
    public void CutNameKeepsSixtyThreeBytesWithoutSplittingACharacter()
    {
        Assert.Equal(new string('a', 63), SparStatsState<int>.CutName(new string('a', 70)));
        Assert.Equal(new string('a', 63), SparStatsState<int>.CutName(new string('a', 63)));
        // 62 ASCII + 'é' (2 bytes) = 64: the é is dropped, not split
        Assert.Equal(new string('a', 62), SparStatsState<int>.CutName(new string('a', 62) + "é"));
        // 61 + 'é' = 63 fits exactly
        Assert.Equal(new string('a', 61) + "é", SparStatsState<int>.CutName(new string('a', 61) + "éx"));
        // 3-byte and 4-byte (surrogate pair) characters
        Assert.Equal(new string('日', 21), SparStatsState<int>.CutName(new string('日', 30)));
        string emoji = string.Concat(Enumerable.Repeat("😀", 20));   // 4 bytes each
        Assert.Equal(string.Concat(Enumerable.Repeat("😀", 15)), SparStatsState<int>.CutName(emoji));
        // a lone surrogate counts as 3 bytes (.NET writes EF BF BD) and is kept as it is
        Assert.Equal(new string('a', 60) + "\uD800", SparStatsState<int>.CutName(new string('a', 60) + "\uD800x"));
        foreach (string n in new[] { new string('a', 62) + "é", new string('日', 30), emoji })
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(SparStatsState<int>.CutName(n)) <= SparPodium.NameMaxChars);
    }

    [Fact]
    public void SnapshotClearZeroesInPlace()
    {
        var snap = new SparPodium.Snapshot
        {
            Outcome = SparPodium.OutcomeKind.Draw, Mode = MatchRules.MatchMode.LastStanding, Header = "x",
            FighterCount = 3, EliminationCount = 2, LastStandingId = 4, PodiumCount = 1,
        };
        var fighters = snap.Fighters; var ids = snap.EliminationIds; var podium = snap.Podium;
        fighters[2].Name = "n"; fighters[2].MarksEarned = 5; ids[1] = 7; podium[0].Place = 1;
        snap.Clear();
        Assert.Same(fighters, snap.Fighters);
        Assert.Same(ids, snap.EliminationIds);
        Assert.Same(podium, snap.Podium);
        Assert.Equal(default, snap.Outcome);
        Assert.Equal(default, snap.Mode);
        Assert.Equal("", snap.Header);
        Assert.Equal((0, 0, 0, 0), (snap.FighterCount, snap.EliminationCount, snap.LastStandingId, snap.PodiumCount));
        Assert.Null(fighters[2].Name);
        Assert.Equal(0, fighters[2].MarksEarned);
        Assert.Equal(0, ids[1]);
        Assert.Equal(0, podium[0].Place);
    }
}
