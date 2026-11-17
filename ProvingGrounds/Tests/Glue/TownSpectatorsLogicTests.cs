using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.2: TownSpectators.cpp's decisions (Core/Glue/TownSpectatorsLogic.cs) over a fake world: the seats Begin takes, the audience
/// (roles, round robin, the permanent-job stand-in, idle fighters, the cap), the maintenance pass (sit order, 3 s re-order, seated, stood up,
/// taken, timeout, deferred release), the chatter (start, spacing, voices, potato), the result lines, Release / ReleaseCharacter / AbandonWorldState,
/// and a guest that throws.</summary>
public class TownSpectatorsLogicTests
{
    const long Town = 7, OtherTown = 8;
    static readonly ArenaBuildingRef ArenaRef = new(ArenaBuildingSource.Town, "arena", PgRecords.Arena);

    sealed class Npc
    {
        public string Record = PgRecords.ScratchCivilian;
        public string Name = "";
        public long Town = TownSpectatorsLogicTests.Town;
        public bool Player, Dead, Ko, Combat, Carried, Duty, Faction = true, OtherOwner, Gone;
        public bool CanSit = true, Fighter;
        public string? OnWayTo;      // the sit task's seat (walking or sitting)
        public bool Sitting;         // at the seat
    }

    sealed class FakeHost : ITownSpectatorsHost<int>
    {
        public readonly Dictionary<int, Npc> Npcs = [];
        public readonly List<int> Nearby = [];
        public readonly List<PlacedBuilding> Buildings = [];
        public readonly Dictionary<string, SpectatorSeat> Seats = [];   // by building key
        public readonly Dictionary<string, int> AiSitters = [];          // seat id -> an AI sitter
        public readonly HashSet<string> Claims = [];
        public readonly HashSet<int> Held = [];
        public readonly List<string> Calls = [];
        public readonly List<string> Debugs = [];
        public readonly List<(int Who, string Line)> Said = [];
        public bool Spar, Teams1v1;
        public int[] Participants = [];
        public uint Now = 1000;
        public double Hours = 1.0;
        public int ThrowOnSitFor = -1;
        public int ReadSeatsCalls;

        public bool IsPaused => false;
        public uint NowMs => Now;
        public double TotalHours => Hours;
        public void Debug(string line) => Debugs.Add(line);
        public int None => -1;
        public bool IsNull(int c) => c < 0;
        public bool Valid(int c) => Npcs.TryGetValue(c, out var n) && !n.Gone;
        public bool IsPlayer(int c) => Npcs[c].Player;
        public bool IsDead(int c) => Npcs[c].Dead;
        public bool IsUnconscious(int c) => Npcs[c].Ko;
        public bool InCombatMode(int c) => Npcs[c].Combat;
        public bool NeedsStabilization(int c) => false;
        public bool IsCarried(int c) => Npcs[c].Carried;
        public bool IsCarrying(int c) => false;
        public string Name(int c) => Npcs[c].Name;
        public string RecordId(int c) => Npcs.TryGetValue(c, out var n) ? n.Record : "";
        public long CurrentTown(int c) => Npcs[c].Town;
        public bool IsTownFaction(int c) => Npcs[c].Faction;
        public bool HasDuty(int c) => Npcs[c].Duty;
        public bool HeldByAnotherOwner(int c) => Npcs[c].OtherOwner;
        public bool IsTownFighter(int c) => Npcs[c].Fighter;
        public bool CanSit(int c) => Npcs[c].CanSit;
        public PlacedBuilding? Resolve(ArenaBuildingRef held) => Buildings.Find(b => b.Building.Ref == held) is { Building.Key: not null } p ? p : null;
        public IReadOnlyList<PlacedBuilding>? BuildingsInSphere(Vector3 origin, float radius, int max) => Buildings;
        public IReadOnlyList<int>? CharactersInSphere(Vector3 origin, float radius, int max) => Nearby;
        public SpectatorSeat? SeatOf(ArenaBuilding building) => Seats.TryGetValue(building.Key, out var s) ? s : null;
        public bool SeatExists(string seat) => Seats.Values.Any(s => s.Id == seat);
        public readonly List<bool> BoardClaims = [];
        public readonly HashSet<int> RefuseSit = [];
        public void ReadSeats(IReadOnlyList<SpectatorSeat> seats, int[] occupants, bool[] claimed, bool boardClaims)
        {
            ReadSeatsCalls++;
            BoardClaims.Add(boardClaims);
            for (int i = 0; i < seats.Count; i++)
            {
                occupants[i] = -1; claimed[i] = Claims.Contains(seats[i].Id);
                if (AiSitters.TryGetValue(seats[i].Id, out int s)) occupants[i] = s;
                foreach (var (id, n) in Npcs) if (n.OnWayTo == seats[i].Id && n.Sitting && occupants[i] < 0) occupants[i] = id;
            }
        }
        public bool Hold(int c) { Calls.Add("hold " + c); Held.Add(c); return true; }
        public void ReleaseHold(int c) { Calls.Add("release " + c); Held.Remove(c); }
        public bool Sit(int c, string seat)
        {
            Calls.Add("sit " + c + " " + seat);
            if (c == ThrowOnSitFor) throw new InvalidOperationException("boom");
            if (RefuseSit.Contains(c)) return false;
            Npcs[c].OnWayTo = seat;
            return true;
        }
        public bool SittingOn(int c, string seat) => OnItsWay(c, seat) && Npcs[c].Sitting;
        public bool OnItsWay(int c, string seat) => Npcs.TryGetValue(c, out var n) && n.OnWayTo == seat;
        public void StandUp(int c, string seat)
        {
            if (!OnItsWay(c, seat)) return;
            Calls.Add("stand " + c);
            Npcs[c].OnWayTo = null; Npcs[c].Sitting = false;
        }
        public bool SparIsActive => Spar;
        public bool SparTeams1v1 => Teams1v1;
        public int SparActiveA => Participants.Length > 0 ? Participants[0] : -1;
        public int SparActiveB => Participants.Length > 1 ? Participants[1] : -1;
        public int SparParticipantCount => Participants.Length;
        public int SparParticipant(int i) => Participants[i];
        public bool SparIsEliminated(int c) => false;
        public void Say(int c, string line) => Said.Add((c, line));
    }

