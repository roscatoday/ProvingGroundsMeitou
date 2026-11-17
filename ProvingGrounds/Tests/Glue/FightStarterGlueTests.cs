using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The bookkeeping of FightStarter.cpp's two lists (g_originalStates :50, :106-130, :422-426; g_focusAssignments :51, :137-174),
/// the hit-reaction queue (the observer records, the next serial step applies, CAP 3) and the keep-alive clock (KOWatcher.cpp:24-49,
/// :66-78, :233-237), all in Core/Glue/FightStarterState.cs.</summary>
public class FightStarterGlueTests
{
    [Fact]
    public void RememberKeepsTheFirstStateOnly()
    {
        // C++ :116-130: RememberOriginalState returns when the fighter already has a state
        var m = new FighterList<int, string>();
        Assert.True(m.AddIfAbsent(7, "before"));
        Assert.False(m.AddIfAbsent(7, "later"));
        Assert.True(m.TryGet(7, out var s));
        Assert.Equal("before", s);
        Assert.Equal(1, m.Count);
    }

    [Fact]
    public void FindReturnsTheIndexOrMinusOne()
    {
        var m = new FighterList<int, int>();
        m.AddIfAbsent(3, 30);
        m.AddIfAbsent(5, 50);
        Assert.Equal(0, m.Find(3));
        Assert.Equal(1, m.Find(5));
        Assert.Equal(-1, m.Find(9));
    }

    [Fact]
    public void RemoveErasesTheFirstMatchAndKeepsOrder()
    {
        var m = new FighterList<int, int>();
        m.AddIfAbsent(1, 10); m.AddIfAbsent(2, 20); m.AddIfAbsent(3, 30);
        Assert.True(m.Remove(2));
        Assert.False(m.Remove(2));
        Assert.Equal(0, m.Find(1));
        Assert.Equal(1, m.Find(3));
    }

    [Fact]
    public void SetUpdatesInPlaceOrAppends()
    {
        // C++ :147-163 SetAssignedTarget
        var f = new FighterList<int, int>();
        f.Set(1, 100);
        f.Set(2, 200);
        f.Set(1, 101);
        Assert.Equal(2, f.Count);
        Assert.True(f.TryGet(1, out int t));
        Assert.Equal(101, t);
        Assert.Equal(0, f.Find(1));
        f.Clear();
        Assert.Equal(0, f.Count);
        Assert.False(f.TryGet(1, out _));
    }

    [Fact]
    public void QueueIsFifoAndBounded()
    {
        var q = new IncomingAttackQueue<int>(3);
        Assert.True(q.TryAdd(1, 10));
        Assert.True(q.TryAdd(2, 20));
        Assert.True(q.TryAdd(3, 30));
        Assert.False(q.TryAdd(4, 40));   // full: dropped, counted
        Assert.Equal(1, q.Dropped);
        Assert.True(q.TryTake(out int d, out int a)); Assert.Equal((1, 10), (d, a));
        Assert.True(q.TryAdd(5, 50));   // the ring wraps
        Assert.True(q.TryTake(out d, out a)); Assert.Equal((2, 20), (d, a));
        Assert.True(q.TryTake(out d, out a)); Assert.Equal((3, 30), (d, a));
        Assert.True(q.TryTake(out d, out a)); Assert.Equal((5, 50), (d, a));
        Assert.False(q.TryTake(out _, out _));
    }

    [Fact]
    public void QueueClearEmptiesIt()
    {
        var q = new IncomingAttackQueue<int>(2);
        q.TryAdd(1, 2);
        q.Clear();
        Assert.Equal(0, q.Count);
        Assert.False(q.TryTake(out _, out _));
    }

    [Fact]
    public void KeepAliveCadenceConstantsAreTheOriginals()
    {
        Assert.Equal(0.10f, FightKeepAliveCadence.ScanIntervalSec);        // KOWatcher.cpp:24
        Assert.Equal(0.50f, FightKeepAliveCadence.KeepAliveIntervalSec);   // KOWatcher.cpp:25
    }

