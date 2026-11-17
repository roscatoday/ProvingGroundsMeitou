using ProvingGrounds.Core;
using static ProvingGrounds.Core.SparPodium;
using Team = ProvingGrounds.Core.MatchRules.MatchTeam;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/SparPodiumTests.cpp (one [Fact] per snapshot, same values).</summary>
public class SparPodiumTests
{
    // SparPodiumTests.cpp:13-26 (strncpy(name, src, 63) cuts 63 bytes; the test names are short ASCII, so a char cut is the same)
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

    [Fact]   // SparPodiumTests.cpp:30-43
    public void Team_A_top_three()
    {
        var s = new Snapshot { Mode = MatchRules.MatchMode.TeamAvB, Outcome = OutcomeKind.TeamAWins, FighterCount = 4 };
        s.Fighters[0] = Row(1, Team.TeamA, "Beep", 100f, 10f, 5);
        s.Fighters[1] = Row(2, Team.TeamA, "Ruka", 50f, 20f, 3);
        s.Fighters[2] = Row(3, Team.TeamB, "Hobbs", 200f, 5f, 9);
        s.Fighters[3] = Row(4, Team.TeamA, "Burn", 75f, 15f, 4);
        BuildPodium(s);
        Assert.Equal(3, s.PodiumCount);
        Assert.Equal(1, s.Podium[0].Fighter.Id);
        Assert.Equal(4, s.Podium[1].Fighter.Id);
        Assert.Equal(2, s.Podium[2].Fighter.Id);
        Assert.Contains("Team A", s.Header);
    }

    [Fact]   // SparPodiumTests.cpp:45-65
    public void Mvp_weighs_offense_defense_accuracy()
    {
        var mvp = new Snapshot { Mode = MatchRules.MatchMode.TeamAvB, Outcome = OutcomeKind.TeamAWins, FighterCount = 4 };
        mvp.Fighters[0] = Row(20, Team.TeamA, "Glass Cannon", 120f, 120f, 6);
        mvp.Fighters[0].Misses = 8;
        mvp.Fighters[0].EliminationIndex = 0;
        mvp.Fighters[1] = Row(21, Team.TeamA, "Guardian", 95f, 35f, 5);
        mvp.Fighters[1].DamageMitigated = 60f;
        mvp.Fighters[1].Blocks = 5;
        mvp.Fighters[1].Misses = 1;
        mvp.Fighters[1].Dodges = 3;
        mvp.Fighters[2] = Row(22, Team.TeamA, "Precise", 90f, 20f, 6);
        mvp.Fighters[2].Dodges = 1;
        mvp.Fighters[3] = Row(23, Team.TeamA, "Striker", 200f, 80f, 8);
        mvp.Fighters[3].Misses = 4;
        mvp.Fighters[3].EliminationIndex = 1;
        BuildPodium(mvp);
        Assert.Equal(23, mvp.Podium[0].Fighter.Id);   // MVP offense remains primary
        Assert.Equal(21, mvp.Podium[1].Fighter.Id);   // MVP rewards defense and efficiency
        Assert.Equal(22, mvp.Podium[2].Fighter.Id);   // MVP rewards accurate survivor
    }

    [Fact]   // SparPodiumTests.cpp:67-75
    public void Draw_has_empty_podium()
    {
        var d = new Snapshot { Mode = MatchRules.MatchMode.TeamAvB, Outcome = OutcomeKind.Draw, FighterCount = 2 };
        d.Fighters[0] = Row(1, Team.TeamA, "A", 1f, 1f, 1);
        d.Fighters[1] = Row(2, Team.TeamB, "B", 1f, 1f, 1);
        BuildPodium(d);
        Assert.Equal(0, d.PodiumCount);
        Assert.Contains("Draw", d.Header);
    }

    [Fact]   // SparPodiumTests.cpp:77-94
    public void Last_standing_winner_then_latest_knockouts()
    {
        var ls = new Snapshot { Mode = MatchRules.MatchMode.LastStanding, Outcome = OutcomeKind.LastStanding, FighterCount = 4 };
        ls.Fighters[0] = Row(10, Team.None, "FirstOut", 10f, 1f, 1);
        ls.Fighters[1] = Row(11, Team.None, "SecondOut", 20f, 1f, 1);
        ls.Fighters[2] = Row(12, Team.None, "ThirdOut", 5f, 1f, 1);
        ls.Fighters[3] = Row(13, Team.None, "Winner", 30f, 1f, 1);
        ls.EliminationCount = 3;
        ls.EliminationIds[0] = 10;
        ls.EliminationIds[1] = 11;
        ls.EliminationIds[2] = 12;
        ls.LastStandingId = 13;
        BuildPodium(ls);
        Assert.Equal(3, ls.PodiumCount);
        Assert.Equal(13, ls.Podium[0].Fighter.Id);   // LMS 1st winner
        Assert.Equal(12, ls.Podium[1].Fighter.Id);   // LMS 2nd last KO
        Assert.Equal(11, ls.Podium[2].Fighter.Id);   // LMS 3rd
    }

    [Fact]   // SparPodiumTests.cpp:96-106
    public void Stopped_ranks_everyone()
    {
        var st = new Snapshot { Mode = MatchRules.MatchMode.Teams1v1, Outcome = OutcomeKind.Stopped, FighterCount = 3 };
        st.Fighters[0] = Row(1, Team.TeamA, "A", 10f, 0f, 1);
        st.Fighters[1] = Row(2, Team.TeamB, "B", 40f, 0f, 2);
        st.Fighters[2] = Row(3, Team.TeamA, "C", 25f, 0f, 3);
        BuildPodium(st);
        Assert.Equal(3, st.PodiumCount);
        Assert.Equal(2, st.Podium[0].Fighter.Id);   // stopped #1 by damage
        Assert.Contains("Stopped", st.Header);
    }
}