    static ArenaBuilding Building(string key, string record, Vector3 pos) => new(ArenaBuildingSource.Town, key, record, null, true, pos, 0);

    /// <summary>An arena at the origin in <see cref="Town"/> with <paramref name="pillows"/> pillows 100 units out in a row.</summary>
    static FakeHost World(int pillows, float spacing = 1)
    {
        var h = new FakeHost();
        h.Buildings.Add(new PlacedBuilding(Building("arena", PgRecords.Arena, Vector3.Zero), Town));
        for (int i = 0; i < pillows; i++)
        {
            var pos = new Vector3(100 + i * spacing, 0, 0);
            string key = "pillow" + i;
            h.Buildings.Add(new PlacedBuilding(Building(key, PgRecords.ArenaPillow, pos), Town));
            h.Seats[key] = new SpectatorSeat(key + "@seat0", key, pos);
        }
        return h;
    }

    static int Add(FakeHost h, int id, string record, Action<Npc>? set = null)
    {
        var n = new Npc { Record = record, Name = "N" + id };
        set?.Invoke(n);
        h.Npcs[id] = n;
        h.Nearby.Add(id);
        return id;
    }

    static string Seating(TownSpectatorsLogic<int> l, FakeHost h) => h.Debugs.Single(d => d.StartsWith("Proving Grounds: spectator seating - "));

