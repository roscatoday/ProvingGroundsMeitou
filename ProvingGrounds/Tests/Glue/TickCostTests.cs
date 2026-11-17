using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 9.2a: the cost-per-tick instrument's pure half (Core/Glue/TickCost.cs). A frequency of 1,000 timestamp ticks per second makes
/// one tick of duration one millisecond.</summary>
public class TickCostTests
{
    const long Freq = 1000;

    [Fact]
    public void EmptyWindowIsZero()
    {
        var w = new TickCostWindow(Freq);
        Assert.Equal(new TickCostStats(0, 0, 0), w.Stats());
        Assert.Equal(0, w.Count);
    }

    [Fact]
    public void FillingWindowAveragesWhatItHolds()
    {
        var w = new TickCostWindow(Freq);
        w.Add(1); w.Add(2); w.Add(6);
        var s = w.Stats();
        Assert.Equal(3, s.Count);
        Assert.Equal(3.0, s.AvgMs, 9);
        Assert.Equal(6.0, s.MaxMs, 9);
    }

    [Fact]
    public void WindowWrapsAtSixHundred()
    {
        var w = new TickCostWindow(Freq);
        for (int i = 0; i < TickCostWindow.Size; i++) w.Add(1);
        Assert.Equal(600, w.Count);
        for (int i = 0; i < 100; i++) w.Add(4);   // the first 100 ones leave
        var s = w.Stats();
        Assert.Equal(600, s.Count);
        Assert.Equal((500 * 1 + 100 * 4) / 600.0, s.AvgMs, 9);
        Assert.Equal(4.0, s.MaxMs, 9);
    }

    [Fact]
    public void MaximumLeavesTheWindow()
    {
        var w = new TickCostWindow(Freq);
        w.Add(50);
        for (int i = 0; i < TickCostWindow.Size - 1; i++) w.Add(2);
        Assert.Equal(50.0, w.Stats().MaxMs, 9);   // still inside: the oldest of the 600
        w.Add(2);                                  // pushes it out
        Assert.Equal(2.0, w.Stats().MaxMs, 9);
        Assert.Equal(2.0, w.Stats().AvgMs, 9);
    }

    [Fact]
    public void ResetEmptiesAndRefills()
    {
        var w = new TickCostWindow(Freq);
        for (int i = 0; i < 700; i++) w.Add(9);
        w.Reset();
        Assert.Equal(new TickCostStats(0, 0, 0), w.Stats());
        w.Add(3);
        Assert.Equal(new TickCostStats(3, 3, 1), w.Stats());
    }

    [Fact]
    public void NegativeDurationCountsAsZero()
    {
        var w = new TickCostWindow(Freq);
        w.Add(-5); w.Add(4);
        Assert.Equal(new TickCostStats(2, 4, 2), w.Stats());
    }

    [Fact]
    public void MillisecondsFollowTheFrequency()
    {
        var w = new TickCostWindow(10_000_000);   // 100 ns ticks
        w.Add(2_500);                              // 0.25 ms
        Assert.Equal(0.25, w.Stats().AvgMs, 12);
    }

    [Fact]
    public void RowTextUsesThreeDecimals() =>
        Assert.Equal("tick avg 0.125 ms max 1.500 ms (600)", new TickCostStats(0.125, 1.5, 600).RowText());

    [Fact]
    public void StatusRowWithoutPerfIsUnchanged()
    {
        string before = PgStatusRow.Text("S", [("a", "ready")], "none", "idle");
        Assert.Equal("S; subsystems: a ready; current match: none; town: idle", before);
        Assert.Equal(before, PgStatusRow.Text("S", [("a", "ready")], "none", "idle", null));
        Assert.Equal(before + "; tick avg 0.100 ms max 0.200 ms (3)",
            PgStatusRow.Text("S", [("a", "ready")], "none", "idle", new TickCostStats(0.1, 0.2, 3).RowText()));
    }

    // ---------- the patch counters ----------

    [Fact]
    public void PatchCountersRecordCallsTotalAverageMax()
    {
        var c = new PatchCostCounters(["a", "b"], Freq);
        c.Record(0, 2); c.Record(0, 6); c.Record(1, 1);
        Assert.Equal((2L, 8.0, 4.0, 6.0), c.Read(0));
        Assert.Equal((1L, 1.0, 1.0, 1.0), c.Read(1));
        c.Reset();
        Assert.Equal((0L, 0.0, 0.0, 0.0), c.Read(0));
    }

    [Fact]
    public void OffMeasuresNothing()
    {
        var c = new PatchCostCounters(["a"], Freq);
        Assert.False(c.On);
        Assert.Equal(0, c.Begin());
        using (c.Measure(0)) { }
        c.End(0, 0);
        Assert.Equal(0, c.Read(0).Calls);
        c.On = true;
        using (c.Measure(0)) { }
        Assert.Equal(1, c.Read(0).Calls);
    }

    [Fact]
    public void BodyBegunWhileOffIsNotCountedWhenTurnedOnInside()
    {
        var c = new PatchCostCounters(["a"], Freq);
        using (c.Measure(0)) { c.On = true; }
        Assert.Equal(0, c.Read(0).Calls);
    }

    [Fact]
    public void SlotOutsideTheTableIsIgnored()
    {
        var c = new PatchCostCounters(["a"], Freq);
        c.Record(5, 1); c.Record(-1, 1);
        Assert.Equal(0, c.Read(0).Calls);
    }

    [Fact]
    public void ParallelRecordsAreAllCountedAndMaxIsTheLargest()
    {
        var c = new PatchCostCounters(["a"], Freq);
        Parallel.For(0, 8, t => { for (int i = 1; i <= 10_000; i++) c.Record(0, i % 97 + (t == 3 && i == 5_000 ? 1_000 : 0)); });
        var (calls, total, _, max) = c.Read(0);
        Assert.Equal(80_000, calls);
        long expected = 0;
        for (int t = 0; t < 8; t++) for (int i = 1; i <= 10_000; i++) expected += i % 97 + (t == 3 && i == 5_000 ? 1_000 : 0);
        Assert.Equal((double)expected, total, 6);
        Assert.Equal(5_000 % 97 + 1_000.0, max, 9);
    }

    [Fact]
    public void JsonOfWindowAndPatches()
    {
        Assert.Equal("{\"avgMs\":0.1250,\"maxMs\":2.0000,\"ticks\":4}", TickCostJson.Window(new TickCostStats(0.125, 2, 4)));
        var c = new PatchCostCounters(["x \"y\""], Freq);
        c.Record(0, 3);
        Assert.Equal("[{\"patch\":\"x \\\"y\\\"\",\"calls\":1,\"totalMs\":3.0000,\"avgMs\":3.0000,\"maxMs\":3.0000}]", TickCostJson.Patches(c));
    }
}
