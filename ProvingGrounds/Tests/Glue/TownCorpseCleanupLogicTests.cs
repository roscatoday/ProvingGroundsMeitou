using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.6: TownCorpseCleanup.cpp's decisions (Core/Glue/TownCorpseCleanupLogic.cs) over a fake world: Begin's statuses, the furnace search,
/// the handler (record, town, aftercare, the audience, the shared external slots), the 500 ms poll, the lift and walk orders every 15 s, the feed at
/// the furnace, the statuses in order, the sweep after the medics, the 180 s batch limit, the native hand-back and the deferred one, a body taken by
/// someone else, a throwing corpse, Release and AbandonWorldState.</summary>
public class TownCorpseCleanupLogicTests
{
    const long Town = 7, OtherTown = 8;
    static readonly ArenaBuildingRef ArenaRef = new(ArenaBuildingSource.Town, "arena", PgRecords.Arena);
    static readonly Vector3 ArenaPos = new(0, 0, 0);

    sealed class Npc
    {
        public string Record = PgRecords.ScratchCivilian;
        public string Name = "";
        public long Town = TownCorpseCleanupLogicTests.Town;
        public Vector3 Pos;
        public bool Player, Dead, Ko, Combat, Carried, Gone, Managed, InBedOrCage;
        public int CarriedBy = -1;
        public int Carrying = -1;
        public bool Hold = true, Passive = true;
        public byte Speed = 1;
    }

    sealed class FakeHost : ITownCorpseCleanupHost<int>
    {
        public readonly Dictionary<int, Npc> Npcs = [];
        public readonly List<int> Nearby = [];
        public readonly List<CorpseFurnace> Furnaces = [];
        public readonly List<string> Calls = [];
        public readonly List<string> Debugs = [];
        public readonly List<string> Statuses = [];
        public readonly HashSet<int> Reserved = [];
        public int ExternalCapacity = 4;
        public bool ArenaGone, LiftRefuses, AtFurnaceNow;
        public CorpseDisposalResult? DisposeAnswer;
        public uint Now = 1000;
        public int Failures;