    [Fact]
    public void Begin_takes_free_pillow_seats_of_this_arena_only_and_logs_the_seating_line()
    {
        var h = World(3, spacing: 50);   // pillows at x 100, 150, 200
        // a pillow 600 out (beyond 500) and one of another town: rejected with the C++ line; pillow1 is occupied (not free, no line); a second
        // arena of the same town nearer pillow2 takes that one
        h.Buildings.Add(new PlacedBuilding(Building("far", PgRecords.ArenaPillow, new Vector3(600, 0, 0)), Town));
        h.Seats["far"] = new SpectatorSeat("far@seat0", "far", new Vector3(600, 0, 0));
        h.Buildings.Add(new PlacedBuilding(Building("other", PgRecords.ArenaPillow, new Vector3(50, 0, 0)), OtherTown));
        h.Seats["other"] = new SpectatorSeat("other@seat0", "other", new Vector3(50, 0, 0));
        h.AiSitters["pillow1@seat0"] = 99;
        h.Npcs[99] = new Npc();
        h.Buildings.Add(new PlacedBuilding(Building("arena2", PgRecords.Arena, new Vector3(200, 0, 180)), Town));
        Add(h, 1, PgRecords.ScratchCivilian);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        Assert.Equal([
            "Proving Grounds: pillow association rejected distanceSq=40000.0 otherArenaDistanceSq=32400.0 sameTown=1",
            "Proving Grounds: pillow association rejected distanceSq=360000.0 otherArenaDistanceSq=192400.0 sameTown=1",
            "Proving Grounds: pillow association rejected distanceSq=2500.0 otherArenaDistanceSq=54900.0 sameTown=0",
            "Proving Grounds: spectator seating - night target=60, 7 nearby buildings, 5 matching pillows, 1 free pillows, 1 invited (candidates: 1 civilians, 0 bar residents, 0 cage handlers, 0 idle fighters)",
        ], h.Debugs);
        Assert.Equal("pillow0@seat0", l.Guests.Single().Seat.Id);
    }

    [Fact]
    public void A_claimed_seat_is_never_reserved()
    {
        var h = World(2);
        h.Claims.Add("pillow0@seat0");
        Add(h, 1, PgRecords.ScratchCivilian);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        Assert.Equal("pillow1@seat0", l.Guests.Single().Seat.Id);
    }