    [Fact]
    public void FirstScanFiresAtOnceThenEveryTenthOfASecond()
    {
        // C++: scan elapsed starts at 0.10 (:27, :49), the first dt is 0 (g_lastTick == 0, :38-41): the first frame scans
        var c = new FightKeepAliveCadence();
        long tick = 0;
        Assert.True(c.Scan(SimClock.NowMsAfter(tick)));
        var scans = new List<long>();
        for (tick = 1; tick <= 30; tick++) if (c.Scan(SimClock.NowMsAfter(tick))) scans.Add(tick);
        // replay the C++ arithmetic: dt from GetTickCount-style ms, float sum, fmodf remainder
        float elapsed = 0f; uint last = SimClock.NowMsAfter(0);
        var expected = new List<long>();
        for (long t = 1; t <= 30; t++)
        {
            uint now = SimClock.NowMsAfter(t);
            float dt = (float)(now - last) / 1000.0f; last = now;
            if (FrameCadencePolicy.Advance(ref elapsed, dt > 1.0f ? 1.0f : dt, 0.10f)) expected.Add(t);
        }
        Assert.Equal(expected, scans);
        Assert.InRange(scans.Count, 9, 10);
    }

    [Fact]
    public void KeepAliveEveryFifthScan()
    {
        // C++ :236-237: Advance(g_keepAliveElapsedSec, kKoScanIntervalSec, kKeepAliveIntervalSec): 0.1 per scan, due at 0.5
        var c = new FightKeepAliveCadence();
        var due = new List<int>();
        for (int scan = 1; scan <= 12; scan++) if (c.KeepAlive()) due.Add(scan);
        float e = 0f; var expected = new List<int>();
        for (int scan = 1; scan <= 12; scan++) if (FrameCadencePolicy.Advance(ref e, 0.10f, 0.50f)) expected.Add(scan);
        Assert.Equal(expected, due);
        Assert.NotEmpty(due);
    }

    [Fact]
    public void ResetKeepAliveRestartsItsCount()
    {
        // C++ :233 FrameCadencePolicy::Reset(g_keepAliveElapsedSec) after a retarget
        var c = new FightKeepAliveCadence();
        for (int i = 0; i < 4; i++) c.KeepAlive();
        c.ResetKeepAlive();
        Assert.False(c.KeepAlive());
    }

    [Fact]
    public void DtIsCappedAtOneSecondAndWrapsLikeADword()
    {
        // C++ :36-43: (now - g_lastTick) unsigned, / 1000, capped at 1 s
        Assert.Equal(1.0f, FightKeepAliveCadence.Dt(5_000, 1_000));
        Assert.Equal(0.033f, FightKeepAliveCadence.Dt(1_033, 1_000));
        Assert.Equal(0.050f, FightKeepAliveCadence.Dt(20, uint.MaxValue - 29));   // across the 2^32 wrap: 50 ms
    }

    [Fact]
    public void ResetStartsOverWithAnImmediateScan()
    {
        var c = new FightKeepAliveCadence();
        c.Scan(SimClock.NowMsAfter(0));
        c.Scan(SimClock.NowMsAfter(1));
        c.Reset();
        Assert.True(c.Scan(SimClock.NowMsAfter(2)));
    }

    [Fact]
    public void AnAttackOrderedInTheDisengageTickNeedsTheLateStop()
    {
        // Meitou-only: an AttackOrder deferred in the disengage's tick beats its StopCommand at tick+1 (PlayerSystem before CombatSystem)
        var s = new AttackOrderStamps<int>();
        s.Stamp(1, 100);
        s.Stamp(2, 99);
        s.Stamp(2, 100);   // re-ordered this tick: the latest stamp counts
        s.Stamp(3, 98);
        Assert.True(s.TakeOrderedIn(1, 100));
        Assert.True(s.TakeOrderedIn(2, 100));
        Assert.False(s.TakeOrderedIn(3, 100));   // ordered earlier: its order already ran before this tick's stop
        Assert.False(s.TakeOrderedIn(4, 100));   // never ordered
        Assert.Equal(0, s.Count);                // the disengage forgets each fighter
        Assert.False(s.TakeOrderedIn(1, 100));   // taken once
        s.Stamp(5, 7);
        s.Clear();
        Assert.False(s.TakeOrderedIn(5, 7));
    }
}
