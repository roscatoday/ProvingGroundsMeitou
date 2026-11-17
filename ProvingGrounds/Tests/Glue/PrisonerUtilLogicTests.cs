using System.Numerics;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake world for PrisonerUtilLogic: ints are characters (0 = null), strings are buildings / cages.</summary>
sealed partial class PrisonerFake : IPrisonerHost<int>
{
    public sealed class B
    {
        public Vector3 Pos;
        public bool Valid = true, ThePlayer, Owned, Cage;
        public string? Useable;            // getUseableStuff() (null: none)
        public List<string> Furniture = [];
        public int Occupant;
    }

    public readonly Dictionary<string, B> Buildings = [];
    public readonly HashSet<int> Invalid = [], Dead = [], Imprisoned = [];
    public readonly Dictionary<int, Vector3> Pos = [];
    public readonly Dictionary<int, string> Held = [];
    public List<int>? CharSphere = [];         // null: no world
    public List<string>? BuildingSphere = [];  // null: no world
    public readonly List<string> OwnedCages = [];
    public bool RegistryIsValid = true;
    public ArenaBuilding? Bound;
    public readonly List<int[]> Disengaged = [];
    public readonly List<string> Debugs = [];

    public static readonly ArenaBuilding Registry = new(ArenaBuildingSource.Player, "reg", "45-Proving Grounds.mod", "47-Proving Grounds.mod", true,
        new Vector3(1000, 50, 2000), 0);

    /// <summary>A player-owned cage (in the ownership list) at registry + (dx, dz), its own UseableStuff.</summary>
    public B Cage(string id, float dx, float dz, int occupant = 0, bool owned = true)
    {
        var b = new B { Pos = Registry.Position + new Vector3(dx, 0, dz), Cage = true, Useable = id, Occupant = occupant, ThePlayer = owned };
        Buildings[id] = b;
        if (owned) OwnedCages.Add(id);
        return b;
    }

    public int Char(int id, float dx, float dz, bool imprisoned = false, string? heldIn = null)
    {
        Pos[id] = Registry.Position + new Vector3(dx, 0, dz);
        if (imprisoned) Imprisoned.Add(id);
        if (heldIn is not null) Held[id] = heldIn;
        CharSphere?.Add(id);
        return id;
    }

    public bool IsNull(int c) => c == 0;
    public bool Valid(int c) => c != 0 && !Invalid.Contains(c);
    public bool IsDead(int c) => Dead.Contains(c);
    public Vector3 Position(int c) => Pos.TryGetValue(c, out var p) ? p : Vector3.Zero;
    public bool IsImprisoned(int c) => Imprisoned.Contains(c);
    public string? HeldIn(int c) => Held.TryGetValue(c, out var b) ? b : null;
    public IReadOnlyList<int>? CharactersInSphere(Vector3 origin, float radius, int max) => CharSphere;
    public bool RegistryValid(ArenaBuilding registry) => RegistryIsValid;
    public bool BuildingValid(string building) => Buildings.TryGetValue(building, out var b) && b.Valid;
    public Vector3 BuildingPosition(string building) => Buildings[building].Pos;
    public bool IsThePlayer(string building) => Buildings[building].ThePlayer;
    public bool IsCageFunction(string building) => Buildings[building].Cage;
    public string? UseableStuffOf(string building) => Buildings[building].Useable;
    public void CageFurniture(string building, List<string> into) => into.AddRange(Buildings[building].Furniture);
    public int Occupant(string useable) => Buildings.TryGetValue(useable, out var b) ? b.Occupant : 0;
    public IReadOnlyList<string>? BuildingsInSphere(Vector3 origin, float radius, int max) => BuildingSphere;
    public void CollectPlayerOwnedCageBuildings(List<string> into) => into.AddRange(OwnedCages);
    public bool IsBuildingOwnedByPlayer(string building) => Buildings[building].Owned;
    public ArenaBuilding? BoundRegistry() => Bound;
    public void DisengageMatch(IReadOnlyList<int> handlers) => Disengaged.Add([.. handlers]);
    public void Debug(string line) => Debugs.Add(line);
}

