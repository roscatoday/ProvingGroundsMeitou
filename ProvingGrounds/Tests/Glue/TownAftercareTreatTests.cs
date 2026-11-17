using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.4b (TownAftercare.cpp:219-319, :409-1033, :1293-1401): the treatment and transport over the Fake of
/// <see cref="TownAftercareLogicTests"/>, extended here with the reads and orders Treat uses. The world: medics 30.. near the hospital, fighters 10
/// (Ryn) and 11 (Tok) in the arena, the hospital's beds hosp@bed0, @bed1 (normal), @bed2 (skeleton), @bed3 (another function).</summary>
public partial class TownAftercareLogicTests
{
    sealed partial class Who
    {
        public bool Robot, Crippled, Kit = true, RobotKit, HoldOrder, PassiveOrder;
        public string? InBedId;
        public float Flesh, Repair, Bleed, Blood = 1, Health = 1;
        public string Record = "";
    }

    sealed partial class Fake
    {
        public bool Teams1v1;
        public readonly HashSet<int> Eliminated = [];
        public bool TreatOk = true;
        public bool SparTeams1v1Active => Teams1v1;
        public bool SparIsEliminated(int c) => Eliminated.Contains(c);
        public string RecordId(int c) => P(c).Record;
        public IReadOnlyList<int>? CharactersInSphere(Vector3 origin, float radius, int max) =>
            [.. People.Where(kv => kv.Value.Valid && Vector3.Distance(kv.Value.Pos, origin) <= radius).Select(kv => kv.Key).Take(max)];
        public bool InBed(int c) => P(c).InBedId is not null;
        public bool LiesIn(int c, string bed) => P(c).InBedId == bed;
        public bool IsCrippled(int c) => P(c).Crippled;
        public bool IsRobot(int c) => P(c).Robot;
        public bool HasMedical(int c) => true;
        public float AidScore(int c, bool robot) => robot ? P(c).Repair : P(c).Flesh;
        public float ActionableAidNeed(int c, bool robot) => robot ? P(c).Repair : P(c).Flesh;
        public float BleedRate(int c) => P(c).Bleed;
        public uint WoundCount(int c) => 0;
        public float BloodRatio(int c) => P(c).Blood;
        public IReadOnlyList<AftercarePart> Parts(int c) => [new AftercarePart(false, false, P(c).Health * 100f, 0, 0, 100)];
        public bool RosterRead(int c, out RosterStatus.Snapshot recovery)
        {
            recovery = new RosterStatus.Snapshot { Blood = P(c).Blood, LowestLimb = P(c).Health, Recovery = MathF.Min(P(c).Blood, P(c).Health) };
            recovery.Band = RosterStatusPolicy.BandFor(recovery.Recovery);   // task 8.4c: the bed recovery reads the band
            return true;
        }
        public bool HasAidKit(int c, bool robot) => robot ? P(c).RobotKit : P(c).Kit;
        public (bool Hold, bool Passive) StandingOrders(int c) => (P(c).HoldOrder, P(c).PassiveOrder);
        public void SetStandingOrders(int c, bool hold, bool passive) { P(c).HoldOrder = hold; P(c).PassiveOrder = passive; }
        public bool StartTreat(int medic, int patient, bool robot) { Order.Add((robot ? "repair " : "treat ") + medic + " " + patient); return TreatOk; }
        public bool BeginLift(int medic, int patient) { Order.Add("lift " + medic + " " + patient); return true; }
        public bool PutInBed(int medic, string bed) { Order.Add("putin " + medic + " " + bed); return true; }
        public bool UseBed(int patient, string bed) { Order.Add("usebed " + patient + " " + bed); return true; }
        public void ClearOrders(int c) => Order.Add("clear " + c);

        /// <summary>Puts <paramref name="bed"/> at <paramref name="pos"/> (and an occupant lying there or not).</summary>
        public void SetBed(string bed, Vector3 pos, int occupant = -1, bool lying = false)
        {
            var b = BedTable[bed];
            BedTable[bed] = b with { Position = pos, Occupant = occupant, Occupied = occupant >= 0, OccupantInBed = lying };
            if (occupant >= 0 && lying) P(occupant).InBedId = bed;
        }
    }

    static readonly Vector3 Bed0 = new(1000, 70, 2600), Bed1 = new(1010, 70, 2600), Bed2 = new(1020, 90, 2600);

