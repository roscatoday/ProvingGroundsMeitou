using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>KOWatcher.cpp's decisions (Core/Glue/KOWatcherLogic.cs) over the fake host and the real session logic: the 0.10 s scan, elimination
/// per mode, end detection and its status texts, the retarget after an elimination, the AvB wake-up, the keep-alive (E3).</summary>
public class KOWatcherLogicTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB, N = MatchTeam.None;

    sealed class Rig
    {
        public readonly SparFakeHost H = new();
        public readonly SparSessionLogic<int> S = new(-1);
        public readonly KOWatcherLogic<int> W = new();

        public Rig(int count)
        {
            string[] names = ["Ann", "Bob", "Cat", "Dan", "Eve", "Fay"];
            for (int i = 0; i < count; i++) H.Add(i, names[i], x: i * 100);
        }

        /// <summary>One scan: two ticks 100 ms apart (the first after a reset only starts the clock, its dt is 0; the scan then is due at once).</summary>
        public void Scan()
        {
            H.Now += 100;
            W.Tick(H, S, H.Now);
        }
    }

    [Fact]
    public void Inactive_DoesNothing()
    {
        var r = new Rig(2);
        r.Scan();
        Assert.Empty(r.H.Calls);
    }

    [Fact]
    public void ScansEvery100ms_OnTheSimClock()
    {
        var r = new Rig(2);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        r.H.Calls.Clear();
        r.W.Tick(r.H, r.S, r.H.Now);   // first frame after a reset: the scan is due (elapsed starts at the interval), dt 0
        r.H.C[1].Unconscious = true;
        r.H.Now += 50;
        r.W.Tick(r.H, r.S, r.H.Now);   // 50 ms later: not yet
        Assert.True(r.S.IsActive);
        r.H.Now += 50;
        r.W.Tick(r.H, r.S, r.H.Now);   // 100 ms: the scan sees the KO and ends the AvB 1v1
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: Team B wiped", r.S.GetStatus());
    }

    [Fact]
    public void Scanned_TrueOnlyOnATickThatScanned_StillTrueOnTheScanThatEndedTheMatch()
    {
        var r = new Rig(2);
        Assert.False(r.W.Scanned);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        r.W.Tick(r.H, r.S, r.H.Now);   // due at once after the reset
        Assert.True(r.W.Scanned);
        r.H.Now += 50;
        r.W.Tick(r.H, r.S, r.H.Now);   // between scans
        Assert.False(r.W.Scanned);
        r.H.C[1].Unconscious = true;
        r.H.Now += 50;
        r.W.Tick(r.H, r.S, r.H.Now);   // the scan that sees the KO and ends the match
        Assert.False(r.S.IsActive);
        Assert.True(r.W.Scanned);      // the PG-SPAR end line stamps this scan's tick (task 3.5)
        r.H.Now += 100;
        r.W.Tick(r.H, r.S, r.H.Now);   // nothing running: reset, no scan
        Assert.False(r.W.Scanned);
    }

    [Fact]
    public void AvB_2v2_OneDownRetargets_TheWholeTeamDownEnds()
    {
        var r = new Rig(4);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);
        r.Scan();   // records everyone conscious
        r.H.Calls.Clear();
        r.H.C[0].Unconscious = true;
        r.Scan();
        Assert.True(r.S.IsActive);
        Assert.False(r.S.IsEliminated(r.H, 0));   // AvB: nobody eliminated
        Assert.Contains("elim:0", r.H.Calls);
        Assert.Contains("retarget:0,1,2,3|1,2,3", r.H.Calls);
        // the downed fighter wakes: it rejoins (the state change retargets with it)
        r.H.Calls.Clear();
        r.H.C[0].Unconscious = false;
        r.Scan();
        Assert.Contains("retarget:0,1,2,3|0,1,2,3", r.H.Calls);
        // team A wiped
        r.H.C[0].Unconscious = true;
        r.H.C[1].Dead = true;
        r.H.Calls.Clear();
        r.Scan();
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: Team A wiped", r.S.GetStatus());
        Assert.Contains("freeze:TeamBWins:-1", r.H.Calls);
    }

    [Fact]
    public void LastStanding_3_EndsWithOneConscious_NamedInTheStatus()
    {
        var r = new Rig(3);
        r.S.StartMatch(r.H, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        r.Scan();
        r.H.Calls.Clear();
        r.H.C[0].Unconscious = true;
        r.Scan();
        Assert.True(r.S.IsEliminated(r.H, 0));   // Last Standing: KO = out for good
        Assert.Contains("retarget:0,1,2|1,2", r.H.Calls);
        r.H.C[0].Unconscious = false;            // a wake-up does not bring it back
        r.H.C[2].Unconscious = true;
        r.H.Calls.Clear();
        r.Scan();
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: Bob last standing", r.S.GetStatus());
        Assert.Contains("freeze:LastStanding:1", r.H.Calls);
    }

    [Fact]
    public void LastStanding_NoneStanding_IsADraw()
    {
        var r = new Rig(2);
        r.S.StartMatch(r.H, MatchMode.LastStanding, [0, 1], [N, N], 2);
        r.H.C[0].Unconscious = true;
        r.H.C[1].Unconscious = true;
        r.Scan();
        Assert.Equal("Over: Draw / none standing", r.S.GetStatus());
        Assert.Contains("freeze:Draw:-1", r.H.Calls);
    }

    [Fact]
    public void Teams1v1_2v2_PromotesThenEndsWhenATeamIsOut()
    {
        var r = new Rig(4);
        r.S.StartMatch(r.H, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        r.Scan();
        r.H.Calls.Clear();
        r.H.C[2].Unconscious = true;   // team B's first fighter down
        r.Scan();
        Assert.True(r.S.IsActive);
        Assert.True(r.S.IsEliminated(r.H, 2));
        Assert.Equal(3, r.S.GetActiveB(-1));
        Assert.Contains("engage:0,3:Teams1v1", r.H.Calls);
        Assert.DoesNotContain(r.H.Calls, c => c.StartsWith("freeze"));
        r.H.C[3].Unconscious = true;
        r.H.Calls.Clear();
        r.Scan();
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: Team A wins", r.S.GetStatus());
        Assert.Contains("freeze:TeamAWins:-1", r.H.Calls);
    }

    [Fact]
    public void AFighterGone_StopsInvalid()
    {
        var r = new Rig(2);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        r.H.C[1].Exists = false;
        r.Scan();
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: fighter invalid", r.S.GetStatus());
        Assert.Contains("logabort:invalid_fighter", r.H.Calls);
    }

    [Fact]
    public void KeepAlive_Every500ms()
    {
        var r = new Rig(4);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);
        r.H.Calls.Clear();
        for (int i = 0; i < 4; i++) r.Scan();
        Assert.DoesNotContain(r.H.Calls, c => c.StartsWith("keepmatch"));
        r.Scan();
        Assert.Single(r.H.Calls, c => c == "keepmatch:0,1,2,3");

        var t = new Rig(4);
        t.S.StartMatch(t.H, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        t.H.Calls.Clear();
        for (int i = 0; i < 5; i++) t.Scan();
        Assert.Single(t.H.Calls, c => c == "keep:0,2");
    }

    [Fact]
    public void Teams1v1_AWokenEliminatedFighterDoesNotKeepAWipedTeamAlive()
    {
        var r = new Rig(4);
        r.S.StartMatch(r.H, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        r.Scan();
        r.H.C[0].Unconscious = true;          // Ann out, Bob promoted
        r.Scan();
        Assert.Equal(1, r.S.GetActiveA(-1));
        r.H.C[1].Dead = true;                 // Bob dies (not eliminated in the scan: dead) and Ann wakes in the same scan
        r.H.C[0].Unconscious = false;
        r.H.Calls.Clear();
        r.Scan();
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: Team B wins", r.S.GetStatus());
        Assert.Contains("freeze:TeamBWins:-1", r.H.Calls);
    }

    [Fact]
    public void Teams1v1_KOEnd_ReleasesTheBenchedFighter()
    {
        var r = new Rig(4);
        r.H.C[3].Npc = true;
        r.S.StartMatch(r.H, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        r.Scan();
        r.H.C[0].Unconscious = true;
        r.Scan();                             // Bob walks in (promoted in place): his slot released
        Assert.Contains("release:1", r.H.Calls);
        r.H.C[1].Unconscious = true;
        r.H.Calls.Clear();
        r.Scan();
        Assert.Equal("Over: Team B wins", r.S.GetStatus());
        Assert.Contains("release:3", r.H.Calls);   // Dan never fought: released at the end
        Assert.False(r.S.IsBenched(3));
    }

    [Fact]
    public void AHostMemberThatAlwaysThrows_EndsTheMatchInvalidAfterNScans()
    {
        var r = new Rig(2);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        r.H.ThrowOnRead = true;
        for (int i = 1; i < KOWatcherLogic<int>.MaxConsecutiveFailures; i++)
        {
            r.H.Now += 100;
            Assert.Throws<InvalidOperationException>(() => r.W.TickGuarded(r.H, r.S, r.H.Now));
            Assert.True(r.S.IsActive);
        }
        r.H.Now += 100;
        Assert.Throws<InvalidOperationException>(() => r.W.TickGuarded(r.H, r.S, r.H.Now));
        Assert.False(r.S.IsActive);
        Assert.Equal("Over: fighter invalid", r.S.GetStatus());
        Assert.Contains("logabort:invalid_fighter", r.H.Calls);
        Assert.Contains("disengage:0,1", r.H.Calls);
    }

    [Fact]
    public void TickGuarded_ACompletedScanResetsTheCount()
    {
        var r = new Rig(2);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        for (int round = 0; round < 3; round++)
        {
            r.H.ThrowOnRead = true;
            r.H.Now += 100;
            Assert.Throws<InvalidOperationException>(() => r.W.TickGuarded(r.H, r.S, r.H.Now));
            r.H.Now += 100;
            Assert.Throws<InvalidOperationException>(() => r.W.TickGuarded(r.H, r.S, r.H.Now));
            r.H.ThrowOnRead = false;
            r.H.Now += 100;
            r.W.TickGuarded(r.H, r.S, r.H.Now);
        }
        Assert.True(r.S.IsActive);
    }
}