        public bool IsPaused => false;
        public uint NowMs => Now;
        public void Debug(string line)
        {
            Debugs.Add(line);
            const string p = "Proving Grounds: corpse cleanup - ";
            if (line.StartsWith(p, StringComparison.Ordinal)) Statuses.Add(line[p.Length..]);
        }
        public int None => -1;
        public bool IsNull(int c) => c < 0;
        public bool Valid(int c) => Npcs.TryGetValue(c, out var n) && !n.Gone;
        public bool IsPlayer(int c) => Npcs[c].Player;
        public bool IsDead(int c) => Npcs[c].Dead;
        public bool IsUnconscious(int c) => Npcs[c].Ko;
        public bool InCombatMode(int c) => Npcs[c].Combat;
        public bool IsCarried(int c) => Npcs[c].Carried;
        public bool IsCarrying(int c) => Valid(c) && Npcs[c].Carrying >= 0;
        public int CarryingOf(int c) => Valid(c) ? Npcs[c].Carrying : -1;
        public bool InBedOrCage(int body) => Npcs[body].InBedOrCage;
        public int CarrierOf(int body) => Npcs[body].CarriedBy >= 0 && Valid(Npcs[body].CarriedBy) ? Npcs[body].CarriedBy : -1;
        public bool NeedsStabilization(int c) => false;
        public string RecordId(int c) => Valid(c) ? Npcs[c].Record : "";
        public string Name(int c) => Npcs[c].Name;
        public string ActorText(int c) => "a" + c;
        public Vector3 Position(int c) => Npcs[c].Pos;
        public long CurrentTown(int c) => Npcs[c].Town;
        public IReadOnlyList<int> CharactersInSphere(Vector3 origin, float radius, int max)
        {
            var list = new List<int>();
            foreach (int c in Nearby)
                if (Npcs.TryGetValue(c, out var n) && Vector3.Distance(n.Pos, origin) <= radius && list.Count < max) list.Add(c);
            return list;
        }
        public PlacedBuilding? Resolve(ArenaBuildingRef arena) => ArenaGone ? null
            : new PlacedBuilding(new ArenaBuilding(ArenaBuildingSource.Town, "arena", PgRecords.Arena, null, true, ArenaPos, 0f), Town);
        public CorpseFurnace? ResolveFurnace(string key)
        {
            foreach (var f in Furnaces) if (f.Key == key) return f;
            return null;
        }
        public IReadOnlyList<CorpseFurnace> FurnacesInSphere(Vector3 origin, float radius, int max)
        {
            Calls.Add("furnaces");
            return Furnaces.FindAll(f => Vector3.Distance(f.Position, origin) <= radius);
        }
        public bool AftercareManages(int c) => Npcs[c].Managed;
        public void SpectatorsReleaseCharacter(int c) => Calls.Add("spectator-release " + c);
        public bool ReserveExternal(int c)
        {
            Calls.Add("reserve " + c);
            if (Reserved.Count >= ExternalCapacity && !Reserved.Contains(c)) return false;
            Reserved.Add(c);
            return true;
        }
        public void ReleaseExternal(int c) { Calls.Add("release " + c); Reserved.Remove(c); }
        public (bool Hold, bool Passive) StandingOrders(int c) => (Npcs[c].Hold, Npcs[c].Passive);
        public void SetStandingOrders(int c, bool hold, bool passive) { Calls.Add($"orders {c} {hold} {passive}"); Npcs[c].Hold = hold; Npcs[c].Passive = passive; }
        public byte SpeedOf(int c) => Npcs[c].Speed;
        public void SetSpeed(int c, byte speed) { Calls.Add($"speed {c} {speed}"); Npcs[c].Speed = speed; }
        public void SetRun(int c) => Npcs[c].Speed = 2;
        public void ClearOrders(int c) => Calls.Add("clear " + c);
        public bool BeginLift(int c, int corpse) { Calls.Add($"lift {c} {corpse}"); return !LiftRefuses; }
        public bool LetGo(int c) { Calls.Add("letgo " + c); if (Npcs[c].Carrying < 0) return false; Npcs[Npcs[c].Carrying].Carried = false; Npcs[Npcs[c].Carrying].CarriedBy = -1; Npcs[c].Carrying = -1; return true; }
        public void Carry(int carrier, int body) { Npcs[carrier].Carrying = body; Npcs[body].Carried = true; Npcs[body].CarriedBy = carrier; }
        public void WalkTo(int c, Vector3 position) => Calls.Add($"walk {c}");
        public bool AtFurnace(int c, CorpseFurnace furnace) => AtFurnaceNow;
        public bool DisposalAvailable { get; set; } = true;
        public int FedLines;
        public CorpseDisposalResult Dispose(int handler, int body, CorpseFurnace furnace, bool log)
        {
            Calls.Add($"dispose {handler} {body} {furnace.Label}");
            if (DisposeAnswer is { } a) { if (log || a is not (CorpseDisposalResult.Failed or CorpseDisposalResult.NotCarried)) FedLines++; return a; }
            FedLines++;
            Npcs[handler].Carrying = -1;
            Npcs[body].Gone = true;
            return CorpseDisposalResult.Removed;
        }
        public void CorpseFailed(Exception e) => Failures++;

        public int Add(int id, Npc n) { Npcs[id] = n; Nearby.Add(id); return id; }
    }

    static readonly CorpseFurnace Furnace = new("furnace", "3962-gamedata.base", new Vector3(421, 0, 0), Town, true, "Incinerating", 20f);

    static (FakeHost h, TownCorpseCleanupLogic<int> l) Setup(bool furnace = true, int handlers = 1)
    {
        var h = new FakeHost();
        if (furnace) h.Furnaces.Add(Furnace);
        h.Add(1, new Npc { Record = "1-fighter", Name = "Alpha", Pos = new Vector3(10, 0, 0) });
        h.Add(2, new Npc { Record = "2-fighter", Name = "Beta", Pos = new Vector3(-10, 0, 0) });
        for (int i = 0; i < handlers; i++) h.Add(10 + i, new Npc { Record = PgRecords.CageHandler, Name = "Handler" + i, Pos = new Vector3(100 + i, 0, 0) });
        var l = new TownCorpseCleanupLogic<int>(-1);
        l.Begin(h, ArenaRef, [1, 2], 2);
        return (h, l);
    }