/// <summary>PrisonerUtil (Core/Glue/PrisonerUtilLogic*.cs, task 6.1): the cage discovery (ownership, sphere, furniture, range, owner), the two
/// roster paths (occupant, held in a cage), FindCageForOccupant, CountHandlerCandidates, PrepareMatch (success and every C++ failure), the match
/// reads, ForgetRosterPrisoner, and the state ClearMatchState / AbandonWorldState reset.</summary>
public class PrisonerUtilLogicTests
{
    static List<int> Collect(PrisonerUtilLogic<int> logic, PrisonerFake f, ArenaBuilding? registry = null)
    {
        var output = new List<int> { 99 };   // the C++ clears out first
        logic.CollectNearbyCagePrisoners(f, registry ?? PrisonerFake.Registry, output);
        return output;
    }

    // ---------- the cages ----------

    [Fact]
    public void OwnedCageInRangeIsListedAndItsLivingOccupantIsARosterPrisoner()
    {
        var f = new PrisonerFake();
        f.Cage("c1", 300, 400, occupant: 7);
        f.Pos[7] = PrisonerFake.Registry.Position;
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([7], Collect(logic, f));
        Assert.Equal([new NearbyCage("c1", "c1")], logic.State.NearbyCages);
        Assert.Equal([7], logic.GetRosterPrisoners());
        Assert.True(logic.IsRosterPrisoner(f, 7));
        Assert.False(logic.IsRosterPrisoner(f, 8));
        Assert.False(logic.IsRosterPrisoner(f, 0));
        Assert.Equal(
        [
            "Proving Grounds: cage discovery cages=1 ownership=1 sphereBuildings=0 playerBuildings=0",
            "Proving Grounds: imprisoned scan hits=0 kept=1",
            "Proving Grounds: prisoner roster size=1 cages=1",
        ], f.Debugs);
    }

    [Fact]
    public void RangeIsHorizontalAndInclusiveAt2500()
    {
        var f = new PrisonerFake();
        f.Cage("edge", 1500, 2000, occupant: 1);                  // exactly 2500 on x/z
        f.Cage("tall", 0, 100, occupant: 2).Pos.Y += 9000;        // high above: the height is not measured
        f.Cage("far", 2500.5f, 0, occupant: 3);                   // just out
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([1, 2], Collect(logic, f));
        Assert.Equal(["edge", "tall"], logic.State.NearbyCages.Select(c => c.Building));
    }

    [Fact]
    public void ACageThePlayerDoesNotOwnIsNotListed()
    {
        var f = new PrisonerFake();
        f.Cage("town", 100, 0, occupant: 5, owned: false);
        f.BuildingSphere!.Add("town");
        var logic = new PrisonerUtilLogic<int>();
        Assert.Empty(Collect(logic, f));
        Assert.Empty(logic.State.NearbyCages);
        Assert.Contains("Proving Grounds: cage discovery cages=0 ownership=0 sphereBuildings=1 playerBuildings=0", f.Debugs);
    }

    [Fact]
    public void FurnitureOfAPlayerBuildingInTheSphereIsListedEvenWhenTheCageItselfIsNotOwned()
    {
        var f = new PrisonerFake();
        f.Buildings["house"] = new PrisonerFake.B { Pos = PrisonerFake.Registry.Position + new Vector3(200, 0, 0), ThePlayer = true, Furniture = ["inner", "gone"] };
        f.Cage("inner", 210, 0, occupant: 4, owned: false);
        f.Cage("gone", 220, 0, occupant: 6, owned: false).Valid = false;   // an invalid furniture item is skipped
        f.BuildingSphere!.Add("house");
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([4], Collect(logic, f));
        Assert.Contains("Proving Grounds: cage discovery cages=1 ownership=0 sphereBuildings=1 playerBuildings=1", f.Debugs);
    }

    [Fact]
    public void AStandaloneCageInTheSphereOwnedByThePlayersOwnershipsIsListed()
    {
        var f = new PrisonerFake();
        var b = f.Cage("pole", 100, 100, occupant: 3, owned: false);
        b.Owned = true;                                           // IsBuildingOwnedByPlayer, not isThePlayer, not in the ownership list
        f.BuildingSphere!.Add("pole");
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([3], Collect(logic, f));
        // IsPlayerCageBuilding(pole) is true through the ownership: it is counted as a player building and found through its own tree
        Assert.Contains("Proving Grounds: cage discovery cages=1 ownership=0 sphereBuildings=1 playerBuildings=1", f.Debugs);
    }

