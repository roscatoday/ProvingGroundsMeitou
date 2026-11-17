using System.Globalization;
using System.Text;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.2: TownMatchmakingRuntime.cpp's Collect and helpers (TownMatchmakingRuntimeLogic) over the town fake, and TownArena.cpp's
/// Ready / SnapshotFor / the preview's throttle and frozen roster (TownArenaLogic.Matchmaking.cs).</summary>
public class TownMatchmakingRuntimeLogicTests
{
    const string Mace = "232-Proving Grounds.mod", MaceMedium = "230-Proving Grounds.mod", Greatsword = "238-Proving Grounds.mod",
        Polearm = "223-Proving Grounds.mod", Civilian = "103-Proving Grounds.mod", Ressa = "616-Proving Grounds.mod";

    static (TownArenaLogic<int> L, TownFake F) Town()
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Player(1);
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        f.Logic = l;
        l.Tick(f);   // binds the registry
        return (l, f);
    }

    static PlacedBuilding Registry => TownFake.TownRegistry();

    static TownRosterSnapshot<int> Collect(TownArenaLogic<int> l, TownFake f, IReadOnlyList<int> players, bool includeNamed = true) =>
        TownMatchmakingRuntimeLogic.Collect(f, Registry, players, (int c, bool d, bool b, out string r) => l.Ready(f, c, d, b, out r), includeNamed);

    // ---------- Collect: who is found ----------

    [Fact]
    public void FightersByRecordMedicsAndNamedOnlyForChallengesSortedById()
    {
        var (l, f) = Town();
        f.Npc(30, Mace); f.Npc(20, Greatsword); f.Npc(40, TownFake.Medic); f.Npc(50, Civilian); f.Npc(60, Ressa);
        var s = Collect(l, f, []);
        Assert.Equal(["fid-20", "fid-30", "fid-60"], s.Npcs.Select(n => n.Id));
        Assert.Equal([Greatsword, Mace, Ressa], s.Npcs.Select(n => n.Role));
        Assert.Equal([40], s.Medics);
        Assert.Equal(5, s.InTown);   // the civilian is in town (counted), not a candidate
        Assert.Equal(1, s.Roles[Civilian]);

        var ambient = Collect(l, f, [], includeNamed: false);
        Assert.Equal(["fid-20", "fid-30"], ambient.Npcs.Select(n => n.Id));
        Assert.Contains(60, f.Removed);   // a named fighter's old Marks rows go even when it is not an opponent (:82-83)
        Assert.DoesNotContain(50, f.Removed);
        Assert.DoesNotContain(40, f.Removed);
    }

    [Fact]
    public void OtherTownOutsideTheCircleAndOtherFactionsAreNotDiscovered()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        f.Npc(11, Mace, town: TownFake.Elsewhere);
        f.Npc(12, Mace, dx: 2600);                     // outside 2,500
        f.Npc(13, Mace); f.OtherFaction.Add(13);       // port only (census): not Bloodstamp
        var s = Collect(l, f, []);
        Assert.Equal(["fid-10"], s.Npcs.Select(n => n.Id));
        Assert.Equal(3, s.Scanned);   // 10, 11, 13 in the sphere
        Assert.Equal(1, s.InTown);
        Assert.DoesNotContain("fid-13", f.Created);   // never given an identity
    }

    [Fact]
    public void ATypeWithNobodyPresentIsJustAbsentNotAnError()
    {
        // the owner's mod list (census B): no Polearm, Sabre, Katana or Hacker fighter at all
        var (l, f) = Town();
        f.Npc(10, Mace); f.Npc(11, MaceMedium); f.Npc(12, Greatsword); f.Npc(13, TownFake.Medic);
        var s = Collect(l, f, []);
        Assert.Equal(3, s.Npcs.Count);
        Assert.DoesNotContain(s.Npcs, n => TownFighterCatalog.Find(n.Role)!.Type == "Polearm");
        Assert.Empty(s.Blockers);
        Assert.Equal("3 eligible opponents, 1 available medics", s.Availability);
    }

    [Fact]
    public void NoWorldNoRegistryNoTownOrNoCallbackGivesTheUnavailableSnapshot()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        foreach (var s in new[]
        {
            TownMatchmakingRuntimeLogic.Collect(f, null, [], (int c, bool d, bool b, out string r) => l.Ready(f, c, d, b, out r), true),
            TownMatchmakingRuntimeLogic.Collect(f, Registry with { Town = TownKey.None }, [], (int c, bool d, bool b, out string r) => l.Ready(f, c, d, b, out r), true),
            TownMatchmakingRuntimeLogic.Collect(f, Registry, [], null, true),
        })
        {
            Assert.Equal("Town registry unavailable", s.Availability);
            Assert.Equal("registry/world/town/readiness callback unavailable", s.Diagnostics);
            Assert.Empty(s.Npcs);
            Assert.Equal("", s.Context);
        }
        Assert.Equal(0, f.CharacterQueries);
    }

    // ---------- Collect: who is rejected and why ----------

    [Fact]
    public void BlockersCountEachReasonByRole()
    {
        var (l, f) = Town();
        f.Npc(10, Mace); f.Aftercare.Add(10);
        f.Npc(11, Mace); f.InLimbShop.Add(11);
        f.Npc(12, TownFake.Medic); f.CarryingSet.Add(12);
        f.Npc(13, TownFake.Medic); f.CarriedSet.Add(13);
        f.Npc(14, TownFake.Medic); f.DeadSet.Add(14);
        f.Npc(15, Mace); f.NoIdentity.Add(15);
        f.Npc(16, Mace); f.TokenOf[16] = "dup";
        f.Npc(17, Mace); f.TokenOf[17] = "dup";
        f.Npc(18, Mace); f.Health[18] = (0.5f, 1f);
        f.Npc(19, Mace, ability: -1);
        f.Npc(20, Mace, ability: 0);
        f.Npc(21, Mace); f.KoSet.Add(21);
        var s = Collect(l, f, []);
        Assert.Empty(s.Npcs);
        Assert.Empty(s.Medics);
        Assert.Equal(2, s.Blockers[Mace + ": active bout/recovery ownership"]);
        Assert.Equal(2, s.Blockers[TownFake.Medic + ": carrying/being carried"]);   // carrying, being carried
        Assert.Equal(1, s.Blockers[TownFake.Medic + ": dead"]);
        Assert.Equal(3, s.Blockers[Mace + ": missing/ambiguous opponent identity"]);   // no identity + both namesakes
        Assert.Equal(1, s.Blockers[Mace + ": recovering"]);
        Assert.Equal(2, s.Blockers[Mace + ": invalid ability stats"]);
        Assert.Equal(1, s.Blockers[Mace + ": unconscious"]);
        Assert.Equal(4, s.MissingIdentity);   // the three medics (never given one) and 15
        Assert.Equal(1, s.Ambiguous);
        Assert.False(s.Characters["dup"].Unique);
    }

    [Fact]
    public void AnActiveBoutFighterIsOwnedByTheBout()
    {
        var (l, f) = Town();
        f.Npc(10, Mace); f.Npc(11, Greatsword);
        l.State.Phase = TownPhase.Fighting; l.State.Fighters[0] = 10; l.State.FighterCount = 1;
        var s = Collect(l, f, []);
        Assert.Equal(["fid-11"], s.Npcs.Select(n => n.Id));
        Assert.Equal(1, s.Blockers[Mace + ": active bout/recovery ownership"]);
    }

    [Fact]
    public void ResolveNeedsAUniqueKeyAndTheSameIdentityNow()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        var s = Collect(l, f, []);
        Assert.True(TownMatchmakingRuntimeLogic.Resolve(f, s, "fid-10", out int c));
        Assert.Equal(10, c);
        f.TokenOf[10] = "changed";
        Assert.False(TownMatchmakingRuntimeLogic.Resolve(f, s, "fid-10", out _));
        Assert.False(TownMatchmakingRuntimeLogic.Resolve(f, s, "nobody", out _));
    }

    // ---------- Collect: the players ----------

    [Fact]
    public void PlayersReadyNeedsOneToThreeReadyDistinctFighters()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        Assert.False(Collect(l, f, []).PlayersReady);
        f.Player(2); f.Player(3); f.Player(4);
        var one = Collect(l, f, [1]);
        Assert.True(one.PlayersReady);
        Assert.Equal(["player:0:0:0:1:1"], one.Players.Select(p => p.Id));
        Assert.Equal("", one.PlayerBlocker);
        var four = Collect(l, f, [1, 2, 3, 4]);
        Assert.False(four.PlayersReady);
        Assert.Equal(4, four.Players.Count);   // still read, only not ready
    }

    [Fact]
    public void PlayerReasonsAsTheCppOrdersThemTheFirstBlockerNamesTheFighter()
    {
        var (l, f) = Town();
        f.Player(2, town: TownFake.Elsewhere);
        f.Player(3); f.DeadSet.Add(3);
        f.Player(4); f.Aftercare.Add(4);
        f.Player(5); f.NoHandle.Add(5);
        f.Player(6); f.AbilityOf[6] = 0;
        f.Invalid.Add(7); f.Players!.Add(7);
        var s = Collect(l, f, [2]);
        Assert.Equal("Player 2: return to the registry town", s.PlayerBlocker);
        Assert.False(s.PlayersReady);
        Assert.Equal(1, s.Blockers["selected player: return to the registry town"]);
        Assert.Equal("Player 3: dead", Collect(l, f, [3]).PlayerBlocker);
        Assert.Equal("Player 4: still in aftercare", Collect(l, f, [4]).PlayerBlocker);
        Assert.Equal("Player 5: live character handle unavailable", Collect(l, f, [5]).PlayerBlocker);
        Assert.Equal("Player 6: combat stats unavailable", Collect(l, f, [6]).PlayerBlocker);
        Assert.Equal("selected character is unavailable", Collect(l, f, [7]).PlayerBlocker);
        var dup = Collect(l, f, [1, 1]);
        Assert.Equal("Player 1: same fighter selected more than once", dup.PlayerBlocker);
        Assert.Single(dup.Players);
    }

    [Fact]
    public void ABookedPlayerThatIsNoPlayerCharacterGetsThePlainFallback()
    {
        var (l, f) = Town();
        f.Npc(9, Mace);   // a selected character that is not the player's: Ready(bookedPlayer) returns false with the first reason
        Assert.Equal("Npc 9: health data unavailable", Collect(l, f, [9]).PlayerBlocker);
    }

    // ---------- Collect: diagnostics and context ----------

    [Fact]
    public void DiagnosticsAndContextAreTheCppText()
    {
        var (l, f) = Town();
        f.Npc(20, Greatsword, ability: 61.25, rating: 112.5); f.Npc(10, Mace, ability: 50, rating: 100); f.Npc(30, TownFake.Medic);
        f.Npc(40, Mace); f.DeadSet.Add(40);
        f.AbilityOf[1] = 70.5; f.RatingOf[1] = 101;
        var s = Collect(l, f, [1]);
        Assert.Equal("2 eligible opponents, 1 available medics; scanned=4 inTown=4 missingIdentity=1 ambiguousKeys=0 selected=1 eligiblePlayers=1 " +
            "playersReady=1; roles=[100-Proving Grounds.mod=1][232-Proving Grounds.mod=2][238-Proving Grounds.mod=1]; rejected=[232-Proving Grounds.mod: dead=1]",
            s.Diagnostics);
        string raw = "1:1|P16:player:0:0:0:1:1:70.5:101|N6:fid-10:232-Proving Grounds.mod:50:100|N6:fid-20:238-Proving Grounds.mod:61.25:112.5";
        Assert.Equal(raw, TownMatchmakingRuntimeLogic.RawContext(s));
        uint first = 2166136261u, second = 5381u;
        foreach (byte b in Encoding.UTF8.GetBytes(raw)) { unchecked { first = (first ^ b) * 16777619u; second = (second * 33u) ^ b; } }
        Assert.Equal(first.ToString(CultureInfo.InvariantCulture) + ":" + second.ToString(CultureInfo.InvariantCulture), s.Context);
    }

    [Fact]
    public void ContextPrintsDoublesWithSeventeenDigits()
    {
        var (l, f) = Town();
        f.Npc(10, Mace, ability: 0.1, rating: 1.0 / 3.0);
        var s = Collect(l, f, []);
        Assert.Equal("0:0|N6:fid-10:232-Proving Grounds.mod:0.10000000000000001:0.33333333333333331", TownMatchmakingRuntimeLogic.RawContext(s));
    }

    [Fact]
    public void DiagnosticRowsStopAtSixtyFour()
    {
        var (l, f) = Town();
        for (int i = 0; i < 70; i++) { f.Npc(100 + i, "x" + i.ToString("D2", CultureInfo.InvariantCulture)); }
        var s = Collect(l, f, []);
        string roles = s.Diagnostics.Split("; roles=")[1].Split("; rejected=")[0];
        Assert.Equal(64, roles.Count(ch => ch == '['));
    }

    [Fact]
    public void LabelNamesTheCatalogDivisionAndType()
    {
        var (_, f) = Town();
        f.Npc(10, MaceMedium); f.Npc(11, Civilian);
        Assert.Equal("Npc 10 (Medium Mace)", TownMatchmakingRuntimeLogic.Label(f, 10));
        Assert.Equal("Npc 11", TownMatchmakingRuntimeLogic.Label(f, 11));
        f.Invalid.Add(11);
        Assert.Equal("Unavailable", TownMatchmakingRuntimeLogic.Label(f, 11));
    }

    // ---------- Ready (TownArena.cpp:297-346) ----------

    [Fact]
    public void ReadyThresholdsAndReasonsInTheCppOrder()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        Assert.True(l.Ready(f, 10, false, false, out string r)); Assert.Equal("", r);
        f.Health[10] = (0.9f, 0.9f);
        Assert.True(l.Ready(f, 10, false, false, out _));
        f.Health[10] = (0.89f, 1f);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("recovering", r);
        f.Health[10] = (1f, 0.89f);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("recovering", r);
        f.NeedsAid.Add(10);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("needs first aid", r);
        f.Fighting.Add(10);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("already in combat", r);
        f.KoSet.Add(10);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("unconscious", r);
        f.DeadSet.Add(10);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("dead", r);
        Assert.False(l.Ready(f, 1, false, false, out r)); Assert.Equal("belongs to the player squad", r);
        f.NoHealth.Add(11); f.Npc(11, Mace);
        Assert.False(l.Ready(f, 11, false, false, out r)); Assert.Equal("anatomy is empty", r);
        f.Invalid.Add(12);
        Assert.False(l.Ready(f, 12, false, false, out r)); Assert.Equal("health data unavailable", r);
        Assert.False(l.Ready(f, TownFake.NoCharacter, false, false, out r)); Assert.Equal("health data unavailable", r);   // the C++ NULL
    }

    [Fact]
    public void BookedPlayerUsesTheSameThresholdsAndQuarantineSparesMedics()
    {
        var (l, f) = Town();
        Assert.True(l.Ready(f, 1, false, true, out _));
        f.Health[1] = (0.5f, 1f);
        Assert.False(l.Ready(f, 1, false, true, out string r)); Assert.Equal("recovering", r);
        f.Npc(10, Mace);
        Assert.False(l.Ready(f, 10, false, true, out r)); Assert.Equal("health data unavailable", r);
        l.State.IngressQuarantine.Add(10);
        Assert.False(l.Ready(f, 10, false, false, out r)); Assert.Equal("could not reach arena formation", r);
        Assert.True(l.Ready(f, 10, true, false, out _));   // a doctor is not quarantined
    }

    [Fact]
    public void MedicsUseNativeCapacityNotTheFightersReserve()
    {
        var (l, f) = Town();
        f.Npc(30, TownFake.Medic); f.Health[30] = (0.2f, 0.3f);
        Assert.True(l.Ready(f, 30, true, false, out _));
        f.NotMedicallyAble.Add(30);
        Assert.False(l.Ready(f, 30, true, false, out string r)); Assert.Equal("native medical capacity check failed", r);
    }

    [Fact]
    public void TheCandidateLineIsLoggedUnlessDiscoveryIsQuiet()
    {
        var (l, f) = Town();
        f.Npc(10, Mace); f.Health[10] = (0.875f, 0.9996f); f.Fighting.Add(10);
        f.Debugs.Clear();
        l.Ready(f, 10, false, false, out _);
        Assert.Equal(["Town candidate 232-Proving Grounds.mod: ready=0 health=0.875 blood=1.000 player=0 dead=0 ko=0 combat=1 aid=0"], f.Debugs);
        f.Debugs.Clear();
        l.SnapshotFor(f, []);
        Assert.DoesNotContain(f.Debugs, d => d.StartsWith("Town candidate", StringComparison.Ordinal));
        Assert.False(l.State.QuietDiscovery);
    }

    // ---------- SnapshotFor and the preview roster (TownArena.cpp:573-590, 749-793) ----------

    [Fact]
    public void SnapshotForLogsTheDiscoveryLineEveryThirtySeconds()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        f.Debugs.Clear();
        var s = l.SnapshotFor(f, []);
        Assert.Equal(["fid-10"], s.Npcs.Select(n => n.Id));
        Assert.Equal(["Proving Grounds: matchmaking discovery - " + s.Diagnostics], f.Debugs);
        f.Now += 29_999; l.SnapshotFor(f, []);
        Assert.Single(f.Debugs);
        f.Now += 1; l.SnapshotFor(f, []);
        Assert.Equal(2, f.Debugs.Count);
    }

    [Fact]
    public void SnapshotForWithNoBoundRegistryIsUnavailable()
    {
        var f = new TownFake();
        f.Npc(10, Mace);
        var s = new TownArenaLogic<int>(TownFake.NoCharacter).SnapshotFor(f, []);
        Assert.Equal("Town registry unavailable", s.Availability);
    }

    static void Settle(TownArenaLogic<int> l, TownFake f)
    {
        f.Now += TownArenaLogic<int>.RosterObserveMs;
        l.Tick(f);   // the settle clock starts (the registry was bound after the first tick's check)
        Assert.False(l.RosterSettled(f));
        f.Now += TownArenaLogic<int>.RosterSettleMs;
        l.Tick(f);
        Assert.True(l.RosterSettled(f));
    }

    [Fact]
    public void ThePreviewWaitsForTheTownToFillThenPollsOncePerSecond()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        var card = new TownChallengePolicy.Card();
        Assert.False(l.UpdatePreviewRoster(f, [1], card, out _, out _));   // the town just arrived: not settled
        Settle(l, f);
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out bool same, out string lineup));
        Assert.False(same);
        Assert.Equal("|16:player:0:0:0:1:1", lineup);
        Assert.Equal(["fid-10"], l.State.PreviewSnapshot.Npcs.Select(n => n.Id));
        f.Now += 999;
        Assert.False(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        f.Now += 1;
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out same, out _));
        Assert.True(same);
    }

    [Fact]
    public void ThePreviewNeedsAnActiveSaveAndAnUnlockedLineup()
    {
        var (l, f) = Town();
        Settle(l, f);
        var card = new TownChallengePolicy.Card();
        f.ActiveSave = false;
        Assert.False(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        f.ActiveSave = true;
        l.State.BookingPending = true;
        Assert.False(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        l.State.BookingPending = false;
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out _, out _));
    }

    [Fact]
    public void TheRosterIsFrozenWhileASearchIsQueuedAndRefreshedEveryTenSecondsOtherwise()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        Settle(l, f);
        var card = new TownChallengePolicy.Card();
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        uint first = l.State.PreviewSnapshotTick;
        f.Npc(11, Mace);                       // a new fighter walks in
        l.State.SearchQueued[2] = true;        // task 7.5 queued a search of this generation
        f.Now += 20_000;
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        Assert.Equal(first, l.State.PreviewSnapshotTick);
        Assert.Single(l.State.PreviewSnapshot.Npcs);
        card.Offers[0].Draw = 7;               // the cards changed: read again even while queued
        f.Now += 1000;
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out bool same, out _));
        Assert.False(same);
        Assert.Equal(2, l.State.PreviewSnapshot.Npcs.Count);
        l.State.SearchQueued[2] = false;
        f.Npc(12, Mace);
        f.Now += 9_000;
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        Assert.Equal(2, l.State.PreviewSnapshot.Npcs.Count);   // 9 s: still the old one
        f.Now += 1_000;
        Assert.True(l.UpdatePreviewRoster(f, [1], card, out _, out _));
        Assert.Equal(3, l.State.PreviewSnapshot.Npcs.Count);   // 10 s, nothing queued: read again
        l.RefreshMatchmakingRoster();
        Assert.False(l.State.PreviewSnapshotValid);
        Assert.Equal(0u, l.State.PreviewTick);
    }

    [Fact]
    public void TheSettleClockStopsWhenTheTownIsEmpty()
    {
        var (l, f) = Town();
        Settle(l, f);
        f.TownOf[1] = TownFake.Elsewhere;
        f.Now += 1000; l.Tick(f);
        Assert.False(l.RosterSettled(f));
        f.TownOf[1] = TownFake.Scratch;
        f.Now += 1000; l.Tick(f);
        Assert.False(l.RosterSettled(f));
        f.Now += TownArenaLogic<int>.RosterSettleMs; l.Tick(f);
        Assert.True(l.RosterSettled(f));
    }

    [Fact]
    public void TheSettleClockRunsWhileARestoredBoutWaits()
    {
        // TickRestoredNpc returns from Tick while a restored bout waits for its fighters (C++ :1890-1939; the placeholder always returns): the
        // settle clock must still run, or 7.3's restore (which waits for the roster) and the clock would wait for each other forever
        var (l, f) = Town();
        l.State.PlannedNpc = new TownMatchmakingPolicy.Match { Valid = true };
        l.State.PlannedRegistry = l.State.TownRegistry;
        l.State.RestoredNpcPending = true;
        Settle(l, f);
    }

    [Fact]
    public void ThePreviewCardKeyIsTheCppText()
    {
        var card = new TownChallengePolicy.Card();
        card.Division = 2;
        card.Offers[1].Epoch = 5; card.Offers[1].Draw = 4000000000u; card.Offers[1].Encounter = 3; card.Offers[1].Mode = 1;
        Assert.Equal("|3:a:b:2:-2:0:0:" + card.Offers[0].Mode + ":5:4000000000:3:1:-2:0:0:" + card.Offers[2].Mode + ":-2:0:0:" + card.Offers[3].Mode +
            ":-2:0:0:" + card.Offers[4].Mode, TownArenaLogic<int>.PreviewCardKey("|3:a:b", card));
    }

    [Fact]
    public void LineupKeyIsSortedAndLengthPrefixed()
    {
        var (_, f) = Town();
        f.Player(2); f.NoHandle.Add(2);
        Assert.Equal("|0:|16:player:0:0:0:1:1", TownArenaLogic<int>.PlayerLineupKey(f, [1, 2]));
    }

    [Fact]
    public void ANewSearchContextBeginsTheNextGenerationNeverZero()
    {
        var (l, f) = Town();
        l.State.SearchQueued[1] = l.State.SearchCompleted[3] = l.State.Searching[4] = true;
        f.Debugs.Clear();
        Assert.True(l.RestartSearchIfContextChanged(f, "ctx-a"));
        Assert.Equal([1u], f.Generations);
        Assert.Equal(["Proving Grounds: started lazy challenge search generation"], f.Debugs);
        Assert.False(l.State.SearchQueued[1] || l.State.SearchCompleted[3] || l.State.Searching[4]);
        Assert.False(l.RestartSearchIfContextChanged(f, "ctx-a"));
        l.State.SearchGeneration = uint.MaxValue;
        Assert.True(l.RestartSearchIfContextChanged(f, "ctx-b"));
        Assert.Equal([1u, 1u], f.Generations);
    }

    [Fact]
    public void EligibleOpponentReadsTheSnapshotsNpcs()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        var s = Collect(l, f, []);
        Assert.True(TownArenaLogic<int>.EligibleOpponent(s, "fid-10"));
        Assert.False(TownArenaLogic<int>.EligibleOpponent(s, "fid-11"));
    }

    [Fact]
    public void AbandonWorldStateShutsTheSearchDownFirstAndForgetsThePreview()
    {
        var (l, f) = Town();
        f.Npc(10, Mace);
        Settle(l, f);
        l.UpdatePreviewRoster(f, [1], new TownChallengePolicy.Card(), out _, out _);
        l.RestartSearchIfContextChanged(f, "ctx");
        l.State.SearchQueued[0] = true; l.State.Searching[5] = true; l.State.IngressQuarantine.Add(10);
        f.Calls.Clear();
        l.AbandonWorldState(f);
        Assert.Equal("matchmaking shutdown", f.Calls[0]);
        var st = l.State;
        Assert.False(st.PreviewSnapshotValid);
        Assert.Empty(st.PreviewSnapshot.Npcs);
        Assert.Equal(0u, st.PreviewSnapshotTick);
        Assert.Equal(0u, st.SearchGeneration);
        Assert.Equal("", st.PreviewKey);
        Assert.Equal("", st.RetryContext);
        Assert.Equal(0u, st.MatchmakingDiagnosticTick);
        Assert.Empty(st.IngressQuarantine);
        Assert.False(st.SearchQueued[0] || st.Searching[5]);
        Assert.False(l.RosterSettled(f));
    }
}