    /// <summary>A post-fight world: medic 30 at the arena, fighter 10 KO'd at the arena, fighter 11 (the winner) standing healthy; the beds placed.</summary>
    static (TownAftercareLogic<int> L, Fake F) AfterFight()
    {
        var (l, f) = World(1);
        f.People[30].Pos = ArenaPos + new Vector3(50, 0, 0);
        f.SetBed("hosp@bed0", Bed0); f.SetBed("hosp@bed1", Bed1); f.SetBed("hosp@bed2", Bed2); f.SetBed("hosp@bed3", Bed0);
        Begin(l, f, [30], [10, 11]);
        l.MarkWinner(f, 11);
        f.People[10].Ko = true;
        f.Debugs.Clear(); f.Order.Clear(); f.Moves.Clear();
        return (l, f);
    }

    static bool Has(Fake f, string start) => f.Debugs.Any(d => d.StartsWith(start, StringComparison.Ordinal));

    // ---------- Bandage, then the carry into the coordinator's bed ----------

    [Fact]
    public void A_wounded_KO_patient_is_bandaged_first_with_the_native_order_line_and_the_Treat_task()
    {
        var (l, f) = AfterFight();
        f.People[10].Flesh = 5; f.People[10].Bleed = 1;
        Assert.True(l.Treat(f, 0, eliminatedOnly: false));
        Assert.Contains("treat 30 10", f.Order);
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=none action=bandage task=25 executor=medic trigger=transition elapsed=0.0"));
        Assert.True(Has(f, "Proving Grounds: aftercare assignment care=" + l.CareId + " slot=1 patient=c:10 bed=none from=wait to=bandage elapsed=0.0"));
        Assert.Equal("Aftercare: 1 awaiting treatment; 0 awaiting hospital transfer", l.GetStatus());
        Assert.EndsWith("bandage ok", l.Medics[0].LastOrder);
        Assert.False(l.PatientReserved(10));   // a KO patient is never directed (DirectPatient :271)
    }

    [Fact]
    public void After_treatment_the_medic_runs_to_the_patient_lifts_it_and_puts_it_in_the_nearest_normal_bed_then_care_completes()
    {
        var (l, f) = AfterFight();
        // far: approach
        Assert.True(l.Treat(f, 0, false));
        Assert.Contains(f.Moves, m => m.Id == 30 && m.To == ArenaPos);
        Assert.Contains(30, f.Runs);
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=hosp@bed0 action=approach-pickup task=29"));
        // near: pick up
        f.People[30].Pos = ArenaPos + new Vector3(5, 0, 0);
        f.Debugs.Clear();
        Assert.True(l.Treat(f, 1, false));
        Assert.Contains("lift 30 10", f.Order);
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=hosp@bed0 action=pickup task=225"));
        // carrying: deliver to the coordinator's bed (bed0, the nearest normal one)
        f.People[30].Carrying = true; f.People[30].CarryingId = 10; f.People[10].Carried = true;
        Assert.True(l.Treat(f, 2, false));
        Assert.Contains("putin 30 hosp@bed0", f.Order);
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=hosp@bed0 action=deliver task=99"));
        // lies in the bed: the poll sees the entry, the bed registry takes it, the care completes
        f.People[30].Carrying = false; f.People[30].CarryingId = -1; f.People[10].Carried = false;
        f.SetBed("hosp@bed0", Bed0, 10, lying: true);
        f.People[30].Pos = Bed0;
        f.Debugs.Clear();
        Assert.False(l.Treat(f, 3, false));
        Assert.Equal(TownAftercareLogic<int>.CareCompleteText, l.GetStatus());
        Assert.True(Has(f, "Proving Grounds: aftercare outcome care=" + l.CareId + " elapsed=3.0 patient=c:10 outcome=delivered reason=observed-patient-state"));
        Assert.Equal(10, l.BedOccupants[0].Actor);
        Assert.Equal("hosp@bed0", l.BedOccupants[0].Bed);
        // the winner (healthy, awake) went free as Treated, never directed
        Assert.False(l.PatientReserved(11));
    }

    [Fact]
    public void A_robot_patient_is_given_the_skeleton_bed_and_a_human_never()
    {
        var (l, f) = AfterFight();
        f.People[10].Robot = true;
        f.People[30].Pos = ArenaPos;
        f.People[30].Carrying = true; f.People[30].CarryingId = 10; f.People[10].Carried = true;
        Assert.True(l.Treat(f, 0, false));
        Assert.Contains("putin 30 hosp@bed2", f.Order);
    }

