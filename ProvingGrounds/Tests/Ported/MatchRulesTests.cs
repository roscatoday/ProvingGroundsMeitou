using ProvingGrounds.Core;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/MatchRulesTests.cpp (one [Fact] per Expect group, same values).</summary>
public class MatchRulesTests
{
    static MatchParticipant P(int id, MatchTeam team, bool conscious, bool dead, bool eliminated = false) =>
        new() { Id = id, Team = team, Conscious = conscious, Dead = dead, Eliminated = eliminated };

    [Fact]   // MatchRulesTests.cpp:15-26
    public void AvB_opponents_and_wipe()
    {
        MatchParticipant[] avb =
        {
            P(1, MatchTeam.TeamA, true, false),
            P(2, MatchTeam.TeamA, true, false),
            P(3, MatchTeam.TeamB, true, false),
            P(4, MatchTeam.TeamB, false, false), // KO
        };
        Assert.True(IsOpponent(MatchMode.TeamAvB, avb[0], avb[2]), "AvB cross-team enemy");
        Assert.False(IsOpponent(MatchMode.TeamAvB, avb[0], avb[1]), "AvB same-team not enemy");
        Assert.Equal(MatchEndKind.None, EvaluateEnd(MatchMode.TeamAvB, avb, 4));   // AvB not wiped yet

        avb[2].Conscious = false;
        Assert.Equal(MatchEndKind.TeamWipeB, EvaluateEnd(MatchMode.TeamAvB, avb, 4));   // AvB B wiped
    }

    [Fact]   // MatchRulesTests.cpp:28-42
    public void Teams1v1_opponents_and_wipe()
    {
        MatchParticipant[] t1 =
        {
            P(1, MatchTeam.TeamA, true, false),
            P(2, MatchTeam.TeamA, true, false),
            P(3, MatchTeam.TeamB, true, false),
            P(4, MatchTeam.TeamB, true, false),
        };
        Assert.True(IsOpponent(MatchMode.Teams1v1, t1[0], t1[2]), "Teams1v1 cross-team");
        Assert.False(IsOpponent(MatchMode.Teams1v1, t1[0], t1[1]), "Teams1v1 same-team");
        Assert.Equal(MatchEndKind.None, EvaluateEnd(MatchMode.Teams1v1, t1, 4));   // all up continues

        t1[0].Conscious = false; // first A down, A2 still up
        Assert.Equal(MatchEndKind.None, EvaluateEnd(MatchMode.Teams1v1, t1, 4));   // first A KO continues

        t1[1].Conscious = false; // all A down
        Assert.Equal(MatchEndKind.TeamWipeA, EvaluateEnd(MatchMode.Teams1v1, t1, 4));   // A wiped
    }

    [Fact]   // MatchRulesTests.cpp:44-51
    public void Teams1v1_wake_up_still_wiped()
    {
        // A1 woke up after being KO'd, but is eliminated — match must still end.
        MatchParticipant[] wake =
        {
            P(1, MatchTeam.TeamA, true, false, true),   // awake but eliminated
            P(2, MatchTeam.TeamA, false, false, true),  // KO + eliminated
            P(3, MatchTeam.TeamB, true, false, false),
            P(4, MatchTeam.TeamB, true, false, false),
        };
        Assert.Equal(MatchEndKind.TeamWipeA, EvaluateEnd(MatchMode.Teams1v1, wake, 4));
    }

    [Fact]   // MatchRulesTests.cpp:53-58
    public void Last_standing()
    {
        MatchParticipant[] ls =
        {
            P(1, MatchTeam.None, false, false, false),
            P(2, MatchTeam.None, true, false, false),
            P(3, MatchTeam.None, false, false, false),
        };
        Assert.Equal(MatchEndKind.LastStanding, EvaluateEnd(MatchMode.LastStanding, ls, 3));
    }
}