    [Fact]
    public void ACageIsListedOnceWhenTheOwnershipListAndTheSphereBothFindIt()
    {
        var f = new PrisonerFake();
        f.Cage("c1", 100, 0, occupant: 3);
        f.BuildingSphere!.Add("c1");
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([3], Collect(logic, f));
        Assert.Single(logic.State.NearbyCages);
    }

    [Fact]
    public void ACageWithoutAUseableStuffOrCageFunctionIsNotACage()
    {
        var f = new PrisonerFake();
        f.Buildings["shed"] = new PrisonerFake.B { Pos = PrisonerFake.Registry.Position, ThePlayer = true, Occupant = 3 };
        f.OwnedCages.Add("shed");
        var logic = new PrisonerUtilLogic<int>();
        Assert.Empty(Collect(logic, f));
        Assert.Empty(logic.State.NearbyCages);
    }

    [Fact]
    public void DeadInvalidOrNoOccupantIsNoPrisoner()
    {
        var f = new PrisonerFake();
        f.Cage("empty", 10, 0);
        f.Cage("dead", 20, 0, occupant: 5);
        f.Cage("stale", 30, 0, occupant: 6);
        f.Dead.Add(5);
        f.Invalid.Add(6);
        var logic = new PrisonerUtilLogic<int>();
        Assert.Empty(Collect(logic, f));
        Assert.Equal(3, logic.State.NearbyCages.Count);
    }

    // ---------- the held-in-cage path ----------

    [Fact]
    public void AnImprisonedCharacterHeldInAPlayerCageWithoutAnOccupantFieldIsFound()
    {
        var f = new PrisonerFake();
        f.Cage("c1", 400, 0);                                     // getOccupant() fails (none)
        f.Char(8, 410, 0, imprisoned: true, heldIn: "c1");
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([8], Collect(logic, f));
        Assert.Contains("Proving Grounds: imprisoned scan hits=1 kept=1", f.Debugs);
        Assert.Equal("c1", logic.FindCageForOccupant(f, 8));
    }

    [Fact]
    public void TheHeldPathKeepsUnknownCagesSkipsForeignOnesAndAddsItsCage()
    {
        var f = new PrisonerFake();
        f.Cage("foreign", 100, 0, owned: false);
        f.Cage("extra", 2400, 0, owned: false).ThePlayer = true;  // a player cage the ownership list missed
        f.Char(1, 50, 0, imprisoned: true);                       // inWhat unknown: kept
        f.Char(2, 60, 0, imprisoned: true, heldIn: "foreign");    // not a player cage: skipped
        f.Char(3, 2400, 0, imprisoned: true, heldIn: "extra");    // kept, and its cage added to the nearby cages
        f.Char(4, 2600, 0, imprisoned: true);                     // too far on x/z
        f.Char(5, 70, 0, imprisoned: false);                      // not imprisoned
        f.Char(6, 80, 0, imprisoned: true);
        f.Dead.Add(6);                                            // dead
        f.Char(0, 90, 0, imprisoned: true);                       // a null result
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([1, 3], Collect(logic, f));
        Assert.Contains("Proving Grounds: imprisoned scan hits=4 kept=2", f.Debugs);
        Assert.Equal(["extra"], logic.State.NearbyCages.Select(c => c.Building));
        Assert.Equal("Proving Grounds: prisoner roster size=2 cages=1", f.Debugs[^1]);
    }

    [Fact]
    public void RosterOrderIsTheOccupantPathFirstThenTheHeldPathEachCharacterOnce()
    {
        var f = new PrisonerFake();
        f.Cage("c1", 10, 0, occupant: 2);
        f.Cage("c2", 20, 0, occupant: 1);
        f.Char(3, 30, 0, imprisoned: true);
        f.Char(1, 20, 0, imprisoned: true, heldIn: "c2");         // already found by its cage
        var logic = new PrisonerUtilLogic<int>();
        Assert.Equal([2, 1, 3], Collect(logic, f));
        Assert.Equal([2, 1, 3], logic.GetRosterPrisoners());
    }

