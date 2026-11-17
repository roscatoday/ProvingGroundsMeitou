using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The TEST-ONLY <c>pg spar</c> words (Core/Glue/SparArgs.cs): the original Arena window's three modes and team lists.</summary>
public class SparArgsTests
{
    static bool P(string text, out int id) => int.TryParse(text, out id);

    [Fact]
    public void Modes_AndTeams()
    {
        Assert.True(SparArgs.TryParse<int>(["avb", "1", "2", "vs", "3"], P, out var m, out var f, out var t, out _));
        Assert.Equal(MatchMode.TeamAvB, m);
        Assert.Equal([1, 2, 3], f);
        Assert.Equal([MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.TeamB], t);
        Assert.True(SparArgs.TryParse<int>(["1V1", "1", "VS", "3", "4"], P, out m, out _, out t, out _));
        Assert.Equal(MatchMode.Teams1v1, m);
        Assert.Equal([MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB], t);
        Assert.True(SparArgs.TryParse<int>(["ffa", "1", "2", "3"], P, out m, out f, out t, out _));
        Assert.Equal(MatchMode.LastStanding, m);
        Assert.All(t, x => Assert.Equal(MatchTeam.None, x));
    }

    [Theory]
    [InlineData("avb 1 2")]
    [InlineData("avb 1 vs")]
    [InlineData("avb vs 1")]
    [InlineData("avb 1 vs 2 vs 3")]
    [InlineData("ffa 1 vs 2")]
    [InlineData("duel 1 vs 2")]
    [InlineData("avb 1 vs x")]
    [InlineData("")]
    public void Refused(string words)
    {
        var args = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.False(SparArgs.TryParse<int>(args, P, out _, out _, out _, out string error));
        Assert.Equal(SparArgs.Usage, error);
    }
}
