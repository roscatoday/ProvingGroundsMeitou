using System.Text.Json;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaMarks.cs against Tests/Golden/marks.json (golden/gen_marks.cpp): marks and bonuses exactly, multipliers within
/// GoldenFile.Near.</summary>
public class MarksGoldenTests
{
    static List<JsonElement> Cases(string kind)
    {
        var list = GoldenFile.Load("marks").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(list);
        return list;
    }

    [Fact]
    public void ComputeAwards_and_ApplyWinningTeamBonus_match()
    {
        int ci = 0;
        foreach (JsonElement c in Cases("aw"))
        {
            var snap = new SparPodium.Snapshot
            {
                Mode = (MatchRules.MatchMode)c.GetProperty("mode").GetInt32(),
                Outcome = (SparPodium.OutcomeKind)c.GetProperty("outcome").GetInt32(),
                LastStandingId = c.GetProperty("last").GetInt32(),
                PodiumCount = c.GetProperty("pc").GetInt32(),
            };
            snap.Podium[0].Fighter.Id = c.GetProperty("mvp").GetInt32();
            var f = c.GetProperty("f").EnumerateArray().ToList();
            snap.FighterCount = f.Count;
            float[] cs = new float[16];
            bool[] el = new bool[16];
            for (int i = 0; i < f.Count; ++i)
            {
                snap.Fighters[i].Id = f[i].GetProperty("id").GetInt32();
                snap.Fighters[i].Team = (MatchRules.MatchTeam)f[i].GetProperty("team").GetInt32();
                cs[i] = f[i].GetProperty("cs").GetSingle();
                el[i] = f[i].GetProperty("el").GetInt32() != 0;
            }
            bool hasCs = c.GetProperty("hascs").GetInt32() != 0;
            float mm = c.GetProperty("mm").GetSingle();
            int[] m = Enumerable.Repeat(-7, 16).ToArray();
            ArenaMarks.ComputeAwards(snap, hasCs ? cs : null, m, mm);
            for (int i = 0; i < f.Count; ++i)
                Assert.True(f[i].GetProperty("m0").GetInt32() == m[i], $"case {ci} m0[{i}]: expected {f[i].GetProperty("m0").GetInt32()}, got {m[i]}");
            for (int i = f.Count; i < 16; ++i)
                Assert.Equal(-7, m[i]);   // only [0, fighterCount) is written
            ArenaMarks.ApplyWinningTeamBonus(snap, el, m, c.GetProperty("bonus").GetInt32());
            for (int i = 0; i < f.Count; ++i)
                Assert.True(f[i].GetProperty("m1").GetInt32() == m[i], $"case {ci} m1[{i}]: expected {f[i].GetProperty("m1").GetInt32()}, got {m[i]}");
            ++ci;
        }
    }

    [Fact]
    public void ComputeAwards_default_multiplier_is_one()
    {
        var snap = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins, FighterCount = 1 };
        snap.Fighters[0].Team = MatchRules.MatchTeam.TeamA;
        int[] m = new int[1];
        ArenaMarks.ComputeAwards(snap, null, m);
        Assert.Equal(ArenaMarks.CompletionMarks + ArenaMarks.VictoryMarks, m[0]);
    }

    [Fact]
    public void Difficulty_matches()
    {
        foreach (JsonElement c in Cases("ad"))
        {
            float self = c.GetProperty("self").GetSingle(), opp = c.GetProperty("opp").GetSingle();
            int b = c.GetProperty("base").GetInt32();
            GoldenFile.Near(c.GetProperty("dm").GetSingle(), ArenaMarks.DifficultyMultiplier(self, opp), $"dm {self} {opp}");
            Assert.Equal(c.GetProperty("ad").GetInt32(), ArenaMarks.ApplyDifficulty(b, self, opp));
        }
    }

    [Fact]
    public void Challenge_multipliers_match()
    {
        var all = Cases("cm");
        Assert.Equal(30, all.Count);
        foreach (JsonElement c in all)
        {
            int d = c.GetProperty("div").GetInt32(), r = c.GetProperty("rar").GetInt32();
            Assert.Equal(c.GetProperty("dv").GetSingle(), ArenaMarks.DivisionMultiplier(d));
            Assert.Equal(c.GetProperty("rv").GetSingle(), ArenaMarks.RarityMultiplier(r));
            Assert.Equal(c.GetProperty("cv").GetSingle(), ArenaMarks.ChallengeMultiplier(d, r));
        }
    }

    [Fact]
    public void UniqueChallengeBonus_matches()
    {
        foreach (JsonElement c in Cases("ub"))
        {
            PGConfig.Challenges v = PGConfig.State.Shipped().Challenges;
            v.UniqueBonusMultiplier = c.GetProperty("um").GetDouble();
            v.SkarnBonusMultiplier = c.GetProperty("sm").GetDouble();
            using (PGConfig.UseChallengesForTest(v))
                Assert.Equal(c.GetProperty("ub").GetInt32(),
                    ArenaMarks.UniqueChallengeBonus(c.GetProperty("div").GetInt32(), c.GetProperty("skarn").GetInt32() != 0));
        }
    }
}
