using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.4a (TownAftercare.cpp:158-218, :320-407, :443-482, :1051-1226): the reservation tables, Begin (the medical team, the hospital's
/// beds, the adoption), Standby / StageMedic, Release and the world teardown, over <see cref="Fake"/>.</summary>
public partial class TownAftercareLogicTests
{
    const string Hospital = "163-Proving Grounds.mod";
    const long Scratch = 304;
    static readonly Vector3 ArenaPos = new(1000, 50, 2000);
    static readonly ArenaBuilding ArenaB = new(ArenaBuildingSource.Town, "arena", "183-Proving Grounds.mod", null, true, ArenaPos, 0f);

    sealed partial class Who
    {
        public bool Valid = true, Player, Dead, Ko, Carried, Carrying, Combat, NeedsAid, Goal;
        public string Name = "";
        public Vector3 Pos;
        public Vector3? Dest;
        public byte Speed = 1;
        public int CarryingId = -1;
    }

    /// <summary>The world as the aftercare reads it; holds go through a real HoldRegistry over a recording engine.</summary>
    sealed partial class Fake : ITownAftercareHost<int>, IHoldEngine<int>
    {
        public readonly Dictionary<int, Who> People = [];
        public readonly List<PlacedBuilding> Buildings = [];
        public readonly Dictionary<string, List<string>> BedLists = [];
        public readonly Dictionary<string, AftercareBed<int>> BedTable = [];
        public readonly HoldRegistry<int> Holds = new();
        public readonly HashSet<int> Engaged = [];
        public readonly List<string> Debugs = [];
        public readonly List<(int Id, Vector3 To)> Moves = [];
        public readonly List<int> Runs = [];
        public uint Now = 10_000;
        public uint Ids = 41;

        public Who Add(int id, Vector3 pos, string name = "", bool player = false)
        {
            var w = new Who { Pos = pos, Name = name.Length > 0 ? name : "c" + id, Player = player };
            People[id] = w;
            return w;
        }

        public void AddHospital(string key, long town, Vector3 pos, params (string Id, int Function)[] beds)
        {
            Buildings.Add(new PlacedBuilding(new ArenaBuilding(ArenaBuildingSource.Town, key, Hospital, null, true, pos, 0f), town));
            BedLists[key] = [.. beds.Select(b => b.Id)];
            foreach (var b in beds) BedTable[b.Id] = new AftercareBed<int>(b.Id, Hospital, b.Function, -1, false, false);
        }

        Who P(int c) => People[c];

        public bool IsPaused => false;
        public uint NowMs => Now;
        public void Debug(string line) => Debugs.Add(line);
        public uint NextLogId() => ++Ids;
        public int None => -1;
        public bool IsNull(int c) => c < 0;
        public bool Valid(int c) => c >= 0 && People.TryGetValue(c, out var w) && w.Valid;
        public bool IsPlayer(int c) => People.TryGetValue(c, out var w) && w.Player;
        public bool IsDead(int c) => P(c).Dead;
        public bool IsUnconscious(int c) => P(c).Ko;
        public bool IsCarried(int c) => P(c).Carried;
        public bool IsCarrying(int c) => P(c).Carrying;
        public bool InCombatMode(int c) => P(c).Combat;
        public bool NeedsStabilization(int c) => P(c).NeedsAid;
        public string Name(int c) => P(c).Name;
        public string ActorText(int c) => "c:" + c;
        public Vector3 Position(int c) => P(c).Pos;
        public bool TryGetMoveDestination(int c, out Vector3 destination)
        {
            destination = P(c).Dest ?? Vector3.Zero;
            return P(c).Dest is not null;
        }
        public bool HasAiGoal(int c) => P(c).Goal;
        public readonly List<string> Order = [];
        public int CarryingOf(int c) => P(c).CarryingId;
        public void DropCarried(int c) { Order.Add("drop " + c); P(c).CarryingId = -1; }
        public AftercarePatientRead ReadForRelease(int c) =>
            new(P(c).Dead, P(c).Ko, P(c).Combat, P(c).Carried, false, false, true, 0.25f, 0f, 1.5f, 2, true, 0.75f, 0.5f, P(c).Goal, P(c).Pos);
        public bool Hold(int c, HoldOwner owner) => IsPlayer(c) ? Valid(c) : Holds.Hold(this, c, owner);
        public void ReleaseHold(int c, HoldOwner owner) => Holds.Release(this, c, owner);
        public byte MoveSpeed(int c) => P(c).Speed;
        public void RestoreMoveSpeed(int c, byte speed) { Order.Add("speed " + c); P(c).Speed = speed; }
        public void Run(int c) { Runs.Add(c); P(c).Speed = 2; }
        public void MoveTo(int c, Vector3 point) { Moves.Add((c, point)); P(c).Dest = point; }
        public PlacedBuilding? Resolve(ArenaBuildingRef held)
        {
            if (held.Key == "arena") return new PlacedBuilding(ArenaB, Scratch);
            foreach (var b in Buildings) if (b.Building.Key == held.Key) return b;   // a hospital (task 8.4b: the medics' return point)
            return null;
        }
        public IReadOnlyList<PlacedBuilding>? BuildingsInSphere(Vector3 origin, float radius, int max) =>
            [.. Buildings.Where(b => Vector3.Distance(b.Building.Position, origin) <= radius).Take(max)];
        public IReadOnlyList<string> BedsOf(ArenaBuilding building) => BedLists.TryGetValue(building.Key, out var l) ? l : [];
        public AftercareBed<int>? Bed(string id) => BedTable.TryGetValue(id, out var b) ? b : null;

        // IHoldEngine
        bool IHoldEngine<int>.Hold(int id) { if (!Valid(id)) return false; Engaged.Add(id); return true; }
        public bool Reassert(int id) { if (!Valid(id)) return false; Engaged.Add(id); return true; }
        public void Release(int id) { Order.Add("release " + id); Engaged.Remove(id); }
    }

    /// <summary>Medics 30-33 near the hospital; fighters 10, 11 (NPCs) in the arena; the hospital in Scratch with four beds: two normal, one
    /// skeleton, one of another function.</summary>
    static (TownAftercareLogic<int> L, Fake F) World(int medics = 4)
    {
        var f = new Fake();
        for (int i = 0; i < medics; ++i) f.Add(30 + i, new Vector3(1000 + i, 50, 2400));
        f.Add(10, ArenaPos, "Ryn");
        f.Add(11, ArenaPos, "Tok");
        f.AddHospital("hosp", Scratch, new Vector3(1000, 70, 2600), ("hosp@bed0", 6), ("hosp@bed1", 6), ("hosp@bed2", 25), ("hosp@bed3", 3));
        return (new TownAftercareLogic<int>(-1, controlAvailable: true), f);
    }

    static readonly Vector3 Standby = ArenaPos + new Vector3(0, 0, 190);   // toward the registry at +z

    static void Begin(TownAftercareLogic<int> l, Fake f, int[] doctors, int[] fighters) =>
        l.Begin(f, doctors, fighters, ArenaB.Ref, Standby);

    // ---------- the reservation tables ----------

    [Fact]
    public void Bench_is_refused_for_a_medic_and_external_for_a_medic_or_a_bench_fighter()
    {
        var (l, f) = World();
        Begin(l, f, [30, 31], [10, 11]);   // the first Standby directs both medics
        Assert.True(l.MedicReserved(30));
        Assert.False(l.ReserveBenchOrders(f, 30));
        Assert.False(l.ReserveExternalOrders(f, 30));
        Assert.True(l.ReserveBenchOrders(f, 10));
        Assert.True(l.ReserveBenchOrders(f, 10));   // already reserved: Add answers true (TownMedicAIPolicy.h:72)
        Assert.False(l.ReserveExternalOrders(f, 10));
        l.ReleaseBenchOrders(f, 10);
        Assert.True(l.ReserveExternalOrders(f, 10));
        Assert.False(l.ReserveBenchOrders(f, 10));   // now external
    }

    [Fact]
    public void External_table_holds_four_and_nothing_when_control_is_unavailable()
    {
        var (l, f) = World();
        for (int i = 50; i < 55; ++i) f.Add(i, Vector3.Zero);
        Assert.True(l.ReserveExternalOrders(f, 50));
        Assert.True(l.ReserveExternalOrders(f, 51));
        Assert.True(l.ReserveExternalOrders(f, 52));
        Assert.True(l.ReserveExternalOrders(f, 53));
        Assert.False(l.ReserveExternalOrders(f, 54));
        Assert.False(l.ReserveExternalOrders(f, -1));   // the C++ NULL receiver
        var off = new TownAftercareLogic<int>(-1, controlAvailable: false);
        Assert.False(off.ControlAvailable());
        Assert.False(off.ReserveBenchOrders(f, 50));
        Assert.False(off.ReserveExternalOrders(f, 50));
    }

    [Fact]
    public void Bench_and_external_survive_Release_and_are_cleared_by_the_world_teardown()
    {
        var (l, f) = World();
        Assert.True(l.ReserveBenchOrders(f, 10));
        Assert.True(l.ReserveExternalOrders(f, 11));
        Begin(l, f, [30], [10, 11]);
        l.Release(f);
        Assert.True(l.BenchReserved(10));
        Assert.True(l.ExternalReserved(11));
        Assert.False(l.MedicReserved(30));
        l.AbandonWorldState();
        Assert.False(l.BenchReserved(10));
        Assert.False(l.ExternalReserved(11));
    }

    // ---------- Begin ----------

    [Fact]
    public void Begin_reserves_up_to_min_fighters_ten_medics_and_registers_every_fighter_with_the_cpp_lines()
    {
        var (l, f) = World(medics: 4);
        f.Add(0, ArenaPos, "Hero", player: true);
        Begin(l, f, [30, 31, 32, 33], [10, 0, 11]);
        Assert.Equal([30, 31, 32], l.Medics.ToArray().Select(m => m.Actor));
        Assert.Equal(3, l.Patients.Count);
        Assert.Equal(42u, l.CareId);
        Assert.Equal(new List<string>
        {
            "Proving Grounds: aftercare controller=native-v2; native medical scores, independent patients, no direct pickup fallback",
            "Proving Grounds: medic reserved slot=1 actor=c:30",
            "Proving Grounds: medic reserved slot=2 actor=c:31",
            "Proving Grounds: medic reserved slot=3 actor=c:32",
            "Proving Grounds: aftercare fighter registered slot=1 name=Ryn actor=c:10 player=0 valid=1",
            "Proving Grounds: aftercare fighter registered slot=2 name=Hero actor=c:0 player=1 valid=1",
            "Proving Grounds: aftercare fighter registered slot=3 name=Tok actor=c:11 player=0 valid=1",
            "Proving Grounds: medical team - 3 medics, 3 hospital beds",
            "Proving Grounds: aftercare bed adopt care=42 occupied=0 owned=0 tracked=0",
        }, f.Debugs);
    }

    [Fact]
    public void A_doctor_list_slot_number_is_the_doctor_index_and_an_invalid_fighter_is_named_invalid()
    {
        var (l, f) = World();
        f.People[11].Valid = false;
        Begin(l, f, [30, 31], [10, 11]);
        Assert.Contains("Proving Grounds: aftercare fighter registered slot=2 name=invalid actor=c:11 player=0 valid=0", f.Debugs);
    }

    [Fact]
    public void Begin_takes_normal_and_skeleton_beds_of_the_arena_towns_hospital_only()
    {
        var (l, f) = World();
        f.AddHospital("far", Scratch + 1, new Vector3(1000, 70, 2700), ("far@bed0", 6));     // another town's hospital
        f.AddHospital("out", Scratch, new Vector3(9000, 70, 2600), ("out@bed0", 6));        // beyond 2,500
        Begin(l, f, [30], [10]);
        Assert.Equal(["hosp@bed0", "hosp@bed1", "hosp@bed2"], l.Beds);
        Assert.Equal("hosp", l.ReturnHospital?.Key);
        Assert.True(TownAftercareLogic<int>.Compatible(l.HospitalBed(f, "hosp@bed2"), robot: true));
        Assert.False(TownAftercareLogic<int>.Compatible(l.HospitalBed(f, "hosp@bed2"), robot: false));
        Assert.True(TownAftercareLogic<int>.Compatible(l.HospitalBed(f, "hosp@bed0"), robot: false));
        Assert.Null(l.HospitalBed(f, "hosp@bed3"));
    }

    [Fact]
    public void Begin_adopts_npcs_lying_in_the_beds_but_not_players_or_occupants_not_lying_there()
    {
        var (l, f) = World();
        f.Add(70, new Vector3(1000, 70, 2600));
        f.Add(71, new Vector3(1000, 70, 2600), player: true);
        f.Add(72, new Vector3(1000, 70, 2600));
        f.BedTable["hosp@bed0"] = new AftercareBed<int>("hosp@bed0", Hospital, 6, 70, true, true);
        f.BedTable["hosp@bed1"] = new AftercareBed<int>("hosp@bed1", Hospital, 6, 71, true, true);
        f.BedTable["hosp@bed2"] = new AftercareBed<int>("hosp@bed2", Hospital, 25, 72, true, false);
        Begin(l, f, [30], [10]);
        Assert.Contains("Proving Grounds: aftercare bed adopt care=42 occupied=3 owned=1 tracked=1", f.Debugs);
        Assert.Equal(70, l.BedOccupants[0].Actor);
        Assert.Equal("hosp@bed0", l.BedOccupants[0].Bed);
    }

    [Fact]
    public void The_bed_registry_survives_Release_and_a_new_Begin_and_is_dropped_by_the_teardown()
    {
        var (l, f) = World();
        f.Add(70, new Vector3(1000, 70, 2600));
        f.BedTable["hosp@bed0"] = new AftercareBed<int>("hosp@bed0", Hospital, 6, 70, true, true);
        Begin(l, f, [30], [10]);
        l.Release(f);
        Assert.Equal(1, l.BedOccupants.Length);
        Begin(l, f, [30], [10]);   // adopted again: the same entry, not a second
        Assert.Contains("Proving Grounds: aftercare bed adopt care=43 occupied=1 owned=1 tracked=1", f.Debugs);
        l.AbandonWorldState();
        Assert.Equal(0, l.BedOccupants.Length);
    }

    // ---------- Standby / StageMedic ----------

    [Fact]
    public void Standby_walks_the_medics_onto_a_line_sixteen_apart_sideways_to_the_standby_direction_and_holds_them()
    {
        var (l, f) = World();
        Begin(l, f, [30, 31, 32], [10, 11, 10]);   // three patients -> three medics
        // standby direction +z -> side (-dz, 0, dx) = (-1, 0, 0); slots -16, 0, +16 along it
        Assert.Equal(new Vector3(-1, 0, 0), l.StandbySide);
        Assert.Equal((30, Standby + new Vector3(16, 0, 0)), f.Moves[0]);
        Assert.Equal((31, Standby), f.Moves[1]);
        Assert.Equal((32, Standby + new Vector3(-16, 0, 0)), f.Moves[2]);
        Assert.Equal([30, 31, 32], f.Runs);
        foreach (int m in new[] { 30, 31, 32 })
        {
            Assert.True(f.Holds.IsHeldBy(m, HoldOwner.AftercareMedic));
            Assert.Contains(m, f.Engaged);
        }
        Assert.All(l.Medics.ToArray(), m => Assert.Equal(TownAftercarePolicy.Action.Gather, m.Command.Action));
    }

    [Fact]
    public void An_arrived_medic_holds_its_own_spot_with_hysteresis_and_stops_running()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10]);
        f.Moves.Clear(); f.Runs.Clear();
        f.People[30].Pos = Standby + new Vector3(5, 0, 0);   // within 8
        f.People[30].Dest = null;                             // stopped
        f.Now += 1000;
        l.Standby(f);
        Assert.Equal([(30, Standby + new Vector3(5, 0, 0))], f.Moves);   // the hold order: its own position
        Assert.Empty(f.Runs);
        Assert.Equal(TownAftercarePolicy.Action.Hold, l.Medics[0].Command.Action);
        f.Moves.Clear();
        f.People[30].Pos = Standby + new Vector3(15, 0, 0);   // drifted, but within the 18 hold band
        f.Now += 1000;
        l.Standby(f);
        Assert.Empty(f.Moves);
        Assert.Empty(f.Runs);
        f.People[30].Pos = Standby + new Vector3(20, 0, 0);   // outside 18: gather again
        f.Now += 1000;
        l.Standby(f);
        Assert.Equal([(30, Standby)], f.Moves);
        Assert.Equal([30], f.Runs);
    }

    [Fact]
    public void A_lost_standby_order_is_reissued_on_the_one_second_check_and_logged_at_most_every_thirty_seconds()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10]);
        f.Moves.Clear();
        f.People[30].Dest = Standby + new Vector3(0, 0, 30);   // walking elsewhere (30 > 18)
        f.Now += 500;
        l.Standby(f);   // elapsed 0.5: the first recall check is due (LastRecall starts at -1000)
        Assert.Single(f.Moves);
        Assert.Single(f.Debugs, d => d.StartsWith("Proving Grounds: medic repeatedly recalled slot=1 destinationError=30.0"));
        f.Moves.Clear();
        f.People[30].Dest = Standby + new Vector3(0, 0, 30);
        f.Now += 500;
        l.Standby(f);   // 0.5 s after the recall: no check
        Assert.Empty(f.Moves);
        f.Now += 600;
        l.Standby(f);   // 1.1 s: re-issued, not logged again
        Assert.Single(f.Moves);
        Assert.Single(f.Debugs, d => d.StartsWith("Proving Grounds: medic repeatedly recalled"));
    }

    [Fact]
    public void Standby_caps_a_long_gap_at_one_second()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10]);
        f.Moves.Clear();
        f.People[30].Dest = Standby + new Vector3(0, 0, 30);
        f.Now += 1000;
        l.Standby(f);   // the first recall at elapsed 1
        Assert.Single(f.Moves);
        f.Moves.Clear();
        f.People[30].Dest = Standby + new Vector3(0, 0, 30);
        f.Now += 60_000;   // a suspended minute counts as 1 s: elapsed 2, a check is due again
        l.Standby(f);
        Assert.Single(f.Moves);
    }

    [Fact]
    public void An_unavailable_medic_is_released_and_taken_again_once_available()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10]);
        f.Debugs.Clear();
        f.People[30].Combat = true;
        f.Now += 1000;
        l.Standby(f);
        Assert.False(f.Holds.IsHeld(30));
        Assert.False(l.MedicReserved(30));
        Assert.Equal(["Proving Grounds: medic returned to town AI actor=c:30"], f.Debugs);   // in combat: no hand-off line
        f.People[30].Combat = false;
        f.Now += 1000;
        l.Standby(f);
        Assert.True(f.Holds.IsHeldBy(30, HoldOwner.AftercareMedic));
        Assert.True(l.MedicReserved(30));
    }

    [Fact]
    public void A_medic_carrying_someone_is_not_staged()
    {
        var (l, f) = World();
        f.People[30].Carrying = true;
        Begin(l, f, [30], [10]);
        Assert.Empty(f.Moves);
        Assert.False(f.Holds.IsHeld(30));
    }

    // ---------- Release, IsManaged, MarkWinner ----------

    [Fact]
    public void Release_gives_the_medics_back_with_their_speed_and_the_cpp_lines()
    {
        var (l, f) = World();
        f.People[30].Speed = 1;
        f.People[30].Goal = true;
        Begin(l, f, [30], [10]);
        Assert.Equal(2, f.People[30].Speed);   // running to the line
        f.Debugs.Clear();
        l.Release(f);
        Assert.Equal(1, f.People[30].Speed);
        Assert.False(f.Holds.IsHeld(30));
        Assert.DoesNotContain(30, f.Engaged);
        Assert.Equal(new List<string>
        {
            "Proving Grounds: medic native handoff actor=c:30 goalSelected=1 scheduled=1",
            "Proving Grounds: medic returned to town AI actor=c:30",
            "Proving Grounds: aftercare fighter released name=Ryn actor=c:10 npcOwned=0 nativeRestarted=0 dead=0 ko=0 combat=0 carried=0 inBed=0 " +
                "hold=0 passive=1 aidFlesh=0.250 aidRepair=0.000 bleed=1.500 wounds=2 blood=0.750 lowestLimb=0.500 recoveryRead=1 nativeGoal=0 " +
                "position=(1000.0,50.0,2000.0)",
        }, f.Debugs);
        Assert.Equal(["speed 30", "release 30"], f.Order);   // the speed first, then the hold (TownAftercare.cpp:205-208)
        Assert.Equal(0, l.Medics.Length);
        Assert.Empty(l.Patients);
        Assert.Empty(l.Beds);
        Assert.Null(l.ArenaSite);
    }

    [Fact]
    public void A_medic_another_owner_also_holds_stays_held_after_Release()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10]);
        f.Holds.Hold(f, 30, HoldOwner.Spectator);
        l.Release(f);
        Assert.Equal(HoldOwner.Spectator, f.Holds.OwnersOf(30));
        Assert.Contains(30, f.Engaged);
    }

    [Fact]
    public void IsManaged_covers_medics_and_patients_not_yet_done_until_Release()
    {
        var (l, f) = World();
        f.Add(99, Vector3.Zero);
        Begin(l, f, [30], [10, 11]);
        Assert.True(l.IsManaged(f, 30));
        Assert.True(l.IsManaged(f, 10));
        Assert.True(l.IsManaged(f, 11));
        Assert.False(l.IsManaged(f, 99));
        Assert.False(l.IsManaged(f, -1));
        l.Release(f);
        Assert.False(l.IsManaged(f, 30));
        Assert.False(l.IsManaged(f, 10));
    }

    [Fact]
    public void MarkWinner_marks_the_patient_with_the_cpp_line()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10, 11]);
        f.Debugs.Clear();
        l.MarkWinner(f, 11);
        Assert.True(l.Patients[1].Winner);
        Assert.False(l.Patients[0].Winner);
        Assert.Equal(["Proving Grounds: aftercare fighter marked winner name=Tok actor=c:11"], f.Debugs);
    }

    [Fact]
    public void Release_writes_the_released_line_for_every_registered_patient_directed_or_not_once()
    {
        var (l, f) = World();
        f.People[11].Valid = false;
        Begin(l, f, [30], [10, 11]);
        f.Debugs.Clear();
        l.Release(f);
        Assert.Single(f.Debugs, d => d.StartsWith("Proving Grounds: aftercare fighter released name=Ryn actor=c:10 npcOwned=0 nativeRestarted=0"));
        Assert.Single(f.Debugs, d => d == "Proving Grounds: aftercare fighter released actor=c:11 valid=0");
        f.Debugs.Clear();
        l.Release(f);   // the patients are gone after the first Release: nothing more
        Assert.DoesNotContain(f.Debugs, d => d.Contains("aftercare fighter released"));
    }

    [Fact]
    public void A_medic_never_directed_has_no_receiver_to_release()
    {
        var (l, f) = World();
        f.People[30].Combat = true;   // never available: never directed
        Begin(l, f, [30], [10]);
        Assert.False(l.MedicReserved(30));
        f.Order.Clear();
        l.Release(f);
        Assert.Empty(f.Order);
        Assert.DoesNotContain(f.Debugs, d => d.StartsWith("Proving Grounds: medic returned"));
    }

    [Fact]
    public void DropPatient_drops_only_the_medics_own_patient()
    {
        var (l, f) = World();
        Begin(l, f, [30], [10]);
        var m = l.Medics[0];
        m.Patient = 10;
        f.People[30].CarryingId = 10;
        TownAftercareLogic<int>.DropPatientForTest(f, ref m);
        Assert.Equal(["drop 30"], f.Order);
        f.Order.Clear();
        f.People[30].CarryingId = 11;   // someone else
        TownAftercareLogic<int>.DropPatientForTest(f, ref m);
        Assert.Empty(f.Order);
        f.People[30].Player = true;     // a player character is never made to drop (C++ !Player(c))
        f.People[30].CarryingId = 10;
        TownAftercareLogic<int>.DropPatientForTest(f, ref m);
        Assert.Empty(f.Order);
    }
}
