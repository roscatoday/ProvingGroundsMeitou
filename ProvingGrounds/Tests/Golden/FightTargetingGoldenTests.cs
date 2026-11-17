using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.FightTargeting;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/FightTargeting.cs against Tests/Golden/fighttargeting.json (golden/gen_fighttargeting.cpp: FightStarter.cpp:132-135, 176-277
/// copied verbatim over a stand-in Character, linked with the real MatchRules.cpp).</summary>
public class FightTargetingGoldenTests
{
    static (Fighter[] F, MatchRules.MatchTeam[] T) Read(JsonElement c)
    {
        var rows = c.GetProperty("f").EnumerateArray().ToArray();
        var f = new Fighter[rows.Length];
        var t = new MatchRules.MatchTeam[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            f[i] = new Fighter
            {
                Present = r[0].GetInt32() != 0, Valid = r[1].GetInt32() != 0, Dead = r[2].GetInt32() != 0, Unconscious = r[3].GetInt32() != 0,
                X = r[4].GetSingle(), Y = r[5].GetSingle(), Z = r[6].GetSingle(),
            };
            t[i] = (MatchRules.MatchTeam)r[7].GetInt32();
        }
        return (f, t);
    }

    [Fact]
    public void EveryCase_matches()
    {
        var cases = GoldenFile.Load("fighttargeting").EnumerateArray().ToList();
        Assert.True(cases.Count >= 2000);
        int k = 0;
        foreach (var c in cases)
        {
            var mode = (MatchRules.MatchMode)c.GetProperty("mode").GetInt32();
            int count = c.GetProperty("count").GetInt32(), a = c.GetProperty("a").GetInt32();
            var (f, t) = Read(c);
            var living = c.GetProperty("living").EnumerateArray().ToArray();
            var opp = c.GetProperty("opp").EnumerateArray().ToArray();
            var d2 = c.GetProperty("d2").EnumerateArray().ToArray();
            for (int j = 0; j < count; j++)
            {
                Assert.True((living[j].GetInt32() != 0) == IsLivingFighter(f[j]), $"case {k}: living {j}");
                Assert.True((opp[j].GetInt32() != 0) == IsLivingOpponent(mode, a, j, f, t), $"case {k}: opp {j}");
                if (d2[j].ValueKind == JsonValueKind.Number) GoldenFile.Near(d2[j].GetSingle(), HorizontalDistSq(f[a], f[j]), $"case {k}: d2 {j}");
            }
            Assert.True(c.GetProperty("nearest").GetInt32() == FindNearestLivingOpponent(mode, a, f, t, count), $"case {k}: nearest");
            Assert.True(c.GetProperty("matched").GetInt32() == FindMatchedLivingTeamOpponent(mode, a, f, t, count), $"case {k}: matched");
            k++;
        }
    }
}