    static void Poll(FakeHost h, TownCorpseCleanupLogic<int> l, uint ms = 500) { h.Now += ms; l.Tick(h); }

    [Fact]
    public void BeginWatchesTheFightersOrWaitsForAFurnace()
    {
        var (h, l) = Setup();
        Assert.Equal("watching registered fighters", l.GetStatus());
        Assert.Equal(["watching registered fighters"], h.Statuses);
        Assert.Equal(2, l.CorpseCount);
        Assert.False(l.HasPending(h));

        var (h2, l2) = Setup(furnace: false);
        Assert.Equal("waiting for a nearby corpse furnace", l2.GetStatus());

        var h3 = new FakeHost { ArenaGone = true };
        var l3 = new TownCorpseCleanupLogic<int>(-1);
        l3.Begin(h3, ArenaRef, [1], 1);
        Assert.False(l3.Active);
        Assert.Equal("", l3.GetStatus());
    }

    [Fact]
    public void AFurnaceOfAnotherTownOrUnbuiltIsNotUsable()
    {
        var h = new FakeHost();
        h.Furnaces.Add(Furnace with { Key = "f1", Town = OtherTown });
        h.Furnaces.Add(Furnace with { Key = "f2", Complete = false });
        h.Furnaces.Add(Furnace with { Key = "far", Position = new Vector3(2600, 0, 0) });
        h.Add(1, new Npc { Dead = true });
        var l = new TownCorpseCleanupLogic<int>(-1);
        l.Begin(h, ArenaRef, [1], 1);
        Assert.Null(l.Furnace);
        Assert.Equal("waiting for a nearby corpse furnace", l.GetStatus());
        Poll(h, l);
        Assert.Equal("dead fighter waiting: no usable corpse furnace nearby", l.GetStatus());
    }

    [Fact]
    public void TheNearestUsableFurnaceIsTaken()
    {
        var h = new FakeHost();
        h.Furnaces.Add(Furnace with { Key = "far", Position = new Vector3(900, 0, 0) });
        h.Furnaces.Add(Furnace with { Key = "near", Position = new Vector3(300, 0, 0) });
        var l = new TownCorpseCleanupLogic<int>(-1);
        h.Add(1, new Npc());
        l.Begin(h, ArenaRef, [1], 1);
        Assert.Equal("near", l.Furnace!.Value.Key);
    }

    [Fact]
    public void ADeadFighterIsCarriedToTheFurnaceAndFed()
    {
        var (h, l) = Setup();
        h.Npcs[2].Dead = true;
        Assert.True(l.HasPending(h));
        Poll(h, l);
        Assert.Contains("spectator-release 10", h.Calls);
        Assert.Contains("reserve 10", h.Calls);
        Assert.Contains("orders 10 False False", h.Calls);
        Assert.Contains("lift 10 2", h.Calls);
        Assert.Equal(10, l.Handler);
        Assert.Equal(["watching registered fighters", "dispatching cage handler for Beta", "cage handler collecting Beta"], h.Statuses);

        // 500 ms later: no new lift order (every 15 s)
        h.Calls.Clear();
        Poll(h, l);
        Assert.DoesNotContain("lift 10 2", h.Calls);
        Poll(h, l, 15000);
        Assert.Contains("lift 10 2", h.Calls);

        // lifted: the walk to the furnace
        h.Npcs[10].Carrying = 2; h.Npcs[2].Carried = true;
        h.Calls.Clear();
        Poll(h, l);
        Assert.Contains("walk 10", h.Calls);
        Assert.Equal("cage handler taking Beta to the corpse furnace", l.GetStatus());
        h.Calls.Clear();
        Poll(h, l);
        Assert.DoesNotContain("walk 10", h.Calls);

        // at the furnace: fed
        h.AtFurnaceNow = true;
        Poll(h, l);
        Assert.Contains("dispose 10 2 Incinerating", h.Calls);
        Assert.Equal(1, l.Disposed);
        Assert.False(l.HasPending(h));

        l.Seal();
        h.Calls.Clear();
        Poll(h, l);
        Assert.Equal(["watching registered fighters", "dispatching cage handler for Beta", "cage handler collecting Beta",
            "cage handler taking Beta to the corpse furnace", "corpse accepted by furnace", "corpse cleanup complete"], h.Statuses);
        // the handler was healthy: the native hand-back (orders cleared, Hold / Passive / speed back, reservation released)
        Assert.Equal(["clear 10", "orders 10 True True", "speed 10 1", "release 10"], h.Calls);
        Assert.Empty(h.Reserved);
        Assert.Equal(-1, l.Handler);
    }

