using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.SparPodium;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/SparPodium.cs against Tests/Golden/podium.json (golden/gen_podium.cpp): header, podium places and ids exactly,
/// MVP score per fighter within GoldenFile.Near.</summary>
public class PodiumGoldenTests
{
    [Fact]
    public void BuildPodium_and_MvpScore_match()
    {
        var all = GoldenFile.Load("podium").EnumerateArray().ToList();
        Assert.NotEmpty(all);
        int ci = 0;
        foreach (var c in all)
        {
            var snap = new Snapshot
            {
                Mode = (MatchRules.MatchMode)c.GetProperty("mode").GetInt32(),
                Outcome = (OutcomeKind)c.GetProperty("outcome").GetInt32(),
                LastStandingId = c.GetProperty("last").GetInt32(),
            };
            var elim = c.GetProperty("elim").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            snap.EliminationCount = elim.Length;
            elim.CopyTo(snap.EliminationIds, 0);
            var fs = c.GetProperty("f").EnumerateArray().ToList();
            snap.FighterCount = fs.Count;
            for (int i = 0; i < fs.Count; ++i)
            {
                var f = fs[i];
                snap.Fighters[i] = new FighterRow
                {
                    Id = f.GetProperty("id").GetInt32(),
                    Team = (MatchRules.MatchTeam)f.GetProperty("team").GetInt32(),
                    Name = f.GetProperty("name").GetString()!,
                    DamageDealt = f.GetProperty("dealt").GetSingle(),
                    DamageTaken = f.GetProperty("taken").GetSingle(),
                    DamageMitigated = f.GetProperty("mit").GetSingle(),
                    HitsLanded = f.GetProperty("hits").GetInt32(),
                    Blocks = f.GetProperty("blocks").GetInt32(),
                    Misses = f.GetProperty("misses").GetInt32(),
                    Dodges = f.GetProperty("dodges").GetInt32(),
                    EliminationIndex = f.GetProperty("elimIdx").GetInt32(),
                };
                GoldenFile.Near(f.GetProperty("mvp").GetSingle(), ComputeMvpScore(snap.Fighters[i]), $"case {ci} fighter {i} mvp");
            }

            BuildPodium(snap);

            Assert.Equal(c.GetProperty("header").GetString(), snap.Header);
            var podium = c.GetProperty("podium").EnumerateArray().ToList();
            Assert.True(podium.Count == snap.PodiumCount, $"case {ci}: podium count {podium.Count} vs {snap.PodiumCount}");
            for (int i = 0; i < podium.Count; ++i)
            {
                Assert.Equal(podium[i][0].GetInt32(), snap.Podium[i].Place);
                Assert.True(podium[i][1].GetInt32() == snap.Podium[i].Fighter.Id, $"case {ci}: podium[{i}] id");
            }
            ci++;
        }
    }
}