    [Fact]
    public void NoRegistryInvalidRegistryOrNoWorld()
    {
        var f = new PrisonerFake();
        f.Cage("c1", 10, 0, occupant: 2);
        var logic = new PrisonerUtilLogic<int>();
        var output = new List<int> { 5 };
        logic.CollectNearbyCagePrisoners(f, null, output);
        Assert.Empty(output);
        Assert.Equal(["Proving Grounds: prisoner collect skipped — no bound registry"], f.Debugs);

        f.Debugs.Clear();
        f.RegistryIsValid = false;
        Assert.Empty(Collect(logic, f));
        Assert.Equal(["Proving Grounds: prisoner roster size=0 cages=0"], f.Debugs);   // no discovery line, no scan line (both return first)

        f.Debugs.Clear();
        f.RegistryIsValid = true;
        f.BuildingSphere = null;
        f.CharSphere = null;
        Assert.Equal([2], Collect(logic, f));
        Assert.Equal(["Proving Grounds: cage discovery ownership=1 sphere=skipped (no world)", "Proving Grounds: prisoner roster size=1 cages=1"], f.Debugs);
    }

    [Fact]
    public void ADiscoveryReplacesTheLastRosterAndCages()
    {
        var f = new PrisonerFake();
        f.Cage("c1", 10, 0, occupant: 2);
        var logic = new PrisonerUtilLogic<int>();
        Collect(logic, f);
        f.Buildings["c1"].Occupant = 0;
        Assert.Empty(Collect(logic, f));
        Assert.Empty(logic.GetRosterPrisoners());
        Assert.False(logic.IsRosterPrisoner(f, 2));
    }

    [Fact]
    public void ForgetRosterPrisonerRemovesEveryCopy()
    {
        var logic = new PrisonerUtilLogic<int>();
        logic.State.RosterPrisoners.AddRange([1, 2, 1, 3]);
        logic.ForgetRosterPrisoner(1);
        Assert.Equal([2, 3], logic.GetRosterPrisoners());
    }

    // ---------- FindCageForOccupant ----------

    [Fact]
    public void FindCageForOccupantTriesTheHeldCageThenTheNearbyOccupantsThenTheMatch()
    {
        var f = new PrisonerFake();
        f.Cage("held", 10, 0);
        f.Cage("near", 20, 0, occupant: 2);
        var logic = new PrisonerUtilLogic<int>();
        Collect(logic, f);
        f.Char(1, 0, 0, imprisoned: true, heldIn: "held");
        Assert.Equal("held", logic.FindCageForOccupant(f, 1));
        Assert.Equal("near", logic.FindCageForOccupant(f, 2));
        f.Dead.Add(2);
        Assert.Equal("near", logic.FindCageForOccupant(f, 2));   // the C++ occupant loop has no dead test
        f.Held[3] = "held";
        Assert.Null(logic.FindCageForOccupant(f, 3));            // held in a cage but not imprisoned: no first branch
        logic.State.MatchEntries.Add(new MatchEntry<int> { Prisoner = 3, CageUseable = "held" });
        Assert.Equal("held", logic.FindCageForOccupant(f, 3));
        Assert.Null(logic.FindCageForOccupant(f, 0));
        f.Invalid.Add(3);
        Assert.Null(logic.FindCageForOccupant(f, 3));
    }

    // ---------- handlers ----------

    [Fact]
    public void CountHandlerCandidatesCountsSquadMembersWhoAreNotMatchFighters()
    {
        var f = new PrisonerFake();
        // 4 is a prisoner fighter: as a match fighter it is never counted; 0 (null) is skipped
        Assert.Equal(2, PrisonerUtilLogic<int>.CountHandlerCandidates(f, [1, 2, 0, 3], [2, 4]));
        Assert.Equal(0, PrisonerUtilLogic<int>.CountHandlerCandidates(f, [], [1]));
        Assert.Equal(3, PrisonerUtilLogic<int>.CountHandlerCandidates(f, [1, 2, 3], []));
    }

    // ---------- PrepareMatch ----------

    static (PrisonerFake, PrisonerUtilLogic<int>) TwoCaged()
    {
        var f = new PrisonerFake { Bound = PrisonerFake.Registry };
        f.Cage("c1", 10, 0, occupant: 7);
        f.Cage("c2", 20, 0, occupant: 8);
        var logic = new PrisonerUtilLogic<int>();
        Collect(logic, f);
        return (f, logic);
    }

