using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.4c (TownAftercare.cpp:371-402, :1035-1050, :1227-1292): the bed recovery sweep (the scan of the hospital's beds, the release
/// rules and the C++ lines), the release paths and RecordTreatment, over the Fake of <see cref="TownAftercareLogicTests"/> extended here.</summary>
public partial class TownAftercareLogicTests
{
    sealed partial class Fake
    {
        public readonly List<string> ScanBeds = [];
        /// <summary>LeaveBedMode leaves the character lying (the C++ "seated" case); <see cref="StopSticks"/>: StopOperating too.</summary>
        public bool ModeSticks, StopSticks;
        /// <summary>LeaveBedMode throws for this character (the quality review's "one entry throws every poll").</summary>
        public int ThrowFor = -1;
        public IReadOnlyList<string> RecoveryBeds() => ScanBeds;
        public void LeaveBedMode(int c)
        {
            if (c == ThrowFor) throw new InvalidOperationException("boom");
            Order.Add("bedmode " + c);
            if (!ModeSticks) OutOfBed(c);
        }
        public void StopOperating(int c, string bed) { Order.Add("stopop " + c + " " + bed); if (!StopSticks) OutOfBed(c); }
        public void ResumeNative(int c) => Order.Add("resume " + c);

        public void OutOfBed(int c)
        {
            string? bed = P(c).InBedId;
            P(c).InBedId = null;
            if (bed is not null && BedTable[bed].Occupant == c)
                BedTable[bed] = BedTable[bed] with { Occupant = -1, Occupied = false, OccupantInBed = false };
        }
    }

    /// <summary>No care running; character 12 (an NPC) lying in hosp@bed0; the hospital's four beds scanned.</summary>
    static (TownAftercareLogic<int> L, Fake F) Ward(float health = 1, float blood = 1)
    {
        var (l, f) = World(0);
        f.SetBed("hosp@bed0", Bed0); f.SetBed("hosp@bed1", Bed1); f.SetBed("hosp@bed2", Bed2); f.SetBed("hosp@bed3", Bed0);
        var w = f.Add(12, Bed0, "Sleeper");
        w.Health = health; w.Blood = blood;
        f.SetBed("hosp@bed0", Bed0, 12, lying: true);
        f.ScanBeds.AddRange(["hosp@bed0", "hosp@bed1", "hosp@bed2", "hosp@bed3"]);
        return (l, f);
    }

    static void Poll(TownAftercareLogic<int> l, Fake f, int seconds = 1)
    {
        for (int i = 0; i < seconds; ++i) { f.Now += 1000; l.TickBedRecovery(f); }
    }

    [Fact]
    public void The_scan_registers_an_npc_lying_in_a_hospital_bed_but_not_a_player_one_not_lying_or_another_function()
    {
        var (l, f) = Ward(health: .5f);
        f.Add(13, Bed1, "Player", player: true); f.SetBed("hosp@bed1", Bed1, 13, lying: true);
        f.Add(14, Bed2, "Sitter"); f.SetBed("hosp@bed2", Bed2, 14, lying: false);
        f.Add(15, Bed0, "Other"); f.SetBed("hosp@bed3", Bed0, 15, lying: true);   // function 3: not a hospital bed
        l.TickBedRecovery(f);
        Assert.Equal([12], l.BedOccupants.ToArray().Select(o => o.Actor));
        Assert.Equal("hosp@bed0", l.BedOccupants[0].Bed);
    }

    [Fact]
    public void A_healed_occupant_is_taken_out_at_the_next_poll_with_its_ai_back_and_the_cpp_lines()
    {
        var (l, f) = Ward();
        Poll(l, f);
        Assert.Equal(["bedmode 12", "resume 12"], f.Order);
        Assert.Contains("Proving Grounds: aftercare bed release actor=c:12 name=Sleeper bed=hosp@bed0 reason=healed path=set-bed-mode stayAge=0.0 " +
            "recovery=1.000 blood=1.000 lowestLimb=1.000 band=2 leftBed=1 bedFree=1", f.Debugs);
        Assert.Contains("Proving Grounds: aftercare bed sweep tracked=1 released=1 elapsed=0", f.Debugs);
        Assert.Equal(0, l.BedOccupants.Length);
    }

    [Fact]
    public void A_still_hurt_awake_occupant_stays_600_seconds_then_leaves_for_the_stay_limit()
    {
        var (l, f) = Ward(health: .5f);
        Poll(l, f, 600);   // registered at elapsed 0 (the first poll), so age 599 at the 600th poll
        Assert.DoesNotContain("bedmode 12", f.Order);
        Assert.Contains("Proving Grounds: aftercare bed sweep tracked=1 released=0 elapsed=30", f.Debugs);   // the 30 s trace
        Poll(l, f);
        Assert.Contains(f.Debugs, d => d.StartsWith("Proving Grounds: aftercare bed release actor=c:12 name=Sleeper bed=hosp@bed0 reason=stay-limit " +
            "path=set-bed-mode stayAge=600.0 recovery=0.500 blood=1.000 lowestLimb=0.500 band=1 leftBed=1 bedFree=1", StringComparison.Ordinal));
    }

    [Fact]
    public void Never_taken_out_while_ko_carried_fighting_or_managed()
    {
        var (l, f) = Ward();
        f.People[12].Ko = true; Poll(l, f, 3);
        f.People[12].Ko = false; f.People[12].Carried = true; Poll(l, f, 3);
        f.People[12].Carried = false; f.People[12].Combat = true; Poll(l, f, 3);
        f.People[12].Combat = false;
        // managed: a registered fighter of a running care, not yet done
        f.Add(30, new Vector3(1000, 50, 2400));
        Begin(l, f, [30], [12]);
        Poll(l, f, 3);
        Assert.DoesNotContain("bedmode 12", f.Order);
        Assert.Equal(1, l.BedOccupants.Length);
        l.Release(f);
        f.Order.Clear();
        Poll(l, f);
        Assert.Contains("bedmode 12", f.Order);
    }

    [Fact]
    public void A_dead_occupant_leaves_without_the_ai_back_even_when_ko()
    {
        var (l, f) = Ward(health: .2f);
        f.People[12].Dead = true; f.People[12].Ko = true;
        Poll(l, f);
        Assert.Equal(["bedmode 12"], f.Order);
        Assert.Contains(f.Debugs, d => d.Contains(" reason=dead path=set-bed-mode ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_stop_operating_fallback_and_a_refusal_retried_every_thirty_seconds()
    {
        var (l, f) = Ward();
        f.ModeSticks = true;
        Poll(l, f);
        Assert.Equal(["bedmode 12", "stopop 12 hosp@bed0", "resume 12"], f.Order);
        Assert.Contains(f.Debugs, d => d.Contains(" path=stop-operating ", StringComparison.Ordinal) && d.EndsWith("leftBed=1 bedFree=1", StringComparison.Ordinal));

        (l, f) = Ward();
        f.ModeSticks = f.StopSticks = true;
        Poll(l, f);
        Assert.Contains(f.Debugs, d => d.Contains(" path=none ", StringComparison.Ordinal) && d.EndsWith("leftBed=0 bedFree=0", StringComparison.Ordinal));
        Assert.DoesNotContain("resume 12", f.Order);
        Assert.Equal(1, l.BedOccupants.Length);
        f.Order.Clear();
        Poll(l, f, 29);
        Assert.Empty(f.Order);
        Poll(l, f);
        Assert.Equal(["bedmode 12", "stopop 12 hosp@bed0"], f.Order);
    }

    [Fact]
    public void An_occupant_already_out_or_a_player_is_forgotten_without_a_line()
    {
        var (l, f) = Ward(health: .5f);
        l.TickBedRecovery(f);
        Assert.Equal(1, l.BedOccupants.Length);
        f.OutOfBed(12);
        Poll(l, f);
        Assert.Equal(0, l.BedOccupants.Length);
        Assert.DoesNotContain(f.Debugs, d => d.Contains("bed release", StringComparison.Ordinal));
        Assert.Empty(f.Order);
    }

    [Fact]
    public void The_poll_runs_once_a_second_and_a_long_gap_ages_a_stay_one_second()
    {
        var (l, f) = Ward(health: .5f);
        l.TickBedRecovery(f);   // first poll: registers at elapsed 0
        f.Now += 999; l.TickBedRecovery(f);
        f.Now += 60_000; l.TickBedRecovery(f);   // one step, capped at 1 s
        f.Now += 1000; l.TickBedRecovery(f);
        f.Debugs.Clear();
        f.Now += 30_000 - 2000; l.TickBedRecovery(f);   // nothing: the gap is capped again (elapsed 3)
        Assert.Empty(f.Debugs);
        Assert.Equal(0f, l.BedOccupants[0].Entered);
    }

    [Fact]
    public void A_move_to_another_bed_restarts_the_clock_and_the_teardown_drops_the_registry_and_clocks()
    {
        var (l, f) = Ward(health: .5f);
        Poll(l, f, 5);   // elapsed 0..4, registered at 0
        Assert.Equal(0f, l.BedOccupants[0].Entered);
        f.OutOfBed(12);
        f.SetBed("hosp@bed1", Bed1, 12, lying: true);
        Poll(l, f);   // the sweep forgets the bed0 entry only if not in a bed: it is in bed1, so the entry is moved by the scan
        Assert.Equal("hosp@bed1", l.BedOccupants[0].Bed);
        Assert.Equal(5f, l.BedOccupants[0].Entered);
        l.AbandonWorldState();
        Assert.Equal(0, l.BedOccupants.Length);
        f.Debugs.Clear();
        l.TickBedRecovery(f);   // the clock restarted at 0 (first poll after the teardown is due at once)
        Assert.Equal(0f, l.BedOccupants[0].Entered);
    }

    [Fact]
    public void The_registry_holds_at_most_64()
    {
        var (l, f) = Ward(health: .5f);
        f.ScanBeds.Clear();
        for (int i = 0; i < 70; ++i)
        {
            string id = "hosp@x" + i;
            f.BedTable[id] = new AftercareBed<int>(id, Hospital, 6, 100 + i, true, true, Bed0);
            f.Add(100 + i, Bed0).InBedId = id;
            f.People[100 + i].Health = .5f;
            f.ScanBeds.Add(id);
        }
        l.TickBedRecovery(f);
        Assert.Equal(TownMedicCoordinator.MaxBeds, l.BedOccupants.Length);
    }

    [Fact]
    public void An_entry_that_throws_every_poll_is_forgotten_and_logged_three_times_while_the_others_are_released()
    {
        var (l, f) = Ward();
        f.Add(13, Bed1, "Second"); f.SetBed("hosp@bed1", Bed1, 13, lying: true);
        f.ThrowFor = 12;
        Poll(l, f);
        Assert.Contains("bedmode 13", f.Order);
        Assert.Contains("Proving Grounds: aftercare bed recovery entry failed actor=c:12 error=InvalidOperationException: boom", f.Debugs);
        Assert.Contains("Proving Grounds: aftercare bed sweep tracked=2 released=1 elapsed=0", f.Debugs);
        // still lying in its bed: the scan finds it again each poll, and it throws again; the count goes on, the log stops at three
        Poll(l, f, 4);
        Assert.Equal(5, l.BedEntryFailures);
        Assert.Equal(3, f.Debugs.Count(d => d.StartsWith("Proving Grounds: aftercare bed recovery entry failed", StringComparison.Ordinal)));
        f.ThrowFor = -1;
        Poll(l, f);
        Assert.Contains("bedmode 12", f.Order);
    }

    [Fact]
    public void RecordTreatment_fills_the_medics_callback_counts()
    {
        var (l, f) = AfterFight();
        f.People[10].Flesh = 5; f.People[10].Bleed = 1;
        Assert.True(l.Treat(f, 0, false));   // the bandage order: medic 30 directed, its trace slot taken
        l.RecordTreatment(30, 10, false, false, false, .5f);
        l.RecordTreatment(30, 10, false, false, false, .5f);
        l.RecordTreatment(30, 10, true, true, false, .5f);
        l.RecordTreatment(30, 10, false, false, true, .5f);
        l.RecordTreatment(31, 10, false, false, false, .5f);   // not a directed medic: nothing
        l.RecordTreatment(-1, 10, false, false, false, .5f);   // the NULL medic
        f.Debugs.Clear();
        Assert.True(l.Treat(f, 6, false));
        Assert.Contains(f.Debugs, d => d.StartsWith("Proving Grounds: aftercare medic callbacks slot=1 firstAid=3 doctoring=1 blocked=1 complete=1 " +
            "aidTime=1.500 doctorTime=0.000 lastPatientMatches=1 ", StringComparison.Ordinal));
    }
}
