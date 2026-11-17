using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownMedicAIPolicy;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownMedicAIPolicy.cs beside medics.json: handles compare by reference (C++ pointers), capacities
/// and overflow, the typedef capacities.</summary>
public class TownMedicAIPolicyTests
{
    [Fact]
    public void Typedef_capacities()
    {
        Assert.Equal(10, NewReservations().Capacity);
        Assert.Equal(16, NewPatientReservations().Capacity);
        Assert.Equal(4, NewExternalReservations().Capacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReceiverReservations(0));
    }

    [Fact]
    public void Reservations_fill_up_and_refuse_beyond_capacity()
    {
        var r = NewExternalReservations();
        var items = Enumerable.Range(0, 5).Select(_ => new object()).ToArray();
        for (int i = 0; i < 4; ++i) Assert.True(r.Add(items[i]));
        Assert.False(r.Add(items[4]));
        Assert.True(r.Add(items[2]));        // already held: true without a new slot
        Assert.False(r.Add(null));
        Assert.False(r.Contains(null));
        r.Remove(null);                      // matches only empty slots: no change
        Assert.True(r.Contains(items[0]));
        r.Remove(items[1]);
        Assert.True(r.Add(items[4]));        // takes the freed slot
        Assert.Same(items[4], r.receivers[1]);
    }

    [Fact]
    public void Handles_compare_by_reference_not_by_value()
    {
        string a = new string('x', 3), b = new string('x', 3);
        var r = NewReservations();
        r.Add(a);
        Assert.True(r.Contains(a));
        Assert.False(r.Contains(b));
        var d = new Deliveries();
        d.Watch(a, "bed"); d.Entered(b, "bed");
        Assert.False(d.Completed(a));
    }

    [Fact]
    public void Deliveries_hold_sixteen_patients_and_freeze_a_completed_bed()
    {
        var d = new Deliveries();
        var patients = Enumerable.Range(0, Deliveries.Slots + 1).Select(_ => new object()).ToArray();
        object bed1 = new(), bed2 = new();
        foreach (var p in patients) d.Watch(p, bed1);
        d.Entered(patients[Deliveries.Slots], bed1);
        Assert.False(d.Completed(patients[Deliveries.Slots]));   // the 17th was never recorded
        d.Entered(patients[0], bed1);
        Assert.True(d.Completed(patients[0]));
        d.Watch(patients[0], bed2);                // complete: the bed stays
        Assert.Same(bed1, d.receipts[0].Bed);
        d.Watch(patients[1], bed2);                // not complete: the bed moves
        Assert.Same(bed2, d.receipts[1].Bed);
        Assert.False(d.Completed(null));
        d.Clear();
        Assert.False(d.Completed(patients[0]));
    }
}