    [Fact]
    public void Candidates_round_robin_by_role_the_start_role_rotating_per_bout_then_idle_fighters()
    {
        var h = World(10);
        Add(h, 1, PgRecords.ScratchCivilian); Add(h, 2, PgRecords.ScratchCivilian);
        Add(h, 3, PgRecords.ScratchBarResident);
        Add(h, 4, PgRecords.CageHandler);
        const string fighterRecord = "223-Proving Grounds.mod";
        Add(h, 5, fighterRecord); Add(h, 6, fighterRecord, n => n.Fighter = true);   // 6 fights in the bout
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);   // sequence 0: civilians first
        Assert.Equal([1, 3, 4, 2, 5], l.Guests.Select(g => g.Actor));
        Assert.EndsWith("(candidates: 2 civilians, 1 bar residents, 1 cage handlers, 1 idle fighters)", Seating(l, h));
        h.Debugs.Clear();
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);   // sequence 1: bar residents first, the nearby list starts one later
        Assert.Equal(3, l.Guests[0].Actor);
        Assert.Equal(5, l.Guests.Count);
    }

    [Fact]
    public void The_permanent_job_stand_in_keeps_civilians_at_their_posts_but_not_residents_or_handlers()
    {
        var h = World(10);
        Add(h, 1, PgRecords.ScratchCivilian, n => n.Duty = true);
        Add(h, 2, PgRecords.ScratchBarResident, n => n.Duty = true);
        Add(h, 3, PgRecords.CageHandler, n => n.Duty = true);
        Add(h, 4, PgRecords.ScratchCivilian);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        Assert.Equal([2, 3, 4], l.Guests.Select(g => g.Actor).OrderBy(x => x));
        Assert.Equal(1, l.ExcludedByDuty);
    }

    [Fact]
    public void Players_strangers_the_unhealthy_other_towns_and_held_npcs_never_watch()
    {
        var h = World(10);
        Add(h, 1, PgRecords.ScratchCivilian, n => n.Player = true);
        Add(h, 2, PgRecords.ScratchCivilian, n => n.Faction = false);
        Add(h, 3, PgRecords.ScratchCivilian, n => n.Ko = true);
        Add(h, 4, PgRecords.ScratchCivilian, n => n.Town = OtherTown);
        Add(h, 5, PgRecords.ScratchCivilian, n => n.OtherOwner = true);
        Add(h, 6, PgRecords.ScratchMedic);
        Add(h, 7, PgRecords.ScratchCivilian, n => n.CanSit = false);   // a candidate, but no seat takes it
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        Assert.Empty(l.Guests);
        Assert.Equal(1, l.ExcludedByHold);
        Assert.EndsWith("0 invited (candidates: 1 civilians, 0 bar residents, 0 cage handlers, 0 idle fighters)", Seating(l, h));
    }

    [Fact]
    public void The_audience_is_capped_by_the_profile_and_the_free_seats()
    {
        var h = World(40);
        for (int i = 0; i < 30; i++) Add(h, i, PgRecords.ScratchCivilian);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Potato);
        Assert.Equal(20, l.Guests.Count);
        Assert.Equal(20, l.Guests.Select(g => g.Seat.Id).Distinct().Count());
        var h2 = World(5);
        for (int i = 0; i < 30; i++) Add(h2, i, PgRecords.ScratchCivilian);
        var l2 = new TownSpectatorsLogic<int>(-1);
        l2.Begin(h2, ArenaRef, ArenaPerformance.Profile.High);
        Assert.Equal(5, l2.Guests.Count);
    }

    static (FakeHost, TownSpectatorsLogic<int>) OneGuest(ArenaPerformance.Profile profile = ArenaPerformance.Profile.Normal)
    {
        var h = World(1);
        Add(h, 1, PgRecords.ScratchCivilian);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, profile);
        return (h, l);
    }

    [Fact]
    public void Begin_holds_and_orders_the_guest_to_its_seat_then_reorders_every_3_s_only_when_the_task_was_lost()
    {
        var (h, l) = OneGuest();
        Assert.Equal(["hold 1", "sit 1 pillow0@seat0"], h.Calls);
        h.Now += 3000; l.Tick(h);
        Assert.Equal(2, h.Calls.Count);   // still on its way: no new order
        h.Npcs[1].OnWayTo = null;          // the task was lost
        h.Now += 1000; l.Tick(h);
        Assert.Equal(2, h.Calls.Count);   // 4 s since the order, but the last order time is 3 s
        h.Now += 2100; l.Tick(h);
        Assert.Equal("sit 1 pillow0@seat0", h.Calls[^1]);
    }

    [Fact]
    public void A_seated_guest_that_stands_up_is_released()
    {
        var (h, l) = OneGuest();
        h.Npcs[1].Sitting = true;
        h.Now += 200; l.Tick(h);
        Assert.True(l.Guests.Single().Seated);
        h.Npcs[1].Sitting = false; h.Npcs[1].OnWayTo = null;   // an AI sitter's arrival ended its task
        h.Now += 200; l.Tick(h);
        Assert.Empty(l.Guests);
        Assert.Equal("release 1", h.Calls[^1]);
        Assert.DoesNotContain(1, h.Held);
    }

    [Fact]
    public void A_seat_taken_by_someone_else_releases_the_guest_and_its_walk_is_stopped()
    {
        var (h, l) = OneGuest();
        h.AiSitters["pillow0@seat0"] = 50; h.Npcs[50] = new Npc();
        h.Now += 200; l.Tick(h);
        Assert.Empty(l.Guests);
        Assert.Equal(["stand 1", "release 1"], h.Calls.Skip(2));
    }

    [Fact]
    public void Not_seated_within_180_s_releases_the_guest()
    {
        var (h, l) = OneGuest();
        for (int i = 0; i < 179; i++) { h.Now += 1000; l.Tick(h); }
        Assert.Single(l.Guests);
        h.Now += 1000; l.Tick(h);
        Assert.Empty(l.Guests);
    }

    [Fact]
    public void The_maintenance_gate_follows_the_profile()
    {
        var (h, l) = OneGuest();   // normal: 100 ms
        int reads = h.ReadSeatsCalls;
        h.Now += 50; l.Tick(h);
        Assert.Equal(reads, h.ReadSeatsCalls);
        h.Now += 60; l.Tick(h);
        Assert.Equal(reads + 1, h.ReadSeatsCalls);
    }

    [Fact]
    public void An_unhealthy_guest_is_released_later_keeping_the_hold_until_it_is_healthy()
    {
        var (h, l) = OneGuest();
        h.Npcs[1].Combat = true;
        h.Now += 200; l.Tick(h);
        Assert.Empty(l.Guests);
        Assert.Equal([1], l.PendingReleases);
        Assert.Contains(1, h.Held);
        h.Now += 200; l.Tick(h);
        Assert.Contains(1, h.Held);
        h.Npcs[1].Combat = false;
        h.Now += 200; l.Tick(h);
        Assert.Empty(l.PendingReleases);
        Assert.DoesNotContain(1, h.Held);
    }

    [Fact]
    public void ReleaseCharacter_lets_a_pending_guest_go_too()
    {
        var (h, l) = OneGuest();
        h.Npcs[1].Combat = true;
        h.Now += 200; l.Tick(h);
        l.ReleaseCharacter(h, 1);
        Assert.Empty(l.PendingReleases);
        Assert.DoesNotContain(1, h.Held);
    }

    [Fact]
    public void Release_stands_everyone_up_logs_once_and_AbandonWorldState_forgets_everything()
    {
        var h = World(3);
        Add(h, 1, PgRecords.ScratchCivilian); Add(h, 2, PgRecords.ScratchBarResident);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        h.Npcs[1].Sitting = true;
        l.Release(h);
        Assert.Empty(l.Guests);
        Assert.Empty(h.Held);
        Assert.Contains("stand 1", h.Calls);
        Assert.Equal(1, h.Debugs.Count(d => d == "Proving Grounds: arena spectators released from arena control"));
        l.Release(h);
        Assert.Equal(1, h.Debugs.Count(d => d == "Proving Grounds: arena spectators released from arena control"));
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        l.AbandonWorldState();
        Assert.Empty(l.Guests);
        Assert.Null(l.ActiveArena);
    }

    [Fact]
    public void A_guest_whose_step_throws_is_released_and_the_others_go_on()
    {
        var h = World(3);
        Add(h, 1, PgRecords.ScratchCivilian); Add(h, 2, PgRecords.ScratchBarResident);
        h.ThrowOnSitFor = 1;
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        Assert.Equal([2], l.Guests.Select(g => g.Actor));
        Assert.Equal(1, l.GuestFailures);
        Assert.DoesNotContain(1, h.Held);
        Assert.Contains("sit 2 pillow1@seat0", h.Calls);
        Assert.Contains(h.Debugs, d => d.StartsWith("Proving Grounds: spectator guest failed error=InvalidOperationException: boom"));
    }

    static void SeatAll(FakeHost h, TownSpectatorsLogic<int> l)
    {
        foreach (var n in h.Npcs.Values) if (n.OnWayTo != null) n.Sitting = true;
        h.Now += 200; l.Tick(h);
    }

    [Fact]
    public void Chatter_starts_with_the_fight_speaks_at_the_normal_spacing_and_stops_with_it()
    {
        var h = World(60);
        for (int i = 0; i < 60; i++) Add(h, i, PgRecords.ScratchCivilian);
        h.Npcs[100] = new Npc { Name = "Ada" }; h.Npcs[101] = new Npc { Name = "Bo" };
        h.Participants = [100, 101];
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        SeatAll(h, l);
        for (int i = 0; i < 50; i++) { h.Now += 200; l.Tick(h); }
        Assert.Empty(h.Said);   // no match yet
        h.Spar = true;
        uint t0 = h.Now;
        var times = new List<uint>();
        for (int i = 0; i < 400; i++)
        {
            h.Now += 100; int before = h.Said.Count; l.Tick(h);
            for (int k = before; k < h.Said.Count; k++) times.Add(h.Now - t0);
        }
        // 40 s: 60 seated -> ceil(60/25) = 3 voices, capped at 2 (normal) per 8-12 s cycle
        Assert.InRange(h.Said.Count, 6, 10);
        Assert.All(h.Said, s => Assert.InRange(s.Who, 0, 59));
        var oneCycle = times.Where(t => t < 8000).Count();
        Assert.InRange(oneCycle, 1, 2);
        h.Spar = false;
        int said = h.Said.Count;
        for (int i = 0; i < 200; i++) { h.Now += 100; l.Tick(h); }
        Assert.Equal(said, h.Said.Count);
        Assert.False(l.ChatterActive);
    }

    [Fact]
    public void Potato_profile_never_chatters_nor_reacts()
    {
        var (h, l) = OneGuest(ArenaPerformance.Profile.Potato);
        SeatAll(h, l);
        h.Spar = true;
        for (int i = 0; i < 300; i++) { h.Now += 300; l.Tick(h); }
        l.ReactToResult(h, new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins });
        Assert.Empty(h.Said);
    }

    [Fact]
    public void ReactToResult_seated_guests_say_the_result_lines_a_stop_says_nothing()
    {
        var (h, l) = OneGuest();
        l.ReactToResult(h, new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.Draw });
        Assert.Empty(h.Said);   // nobody seated yet
        SeatAll(h, l);
        l.ReactToResult(h, new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.Stopped });
        Assert.Empty(h.Said);
        l.ReactToResult(h, new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.Draw });
        var (who, line) = Assert.Single(h.Said);
        Assert.Equal(1, who);
        Assert.Contains(line, new[] { "Run it back!", "Again! We need a winner!", "Nobody won my Cats!", "You can't end it there!" });
    }

    [Fact]
    public void A_refused_sit_order_is_counted_logged_once_and_releases_the_guest_after_two_in_a_row()
    {
        var h = World(3);
        Add(h, 1, PgRecords.ScratchCivilian); Add(h, 2, PgRecords.ScratchBarResident);
        h.RefuseSit.Add(1); h.RefuseSit.Add(2);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);   // the first orders: both refused
        Assert.Equal(2, l.Guests.Count);
        Assert.Equal(2, l.SitRefused);
        Assert.Single(h.Debugs, d => d.StartsWith("Proving Grounds: spectator sit order refused seat="));
        h.RefuseSit.Remove(2);
        h.Now += 3000; l.Tick(h);   // the second: guest 1 refused again (released), guest 2 accepted
        Assert.Equal([2], l.Guests.Select(g => g.Actor));
        Assert.Equal(0, l.Guests[0].SitRefusals);
        Assert.DoesNotContain(1, h.Held);
        Assert.Equal(3, l.SitRefused);
        Assert.Single(h.Debugs, d => d.StartsWith("Proving Grounds: spectator sit order refused seat="));
    }

    [Fact]
    public void Seated_guests_read_their_own_seat_and_the_table_scan_runs_only_while_one_is_not_seated()
    {
        var h = World(3);
        Add(h, 1, PgRecords.ScratchCivilian); Add(h, 2, PgRecords.ScratchBarResident);
        var l = new TownSpectatorsLogic<int>(-1);
        l.Begin(h, ArenaRef, ArenaPerformance.Profile.Normal);
        Assert.Equal([true, false], h.BoardClaims);   // Begin with the board claims, Tick without
        h.Npcs[1].Sitting = true; h.Npcs[2].Sitting = true;
        h.Now += 200; l.Tick(h);   // both seen seated by the scan
        int reads = h.ReadSeatsCalls;
        for (int i = 0; i < 5; i++) { h.Now += 200; l.Tick(h); }
        Assert.Equal(reads, h.ReadSeatsCalls);
        Assert.Equal(2, l.Guests.Count(g => g.Seated));
        // another character takes guest 1's seat: Meitou ends the guest's sit task (UseSpecial), the guest no longer sits there: released
        h.AiSitters["pillow0@seat0"] = 50; h.Npcs[50] = new Npc();
        h.Npcs[1].Sitting = false; h.Npcs[1].OnWayTo = null;
        h.Now += 200; l.Tick(h);
        Assert.Equal([2], l.Guests.Select(g => g.Actor));
        Assert.DoesNotContain(1, h.Held);
        Assert.Equal(reads, h.ReadSeatsCalls);
    }

    [Fact]
    public void An_operator_at_the_seat_wins_it_over_a_resting_sitter_whatever_the_slot_order()
    {
        Assert.True(SpectatorSeatRules.OccupantWins(true, hasOccupant: false, occupantIsOperator: false));
        Assert.True(SpectatorSeatRules.OccupantWins(true, hasOccupant: true, occupantIsOperator: false));    // a resting sitter found first
        Assert.False(SpectatorSeatRules.OccupantWins(true, hasOccupant: true, occupantIsOperator: true));   // the first operator keeps it
        Assert.True(SpectatorSeatRules.OccupantWins(false, hasOccupant: false, occupantIsOperator: false));
        Assert.False(SpectatorSeatRules.OccupantWins(false, hasOccupant: true, occupantIsOperator: true));  // an operator found first
        Assert.False(SpectatorSeatRules.OccupantWins(false, hasOccupant: true, occupantIsOperator: false));
    }
}