    [Fact]
    public void A_patient_that_can_walk_is_held_run_and_sent_to_lie_in_its_bed_and_given_back_when_done()
    {
        var (l, f) = AfterFight();
        f.People[10].Ko = false; f.People[10].Health = 0.5f;   // a loser needing recovery, awake: walks
        f.People[10].PassiveOrder = false;
        Assert.True(l.Treat(f, 0, false));
        Assert.Contains("usebed 10 hosp@bed0", f.Order);
        Assert.True(Has(f, "Proving Grounds: aftercare fighter assigned name=Ryn actor=c:10"));
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=hosp@bed0 action=walk-bed task=258 executor=patient"));
        Assert.True(l.PatientReserved(10));
        Assert.Contains(10, f.Engaged);
        Assert.True(f.Holds.IsHeldBy(10, HoldOwner.AftercarePatient));
        Assert.Contains(10, f.Runs);
        Assert.True(f.People[10].PassiveOrder);
        // in the bed: delivered, given back (standing orders and speed back, the hold released)
        f.SetBed("hosp@bed0", Bed0, 10, lying: true);
        f.Debugs.Clear();
        Assert.False(l.Treat(f, 1, false));
        Assert.False(l.PatientReserved(10));
        Assert.False(f.Holds.IsHeldBy(10, HoldOwner.AftercarePatient));
        Assert.False(f.People[10].PassiveOrder);
        Assert.True(Has(f, "Proving Grounds: aftercare fighter released name=Ryn actor=c:10 npcOwned=1 nativeRestarted=0"));   // in bed: no restart
    }

    [Fact]
    public void A_walking_patient_released_before_it_lies_down_has_its_bed_walk_stopped_and_one_in_bed_keeps_lying()
    {
        var (l, f) = AfterFight();
        f.People[10].Ko = false; f.People[10].Health = 0.5f;
        l.Treat(f, 0, false);
        Assert.Contains("usebed 10 hosp@bed0", f.Order);
        // handed back mid-walk (here by Release): orders cleared (the walk stopped) before the hold goes back, the restart logged
        f.Debugs.Clear(); f.Order.Clear();
        l.Release(f);
        int clear = f.Order.IndexOf("clear 10"), release = f.Order.IndexOf("release 10");
        Assert.True(clear >= 0 && release > clear, string.Join(", ", f.Order));
        Assert.True(Has(f, "Proving Grounds: aftercare fighter released name=Ryn actor=c:10 npcOwned=1 nativeRestarted=1"));
        // in a bed: no clearing ("do not cancel bed occupancy"), the hold still given back
        var (l2, f2) = AfterFight();
        f2.People[10].Ko = false; f2.People[10].Health = 0.5f;
        l2.Treat(f2, 0, false);
        f2.People[10].InBedId = "hosp@bed0";
        f2.Order.Clear();
        l2.Release(f2);
        Assert.DoesNotContain("clear 10", f2.Order);
        Assert.Contains("release 10", f2.Order);
    }

    [Fact]
    public void A_patient_whose_hold_cannot_be_taken_fails_the_claim_with_the_ownership_line()
    {
        var (l, f) = AfterFight();
        f.People[10].Ko = false; f.People[10].Health = 0.5f;
        // the hold engine refuses: the registry's Hold answers false (a stale or refused hold)
        var (l2, f2) = (l, new RefusingHolds(f));
        Assert.True(l2.Treat(f2, 0, false));
        Assert.True(Has(f, "Proving Grounds: aftercare fighter ownership failed name=Ryn actor=c:10 receiver=c:10"));
        Assert.False(l.PatientReserved(10));
    }

    /// <summary>The Fake with every hold of the patient owner refused (the registry's Hold answering false).</summary>
    sealed class RefusingHolds(Fake inner) : DelegatingHost(inner)
    {
        public override bool Hold(int c, HoldOwner owner) => owner != HoldOwner.AftercarePatient && base.Hold(c, owner);
    }

    // ---------- the bounds: the 300 s cap, the 45 s bed wait, two stalled retries ----------

