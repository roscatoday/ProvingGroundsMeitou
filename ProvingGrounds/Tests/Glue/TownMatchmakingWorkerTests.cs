using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.2: the background search worker (TownMatchmakingSearch.cpp's thread, events and lock) with real threads and a fake search
/// function: start, queue, result, the generation rule across the lock, shutdown, a world change leaving no thread.</summary>
public class TownMatchmakingWorkerTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    static TownMatchmakingSearch.Request Req(uint generation, int slot, params string[] npcs)
    {
        var r = new TownMatchmakingSearch.Request { Generation = generation, Slot = slot };
        foreach (var id in npcs) r.Npcs.Add(new TownMatchmakingPolicy.Fighter { Id = id, Role = "232-Proving Grounds.mod", Ability = 50 });
        return r;
    }

    static TownMatchmakingPolicy.Match MatchOf(params string[] b)
    {
        var m = new TownMatchmakingPolicy.Match { Valid = b.Length > 0 };
        m.B.AddRange(b);
        return m;
    }

    static TownMatchmakingSearch.Result PollUntil(TownMatchmakingWorker w)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (w.Poll(out var r)) return r!;
            Thread.Sleep(5);
        }
        throw new TimeoutException("no result");
    }

    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); Thread.Sleep(5); }
    }

    [Fact]
    public void NothingRunsBeforeTheFirstUseAndPollAnswersFalse()
    {
        var w = new TownMatchmakingWorker(_ => { }, _ => MatchOf("a"));
        Assert.False(w.Running);
        Assert.False(w.Poll(out _));
        w.Shutdown();   // never started: nothing
        Assert.False(w.Running);
    }

    [Fact]
    public void BeginGenerationStartsOneBelowNormalBackgroundThread()
    {
        var w = new TownMatchmakingWorker(_ => { }, _ => MatchOf("a"));
        try
        {
            w.BeginGeneration(1);
            var t = w.ThreadForTest!;
            Assert.True(w.Running);
            Assert.Equal(ThreadPriority.BelowNormal, t.Priority);
            Assert.True(t.IsBackground);
            w.BeginGeneration(2);
            Assert.Same(t, w.ThreadForTest);   // created once
            Assert.Equal(2u, w.Generation);
        }
        finally { w.Shutdown(); }
    }

    [Fact]
    public void AQueuedRequestOfTheCurrentGenerationIsSearchedOnACopyAndPolled()
    {
        TownMatchmakingSearch.Request? seen = null;
        int searchThread = -1;
        var w = new TownMatchmakingWorker(_ => { }, r => { seen = r; searchThread = Environment.CurrentManagedThreadId; return MatchOf("n1"); });
        try
        {
            w.BeginGeneration(5);
            var request = Req(5, 3, "n1", "n2");
            Assert.True(w.Enqueue(request));
            request.Npcs.Clear();   // the caller's object is not the worker's
            var result = PollUntil(w);
            Assert.Equal(5u, result.Generation);
            Assert.Equal(3, result.Slot);
            Assert.True(result.Match.Valid);
            Assert.Equal(["n1"], result.Match.B);
            Assert.Equal(2, seen!.Npcs.Count);
            Assert.NotEqual(Environment.CurrentManagedThreadId, searchThread);
            Assert.False(w.Poll(out _));
            Assert.Equal(1, w.Searches);
        }
        finally { w.Shutdown(); }
    }

    [Fact]
    public void ARequestOfAnOldGenerationIsRefused()
    {
        var w = new TownMatchmakingWorker(_ => { }, _ => MatchOf("a"));
        try
        {
            w.BeginGeneration(2);
            Assert.False(w.Enqueue(Req(1, 0, "a")));
            Assert.Equal(0, w.PendingRequests);
        }
        finally { w.Shutdown(); }
    }

    [Fact]
    public void AResultThatWentStaleDuringItsSearchIsDropped()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var w = new TownMatchmakingWorker(_ => { }, r => { started.Set(); release.Wait(Wait); return MatchOf("a"); });
        try
        {
            w.BeginGeneration(1);
            Assert.True(w.Enqueue(Req(1, 0, "a")));
            Assert.True(started.Wait(Wait));
            w.BeginGeneration(2);   // the cards changed while the search ran
            release.Set();
            WaitFor(() => w.Searches == 1);   // counted inside the lock right after Complete: the stale result was dropped by then
            Assert.False(w.Poll(out _));
        }
        finally { release.Set(); w.Shutdown(); }
    }

    [Fact]
    public void AValidResultTakesItsOpponentsOutOfTheOtherQueuedCards()
    {
        using var firstRunning = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pools = new List<string[]>();
        var histories = new List<int>();
        var w = new TownMatchmakingWorker(_ => { }, r =>
        {
            lock (pools) { pools.Add(r.Npcs.Select(n => n.Id).ToArray()); histories.Add(r.History.Count); }
            if (r.Slot == 0) { firstRunning.Set(); release.Wait(Wait); return MatchOf("a", "b"); }
            return MatchOf();
        });
        try
        {
            w.BeginGeneration(1);
            w.Enqueue(Req(1, 0, "a", "b", "c"));
            Assert.True(firstRunning.Wait(Wait));
            w.Enqueue(Req(1, 1, "a", "b", "c"));   // queued while slot 0 searches
            release.Set();
            var r0 = PollUntil(w);
            var r1 = PollUntil(w);
            Assert.Equal(0, r0.Slot);
            Assert.Equal(1, r1.Slot);
            Assert.Equal(["c"], pools[1]);
            Assert.Equal(1, histories[1]);   // slot 0's lineup recorded in slot 1's history
        }
        finally { release.Set(); w.Shutdown(); }
    }

    [Fact]
    public void ShutdownWaitsForTheSearchJoinsAndDropsTheQueue()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var w = new TownMatchmakingWorker(_ => { }, r => { started.Set(); release.Wait(Wait); return MatchOf("a"); });
        w.BeginGeneration(1);
        w.Enqueue(Req(1, 0, "a"));
        w.Enqueue(Req(1, 1, "b"));
        Assert.True(started.Wait(Wait));
        var t = w.ThreadForTest!;
        var shut = Task.Run(w.Shutdown);
        Thread.Sleep(50);
        Assert.False(shut.IsCompleted);   // the running search finishes first
        release.Set();
        Assert.True(shut.Wait(Wait));
        Assert.False(t.IsAlive);
        Assert.False(w.Running);
        Assert.Null(w.ThreadForTest);
        Assert.False(w.Poll(out _));      // never started now: false, and the finished search was dropped
        Assert.Equal(0, w.PendingRequests);
        Assert.Equal(0, w.PendingResults);
        Assert.Equal(1u, w.Generation);   // g_generation is kept
        // the next use starts a new thread
        Assert.True(w.Enqueue(Req(1, 2, "c")));
        Assert.NotSame(t, w.ThreadForTest);
        var r = PollUntil(w);
        Assert.Equal(2, r.Slot);
        w.Shutdown();
        Assert.False(w.Running);
    }

    [Fact]
    public void ASearchThatThrowsIsAnUnansweredSlotLoggedOnceAndTheThreadLives()
    {
        var log = new List<string>();
        var w = new TownMatchmakingWorker(l => { lock (log) log.Add(l); }, r => r.Slot < 2 ? throw new InvalidOperationException("boom") : MatchOf("x"));
        try
        {
            w.BeginGeneration(1);
            w.Enqueue(Req(1, 0, "a"));
            w.Enqueue(Req(1, 1, "a"));
            w.Enqueue(Req(1, 2, "a"));
            var a = PollUntil(w); var b = PollUntil(w); var c = PollUntil(w);
            Assert.False(a.Match.Valid);
            Assert.False(b.Match.Valid);
            Assert.True(c.Match.Valid);
            Assert.Equal(2, w.Failures);
            Assert.True(w.Running);
            lock (log) Assert.Equal(["Proving Grounds: challenge search failed (logged once): InvalidOperationException: boom"], log);
        }
        finally { w.Shutdown(); }
    }

    [Fact]
    public void ANullMatchIsAnUnansweredSlot()
    {
        var w = new TownMatchmakingWorker(_ => { }, _ => null!);
        try
        {
            w.BeginGeneration(1);
            w.Enqueue(Req(1, 0, "a"));
            var r = PollUntil(w);
            Assert.False(r.Match.Valid);
            Assert.True(w.Running);
        }
        finally { w.Shutdown(); }
    }

    [Fact]
    public void AThrowOutsideTheSearchEndsTheThreadNotTheProcess()
    {
        // the search throws, then the log throws too: the thread ends (a dead C++ thread), the test process lives, Enqueue keeps accepting
        var w = new TownMatchmakingWorker(_ => throw new InvalidOperationException("log down"), _ => throw new InvalidOperationException("boom"));
        try
        {
            w.BeginGeneration(1);
            Assert.True(w.Enqueue(Req(1, 0, "a")));
            var t = w.ThreadForTest!;
            Assert.True(t.Join(Wait));
            Assert.False(w.Running);
            Assert.Equal(2, w.Failures);   // the search, then the loop
            Assert.True(w.Enqueue(Req(1, 1, "b")));
            Assert.False(w.Poll(out _));
        }
        finally { w.Shutdown(); }
    }

    [Fact]
    public void AWorldChangeLeavesNoThread()
    {
        // TownArena.cpp:1799: AbandonWorldState shuts the search down first; the fake host passes it to a real worker.
        var f = new TownFake();
        f.Worker = new TownMatchmakingWorker(_ => { }, _ => MatchOf("a"));
        f.Worker.BeginGeneration(1);
        f.Worker.Enqueue(Req(1, 0, "a"));
        var t = f.Worker.ThreadForTest!;
        new TownArenaLogic<int>(TownFake.NoCharacter).AbandonWorldState(f);
        Assert.False(t.IsAlive);
        Assert.False(f.Worker.Running);
        Assert.False(f.Worker.Poll(out _));
    }

    [Fact]
    public void TheDefaultSearchIsSelectPlayer()
    {
        var w = new TownMatchmakingWorker(_ => { });
        try
        {
            w.BeginGeneration(1);
            var r = Req(1, 0);   // no players: SelectPlayer answers an invalid match
            Assert.True(w.Enqueue(r));
            var result = PollUntil(w);
            var direct = TownMatchmakingSearch.Search(Req(1, 0));
            Assert.Equal(direct.Valid, result.Match.Valid);
            Assert.False(result.Match.Valid);
        }
        finally { w.Shutdown(); }
    }
}
