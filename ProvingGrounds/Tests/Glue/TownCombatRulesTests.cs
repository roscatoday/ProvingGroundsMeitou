using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaMedicalPolicy;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.4, the town rules for fights (E3): hand tests of the C++ predicates TownArena::IsIncidentPair / IsActiveCollateralIncidentPair
/// (TownArena.cpp:1415-1438) over TownArenaLogic's state; the same answers from the combat hooks' published view (CombatMatchView, which the patches
/// read); SanctionedPair (CombatHooks.cpp:37-40); BlockTownBystander (ArenaMedical.cpp:50-55) in the pick-up refusal, the first-aid finder and
/// BlockFor (:157-165); and the port-only after-bout forgiveness of TEMP_ENEMY (TownBoutForgiveness).</summary>
public class TownCombatRulesTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB, N = MatchTeam.None;

    // characters: Null (TownFake.NoCharacter, -1) = the C++ NULL in the TownArenaLogic tests (0 in the view-only tests below, whose views take 0 as none); 11, 12 Team A; 21, 22 Team B; 31 a Team None fighter (side 1, as the C++ reads any team but A); 7, 8 outsiders
    static (TownArenaLogic<int> L, TownFake F) Bout(TownPhase phase, bool owned, int[]? fighters = null, MatchTeam[]? teams = null)
    {
        var f = new TownFake();
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        fighters ??= [11, 12, 21, 22];
        teams ??= [A, A, B, B];
        for (int i = 0; i < fighters.Length; i++) { l.State.Fighters[i] = fighters[i]; l.State.FighterTeams[i] = teams[i]; }
        l.State.FighterCount = fighters.Length;
        l.State.Phase = phase;
        if (owned) { f.Active = true; f.Participants.AddRange(fighters); }
        return (l, f);
    }

    /// <summary>The view the glue publishes (src/Game/CombatHooks.cs Publish): the town fighters (CopyFighters), the incident state
    /// (CopyIncidentState) and OwnsMatch; a running spar of the town's fighters when <paramref name="f"/> says one is active.</summary>
    static CombatMatchView<int> ViewOf(TownArenaLogic<int> l, TownFake f, int[]? prisoners = null, int[]? handlers = null)
    {
        var town = new List<int>();
        l.CopyFighters(town);
        var inc = new List<int>();
        var incTeams = new List<MatchTeam>();
        l.CopyIncidentState(f, inc, incTeams, out bool retained, out bool collateral);
        int[] spar = f.Active ? [.. f.Participants] : [];
        var sparTeams = spar.Select(c => l.State.FighterTeams[Array.IndexOf(l.State.Fighters, c)]).ToArray();
        return new CombatMatchView<int>(Null, f.Active, MatchMode.TeamAvB, spar, sparTeams, new bool[spar.Length], Null, Null, false, [.. town],
            prisoners ?? [], handlers ?? [], [.. inc], [.. incTeams], retained, collateral, l.OwnsMatch(f));
    }

    const int Null = TownFake.NoCharacter;
    static readonly int[] Everyone = [Null, 0, 7, 8, 11, 12, 21, 22, 31];   // Null = NULL; 0 = a real character (CharacterId's default)

    static void AssertViewEqualsLogic(TownArenaLogic<int> l, TownFake f)
    {
        var v = ViewOf(l, f);
        foreach (int a in Everyone)
            foreach (int b in Everyone)
            {
                Assert.True(l.IsIncidentPair(f, a, b) == v.IsIncidentPair(a, b), $"incident {a},{b} in {l.State.Phase}");
                Assert.True(l.IsActiveCollateralIncidentPair(f, a, b) == v.IsCollateralPair(a, b), $"collateral {a},{b} in {l.State.Phase}");
                Assert.True((l.IsIncidentPair(f, a, b) || l.IsActiveCollateralIncidentPair(f, a, b)) == v.IsSanctionedPair(a, b),
                    $"sanctioned {a},{b} in {l.State.Phase}");
            }
    }

    // ---------- TownArena::IsIncidentPair (TownArena.cpp:1415-1427) ----------

    [Fact]
    public void Incident_OppositeSidesWhileFighting()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        Assert.True(l.IsIncidentPair(f, 11, 21));
        Assert.True(l.IsIncidentPair(f, 22, 12));    // either order
        Assert.False(l.IsIncidentPair(f, 11, 12));   // same side
        Assert.False(l.IsIncidentPair(f, 21, 22));
        Assert.False(l.IsIncidentPair(f, 11, 7));    // an outsider has no side
        Assert.False(l.IsIncidentPair(f, 7, 8));
        Assert.False(l.IsIncidentPair(f, 11, 11));   // one character is never a pair (both on one side)
        Assert.False(l.IsIncidentPair(f, Null, 21));    // the C++ NULL
        Assert.False(l.IsIncidentPair(f, 21, Null));
    }

    [Fact]
    public void Incident_FightingNeedsNoOwnedMatch_AndAftercareRetainsThePairs()
    {
        // :1426: Fighting or Aftercare retain without OwnsMatch (the spar may be over: aftercare reads the bout's list)
        var (l, f) = Bout(TownPhase.Fighting, owned: false);
        Assert.True(l.IsIncidentPair(f, 11, 21));
        (l, f) = Bout(TownPhase.Aftercare, owned: false);
        Assert.True(l.IsIncidentPair(f, 11, 21));
        Assert.False(l.IsIncidentPair(f, 11, 12));
    }

    [Fact]
    public void Incident_PreparingOnlyWhileTheTownOwnsTheMatch()
    {
        var (l, f) = Bout(TownPhase.Preparing, owned: false);
        Assert.False(l.IsIncidentPair(f, 11, 21));
        (l, f) = Bout(TownPhase.Preparing, owned: true);
        Assert.True(l.IsIncidentPair(f, 11, 21));
        // a foreign spar (another lineup) does not make the town own it
        f.Participants.Clear(); f.Participants.AddRange([11, 7]);
        Assert.False(l.IsIncidentPair(f, 11, 21));
    }

    [Fact]
    public void Incident_IdleNever()
    {
        // the list is read whatever the phase, but retained is false in Idle (a stale list after Finish is never a pair)
        var (l, f) = Bout(TownPhase.Idle, owned: true);
        Assert.False(l.IsIncidentPair(f, 11, 21));
    }

    [Fact]
    public void Incident_AnyTeamButAIsSideOne()
    {
        // TownArena.cpp:1421: side = (team == TeamA) ? 0 : 1, so a Team None fighter is on Team B's side
        var (l, f) = Bout(TownPhase.Fighting, owned: true, [11, 21, 31], [A, B, N]);
        Assert.True(l.IsIncidentPair(f, 11, 31));
        Assert.False(l.IsIncidentPair(f, 21, 31));
    }

    [Fact]
    public void Incident_ALaterEntryOfTheSameCharacterWins()
    {
        // the C++ loop overwrites the side on every match (the last entry wins)
        var (l, f) = Bout(TownPhase.Aftercare, owned: false, [11, 21, 11], [A, B, B]);
        Assert.False(l.IsIncidentPair(f, 11, 21));   // 11 ends on side 1, like 21
    }

    // ---------- TownArena::IsActiveCollateralIncidentPair (TownArena.cpp:1428-1438) ----------

    [Fact]
    public void Collateral_FighterAndOutsider_OnlyWhileTheOwnedMatchFights()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        Assert.True(l.IsActiveCollateralIncidentPair(f, 11, 7));
        Assert.True(l.IsActiveCollateralIncidentPair(f, 8, 22));
        Assert.False(l.IsActiveCollateralIncidentPair(f, 11, 21));   // both fighters
        Assert.False(l.IsActiveCollateralIncidentPair(f, 11, 12));
        Assert.False(l.IsActiveCollateralIncidentPair(f, 7, 8));     // neither
        Assert.False(l.IsActiveCollateralIncidentPair(f, Null, 7));
        Assert.False(l.IsActiveCollateralIncidentPair(f, 11, Null));    // NULL first, before the list

        foreach (var (phase, owned) in new[] { (TownPhase.Fighting, false), (TownPhase.Preparing, true), (TownPhase.Aftercare, true), (TownPhase.Idle, true) })
        {
            (l, f) = Bout(phase, owned);
            Assert.False(l.IsActiveCollateralIncidentPair(f, 11, 7), $"{phase} owned={owned}");
        }
    }

    // ---------- the published view answers as the logic (the patches read only the view) ----------

    [Fact]
    public void View_EqualsLogic_InEveryPhase()
    {
        foreach (var phase in new[] { TownPhase.Idle, TownPhase.Preparing, TownPhase.Fighting, TownPhase.Aftercare })
            foreach (bool owned in new[] { false, true })
            {
                var (l, f) = Bout(phase, owned);
                AssertViewEqualsLogic(l, f);
                (l, f) = Bout(phase, owned, [11, 21, 31], [A, B, N]);
                AssertViewEqualsLogic(l, f);
            }
    }

    [Fact]
    public void View_CopyIncidentState_EmptyWhenNoPairCanExist()
    {
        foreach (var (phase, owned) in new[] { (TownPhase.Idle, true), (TownPhase.Preparing, false) })
        {
            var (l, f) = Bout(phase, owned);
            var inc = new List<int>();
            var teams = new List<MatchTeam>();
            l.CopyIncidentState(f, inc, teams, out bool retained, out bool collateral);
            Assert.Empty(inc);
            Assert.Empty(teams);
            Assert.False(retained);
            Assert.False(collateral);
            Assert.Equal(0, ViewOf(l, f).IncidentCount);
        }
    }

    [Fact]
    public void View_NoTownBout_NothingSanctioned()
    {
        var v = CombatMatchView<int>.Idle(0);
        Assert.False(v.IsSanctionedPair(11, 21));
        Assert.False(v.IsTownFighter(11));
        Assert.False(v.BlocksTownBystander(11, false));
        // a player-arena match with no town bout: the town rules never fire (PA §2.8: the town list is empty in the player arena)
        var spar = new CombatMatchView<int>(0, true, MatchMode.TeamAvB, [1, 2], [A, B], [false, false], 0, 0, false, [], []);
        Assert.False(spar.IsSanctionedPair(1, 2));
        Assert.False(spar.BlocksTownBystander(1, false));
        Assert.False(spar.RefusesPickUp(7, 1));
    }

    [Fact]
    public void View_Matches_SeesTheTownState()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        var v = ViewOf(l, f);
        var town = new List<int>();
        l.CopyFighters(town);
        int[] spar = [.. f.Participants];
        var sparTeams = new[] { A, A, B, B };
        var none = new bool[4];
        Assert.True(v.Matches(true, MatchMode.TeamAvB, spar, sparTeams, none, Null, Null, false, [.. town], [], [], [11, 12, 21, 22], [A, A, B, B], true, true, true));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, spar, sparTeams, none, Null, Null, false, [.. town], [], [], [11, 12, 21, 22], [A, A, B, B], true, false, true));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, spar, sparTeams, none, Null, Null, false, [.. town], [], [], [11, 12, 21, 22], [A, B, A, B], true, true, true));
        Assert.False(v.Matches(true, MatchMode.TeamAvB, spar, sparTeams, none, Null, Null, false, [.. town], [], [], [11, 12, 21, 22], [A, A, B, B], true, true, false));
    }

    // ---------- BlockTownBystander (ArenaMedical.cpp:50-55): the finder, the pick-up, BlockFor ----------

    [Fact]
    public void Bystander_OnlyMedicsTreatOrLiftTownFightersWhileTheTownOwnsTheMatch()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        var v = ViewOf(l, f);
        Assert.True(v.BlocksTownBystander(21, actorIsTownMedic: false));
        Assert.False(v.BlocksTownBystander(21, actorIsTownMedic: true));
        Assert.False(v.BlocksTownBystander(7, actorIsTownMedic: false));   // not a town fighter

        // OwnsMatch false (Preparing before the spar starts, or a foreign spar): nobody is blocked
        (l, f) = Bout(TownPhase.Preparing, owned: false);
        v = ViewOf(l, f);
        Assert.True(v.IsTownFighter(21));
        Assert.False(v.BlocksTownBystander(21, false));
        // Aftercare: IsFighter is false (TownArena.cpp:1411), so the medics' aftercare and anyone else are free
        (l, f) = Bout(TownPhase.Aftercare, owned: false);
        v = ViewOf(l, f);
        Assert.False(v.BlocksTownBystander(21, false));
    }

    [Fact]
    public void PickUp_TownFighter_MedicExempt_OthersRefused()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        var v = ViewOf(l, f);
        Assert.True(v.RefusesPickUp(7, 21));                               // an outsider: isolated pair and a bystander
        Assert.False(v.RefusesPickUp(7, 21, carrierIsTownMedic: true));    // a Scratch medic carries a town fighter (ArenaMedical.cpp:66-68)
        Assert.True(v.IsOwnCarrier(7, 21, carrierIsTownMedic: true));
        Assert.True(v.RefusesPickUp(11, 21));                              // the opponent: not isolated (sparring), but not a medic
        Assert.True(v.RefusesPickUp(11, 12));                              // a team-mate
        Assert.False(v.RefusesPickUp(7, 8));                               // two outsiders: untouched
        Assert.True(v.RefusesPickUp(21, 7));                               // a town fighter lifting an outsider: task 3.2b's isolation refusal
        Assert.True(v.ShouldIsolatePair(21, 7));                           // (not a bystander case: the body is no town fighter)
        Assert.False(v.BlocksTownBystander(7, false));
    }

    [Fact]
    public void PickUp_PreparingWithoutTheMatch_KeepsTheIsolationRefusal_ButMedicsLift()
    {
        // before the spar starts the C++ blocks nobody (OwnsMatch false) but task 3.2b's port-only isolation refusal holds for outsiders; the
        // medic exemption is the mod's own carrier, so medics are not refused there either
        var (l, f) = Bout(TownPhase.Preparing, owned: false);
        var v = ViewOf(l, f);
        Assert.True(v.RefusesPickUp(7, 21));
        Assert.False(v.RefusesPickUp(7, 21, carrierIsTownMedic: true));
    }

    [Fact]
    public void PickUp_PrisonerHandlerExemptionUnchanged()
    {
        // task 6.3's own carrier: the handler of match prisoner 40 lifts it; another outsider does not; with a town bout running too
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        var v = ViewOf(l, f, prisoners: [40], handlers: [41]);
        Assert.False(v.RefusesPickUp(41, 40));
        Assert.True(v.RefusesPickUp(7, 40));
        Assert.True(v.RefusesPickUp(7, 40, carrierIsTownMedic: true));   // a medic is the mod's own carrier for town fighters only
    }

    [Fact]
    public void BlockFor_TownHalfAndProtocolHalf()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        var v = ViewOf(l, f);
        // the town half: a non-medic actor on a town fighter
        Assert.True(ArenaMedicalLogic.BlockFor(v, Protocol.RingsideAid, true, 21, actorIsTownMedic: false));
        Assert.False(ArenaMedicalLogic.BlockFor(v, Protocol.RingsideAid, true, 7, actorIsTownMedic: false));
        // no actor: the town half is skipped (C++ `actor && ...`), the protocol half alone decides
        Assert.Equal(ArenaMedicalLogic.BlocksTreatment(v, Protocol.RingsideAid, 21), ArenaMedicalLogic.BlockFor(v, Protocol.RingsideAid, false, 21, false));
        // a medic: the protocol half alone (Bloodsport blocks a participant's care during the match)
        Assert.Equal(ArenaMedicalLogic.BlocksTreatment(v, Protocol.Bloodsport, 21), ArenaMedicalLogic.BlockFor(v, Protocol.Bloodsport, true, 21, true));
        Assert.True(ArenaMedicalLogic.BlockFor(v, Protocol.Bloodsport, true, 21, true));
        // the player arena: unchanged (no town fighters)
        var spar = new CombatMatchView<int>(0, true, MatchMode.TeamAvB, [1, 2], [A, B], [false, false], 0, 0, false, [], []);
        foreach (var p in new[] { Protocol.RingsideAid, Protocol.Bloodsport })
            foreach (int c in new[] { 1, 2, 7 })
                Assert.Equal(ArenaMedicalLogic.BlocksTreatment(spar, p, c), ArenaMedicalLogic.BlockFor(spar, p, true, c, false));
    }

    // ---------- setCrime (CombatHooks.cpp:94-114): the SetPending prefix ----------

    [Fact]
    public void Crime_TheCombatCrimes_AreMurderAssaultAndVipAssault()
    {
        // CombatHooks.cpp:96: CRIME_ASSAULT, CRIME_ASSAULT_VIP, CRIME_MURDER = Meitou's Assault 5, Treason 6, Murder 4 (CrimeTypes.cs)
        Assert.True(CombatHookLogic.IsCombatCrime(4));
        Assert.True(CombatHookLogic.IsCombatCrime(5));
        Assert.True(CombatHookLogic.IsCombatCrime(6));
        foreach (int other in new[] { 0, 1, 2, 3, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })
            Assert.False(CombatHookLogic.IsCombatCrime(other));
    }

    [Fact]
    public void Crime_CombatCrimeBetweenASanctionedPair_IsNotSet()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        var v = ViewOf(l, f);
        Assert.True(v.SkipsCrime(true, 11, 21));     // incident pair
        Assert.True(v.SkipsCrime(true, 21, 11));
        Assert.True(v.SkipsCrime(true, 11, 7));      // collateral pair (fighter and outsider)
        Assert.True(v.SkipsCrime(true, 7, 22));
        Assert.False(v.SkipsCrime(false, 11, 21));   // any other crime (theft, kidnapping, ...) stands
        Assert.False(v.SkipsCrime(true, 11, 12));    // team-mates: not sanctioned
        Assert.False(v.SkipsCrime(true, 7, 8));      // outsiders
        Assert.False(v.SkipsCrime(true, 11, Null));  // no victim
        // aftercare: the incident pairs stay exempt, the collateral ones do not (TownArena.cpp:1426, 1430)
        (l, f) = Bout(TownPhase.Aftercare, owned: false);
        v = ViewOf(l, f);
        Assert.True(v.SkipsCrime(true, 11, 21));
        Assert.False(v.SkipsCrime(true, 11, 7));
        // no town bout: never
        Assert.False(CombatMatchView<int>.Idle(0).SkipsCrime(true, 11, 21));
    }

    // ---------- the after-bout clearing of TEMP_ENEMY (port only, M7-town-core.md task 7.4) ----------

    /// <summary>A squad memory: each character's own squad, its faction's other squads, the TEMP_ENEMY tags held (squad, target) and the clears.</summary>
    sealed class Memory : ITempEnemyHost<int>
    {
        public readonly HashSet<int> Players = [];
        public readonly Dictionary<int, int[]> Squads = [];   // character -> its squad first, then its faction's other squads
        public readonly HashSet<(int, int)> Held = [];
        public readonly List<(int, int)> Cleared = [];
        public bool IsPlayer(int c) => Players.Contains(c);
        public void SquadsOf(int observer, List<int> into) { if (Squads.TryGetValue(observer, out var q)) into.AddRange(q); }
        public bool HoldsTempEnemy(int squad, int target) => Held.Contains((squad, target));
        public void ClearTempEnemy(int squad, int target) { Cleared.Add((squad, target)); Held.Remove((squad, target)); }
    }

    /// <summary>A squad memory whose reads throw (a world gone wrong at the match start).</summary>
    sealed class BrokenMemory : ITempEnemyHost<int>
    {
        public bool IsPlayer(int c) => c == 1;
        public void SquadsOf(int observer, List<int> into) => into.Add(900);
        public bool HoldsTempEnemy(int squad, int target) => throw new InvalidOperationException("broken");
        public void ClearTempEnemy(int squad, int target) => throw new InvalidOperationException("broken");
    }

    // squads: 100 = the player's squad, 101 = another player-faction squad; 900 = town fighter 21's squad, 901 = a guard squad of its faction
    static Memory TwoSides()
    {
        var m = new Memory();
        m.Players.Add(1);
        m.Squads[1] = [100, 101];
        m.Squads[21] = [900, 901];
        m.Squads[22] = [902, 900, 901];
        return m;
    }

    [Fact]
    public void Clearing_ClearsOnlyWhatTheBoutWrote_InEverySquadOfTheSide()
    {
        var m = TwoSides();
        m.Held.Add((777, 1));   // a squad of another faction (not recorded: not on the fighters' sides)
        var g = new TownBoutForgiveness<int>();
        g.Begin(m, [1, 21], [A, B]);
        Assert.Equal(4, g.Count);   // 900, 901 on 1; 100, 101 on 21
        m.Held.Add((900, 1));       // the alert in the fighter's squad
        m.Held.Add((901, 1));       // a massive alarm in the faction's other squad
        m.Held.Add((100, 21));      // the player's squad on the town fighter
        Assert.Equal(3, g.End(m, true));
        Assert.Equal([(900, 1), (901, 1), (100, 21)], m.Cleared);
        Assert.Contains((777, 1), m.Held);
        Assert.Equal(0, g.Count);
        Assert.Equal(0, g.End(m, true));   // ended once
    }

    [Fact]
    public void Clearing_KeepsATagHeldBeforeTheMatch()
    {
        var m = TwoSides();
        m.Held.Add((901, 1));   // the guards already took the player for an enemy
        var g = new TownBoutForgiveness<int>();
        g.Begin(m, [1, 21], [A, B]);
        m.Held.Add((900, 1));
        Assert.Equal(1, g.End(m, true));
        Assert.Equal([(900, 1)], m.Cleared);
        Assert.Contains((901, 1), m.Held);
    }

    [Fact]
    public void Clearing_NothingWrittenNothingCleared()
    {
        var m = TwoSides();
        var g = new TownBoutForgiveness<int>();
        g.Begin(m, [1, 21], [A, B]);
        Assert.Equal(0, g.End(m, true));
        Assert.Empty(m.Cleared);
    }

    [Fact]
    public void Clearing_EachSquadAndTargetOnce_OnlyPlayersAgainstTheTownOnTheOtherSide()
    {
        var m = TwoSides();
        m.Players.Add(2);
        m.Squads[2] = [100, 101];
        m.Squads[11] = [903, 900];
        var g = new TownBoutForgiveness<int>();
        // players 1, 2 with town 11 on Team A against town 21, 22 on Team B: 21's and 22's squads on 1 and 2 (900, 901, 902: three each), and the
        // players' squads on 21 and 22 (100, 101: two each); never 11's squad 903, never the players on each other
        g.Begin(m, [1, 2, 11, 21, 22], [A, A, A, B, B]);
        Assert.Equal(3 * 2 + 2 * 2, g.Count);
        foreach (int sq in new[] { 100, 101, 900, 901, 902, 903 })
            foreach (int t in new[] { 1, 2, 11, 21, 22 })
                m.Held.Add((sq, t));
        Assert.Equal(10, g.End(m, true));
        Assert.Equal([(900, 1), (901, 1), (100, 21), (101, 21), (902, 1), (100, 22), (101, 22), (900, 2), (901, 2), (902, 2)], m.Cleared);
        Assert.Contains((903, 1), m.Held);
        Assert.Contains((100, 2), m.Held);
    }

    [Fact]
    public void Clearing_NpcBout_RecordsNothing_AndBeginReplaces_AndClearForgets()
    {
        var m = TwoSides();
        var g = new TownBoutForgiveness<int>();
        g.Begin(m, [21, 22], [A, B]);
        Assert.Equal(0, g.Count);   // an ambient bout: no player character

        m.Squads[23] = [904];
        g.Begin(m, [1, 21], [A, B]);
        g.Begin(m, [1, 23], [A, B]);   // a new match drops the old record
        m.Held.Add((900, 1));
        m.Held.Add((904, 1));
        Assert.Equal(1, g.End(m, true));
        Assert.Equal([(904, 1)], m.Cleared);

        g.Begin(m, [1, 21], [A, B]);
        m.Held.Add((100, 21));
        g.Clear();
        Assert.Equal(0, g.End(m, true));   // a world unload forgets without clearing
    }

    [Fact]
    public void Clearing_AnotherMatchEnding_LeavesTheTownRecord()
    {
        // a player-arena match's end (the town does not own it) never uses the town's record; the town match's own end does
        var m = TwoSides();
        var g = new TownBoutForgiveness<int>();
        g.Begin(m, [1, 21], [A, B]);
        m.Held.Add((900, 1));
        Assert.Equal(0, g.End(m, ownedMatchEnding: false));
        Assert.True(g.Recorded);
        Assert.Equal(4, g.Count);
        Assert.Empty(m.Cleared);
        Assert.Equal(1, g.End(m, ownedMatchEnding: true));
        Assert.False(g.Recorded);
        // with nothing recorded an owned end does nothing either
        m.Held.Add((901, 1));
        Assert.Equal(0, g.End(m, true));
        Assert.Contains((901, 1), m.Held);
    }

    [Fact]
    public void Clearing_ABrokenStartLeavesNoRecordAndDoesNotThrow()
    {
        var g = new TownBoutForgiveness<int>();
        Assert.Null(g.TryBegin(TwoSides(), [1, 21], [A, B]));
        Assert.True(g.Recorded);
        var e = g.TryBegin(new BrokenMemory(), [1, 21], [A, B]);
        Assert.IsType<InvalidOperationException>(e);
        Assert.False(g.Recorded);   // the earlier record went too: nothing stale is applied later
        Assert.Equal(0, g.Count);
        Assert.Equal(0, g.End(new BrokenMemory(), true));   // nothing to clear, no read
    }

    // ---------- the clearing runs at the town event's end (Finish, task 8.4b): after the town's own abort, and after the aftercare ----------

    [Fact]
    public void Abort_CancelForSave_RunsTheClearingInFinishAfterTheSparAbort()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        l.CancelForSave(f);
        int rules = f.Calls.IndexOf("rules event ended"), abort = f.Calls.IndexOf("spar abort");
        Assert.True(abort >= 0 && rules > abort, string.Join(", ", f.Calls));
        Assert.True(rules > f.Calls.IndexOf("aftercare release"), string.Join(", ", f.Calls));
        Assert.Equal(TownPhase.Idle, l.State.Phase);
    }

    [Fact]
    public void Abort_Cancel_RunsTheClearingInFinishAfterTheSparAbort()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true);
        l.Cancel(f);
        Assert.True(f.Calls.IndexOf("rules event ended") > f.Calls.IndexOf("spar abort"));
        Assert.Contains("spar abort", f.Calls);
    }

    [Fact]
    public void Abort_AForeignSpar_IsNotAbortedButTheTownEventsEndStillClearsItsOwnRecord()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: false);
        f.Active = true; f.Participants.AddRange([5, 6]);   // a player spar, not the town's
        l.CancelForSave(f);
        Assert.DoesNotContain("spar abort", f.Calls);
        Assert.Contains("rules event ended", f.Calls);   // Finish: the town event's own record (none was recorded for the player's spar)
    }

    /// <summary>A squad memory whose clear throws (reads work).</summary>
    sealed class ClearThrows : ITempEnemyHost<int>
    {
        public readonly Memory Inner = TwoSides();
        public bool IsPlayer(int c) => Inner.IsPlayer(c);
        public void SquadsOf(int observer, List<int> into) => Inner.SquadsOf(observer, into);
        public bool HoldsTempEnemy(int squad, int target) => Inner.HoldsTempEnemy(squad, target);
        public void ClearTempEnemy(int squad, int target) => throw new InvalidOperationException("clear failed");
    }

    [Fact]
    public void Abort_AClearThatThrows_StillAbortsAndFinishes_AndDropsTheRecord()
    {
        var (l, f) = Bout(TownPhase.Fighting, owned: true, [1, 21], [A, B]);
        var mem = new ClearThrows();
        var g = new TownBoutForgiveness<int>();
        g.Begin(mem, [1, 21], [A, B]);
        mem.Inner.Held.Add((900, 1));   // the bout wrote a tag: the clear is reached and throws
        f.Rules = g; f.RulesMemory = mem;
        l.CancelForSave(f);
        Assert.IsType<InvalidOperationException>(f.RulesError);
        Assert.True(f.Calls.IndexOf("rules event ended") > f.Calls.IndexOf("spar abort"));   // the abort ran, then Finish
        Assert.Equal(TownPhase.Idle, l.State.Phase);                                                // Finish ran
        Assert.Equal(TownArena.Text.TrialCancelled, l.GetStatus());
        Assert.False(g.Recorded);                                                                    // no half-used record left
        Assert.Equal(0, g.Count);
    }

    [Fact]
    public void TryEnd_ClearsLikeEnd_AndKeepsTheRecordForAnotherMatch()
    {
        var m = TwoSides();
        var g = new TownBoutForgiveness<int>();
        g.Begin(m, [1, 21], [A, B]);
        m.Held.Add((900, 1));
        Assert.Null(g.TryEnd(m, false, out int none));
        Assert.Equal(0, none);
        Assert.True(g.Recorded);
        Assert.Null(g.TryEnd(m, true, out int n));
        Assert.Equal(1, n);
        Assert.Equal([(900, 1)], m.Cleared);
        Assert.False(g.Recorded);
    }
}