    [Fact]
    public void After_300_seconds_the_care_times_out_and_hands_everyone_back()
    {
        var (l, f) = AfterFight();
        Assert.True(l.Treat(f, 10, false));   // the post-fight clock starts here
        f.Debugs.Clear();
        Assert.True(l.Treat(f, 309, false));
        Assert.False(l.Treat(f, 310, false));
        Assert.Equal("Medical care timed out; remaining care returned to town AI", l.GetStatus());
        Assert.True(Has(f, "Proving Grounds: aftercare outcome care=" + l.CareId + " elapsed=310.0 patient=c:10 outcome=unresolved-handoff reason=bout-time-limit"));
        Assert.True(Has(f, "Proving Grounds: aftercare summary care=" + l.CareId + " phase=care elapsed=310.0 outcome=timeout-unresolved-handoff"));
    }

    [Fact]
    public void With_no_free_compatible_bed_the_medic_waits_and_after_45_seconds_the_patient_is_handed_back()
    {
        var (l, f) = AfterFight();
        f.People.Add(50, new Who { Pos = Bed0, Name = "Sleeper" });
        f.People.Add(51, new Who { Pos = Bed1, Name = "Sleeper2" });
        f.SetBed("hosp@bed0", Bed0, 50, lying: true); f.SetBed("hosp@bed1", Bed1, 51, lying: true);
        Assert.True(l.Treat(f, 0, false));
        Assert.Equal("Aftercare: 0 awaiting treatment; 1 awaiting hospital transfer; waiting for a free compatible bed", l.GetStatus());
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=none action=wait task=29"));
        for (int t = 1; t <= 44; ++t) l.Treat(f, t, false);
        f.Debugs.Clear();
        l.Treat(f, 45, false);
        Assert.True(Has(f, "Proving Grounds: aftercare outcome care=" + l.CareId + " elapsed=45.0 patient=c:10 outcome=unresolved-handoff reason=bed-wait-expired"));
    }

    [Fact]
    public void A_pickup_with_no_progress_is_retried_after_10_seconds_and_fails_after_two_stalled_retries()
    {
        var (l, f) = AfterFight();
        f.People[30].Pos = ArenaPos + new Vector3(5, 0, 0);   // in reach: pickup, but the lift never happens
        l.Treat(f, 0, false);
        Assert.Single(f.Order, o => o == "lift 30 10");
        for (int t = 1; t <= 10; ++t) l.Treat(f, t, false);
        Assert.Equal(2, f.Order.Count(o => o == "lift 30 10"));   // the stalled retry at 10 s
        Assert.True(Has(f, "Proving Grounds: aftercare native order care=" + l.CareId + " slot=1 medic=c:30 patient=c:10 bed=hosp@bed0 action=pickup task=225 executor=medic trigger=stalled-retry elapsed=10.0"));
        Assert.True(Has(f, "Proving Grounds: aftercare stall snapshot care=" + l.CareId + " patient=c:10 action=pickup"));
        f.Debugs.Clear();
        for (int t = 11; t <= 20; ++t) l.Treat(f, t, false);
        Assert.True(Has(f, "Proving Grounds: aftercare action failed care=" + l.CareId + " slot=1 patient=c:10 action=pickup retries=2 progressAge=10.0"));
        Assert.True(Has(f, "Proving Grounds: medic returned to town AI actor=c:30"));
    }

    [Fact]
    public void A_spare_medic_heads_back_to_the_hospital_and_is_released_while_care_goes_on()
    {
        var (l, f) = World(2);
        f.People[30].Pos = ArenaPos + new Vector3(50, 0, 0);
        f.People[31].Pos = ArenaPos + new Vector3(60, 0, 0);
        f.SetBed("hosp@bed0", Bed0); f.SetBed("hosp@bed1", Bed1); f.SetBed("hosp@bed2", Bed2); f.SetBed("hosp@bed3", Bed0);
        Begin(l, f, [30, 31], [10, 11]);
        l.MarkWinner(f, 11);
        f.People[10].Ko = true;
        f.Debugs.Clear();
        Assert.True(l.Treat(f, 0, false));
        Assert.True(Has(f, "Proving Grounds: aftercare hospital return care=" + l.CareId + " slot=2 actor=c:31 destination=(1000.0,70.0,2600.0)"));
        Assert.False(l.Medics[1].Directed);
        Assert.DoesNotContain(31, f.Engaged);
        Assert.Contains((31, Bed0), f.Moves);   // the move to the hospital point (the first bed's position)
    }

    /// <summary>The Fake with one read throwing on every call.</summary>
    sealed class ThrowingAid(Fake inner) : DelegatingHost(inner)
    {
        public override bool HasAidKit(int c, bool robot) => throw new InvalidOperationException("kit read failed");
    }

