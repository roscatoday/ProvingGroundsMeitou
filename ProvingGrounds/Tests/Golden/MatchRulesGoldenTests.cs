using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/MatchRules.cs against Tests/Golden/matchrules.json (golden/gen_matchrules.cpp).</summary>
public class MatchRulesGoldenTests
{
    static IEnumerable<JsonElement> Cases(string kind)
    {
        var all = GoldenFile.Load("matchrules").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(all);
        return all;
    }

    static MatchParticipant P(JsonElement a) => new()
    {
        Id = a[0].GetInt32(), Team = (MatchTeam)a[1].GetInt32(), Conscious = a[2].GetInt32() != 0,
        Dead = a[3].GetInt32() != 0, Eliminated = a[4].GetInt32() != 0,
    };

    [Fact]
    public void EvaluateEnd_matches()
    {
        int i = 0;
        foreach (var c in Cases("end"))
        {
            var list = c.GetProperty("p").EnumerateArray().Select(P).ToArray();
            bool isNull = c.GetProperty("null").GetInt32() != 0;
            var mode = (MatchMode)c.GetProperty("mode").GetInt32();
            int pct = c.GetProperty("pct").GetInt32();
            // the C++ list is a fixed array of 8; the count says how many are read
            var padded = new MatchParticipant[8];
            list.CopyTo(padded, 0);
            var end = EvaluateEnd(mode, isNull ? null : padded, list.Length, pct);
            Assert.True(c.GetProperty("end").GetInt32() == (int)end, $"case {i}: end");
            Assert.Equal(c.GetProperty("name").GetString(), EndKindName(end));
            if (pct == 0)
                Assert.Equal(end, EvaluateEnd(mode, isNull ? null : padded, list.Length));   // default argument is 0
            i++;
        }
    }

    [Fact]
    public void IsOpponent_matches()
    {
        foreach (var c in Cases("opp"))
            Assert.Equal(c.GetProperty("opp").GetInt32() != 0,
                IsOpponent((MatchMode)c.GetProperty("mode").GetInt32(), P(c.GetProperty("a")), P(c.GetProperty("b"))));
    }

    [Fact]
    public void EndKindName_matches()
    {
        foreach (var c in Cases("name"))
            Assert.Equal(c.GetProperty("name").GetString(), EndKindName((MatchEndKind)c.GetProperty("kind").GetInt32()));
    }
}