    [Fact]
    public void PrepareMatchPlansOneEntryPerPrisonerWithItsHandlerAndCage()
    {
        var (f, logic) = TwoCaged();
        logic.State.CageReturnFailed = true;
        logic.State.CageReturnStatus = "old";
        Assert.True(logic.PrepareMatch(f, [8, 7], [1, 2, 3], out string status));
        Assert.Equal("", status);
        Assert.False(logic.State.CageReturnFailed);
        Assert.Equal("", logic.State.CageReturnStatus);
        var e = logic.State.MatchEntries;
        Assert.Equal(2, e.Count);
        Assert.Equal((8, "c2", "c2", 1, 1, 8, 1), (e[0].Prisoner, e[0].CageUseable, e[0].CageHand, e[0].Handler, e[0].OriginalCageIndex, e[0].PrisonerHand, e[0].HandlerHand));
        Assert.Equal((7, "c1", 2, 0), (e[1].Prisoner, e[1].CageUseable, e[1].Handler, e[1].OriginalCageIndex));
        Assert.All(e, x => Assert.False(x.Released || x.UnlockPending || x.LockPending || x.ReturnPending || x.WasChained));
        Assert.Equal([1, 2], logic.GetMatchHandlers());   // only as many handlers as prisoners
        Assert.Equal([8, 7], logic.GetMatchPrisoners());
        Assert.True(logic.MatchIncludesPrisoner());
        Assert.True(logic.IsMatchPrisoner(f, 7));
        Assert.True(logic.IsRosterPrisoner(f, 7));
        Assert.False(logic.IsMatchPrisonerReleased(f, 7));
        Assert.False(logic.AllMatchPrisonersReleased());
        Assert.True(logic.HasRuntimeActivity());
        e[0].Released = true;
        Assert.True(logic.IsMatchPrisonerReleased(f, 8));
        Assert.False(logic.AllMatchPrisonersReleased());
        e[1].Released = true;
        Assert.True(logic.AllMatchPrisonersReleased());
    }

    [Fact]
    public void PrepareMatchRefusesAPrisonerMatchWhenTheCageReleaseIsMissing()   // task 9.3d (port only)
    {
        var (f, logic) = TwoCaged();
        f.ReleaseAvailable = false;
        Assert.False(logic.PrepareMatch(f, [7, 8], [1, 2], out string status));
        Assert.Equal(PrisonerUtilLogic<int>.ReleaseUnavailableStatus, status);
        Assert.Empty(logic.State.MatchEntries);
        Assert.False(logic.MatchIncludesPrisoner());
        Assert.True(logic.PrepareMatch(f, [], [1, 2], out status));   // a match without a prisoner is not refused
        Assert.Equal("", status);
    }

    [Fact]
    public void PrepareMatchRefusesTooFewOrNullHandlers()
    {
        var (f, logic) = TwoCaged();
        Assert.False(logic.PrepareMatch(f, [7, 8], [1], out string status));
        Assert.Equal("Not enough handlers for prisoners", status);
        Assert.Empty(logic.State.MatchEntries);
        Assert.False(logic.MatchIncludesPrisoner());
        Assert.False(logic.PrepareMatch(f, [7, 8], [1, 0], out status));
        Assert.Equal("Not enough handlers for prisoners", status);
        Assert.Empty(logic.State.MatchEntries);
    }

    [Fact]
    public void PrepareMatchRefusesAPrisonerWithoutACageAndPlansNothing()
    {
        var (f, logic) = TwoCaged();
        Assert.False(logic.PrepareMatch(f, [7, 9], [1, 2], out string status));
        Assert.Equal("Prisoner has no cage", status);
        Assert.Empty(logic.State.MatchEntries);
        Assert.Empty(logic.GetMatchHandlers());
        Assert.False(logic.MatchIncludesPrisoner());
    }

    [Fact]
    public void PrepareMatchWaitsForAReturnOrAPendingLockWithoutTouchingTheState()
    {
        var (f, logic) = TwoCaged();
        logic.State.Returning = true;
        Assert.False(logic.PrepareMatch(f, [7], [1], out string status));
        Assert.Equal("Wait for prisoner return and cage locking to finish", status);
        logic.State.Returning = false;
        Assert.True(logic.PrepareMatch(f, [7], [1], out _));
        logic.State.MatchEntries[0].LockPending = true;
        Assert.True(logic.HasPendingCageLocks());
        Assert.False(logic.PrepareMatch(f, [8], [2], out status));
        Assert.Equal("Wait for prisoner return and cage locking to finish", status);
        Assert.Equal([7], logic.GetMatchPrisoners());   // the planned match is kept
    }

