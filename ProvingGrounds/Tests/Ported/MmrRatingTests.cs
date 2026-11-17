using ProvingGrounds.Core;
using static ProvingGrounds.Core.SparPodium;
using Team = ProvingGrounds.Core.MatchRules.MatchTeam;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/MmrRatingTests.cpp (one [Fact] per Expect/ExpectNear group, same values).</summary>
public class MmrRatingTests
{
    // MmrRatingTests.cpp:23-36 (strncpy(name, src, 63) cuts 63 bytes; the test names are short ASCII, so a char cut is the same)
    static FighterRow Row(int id, Team team, string name, float dealt, float taken, int hits) => new()
    {
        Id = id,
        Team = team,
        Name = name.Length > NameMaxChars ? name.Substring(0, NameMaxChars) : name,
        DamageDealt = dealt,
        DamageTaken = taken,
        HitsLanded = hits,
        EliminationIndex = -1,
    };

    static void Near(float a, float b, float eps) => Assert.True(MathF.Abs(a - b) <= eps, $"got {a} expected {b}");

    // MmrRatingTests.cpp:70-75
    static Snapshot TeamSnap()
    {
        var snap = new Snapshot { Mode = MatchRules.MatchMode.TeamAvB, Outcome = OutcomeKind.TeamAWins, FighterCount = 2 };
        snap.Fighters[0] = Row(0, Team.TeamA, "Winner", 100f, 20f, 5);
        snap.Fighters[1] = Row(1, Team.TeamB, "Loser", 40f, 80f, 2);
        return snap;
    }

    // `Snapshot upset = snap;` copies the C++ struct; the C# Snapshot is a class, so copy the fields the test sets.
    static Snapshot Copy(Snapshot s)
    {
        var c = new Snapshot { Mode = s.Mode, Outcome = s.Outcome, FighterCount = s.FighterCount, LastStandingId = s.LastStandingId };
        Array.Copy(s.Fighters, c.Fighters, s.Fighters.Length);
        return c;
    }

    [Fact]   // MmrRatingTests.cpp:40-43
    public void Rated_outcomes()
    {
        Assert.True(MmrRating.ShouldRate(OutcomeKind.TeamAWins), "rate team A");
        Assert.True(MmrRating.ShouldRate(OutcomeKind.LastStanding), "rate LMS");
        Assert.False(MmrRating.ShouldRate(OutcomeKind.Stopped), "skip stop");
        Assert.False(MmrRating.ShouldRate(OutcomeKind.Draw), "skip draw");
    }

    [Fact]   // MmrRatingTests.cpp:45-46
    public void Default_and_floor()
    {
        // upstream test expects 0 (MmrRatingTests.cpp:45); the shipped code is 100 (MmrRating.h:8), and the port copies the code.
        Assert.Equal(100f, MmrRating.DefaultMmr);
        Near(MmrRating.FloorMmr, 0.0f, 0.001f);   // "rating floor is zero"
    }

    [Fact]   // MmrRatingTests.cpp:48-50
    public void K_factor()
    {
        Assert.Equal(MmrRating.KEarly, MmrRating.KFactor(0));
        Assert.Equal(MmrRating.KEarly, MmrRating.KFactor(19));
        Assert.Equal(MmrRating.KLate, MmrRating.KFactor(20));
    }

    [Fact]   // MmrRatingTests.cpp:52
    public void Expected_score_equal() => Near(MmrRating.ExpectedScore(1000f, 1000f), 0.5f, 0.001f);

    [Fact]   // MmrRatingTests.cpp:54-60
    public void Perf_multipliers_span()
    {
        var rows = new FighterRow[2];
        rows[0] = Row(0, Team.TeamA, "A", 200f, 10f, 10);
        rows[1] = Row(1, Team.TeamB, "B", 10f, 200f, 1);
        var mult = new float[2];
        MmrRating.ComputePerfMultipliers(rows, 2, mult);
        Near(mult[0], MmrRating.PerfMultMax, 0.001f);   // "best perf max"
        Near(mult[1], MmrRating.PerfMultMin, 0.001f);   // "worst perf min"
    }

    [Fact]   // MmrRatingTests.cpp:62-68
    public void Perf_multipliers_equal()
    {
        var same = new FighterRow[2];
        same[0] = Row(0, Team.TeamA, "A", 50f, 0f, 1);
        same[1] = Row(1, Team.TeamB, "B", 50f, 0f, 1);
        var sameMult = new float[2];
        MmrRating.ComputePerfMultipliers(same, 2, sameMult);
        Near(sameMult[0], 1.0f, 0.001f);
        Near(sameMult[1], 1.0f, 0.001f);
    }