    [Fact]
    public void ThePollRunsEvery500Ms()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        l.Tick(h);   // first poll
        int lifts = h.Calls.Count(c => c.StartsWith("lift"));
        Assert.Equal(1, lifts);
        h.Npcs[10].Carrying = 1; h.Npcs[1].Carried = true;
        Poll(h, l, 499);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("walk"));
        Poll(h, l, 1);
        Assert.Contains(h.Calls, c => c.StartsWith("walk"));
    }

    [Fact]
    public void TheHandlerMustBeAnUnmanagedHealthyCageHandlerOfTheTown()
    {
        var (h, l) = Setup(handlers: 0);
        h.Add(20, new Npc { Record = PgRecords.CageHandler, Town = OtherTown, Pos = new Vector3(5, 0, 0) });
        h.Add(21, new Npc { Record = PgRecords.CageHandler, Managed = true, Pos = new Vector3(6, 0, 0) });
        h.Add(22, new Npc { Record = PgRecords.CageHandler, Ko = true, Pos = new Vector3(7, 0, 0) });
        h.Add(23, new Npc { Record = PgRecords.CageHandler, Carrying = 99, Pos = new Vector3(8, 0, 0) });
        h.Add(99, new Npc());
        h.Add(24, new Npc { Record = PgRecords.ScratchMedic, Pos = new Vector3(9, 0, 0) });
        h.Add(25, new Npc { Record = PgRecords.CageHandler, Player = true, Pos = new Vector3(9, 0, 0) });
        h.Add(26, new Npc { Record = PgRecords.CageHandler, Pos = new Vector3(3000, 0, 0) });
        h.Npcs[1].Dead = true;
        Poll(h, l);
        Assert.Equal("dead fighter waiting: no cage handler available", l.GetStatus());
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("reserve"));

        h.Add(27, new Npc { Record = PgRecords.CageHandler, Pos = new Vector3(50, 0, 0) });
        Poll(h, l);
        Assert.Equal(27, l.Handler);
    }

    [Fact]
    public void FullExternalSlotsRefuseTheHandlerAndTheNextCandidateIsTried()
    {
        var (h, l) = Setup(handlers: 2);
        h.Reserved.UnionWith([100, 101, 102, 103]);   // the announcer and the limb shop hold all four slots
        h.Npcs[1].Dead = true;
        Poll(h, l);
        Assert.Equal("dead fighter waiting: no cage handler available", l.GetStatus());
        Assert.Contains("spectator-release 10", h.Calls);   // taken from the audience first, as the C++
        Assert.Contains("reserve 10", h.Calls);
        Assert.Contains("reserve 11", h.Calls);
        Assert.False(l.Directed);

        h.Reserved.Remove(103);
        Poll(h, l);
        Assert.True(l.Directed);
        Assert.Equal(10, l.Handler);
    }

    [Fact]
    public void ALiftRefusalIsCountedAndRetriedOnTheOrderCadence()
    {
        var (h, l) = Setup();
        h.LiftRefuses = true;
        h.Npcs[1].Dead = true;
        Poll(h, l);
        Assert.Equal(1, l.LiftRefused);
        Poll(h, l);
        Assert.Equal(1, l.LiftRefused);
        Poll(h, l, 15000);
        Assert.Equal(2, l.LiftRefused);
    }

    [Fact]
    public void TheBatchGivesUpAfter180Seconds()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        Assert.True(l.BatchStarted);
        for (int i = 0; i < 400; i++) Poll(h, l);   // 200 s of polls, each dt 0.5 s
        Assert.True(l.BatchExpired);
        Assert.Equal("corpse cleanup timed out; remaining bodies released to town AI", l.GetStatus());
        Assert.False(l.HasPending(h));
        Assert.Empty(h.Reserved);
        Assert.InRange(l.BatchElapsed, 180f, 181f);
    }

    [Fact]
    public void TheBatchClockCapsOneStepAtOneSecond()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        Poll(h, l, 100000);
        Assert.InRange(l.BatchElapsed, 0.99f, 1.01f);
        Assert.False(l.BatchExpired);
    }

    [Fact]
    public void TheSweepAfterTheMedicsAdmitsOtherDeadBodies()
    {
        var (h, l) = Setup();
        h.Add(30, new Npc { Name = "Stray", Dead = true, Pos = new Vector3(100.04f, 2.25f, -50f) });
        h.Add(31, new Npc { Dead = true, Pos = new Vector3(200, 0, 0) });          // outside 175
        h.Add(32, new Npc { Dead = true, Player = true, Pos = new Vector3(5, 0, 0) });
        h.Add(33, new Npc { Dead = true, Carried = true, Pos = new Vector3(5, 0, 0) });
        h.Add(34, new Npc { Pos = new Vector3(5, 0, 0) });                         // alive
        Assert.Equal(1, l.AdmitArenaDeaths(h));
        Assert.Contains("Proving Grounds: final arena death sweep admitted name=Stray actor=a30 slot=3 position=(100.0,2.3,-50.0)", h.Debugs);
        Assert.Equal("final arena sweep found 1 dead NPC", l.GetStatus());
        Assert.Equal(0, l.AdmitArenaDeaths(h));   // known now
        h.Add(35, new Npc { Dead = true, Pos = new Vector3(1, 0, 0) });
        h.Add(36, new Npc { Dead = true, Pos = new Vector3(2, 0, 0) });
        Assert.Equal(2, l.AdmitArenaDeaths(h));
        Assert.Equal("final arena sweep found 2 dead NPCs", l.GetStatus());
        Assert.True(l.HasPending(h));
    }

    [Fact]
    public void ANewDeathAfterATimeoutStartsANewBatch()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        for (int i = 0; i < 400; i++) Poll(h, l);
        Assert.True(l.BatchExpired);
        h.Add(40, new Npc { Dead = true, Pos = new Vector3(3, 0, 0) });
        Assert.Equal(1, l.AdmitArenaDeaths(h));
        Assert.False(l.BatchExpired);
        Assert.False(l.BatchStarted);
        Assert.Equal(0f, l.BatchElapsed);
        Assert.True(l.HasPending(h));
    }

    [Fact]
    public void AHandlerCarryingABodyAtTheEndIsDeferredUntilItCanBeReleased()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Npcs[10].Carrying = 1; h.Npcs[1].Carried = true;
        l.Release(h);   // Finish while it carries the body
        Assert.Equal(1, l.DeferredCount);
        Assert.True(l.Keeps(10));
        Assert.Contains(10, h.Reserved);
        h.Calls.Clear();
        l.Tick(h);   // the deferred pass: drops the dead body, then the native hand-back
        Assert.Equal(["letgo 10", "clear 10", "orders 10 True True", "speed 10 1", "release 10"], h.Calls);
        Assert.Equal(0, l.DeferredCount);
        Assert.Empty(h.Reserved);
    }

    [Fact]
    public void AnUnconsciousHandlerStaysDeferredAndADeadOneIsForgotten()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Npcs[10].Ko = true;
        l.Release(h);
        Assert.Equal(1, l.DeferredCount);
        l.Tick(h);
        Assert.Equal(1, l.DeferredCount);
        h.Npcs[10].Dead = true;
        h.Calls.Clear();
        l.Tick(h);
        Assert.Equal(["release 10"], h.Calls);   // ForgetRelease: only the reservation given back
        Assert.Equal(0, l.DeferredCount);
    }

    [Fact]
    public void ABodySomeoneElseTookIsResolved()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Add(50, new Npc { Name = "Stranger" });
        h.Carry(50, 1);
        Poll(h, l);
        Assert.Equal(1, l.TakenElsewhere);
        Assert.Equal("corpse taken by someone else", l.GetStatus());
        Assert.False(l.HasPending(h));
        Assert.Empty(h.Reserved);
    }

    [Fact]
    public void ABodyInABedOrCageIsResolved()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Npcs[1].InBedOrCage = true;
        Poll(h, l);
        Assert.Equal("corpse taken by someone else", l.GetStatus());
        Assert.False(l.HasPending(h));
    }

    [Fact]
    public void AHandlerDeferredWhileCarryingLeavesTheBodyPendingForTheNextHandler()
    {
        var (h, l) = Setup(handlers: 2);
        h.Npcs[1].Dead = true;
        Poll(h, l);   // handler 10 lifts
        Assert.Equal(10, l.Handler);
        h.Carry(10, 1);
        h.Npcs[10].Combat = true;   // attacked while carrying: no longer operational, deferred (carrying, in combat)
        Poll(h, l);
        Assert.Equal(1, l.DeferredCount);
        Assert.True(l.Keeps(10));
        Assert.Equal(11, l.Handler);
        Poll(h, l);   // the body is still carried by the deferred handler: not "someone else"
        Assert.Equal(0, l.TakenElsewhere);
        Assert.True(l.HasPending(h));
        Assert.False(l.CorpseResolved(0));
        h.Npcs[10].Combat = false;
        h.Calls.Clear();
        Poll(h, l, 15000);   // the deferred pass drops the dead body and hands 10 back; the next 15 s order lifts it with 11
        Assert.Contains("letgo 10", h.Calls);
        Assert.Contains("release 10", h.Calls);
        Assert.Contains("lift 11 1", h.Calls);
        Assert.Equal(0, l.DeferredCount);
        Assert.True(l.HasPending(h));
    }

    [Fact]
    public void ABodyAManagedMedicCarriesStaysPending()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        h.Add(60, new Npc { Record = PgRecords.ScratchMedic, Managed = true });
        h.Carry(60, 1);
        Poll(h, l);
        Assert.Equal(0, l.TakenElsewhere);
        Assert.True(l.HasPending(h));
        Assert.Equal("cage handler collecting Alpha", l.GetStatus());
    }

    [Fact]
    public void AGoneBodyIsResolved()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Npcs[1].Gone = true;
        l.Seal();
        Poll(h, l);
        Assert.Equal(["watching registered fighters", "dispatching cage handler for Alpha", "cage handler collecting Alpha",
            "corpse accepted by furnace", "corpse cleanup complete"], h.Statuses);
        Assert.Empty(h.Reserved);
    }

    [Fact]
    public void ADisposalThatRemovesNothingIsCountedAndTheWalkRepeats()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Npcs[10].Carrying = 1; h.Npcs[1].Carried = true;
        Poll(h, l);
        h.AtFurnaceNow = true;
        h.DisposeAnswer = CorpseDisposalResult.Failed;
        Poll(h, l);
        Assert.Equal(1, l.DisposeFailures);
        Assert.True(l.HasPending(h));
        h.DisposeAnswer = CorpseDisposalResult.NotOurs;
        Poll(h, l);
        Assert.Equal("corpse taken by someone else", l.GetStatus());
    }

    [Fact]
    public void ARepeatedFailedDisposalIsCountedAndLoggedOnce()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Carry(10, 1);
        Poll(h, l);
        h.AtFurnaceNow = true;
        h.DisposeAnswer = CorpseDisposalResult.Failed;
        for (int i = 0; i < 6; i++) Poll(h, l);
        Assert.Equal(6, h.Calls.Count(c => c.StartsWith("dispose")));
        Assert.Equal(1, l.DisposeFailures);
        Assert.Equal(1, h.FedLines);
        h.DisposeAnswer = null;   // removed at last: logged
        Poll(h, l);
        Assert.Equal(2, h.FedLines);
        Assert.Equal(1, l.Disposed);
    }

    [Fact]
    public void WithoutTheRemovalMembersNothingIsFed()
    {
        var (h, l) = Setup();
        h.DisposalAvailable = false;
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Carry(10, 1);
        h.AtFurnaceNow = true;
        for (int i = 0; i < 4; i++) Poll(h, l);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("dispose"));
        Assert.Equal(0, l.DisposeFailures);
        for (int i = 0; i < 400; i++) Poll(h, l);
        Assert.True(l.BatchExpired);
    }

    [Fact]
    public void AHandlerDyingMidCarryDropsTheBodyAndTheNextHandlerLiftsIt()
    {
        var (h, l) = Setup(handlers: 2);
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Carry(10, 1);
        Poll(h, l);
        h.LetGo(10);              // killed while carrying: the engine drops the body
        h.Npcs[10].Dead = true;
        h.Calls.Clear();
        Poll(h, l);
        Assert.Contains("release 10", h.Calls);      // ForgetRelease: only the reservation
        Assert.DoesNotContain("clear 10", h.Calls);
        Assert.Equal(0, l.DeferredCount);
        Assert.Equal(11, l.Handler);
        Assert.Contains("lift 11 1", h.Calls);
        Assert.True(l.HasPending(h));
        Assert.False(l.CorpseResolved(0));
    }

    [Fact]
    public void AThrowWhileResolvingAFailedStepForgetsTheHandlerForTheBackstop()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        h.Npcs[2].Dead = true;
        Poll(h, l);
        Assert.Equal(10, l.Handler);
        var throwing = new ThrowingLift(h) { ThrowRelease = true };
        h.Now += 15000;
        l.Tick(throwing);   // the lift throws; resolving it throws again (the release): the nested catch forgets the handler
        Assert.Equal(1, l.CorpseFailures);
        Assert.Equal(-1, l.Handler);
        Assert.False(l.Directed);
        Assert.Equal(-1, l.Current);
        Assert.False(l.Keeps(10));       // so the glue's Finish backstop gives its reservation back
        Assert.Contains(10, h.Reserved); // still reserved until then
        Assert.True(l.CorpseResolved(0));
        Poll(h, l);                      // the cleanup goes on: a handler for the other body
        Assert.True(l.Directed);
        Assert.Equal(1, l.Current);
    }

    [Fact]
    public void OneBadCorpseNeverStopsTheOthers()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        h.Npcs[2].Dead = true;
        Poll(h, l);   // works on Alpha
        Assert.Equal("cage handler collecting Alpha", l.GetStatus());
        // a throw inside the step (the lift) resolves the corpse and the next poll takes the other
        var throwing = new ThrowingLift(h);
        h.Now += 15000;
        l.Tick(throwing);
        Assert.Equal(1, l.CorpseFailures);
        Assert.Equal(1, throwing.Inner.Failures);
        Assert.True(l.CorpseResolved(0));
        Poll(h, l);
        Assert.Equal("cage handler collecting Beta", l.GetStatus());
    }

    /// <summary>A host whose lift throws (every other call goes to the fake).</summary>
    sealed class ThrowingLift(FakeHost inner) : ITownCorpseCleanupHost<int>
    {
        public FakeHost Inner => inner;
        public bool IsPaused => inner.IsPaused;
        public uint NowMs => inner.NowMs;
        public void Debug(string line) => inner.Debug(line);
        public int None => -1;
        public bool IsNull(int c) => inner.IsNull(c);
        public bool Valid(int c) => inner.Valid(c);
        public bool IsPlayer(int c) => inner.IsPlayer(c);
        public bool IsDead(int c) => inner.IsDead(c);
        public bool IsUnconscious(int c) => inner.IsUnconscious(c);
        public bool InCombatMode(int c) => inner.InCombatMode(c);
        public bool IsCarried(int c) => inner.IsCarried(c);
        public bool IsCarrying(int c) => inner.IsCarrying(c);
        public int CarryingOf(int c) => inner.CarryingOf(c);
        public bool InBedOrCage(int b) => inner.InBedOrCage(b);
        public int CarrierOf(int b) => inner.CarrierOf(b);
        public bool NeedsStabilization(int c) => false;
        public string RecordId(int c) => inner.RecordId(c);
        public string Name(int c) => inner.Name(c);
        public string ActorText(int c) => inner.ActorText(c);
        public Vector3 Position(int c) => inner.Position(c);
        public long CurrentTown(int c) => inner.CurrentTown(c);
        public IReadOnlyList<int> CharactersInSphere(Vector3 o, float r, int m) => inner.CharactersInSphere(o, r, m);
        public PlacedBuilding? Resolve(ArenaBuildingRef a) => inner.Resolve(a);
        public CorpseFurnace? ResolveFurnace(string k) => inner.ResolveFurnace(k);
        public IReadOnlyList<CorpseFurnace> FurnacesInSphere(Vector3 o, float r, int m) => inner.FurnacesInSphere(o, r, m);
        public bool AftercareManages(int c) => inner.AftercareManages(c);
        public void SpectatorsReleaseCharacter(int c) => inner.SpectatorsReleaseCharacter(c);
        public bool ReserveExternal(int c) => inner.ReserveExternal(c);
        public void ReleaseExternal(int c) { if (ThrowRelease) throw new InvalidOperationException("release threw"); inner.ReleaseExternal(c); }
        public (bool Hold, bool Passive) StandingOrders(int c) => inner.StandingOrders(c);
        public void SetStandingOrders(int c, bool h, bool p) => inner.SetStandingOrders(c, h, p);
        public byte SpeedOf(int c) => inner.SpeedOf(c);
        public void SetSpeed(int c, byte s) => inner.SetSpeed(c, s);
        public void SetRun(int c) => inner.SetRun(c);
        public void ClearOrders(int c) => inner.ClearOrders(c);
        public bool BeginLift(int c, int corpse) => throw new InvalidOperationException("lift threw");
        public bool LetGo(int c) => inner.LetGo(c);
        public void WalkTo(int c, Vector3 p) => inner.WalkTo(c, p);
        public bool AtFurnace(int c, CorpseFurnace f) => inner.AtFurnace(c, f);
        public bool ThrowRelease;
        public bool DisposalAvailable => inner.DisposalAvailable;
        public CorpseDisposalResult Dispose(int h, int b, CorpseFurnace f, bool log) => inner.Dispose(h, b, f, log);
        public void CorpseFailed(Exception e) => inner.CorpseFailed(e);
    }

    [Fact]
    public void ReleaseGivesTheHandlerBackAndClearsTheBatch()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        l.Release(h);
        Assert.Empty(h.Reserved);
        Assert.False(l.Active);
        Assert.Equal(0, l.CorpseCount);
        Assert.Equal("", l.GetStatus());
        Assert.False(l.Keeps(10));
    }

    [Fact]
    public void AbandonWorldStateTouchesNoActor()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        h.Npcs[10].Ko = true;
        l.Release(h);
        h.Calls.Clear();
        l.AbandonWorldState();
        Assert.Empty(h.Calls);
        Assert.Equal(0, l.DeferredCount);
        Assert.Equal(-1, l.Handler);
    }

    [Fact]
    public void BeginReleasesThePreviousBatch()
    {
        var (h, l) = Setup();
        h.Npcs[1].Dead = true;
        Poll(h, l);
        l.Begin(h, ArenaRef, [2], 1);
        Assert.Empty(h.Reserved);
        Assert.Equal(1, l.CorpseCount);
    }
}