    [Fact]
    public void PrepareMatchRediscoversTheCagesAroundTheBoundRegistry()
    {
        var (f, logic) = TwoCaged();
        f.Cage("c3", 5, 0, occupant: 9);                 // a cage built after the window's last discovery
        Assert.True(logic.PrepareMatch(f, [9], [1], out _));
        Assert.Equal(["c1", "c2", "c3"], logic.State.NearbyCages.Select(c => c.Building));
        Assert.Equal(2, logic.State.MatchEntries[0].OriginalCageIndex);
        f.Bound = null;                                   // no Registry: no cages, but a held prisoner still has its cage
        f.Char(10, 0, 0, imprisoned: true, heldIn: "c1");
        Assert.True(logic.PrepareMatch(f, [10], [1], out _));
        Assert.Empty(logic.State.NearbyCages);
        Assert.Equal(-1, logic.State.MatchEntries[0].OriginalCageIndex);
    }

    [Fact]
    public void ANewPrepareReplacesThePreviousMatch()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7, 8], [1, 2], out _));
        Assert.True(logic.PrepareMatch(f, [8], [3], out _));
        Assert.Equal([8], logic.GetMatchPrisoners());
        Assert.Equal([3], logic.GetMatchHandlers());
    }

    [Fact]
    public void AFailedSecondPrepareHasAlreadyClearedTheFirstMatch()
    {
        // C++ order (PrisonerUtil.cpp:986-1001): ClearMatchState runs before the handler checks, so a refused prepare leaves no match behind
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7], [1], out _));
        Assert.False(logic.PrepareMatch(f, [7, 8], [1], out string status));
        Assert.Equal("Not enough handlers for prisoners", status);
        Assert.Empty(logic.State.MatchEntries);
        Assert.Empty(logic.GetMatchHandlers());
        Assert.False(logic.MatchIncludesPrisoner());
    }

    [Fact]
    public void PrepareMatchWithNoPrisonerSucceedsWithoutAPrisonerMatch()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [], [], out string status));
        Assert.Equal("", status);
        Assert.False(logic.MatchIncludesPrisoner());
        Assert.Empty(logic.State.MatchEntries);
        Assert.True(logic.AllMatchPrisonersReleased());
    }

    [Fact]
    public void PrepareMatchsOwnClearDisengagesTheRememberedHandlers()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7], [1], out _));
        logic.State.HandlerStatesRemembered = true;
        Assert.True(logic.PrepareMatch(f, [8], [2], out _));
        Assert.Equal([1], Assert.Single(f.Disengaged));
        Assert.False(logic.State.HandlerStatesRemembered);
        Assert.Equal([2], logic.GetMatchHandlers());
    }

    // ---------- the state resets ----------

    [Fact]
    public void ClearMatchStateClearsTheMatchButKeepsTheRoster()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7], [1], out _));
        var s = logic.State;
        s.Unlocking = s.Returning = true;
        s.ReturnLastTick = s.UnlockLastTick = s.RingsideAidLastTick = s.MatchUpkeepLastTick = 5;
        s.ProtectionElapsedSec = s.HandlerElapsedSec = 0.3f;
        s.ProtectionImmediate = true;
        s.CageReturnFailed = true;
        s.CageReturnStatus = "kept";
        logic.ClearMatchState(f);
        Assert.Empty(s.MatchEntries);
        Assert.Empty(s.MatchHandlers);
        Assert.False(s.MatchHasPrisoner || s.Returning || s.Unlocking || s.HandlerStatesRemembered || s.ProtectionImmediate);
        Assert.Equal((0u, 0u, 0u, 0u), (s.ReturnLastTick, s.UnlockLastTick, s.RingsideAidLastTick, s.MatchUpkeepLastTick));
        Assert.Equal((0f, 0f), (s.ProtectionElapsedSec, s.HandlerElapsedSec));
        Assert.Equal([7, 8], s.RosterPrisoners);          // not the C++'s to clear here
        Assert.Equal(2, s.NearbyCages.Count);
        Assert.True(s.CageReturnFailed);                  // ClearMatchState leaves the failure (PrepareMatch / AbandonWorldState clear it)
        Assert.Equal("kept", s.CageReturnStatus);
        Assert.False(logic.HasRuntimeActivity());
        Assert.True(logic.AllMatchPrisonersReleased());
        Assert.Empty(f.Disengaged);                       // no remembered handler states
    }

    [Fact]
    public void ClearMatchStateDisengagesTheSurvivingHandlersWhenTheirStatesWereRemembered()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7, 8], [1, 2], out _));
        logic.State.HandlerStatesRemembered = true;
        f.Invalid.Add(2);
        logic.ClearMatchState(f);
        Assert.Equal([1], Assert.Single(f.Disengaged));
        Assert.Empty(logic.State.MatchEntries);
    }

    [Fact]
    public void ClearMatchStateDoesNothingWhileACageLockIsPending()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7], [1], out _));
        logic.State.MatchEntries[0].LockPending = true;
        logic.ClearMatchState(f);
        Assert.Single(logic.State.MatchEntries);
        Assert.True(logic.MatchIncludesPrisoner());
    }

    [Fact]
    public void AbandonWorldStateForgetsEverything()
    {
        var (f, logic) = TwoCaged();
        Assert.True(logic.PrepareMatch(f, [7], [1], out _));
        var s = logic.State;
        s.MatchEntries[0].LockPending = true;            // abandon ignores a pending lock (no restore, no move)
        s.Returning = s.Unlocking = s.HandlerStatesRemembered = s.CageReturnFailed = true;
        s.CageReturnStatus = "x";
        s.ReturnLastTick = s.UnlockLastTick = s.RingsideAidLastTick = s.MatchUpkeepLastTick = 9;
        s.ProtectionElapsedSec = 1;
        s.ProtectionImmediate = true;
        logic.GetMatchPrisoners();
        logic.AbandonWorldState();
        Assert.Empty(s.RosterPrisoners);
        Assert.Empty(s.NearbyCages);
        Assert.Empty(s.MatchEntries);
        Assert.Empty(s.MatchHandlers);
        Assert.Empty(s.MatchPrisonersScratch);
        Assert.False(s.MatchHasPrisoner || s.Returning || s.Unlocking || s.HandlerStatesRemembered || s.CageReturnFailed || s.ProtectionImmediate);
        Assert.Equal("", s.CageReturnStatus);
        Assert.Equal((0u, 0u, 0u, 0u, 0f), (s.ReturnLastTick, s.UnlockLastTick, s.RingsideAidLastTick, s.MatchUpkeepLastTick, s.ProtectionElapsedSec));
        Assert.Empty(f.Disengaged);
        Assert.False(logic.HasRuntimeActivity());
    }

    [Fact]
    public void TheCageReturnAccessorsReadTheState()
    {
        var logic = new PrisonerUtilLogic<int>();
        Assert.False(logic.IsReturning() || logic.HasPendingCageLocks() || logic.HasCageReturnFailure());
        Assert.Equal("", logic.GetCageReturnStatus());
        logic.State.Returning = true;
        logic.State.CageReturnFailed = true;
        logic.State.CageReturnStatus = "Could not secure X: y";
        Assert.True(logic.IsReturning() && logic.HasCageReturnFailure());
        Assert.Equal("Could not secure X: y", logic.GetCageReturnStatus());
        Assert.True(logic.HasRuntimeActivity());
    }

    [Fact]
    public void IsCageFreeAsTheCpp()
    {
        var f = new PrisonerFake();
        f.Cage("c", 0, 0, occupant: 3);
        Assert.False(PrisonerUtilLogic<int>.IsCageFree(f, null));
        Assert.False(PrisonerUtilLogic<int>.IsCageFree(f, "c"));
        f.Invalid.Add(3);                                         // an invalid occupant (not dead): free
        Assert.True(PrisonerUtilLogic<int>.IsCageFree(f, "c"));
        f.Invalid.Remove(3);
        f.Dead.Add(3);
        Assert.True(PrisonerUtilLogic<int>.IsCageFree(f, "c"));
        f.Buildings["c"].Occupant = 0;
        Assert.True(PrisonerUtilLogic<int>.IsCageFree(f, "c"));
    }
}
