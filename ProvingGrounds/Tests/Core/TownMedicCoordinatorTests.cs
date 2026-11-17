using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownMedicCoordinator;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownMedicCoordinator.cs that medics.json does not reach: the constructors' C++ defaults (and that
/// <c>default(X)</c> is NOT them), the live reference <c>Command</c> returns, and the undefined-behaviour indexes that throw here.</summary>
public class TownMedicCoordinatorTests
{
    [Fact]
    public void Constructors_give_the_cpp_defaults_and_default_struct_does_not()
    {
        var m = new MedicObservation();
        Assert.False(m.Ready); Assert.False(m.Bandages); Assert.False(m.Repairs); Assert.Equal(-1, m.Carrying);
        Assert.Equal(0, default(MedicObservation).Carrying);   // default(...) skips the constructor: patient 0, not empty
        var p = new PatientObservation();
        Assert.True(p.Eligible); Assert.False(p.Valid);
        Assert.Equal(0f, p.Flesh); Assert.Equal(0f, p.Repair); Assert.Equal(0f, p.Bleed);
        Assert.False(default(PatientObservation).Eligible);
        Assert.Equal(-1, new BedObservation().Occupant);
        Assert.Equal(0, default(BedObservation).Occupant);
        var i = new Intent();
        Assert.Equal(Kind.Wait, i.Kind); Assert.Equal(-1, i.Patient); Assert.Equal(-1, i.Bed);
        Assert.Equal(Kind.Gather, default(Intent).Kind);
        var k = new Intent(Kind.Deliver, 3);
        Assert.Equal(Kind.Deliver, k.Kind); Assert.Equal(3, k.Patient); Assert.Equal(-1, k.Bed);
        var o = new Observation();
        Assert.Equal(0, o.MedicCount); Assert.Equal(0, o.PatientCount); Assert.Equal(0, o.BedCount); Assert.False(o.HospitalAvailable);
        for (int x = 0; x < MaxMedics; ++x) Assert.Equal(-1, o.Medics[x].Carrying);
        for (int x = 0; x < MaxPatients; ++x) Assert.True(o.Patients[x].Eligible, $"patient {x} eligible");
        for (int x = 0; x < MaxBeds; ++x) Assert.Equal(-1, o.Beds[x].Occupant);
    }

    [Fact]
    public void A_new_coordinator_releases_every_medic()
    {
        var c = new Coordinator();
        Assert.Equal(Phase.Gathering, c.GetPhase());
        for (int m = 0; m < MaxMedics; ++m) { Assert.Equal(Kind.Release, c.Command(m).Kind); Assert.Equal(-1, c.ReservedBed(m)); }
        Assert.False(c.HasUnresolved());
        Assert.False(c.MissingBed());
    }

    [Fact]
    public void Command_is_a_live_reference_like_the_cpp_const_ref()
    {
        var c = new Coordinator();
        ref readonly Intent held = ref c.Command(0);
        var o = new Observation { MedicCount = 1 };
        o.Medics[0] = new MedicObservation { Ready = true, Carrying = -1 };
        c.Tick(o, false, 1.0f, false);
        Assert.Equal(Kind.Gather, held.Kind);      // the reference taken before the tick shows the new command
        Assert.True(c.HasPatientWork(-1));         // quirk kept: a Gather command has patient -1, so "work" on patient -1
    }

    [Fact]
    public void Fail_ignores_a_patient_out_of_range_but_UB_indexes_throw()
    {
        var c = new Coordinator();
        c.Fail(0, -1, Kind.PickUp);
        c.Fail(0, MaxPatients, Kind.PickUp);
        Assert.Equal(0u, c.failed[0, 0]);
        Assert.Throws<IndexOutOfRangeException>(() => c.Fail(MaxMedics, 0, Kind.PickUp));   // UB in the C++
        Assert.Throws<IndexOutOfRangeException>(() => c.HandoffPending(MaxPatients + 1));    // UB in the C++
        var o = new Observation { MedicCount = MaxMedics + 1 };                              // UB in the C++
        Assert.Throws<IndexOutOfRangeException>(() => c.Tick(o, false, 0, false));
    }

    [Fact]
    public void Distance_is_float_euclidean()
    {
        Assert.Equal(5.0f, Distance(new Point(0, 0, 0), new Point(3, 0, 4)));
        Assert.Equal(MathF.Sqrt(3.0f), Distance(new Point(1, 1, 1), new Point(0, 0, 0)));
    }
}