    [Fact]
    public void A_throw_every_poll_is_logged_once_and_answers_care_complete_so_the_event_finishes_and_Release_gives_every_hold_back()
    {
        var (l, f) = AfterFight();
        var t = new ThrowingAid(f);
        Assert.Contains(30, f.Engaged);   // held by the standby
        Assert.False(l.Treat(t, 0, false));
        Assert.False(l.Treat(t, 1, false));
        Assert.Single(f.Debugs, d => d.StartsWith("Proving Grounds: aftercare treatment failed care=" + l.CareId + " error=InvalidOperationException: kit read failed", StringComparison.Ordinal));
        l.Release(f);   // TownArena's Finish (the event ends on the false answer)
        Assert.DoesNotContain(30, f.Engaged);
        Assert.Empty(l.Medics.ToArray());
    }

    /// <summary>The Fake with every medic hold refused.</summary>
    sealed class RefusingMedicHolds(Fake inner) : DelegatingHost(inner)
    {
        public override bool Hold(int c, HoldOwner owner) => owner != HoldOwner.AftercareMedic && base.Hold(c, owner);
    }

    [Fact]
    public void A_medic_whose_hold_cannot_be_taken_is_not_reserved_nor_moved_and_logged_once()
    {
        var (l, f) = World(1);
        var r = new RefusingMedicHolds(f);
        l.Begin(r, [30], [10, 11], ArenaB.Ref, Standby);   // the first Standby tries to direct it
        Assert.False(l.MedicReserved(30));
        Assert.False(l.Medics[0].Directed);
        Assert.DoesNotContain(f.Moves, m => m.Id == 30);
        l.Standby(r);
        Assert.Single(f.Debugs, d => d == "Proving Grounds: aftercare medic ownership failed actor=c:30");
    }

    // ---------- Teams 1v1 early care, the final sweep ----------

    [Fact]
    public void In_Teams_1v1_only_eliminated_fighters_are_cared_for_while_the_next_duel_runs()
    {
        var (l, f) = AfterFight();
        f.Teams1v1 = true;
        f.Eliminated.Add(10);
        f.People[10].Flesh = 3;
        Assert.True(l.Treat(f, 0, eliminatedOnly: true));
        Assert.True(Has(f, "Proving Grounds: Teams 1v1 early care admitted patient=c:10"));
        Assert.Contains("treat 30 10", f.Order);
        Assert.Equal("Aftercare: 1 awaiting treatment; 0 awaiting hospital transfer", l.GetStatus());
        // not Teams 1v1 any more: eliminatedOnly is ignored (a full care pass)
        f.Teams1v1 = false;
        Assert.True(l.Treat(f, 1, eliminatedOnly: true));
    }

    [Fact]
    public void A_completed_care_sweeps_175_around_the_arena_for_other_unconscious_fighters()
    {
        var (l, f) = AfterFight();
        f.People[10].Ko = false;   // nobody to take: complete at once
        f.Add(70, ArenaPos + new Vector3(100, 0, 0), "Bystander").Ko = true;
        f.Add(71, ArenaPos + new Vector3(120, 0, 0), "Doc").Ko = true;
        f.People[71].Record = "100-Proving Grounds.mod";   // a medic is never swept
        f.Add(72, ArenaPos + new Vector3(400, 0, 0), "Far").Ko = true;
        Assert.True(l.Treat(f, 0, false));
        Assert.Equal("Aftercare final sweep: 1 unconscious fighter awaiting hospital transfer", l.GetStatus());
        Assert.True(Has(f, "Proving Grounds: final arena sweep admitted name=Bystander actor=c:70 slot=3 position=(1100.0,50.0,2000.0)"));
        Assert.True(Has(f, "Proving Grounds: final arena sweep care=" + l.CareId + " candidates="));
        Assert.Equal(3, l.Patients.Count);
        Assert.True(l.Patients[2].SweepAdmitted);
    }
}

