using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>SparSession.cpp's decisions (Core/Glue/SparSessionLogic.cs) over a fake host: the refusal chain and its texts, the start sequence,
/// IsSparringOpponent per mode, the Teams 1v1 bench, elimination and promotion, the stop with its results, the save abort, and the port's
/// exception safety (E3).</summary>
public class SparSessionLogicTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB, N = MatchTeam.None;

    static (SparFakeHost H, SparSessionLogic<int> S) New(int count = 4)
    {
        var h = new SparFakeHost();
        string[] names = ["Ann", "Bob", "Cat", "Dan", "Eve", "Fay"];
        for (int i = 0; i < count; i++) h.Add(i, names[i], x: i * 100);
        return (h, new SparSessionLogic<int>(-1));
    }

    // ---------- the refusals (SparSession.cpp:580-628) ----------

    [Fact]
    public void Refusals_InTheCppOrder_WithTheirTexts()
    {
        var (h, s) = New();
        h.SaveInFlight = true; h.Aftercare = true;
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.Equal(SparSessionLogic<int>.SaveInFlightText, s.GetStatus());   // port-only, first
        h.SaveInFlight = false;
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.Equal("Town medics are clearing the arena", s.GetStatus());
        h.Aftercare = false;
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0], [A], 1));
        Assert.Equal("Need at least two fighters", s.GetStatus());
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, null, [A, B], 2));
        Assert.Equal("Need at least two fighters", s.GetStatus());
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0, 0], [A, B], 2));
        Assert.Equal("Pick different fighters", s.GetStatus());
        h.C[1].Unconscious = true;
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.Equal("Fighters must be valid and conscious", s.GetStatus());
        h.C[1].Unconscious = false; h.C[1].Exists = false;
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.Equal("Fighters must be valid and conscious", s.GetStatus());
        h.C[1].Exists = true;
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, N], 2));
        Assert.Equal("Team modes require TeamA or TeamB", s.GetStatus());
        Assert.False(s.StartMatch(h, MatchMode.Teams1v1, [0, 1], [A, A], 2));
        Assert.Equal("Need at least one fighter per team", s.GetStatus());
        Assert.False(s.StartMatch(h, MatchMode.LastStanding, [0, 1], [N, A], 2));
        Assert.Equal("Last Standing requires no teams", s.GetStatus());
        h.ControlAvailable = false;
        Assert.False(s.StartMatch(h, MatchMode.Teams1v1, [0, 1], [A, B], 2));
        Assert.Equal("Teams 1v1 unavailable: bench AI control is not installed", s.GetStatus());
        Assert.False(s.IsActive);
        Assert.Empty(h.Calls);   // a refusal calls nothing
    }

    [Fact]
    public void AlreadySparring_Refused()
    {
        var (h, s) = New();
        Assert.True(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));
        Assert.False(s.StartMatch(h, MatchMode.TeamAvB, [2, 3], [A, B], 2));
        Assert.Equal("Already sparring", s.GetStatus());
    }

    // ---------- the start (SparSession.cpp:629-671) ----------

    [Fact]
    public void AvB_Start_CallsInTheCppOrder()
    {
        var (h, s) = New();
        Assert.True(s.StartMatch(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4));
        Assert.Equal(["clearwalkin", "cancelbout:Player match took priority", "remember:0,1,2,3", "resultscancel", "resultsclose", "statsreset",
            "logstart:TeamAvB", "engage:0,1,2,3:TeamAvB", "townstarted"], h.Calls);
        Assert.Equal("Sparring: Ann, Bob vs Cat, Dan", s.GetStatus());
        Assert.True(s.IsActive);
        Assert.Equal(4, s.GetParticipantCount());
        Assert.Equal(B, s.GetTeam(3));
        Assert.Equal(N, s.GetTeam(5));
        Assert.Equal(0, s.GetFighterA(-1));
        Assert.Equal(1, s.GetFighterB(-1));
    }

    [Fact]
    public void LastStanding_Status()
    {
        var (h, s) = New(3);
        Assert.True(s.StartMatch(h, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3));
        Assert.Equal("Sparring Last Standing: Ann, Bob, Cat", s.GetStatus());
        Assert.Contains("engage:0,1,2:LastStanding", h.Calls);
    }

    [Fact]
    public void Teams1v1_Start_EngagesTheFirstOfEachTeam_AndBenchesTheRest()
    {
        var (h, s) = New();
        Assert.True(s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4));
        Assert.Equal("Sparring 1v1: Ann vs Cat", s.GetStatus());
        Assert.Contains("engage:0,2:Teams1v1", h.Calls);
        Assert.Contains("look:0", h.Calls);
        Assert.Equal(0, s.GetActiveA(-1));
        Assert.Equal(2, s.GetActiveB(-1));
        Assert.Equal(0, s.GetFighterA(-1));
        Assert.Equal(2, s.GetFighterB(-1));
        // ParkBenchFighters after the engage: Bob and Dan reserved, held, recalled once
        Assert.True(s.IsBenched(1) && s.IsBenched(3));
        Assert.False(s.IsBenched(0) || s.IsBenched(2));
        Assert.Contains("reserve:1", h.Calls);
        Assert.Contains("stance:1:hold", h.Calls);
        Assert.Contains("recall:1@100", h.Calls);
        Assert.Contains("recall:3@300", h.Calls);
    }

    // ---------- IsSparringOpponent (SparSession.cpp:515-540) ----------

    [Fact]
    public void IsSparringOpponent_PerMode()
    {
        var (h, s) = New();
        Assert.False(s.IsSparringOpponent(h, 0, 2));   // inactive
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);
        Assert.True(s.IsSparringOpponent(h, 0, 2));
        Assert.False(s.IsSparringOpponent(h, 0, 1));   // teammates
        Assert.False(s.IsSparringOpponent(h, 0, 5));   // outsider
        Assert.False(s.IsSparringOpponent(h, 0, -1));
        s.StopWithStatus(h, SparStopReason.Manual, -1, null);

        var (h2, s2) = New(3);
        s2.StartMatch(h2, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        Assert.True(s2.IsSparringOpponent(h2, 0, 1));
        Assert.False(s2.IsSparringOpponent(h2, 1, 1));

        var (h3, s3) = New();
        s3.StartMatch(h3, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        Assert.True(s3.IsSparringOpponent(h3, 0, 2));
        Assert.False(s3.IsSparringOpponent(h3, 1, 2));   // benched
        Assert.False(s3.IsSparringOpponent(h3, 0, 3));
    }

    // ---------- the Teams 1v1 bout: KO, elimination, promotion in place (SparSession.cpp:100-129, :347-382, :552-578) ----------

    [Fact]
    public void Teams1v1_KO_EliminatesForGood_RetargetsTheSurvivor_PromotesTheNextInPlace()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        h.Calls.Clear();
        h.C[0].Unconscious = true;
        Assert.True(s.TickTeams1v1Bouts(h));
        Assert.True(s.IsEliminated(h, 0));
        Assert.True(s.IsRetired(0));
        // KO'd fighter alone disengaged, survivor retargeted alone, walk-in tried (refused before task 4.3), next promoted in place
        int dis = h.Calls.IndexOf("disengage:0");
        int ret = h.Calls.IndexOf("retarget:2|null");
        int walk = h.Calls.IndexOf("walkin:1");
        int eng = h.Calls.IndexOf("engage:1,2:Teams1v1");
        Assert.True(dis >= 0 && ret > dis && walk > ret && eng > walk, string.Join(" ", h.Calls));
        Assert.Equal(1, s.GetActiveA(-1));
        Assert.Equal("Sparring 1v1: Bob vs Cat", s.GetStatus());
        Assert.False(s.IsBenched(1));
        Assert.True(s.IsBenched(3));
        Assert.Equal(-1, s.InterBoutWaiting);
        // a woken eliminated fighter stays out
        h.C[0].Unconscious = false;
        h.Calls.Clear();
        Assert.False(s.TickTeams1v1Bouts(h));
        Assert.True(s.IsEliminated(h, 0));
        Assert.False(s.IsSparringOpponent(h, 0, 2));
    }

    [Fact]
    public void Teams1v1_NoNextFighter_NoPromotion()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        h.C[0].Unconscious = true;
        s.TickTeams1v1Bouts(h);
        h.C[1].Unconscious = true;
        h.Calls.Clear();
        Assert.False(s.TickTeams1v1Bouts(h));   // Bob eliminated, nobody left on team A: the watcher's EvaluateEnd ends it
        Assert.True(s.IsEliminated(h, 1));
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("engage"));
    }

    [Fact]
    public void Bench_RecallsADisplacedFighterEveryTwoSeconds_AndHoldsWithinEightUnits()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        h.Calls.Clear();
        h.C[1].X = 103;   // 3 units: inside the 8-unit radius
        h.Now += 2500;
        s.TickTeams1v1Bouts(h);
        Assert.Contains("stance:1:hold", h.Calls);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("recall:1"));
        h.Calls.Clear();
        h.C[1].X = 110;   // 10 units: outside
        h.Now += 1000;    // 3.5 s since the reservation's recall
        s.TickTeams1v1Bouts(h);
        Assert.Contains("stance:1:nohold", h.Calls);
        Assert.Contains("recall:1@100", h.Calls);
    }

    [Fact]
    public void Bench_RecallWaitsTwoSecondsAfterTheLastRecall()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);   // reservation recall at Now
        h.C[1].X = 150;
        h.Calls.Clear();
        h.Now += 1999;
        s.TickTeams1v1Bouts(h);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("recall:1"));
        h.Now += 1;
        s.TickTeams1v1Bouts(h);
        Assert.Contains("recall:1@100", h.Calls);
        // a fighter walking away (destination off the slot) is recalled even inside the radius
        h.C[1].X = 100; h.C[1].Moving = (300, 0, 0);
        h.Calls.Clear();
        h.Now += 2000;
        s.TickTeams1v1Bouts(h);
        Assert.Contains("recall:1@100", h.Calls);
    }

    // ---------- the stop (SparSession.cpp:697-775) ----------

    [Fact]
    public void StopKo_WithPendingOutcome_FreezesRatesLogsSchedulesResults_ThenDisengages()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);
        h.Calls.Clear();
        s.SetPendingEndKind(MatchEndKind.TeamWipeB);
        s.SetPendingResultsOutcome(SparPodium.OutcomeKind.TeamAWins, -1);
        Assert.Equal(MatchEndKind.TeamWipeB, s.GetPendingEndKind());
        s.StopWithStatus(h, SparStopReason.Ko, -1, "Over: Team B wiped");
        Assert.Equal(["freeze:TeamAWins:-1", "apply:Player", "logfinish:Ko", "townresult", "resultsshow:1", "clearwalkin", "disengage:0,1,2,3",
            "return"], h.Calls);
        Assert.False(s.IsActive);
        Assert.Equal("Over: Team B wiped", s.GetStatus());
        Assert.Equal(MatchEndKind.None, s.GetPendingEndKind());
        Assert.Equal(0, s.GetParticipantCount());
    }

    [Fact]
    public void ManualStop_IsOutcomeStopped_EvenWithAPendingOutcome()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        s.SetPendingResultsOutcome(SparPodium.OutcomeKind.TeamAWins, -1);
        h.Calls.Clear();
        s.Stop(h, SparStopReason.Manual, -1);
        Assert.Equal("freeze:Stopped:-1", h.Calls[0]);
        Assert.Contains("resultsshow:1", h.Calls);
        Assert.Equal("Stopped", s.GetStatus());
    }

    [Fact]
    public void StopInvalid_NoResult_AbortLogged()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        h.Calls.Clear();
        s.Stop(h, SparStopReason.Invalid, -1);
        Assert.Equal(["logabort:invalid_fighter", "townaborted", "clearwalkin", "disengage:0,1", "return"], h.Calls);
        Assert.Equal("Over: fighter invalid", s.GetStatus());
    }

    [Fact]
    public void Stop_DefaultTexts_AndReturnStatus()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        s.Stop(h, SparStopReason.Ko, 1);
        Assert.Equal("Over: Bob KO'd", s.GetStatus());
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        s.Stop(h, SparStopReason.Death, 0);
        Assert.Equal("Over: Ann died", s.GetStatus());
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        h.ReturnStatus = "Prisoners returning";
        s.Stop(h, SparStopReason.Ko, -1);
        Assert.Equal("Over: (none) KO'd\nPrisoners returning", s.GetStatus());
        h.Calls.Clear();
        s.Stop(h, SparStopReason.Manual, -1);   // not active: nothing
        Assert.Empty(h.Calls);
    }

    [Fact]
    public void Teams1v1_FinalDisengage_SkipsTheRetired_AndReleasesTheBench()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        h.C[0].Unconscious = true;
        s.TickTeams1v1Bouts(h);
        h.Calls.Clear();
        s.Stop(h, SparStopReason.Manual, -1);
        Assert.Contains("release:3", h.Calls);
        Assert.DoesNotContain("disengage:0", h.Calls);   // retired at its KO
        Assert.Contains("disengage:1", h.Calls);
        Assert.Contains("disengage:2", h.Calls);
        Assert.Contains("disengage:3", h.Calls);
    }

    // ---------- the save abort (SparSession.cpp:387-417) ----------

    [Fact]
    public void AbortForSave_DisengagesAndClears_WithoutResult()
    {
        var (h, s) = New();
        s.AbortForSave(h);
        Assert.Empty(h.Calls);   // nothing running
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        h.Calls.Clear();
        s.AbortForSave(h);
        Assert.Equal(["logabort:safe_save", "clearwalkin", "disengage:0,1", "logabandon"], h.Calls);
        Assert.False(s.IsActive);
        Assert.Equal("Match cancelled to safely save the game", s.GetStatus());
    }

    // ---------- exception safety (port-only, task 3.3 note 13) ----------

    [Fact]
    public void StartThrowingInTheEngage_DisengagesEveryone_ClearsTheSession_AndRethrows()
    {
        var (h, s) = New();
        h.ThrowOn = c => c.StartsWith("engage");
        Assert.Throws<InvalidOperationException>(() => s.StartMatch(h, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4));
        Assert.False(s.IsActive);
        Assert.Contains("logabort:start_failed", h.Calls);
        Assert.Contains("disengage:0,1,2,3", h.Calls);
        h.ThrowOn = null;
        Assert.True(s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2));   // a later start works
    }

    [Fact]
    public void StopWithAThrowingResult_StillDisengagesAndClears()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.TeamAvB, [0, 1], [A, B], 2);
        h.ThrowOn = c => c.StartsWith("apply");
        s.SetPendingResultsOutcome(SparPodium.OutcomeKind.TeamAWins, -1);
        s.StopWithStatus(h, SparStopReason.Ko, -1, "Over: Team B wiped");
        Assert.Contains("disengage:0,1", h.Calls);
        Assert.DoesNotContain("resultsshow:1", h.Calls);   // the rest of the result block was skipped
        Assert.False(s.IsActive);
        Assert.Equal("Over: Team B wiped", s.GetStatus());
    }

    [Fact]
    public void AbortForSave_OneFighterThrowing_TheOthersStillDisengage()
    {
        var (h, s) = New();
        s.StartMatch(h, MatchMode.Teams1v1, [0, 1, 2, 3], [A, A, B, B], 4);
        h.Calls.Clear();
        h.ThrowOn = c => c == "disengage:1";
        s.AbortForSave(h);
        Assert.Contains("disengage:0", h.Calls);
        Assert.Contains("disengage:2", h.Calls);
        Assert.Contains("disengage:3", h.Calls);
        Assert.Contains("logabandon", h.Calls);
        Assert.False(s.IsActive);
        Assert.Equal("Match cancelled to safely save the game", s.GetStatus());
    }

    [Fact]
    public void Eliminate_OnlyOnce_AndNotWhenInactive()
    {
        var (h, s) = New();
        Assert.False(s.EliminateFighter(h, 0));
        s.StartMatch(h, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        Assert.True(s.EliminateFighter(h, 0));
        Assert.False(s.EliminateFighter(h, 0));
        Assert.False(s.EliminateFighter(h, 5));
        Assert.True(s.IsEliminated(h, 0));
        Assert.True(s.IsParticipant(h, 2));
        Assert.False(s.IsParticipant(h, 5));
        Assert.False(s.IsSparringOpponent(h, 0, 1));
    }

    [Fact]
    public void SquaredDistance_IsTheOgreSum()
    {
        Assert.Equal(64.0f, SparSessionLogic<int>.SquaredDistance(8, 0, 0, 0, 0, 0));
        Assert.True(SparSessionLogic<int>.SquaredDistance(8, 0, 0, 0, 0, 0) <= SparSessionLogic<int>.BenchRadiusSquared);
        Assert.Equal(3f, SparSessionLogic<int>.SquaredDistance(1, 1, 1, 0, 0, 0));
    }
}
