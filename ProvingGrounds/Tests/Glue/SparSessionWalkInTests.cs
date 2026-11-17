using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>SparSession's walk-in paths (ArenaIngress is task 4.3; the fake plays it), the Death stop, the start undo by how far the start got,
/// and the bench release (task 3.3 review Q5, Q11).</summary>
public class SparSessionWalkInTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB;

    static (SparFakeHost H, SparSessionLogic<int> S) Teams1v1()
    {
        var h = new SparFakeHost();
        string[] names = ["Ann", "Bob", "Cat", "Dan"];
        for (int i = 0; i < 4; i++) h.Add(i, names[i], x: i * 100);
        var s = new SparSessionLogic<int>(-1);
        Assert.True(s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4));
        h.Calls.Clear();
        return (h, s);
    }

    [Fact]
    public void EliminatingTheWalkInFighter_CancelsTheWalkIn()
    {
        var (h, s) = Teams1v1();
        h.WalkInFighter = 1; h.WalkInPending = true;
        Assert.True(s.EliminateFighter(h, 1));
        Assert.Contains("clearwalkin", h.Calls);
        Assert.Equal(-1, h.WalkInFighter);
        Assert.Contains("disengage:1", h.Calls);
        Assert.True(s.IsRetired(1));
    }

    [Fact]
    public void OneWalkInAtATime_APendingWalkInHoldsThePromotion()
    {
        var (h, s) = Teams1v1();
        int calls = 0;
        h.PendingSequence = () => calls++ > 0;   // not pending at the top of TickTeams1v1Bouts, pending when the promotion asks
        h.C[0].Unconscious = true;
        Assert.True(s.TickTeams1v1Bouts(h));
        Assert.True(s.IsEliminated(h, 0));
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("walkin:"));
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("engage"));
        Assert.Equal(0, s.GetActiveA(-1));                 // not promoted yet
        Assert.Equal("Sparring 1v1: Ann vs Cat", s.GetStatus());
    }

    [Fact]
    public void WalkIn_PendingRefusesTheSparringPair_ArrivalEngagesTheNewPair()
    {
        var (h, s) = Teams1v1();
        h.WalkInStarts = true;
        h.C[0].Unconscious = true;
        Assert.True(s.TickTeams1v1Bouts(h));
        Assert.Contains("walkin:1", h.Calls);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("engage"));
        Assert.Equal(1, s.GetActiveA(-1));
        Assert.Equal(2, s.InterBoutWaiting);
        Assert.Equal("Ann KO'd — Bob walking in", s.GetStatus());
        Assert.False(s.IsSparringOpponent(h, 1, 2));      // walk-in pending: nobody is a sparring opponent
        Assert.True(s.HasPendingWalkIn(h));
        h.Calls.Clear();
        Assert.True(s.TickTeams1v1Bouts(h));               // still walking
        Assert.Equal(["tickwalkin"], h.Calls.Where(c => !c.StartsWith("stance") && !c.StartsWith("recall") && !c.StartsWith("reserve")));
        h.WalkInArrived = true;
        h.Calls.Clear();
        Assert.True(s.TickTeams1v1Bouts(h));
        int tick = h.Calls.IndexOf("tickwalkin"), clear = h.Calls.IndexOf("clearwalkin"), eng = h.Calls.IndexOf("engage:1,2:Teams1v1");
        Assert.True(tick >= 0 && clear > tick && eng > clear, string.Join(" ", h.Calls));
        Assert.Equal("Sparring 1v1: Bob vs Cat", s.GetStatus());
        Assert.Equal(-1, s.InterBoutWaiting);
        Assert.True(s.IsSparringOpponent(h, 1, 2));
    }

    [Fact]
    public void DeathStop_UsesThePendingOutcome_AndTheDiedText()
    {
        var (h, s) = Teams1v1();
        s.SetPendingResultsOutcome(SparPodium.OutcomeKind.TeamAWins, -1);
        s.Stop(h, SparStopReason.Death, 2);
        Assert.Equal("freeze:TeamAWins:-1", h.Calls[0]);
        Assert.Contains("logfinish:Death", h.Calls);
        Assert.Contains("resultsshow:1", h.Calls);
        Assert.Equal("Over: Cat died", s.GetStatus());
    }

    [Fact]
    public void Teams1v1StartThrowingAfterTheEngage_ReleasesTheBench_AndDisengagesEachFighter()
    {
        var h = new SparFakeHost();
        for (int i = 0; i < 4; i++) h.Add(i, "F" + i, x: i * 100);
        var s = new SparSessionLogic<int>(-1);
        h.ThrowOn = c => c == "townstarted";
        Assert.Throws<InvalidOperationException>(() => s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4));
        Assert.Contains("release:1", h.Calls);
        Assert.Contains("release:3", h.Calls);
        foreach (var d in new[] { "disengage:0", "disengage:1", "disengage:2", "disengage:3" }) Assert.Contains(d, h.Calls);
        Assert.Contains("logabort:start_failed", h.Calls);
        Assert.False(s.IsActive);
        Assert.Equal(SparSessionLogic<int>.StartFailedText, s.GetStatus());
    }

    [Fact]
    public void StartUndo_OnlyWhatTheStartGotTo()
    {
        var h = new SparFakeHost();
        for (int i = 0; i < 2; i++) h.Add(i, "F" + i);
        var s = new SparSessionLogic<int>(-1);
        h.ThrowOn = c => c == "statsreset";   // after the remember, before the combat log
        Assert.Throws<InvalidOperationException>(() => s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.Contains("disengage:0,1", h.Calls);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("logabort"));
        h.Calls.Clear();
        h.ThrowOn = c => c.StartsWith("cancelbout");   // before the session was active: nothing to undo
        Assert.Throws<InvalidOperationException>(() => s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("disengage") || c.StartsWith("logabort"));
        Assert.False(s.IsActive);
    }

    [Fact]
    public void FailuresAreCounted()
    {
        var h = new SparFakeHost();
        for (int i = 0; i < 4; i++) h.Add(i, "F" + i, x: i * 100);
        var lines = new List<string>();
        var s2 = new SparSessionLogic<int>(-1, lines.Add);
        Assert.True(s2.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4));
        h.ThrowOn = c => c.StartsWith("disengage:");
        s2.Stop(h, SparStopReason.Manual, -1);
        Assert.Equal(4, s2.FailureCount);          // each fighter's disengage threw
        Assert.Single(lines);                      // logged once for the step name "disengage"
    }
}