/// <summary>An ITownAftercareHost that forwards every member to another (tests that change one answer).</summary>
public class DelegatingHost(ITownAftercareHost<int> inner) : ITownAftercareHost<int>
{
    public bool IsPaused => inner.IsPaused;
    public uint NowMs => inner.NowMs;
    public void Debug(string line) => inner.Debug(line);
    public uint NextLogId() => inner.NextLogId();
    public int None => inner.None;
    public bool IsNull(int c) => inner.IsNull(c);
    public bool Valid(int c) => inner.Valid(c);
    public bool IsPlayer(int c) => inner.IsPlayer(c);
    public bool IsDead(int c) => inner.IsDead(c);
    public bool IsUnconscious(int c) => inner.IsUnconscious(c);
    public bool IsCarried(int c) => inner.IsCarried(c);
    public bool IsCarrying(int c) => inner.IsCarrying(c);
    public bool InCombatMode(int c) => inner.InCombatMode(c);
    public bool NeedsStabilization(int c) => inner.NeedsStabilization(c);
    public string Name(int c) => inner.Name(c);
    public string ActorText(int c) => inner.ActorText(c);
    public Vector3 Position(int c) => inner.Position(c);
    public bool TryGetMoveDestination(int c, out Vector3 destination) => inner.TryGetMoveDestination(c, out destination);
    public bool HasAiGoal(int c) => inner.HasAiGoal(c);
    public int CarryingOf(int c) => inner.CarryingOf(c);
    public AftercarePatientRead ReadForRelease(int c) => inner.ReadForRelease(c);
    public virtual bool Hold(int c, HoldOwner owner) => inner.Hold(c, owner);
    public void ReleaseHold(int c, HoldOwner owner) => inner.ReleaseHold(c, owner);
    public byte MoveSpeed(int c) => inner.MoveSpeed(c);
    public void RestoreMoveSpeed(int c, byte speed) => inner.RestoreMoveSpeed(c, speed);
    public void DropCarried(int c) => inner.DropCarried(c);
    public void Run(int c) => inner.Run(c);
    public void MoveTo(int c, Vector3 point) => inner.MoveTo(c, point);
    public PlacedBuilding? Resolve(ArenaBuildingRef held) => inner.Resolve(held);
    public IReadOnlyList<PlacedBuilding>? BuildingsInSphere(Vector3 origin, float radius, int max) => inner.BuildingsInSphere(origin, radius, max);
    public IReadOnlyList<string> BedsOf(ArenaBuilding building) => inner.BedsOf(building);
    public AftercareBed<int>? Bed(string id) => inner.Bed(id);
    public bool SparTeams1v1Active => inner.SparTeams1v1Active;
    public bool SparIsEliminated(int c) => inner.SparIsEliminated(c);
    public string RecordId(int c) => inner.RecordId(c);
    public IReadOnlyList<int>? CharactersInSphere(Vector3 origin, float radius, int max) => inner.CharactersInSphere(origin, radius, max);
    public bool InBed(int c) => inner.InBed(c);
    public bool LiesIn(int c, string bed) => inner.LiesIn(c, bed);
    public bool IsCrippled(int c) => inner.IsCrippled(c);
    public bool IsRobot(int c) => inner.IsRobot(c);
    public bool HasMedical(int c) => inner.HasMedical(c);
    public float AidScore(int c, bool robot) => inner.AidScore(c, robot);
    public float ActionableAidNeed(int c, bool robot) => inner.ActionableAidNeed(c, robot);
    public float BleedRate(int c) => inner.BleedRate(c);
    public uint WoundCount(int c) => inner.WoundCount(c);
    public float BloodRatio(int c) => inner.BloodRatio(c);
    public IReadOnlyList<AftercarePart> Parts(int c) => inner.Parts(c);
    public bool RosterRead(int c, out RosterStatus.Snapshot recovery) => inner.RosterRead(c, out recovery);
    public virtual bool HasAidKit(int c, bool robot) => inner.HasAidKit(c, robot);
    public (bool Hold, bool Passive) StandingOrders(int c) => inner.StandingOrders(c);
    public void SetStandingOrders(int c, bool hold, bool passive) => inner.SetStandingOrders(c, hold, passive);
    public bool StartTreat(int medic, int patient, bool robot) => inner.StartTreat(medic, patient, robot);
    public bool BeginLift(int medic, int patient) => inner.BeginLift(medic, patient);
    public bool PutInBed(int medic, string bed) => inner.PutInBed(medic, bed);
    public bool UseBed(int patient, string bed) => inner.UseBed(patient, bed);
    public IReadOnlyList<string> RecoveryBeds() => inner.RecoveryBeds();
    public void LeaveBedMode(int c) => inner.LeaveBedMode(c);
    public void StopOperating(int c, string bed) => inner.StopOperating(c, bed);
    public void ResumeNative(int c) => inner.ResumeNative(c);
    public void ClearOrders(int c) => inner.ClearOrders(c);
}