    static readonly float[] Before = { 1000f, 1000f };
    static readonly int[] Matches = { 0, 0 };
    static readonly bool[] Rate = { true, true };

    [Fact]   // MmrRatingTests.cpp:70-88
    public void Winner_gains_loser_loses()
    {
        var snap = TeamSnap();
        float[] after = new float[2];
        int[] wins = new int[2], losses = new int[2];
        MmrRating.ApplyMatch(snap, Before, Matches, Rate, after, wins, losses);
        Assert.True(after[0] > Before[0], "winner gains");
        Assert.True(after[1] < Before[1], "loser loses");
        Assert.True(wins[0] == 1 && losses[0] == 0, "winner W");
        Assert.True(wins[1] == 0 && losses[1] == 1, "loser L");
        Assert.True(after[0] - Before[0] <= MmrRating.MaxAbsDelta + 0.01f, "clamp win");
        Assert.True(Before[1] - after[1] <= MmrRating.MaxAbsDelta + 0.01f, "clamp loss");
    }

    [Fact]   // MmrRatingTests.cpp:89-98
    public void Upset_performance_softens_both_sides()
    {
        // A fighter who performs strongly despite losing should lose less rating, while a weak-performing winner receives a smaller gain.
        var upset = Copy(TeamSnap());
        upset.Fighters[0] = Row(0, Team.TeamA, "WeakWinner", 1f, 200f, 1);
        upset.Fighters[1] = Row(1, Team.TeamB, "StrongLoser", 200f, 1f, 10);
        float[] upsetAfter = new float[2];
        int[] wins = new int[2], losses = new int[2];
        MmrRating.ApplyMatch(upset, Before, Matches, Rate, upsetAfter, wins, losses);
        Assert.True(upsetAfter[0] - Before[0] < 16.0f, "weak winner gains less");
        Assert.True(Before[1] - upsetAfter[1] < 16.0f, "strong loser loses less");
    }

    [Fact]   // MmrRatingTests.cpp:100-105
    public void Stopped_changes_nothing()
    {
        var stopped = Copy(TeamSnap());
        stopped.Outcome = OutcomeKind.Stopped;
        float[] afterStop = new float[2];
        MmrRating.ApplyMatch(stopped, Before, Matches, Rate, afterStop, new int[2], new int[2]);
        Near(afterStop[0], Before[0], 0.001f);
        Near(afterStop[1], Before[1], 0.001f);
    }

    [Fact]   // MmrRatingTests.cpp:107-111
    public void Draw_changes_nothing()
    {
        var draw = Copy(TeamSnap());
        draw.Outcome = OutcomeKind.Draw;
        float[] afterDraw = new float[2];
        MmrRating.ApplyMatch(draw, Before, Matches, Rate, afterDraw, new int[2], new int[2]);
        Near(afterDraw[0], Before[0], 0.001f);
    }

    [Fact]   // MmrRatingTests.cpp:113-119
    public void Mask_skips_unrated()
    {
        bool[] rateOne = { true, false };
        float[] afterMask = new float[2];
        int[] winsM = new int[2], lossesM = new int[2];
        MmrRating.ApplyMatch(TeamSnap(), Before, Matches, rateOne, afterMask, winsM, lossesM);
        Assert.True(afterMask[0] != Before[0], "masked rates self");
        Near(afterMask[1], Before[1], 0.001f);   // "masked skips other"
    }

    [Fact]   // MmrRatingTests.cpp:121-135
    public void Loss_clamps_to_floor()
    {
        float[] lowBefore = { 5f };
        int[] lowMatches = { 0 };
        bool[] lowRate = { true };
        var lms = new Snapshot { Mode = MatchRules.MatchMode.LastStanding, Outcome = OutcomeKind.LastStanding, FighterCount = 1, LastStandingId = 99 };
        lms.Fighters[0] = Row(0, Team.None, "AlmostFloor", 1f, 1f, 0);
        float[] lowAfter = new float[1];
        MmrRating.ApplyMatch(lms, lowBefore, lowMatches, lowRate, lowAfter, new int[1], new int[1]);
        Assert.True(lowAfter[0] >= MmrRating.FloorMmr, "floor mmr");
        Near(lowAfter[0], 0.0f, 0.001f);   // "loss clamps to zero"
    }
}
