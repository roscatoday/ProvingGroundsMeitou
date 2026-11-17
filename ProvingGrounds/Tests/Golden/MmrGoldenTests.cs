using System.Text.Json;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/MmrRating.cs against Tests/Golden/mmr.json (golden/gen_mmr.cpp, shipped BalanceTuning: zeroSumRating true) and
/// mmr_nozs.json (golden/gen_mmr_nozs.cpp, the same cases with zeroSumRating false): after within GoldenFile.Near, wins/losses exactly.</summary>
public class MmrGoldenTests
{
    static void Check(string name)
    {
        var all = GoldenFile.Load(name).EnumerateArray().ToList();
        Assert.Equal(2000, all.Count);
        int ci = 0;
        foreach (JsonElement c in all)
        {
            var snap = new SparPodium.Snapshot
            {
                Mode = (MatchRules.MatchMode)c.GetProperty("mode").GetInt32(),
                Outcome = (SparPodium.OutcomeKind)c.GetProperty("outcome").GetInt32(),
                LastStandingId = c.GetProperty("last").GetInt32(),
            };
            JsonElement f = c.GetProperty("f");
            int n = f.GetArrayLength();
            snap.FighterCount = n;
            float[] before = new float[16], after = new float[16];
            int[] matches = new int[16], wins = new int[16], losses = new int[16];
            bool[] mask = new bool[16];
            for (int i = 0; i < n; ++i)
            {
                JsonElement r = f[i];
                snap.Fighters[i] = new SparPodium.FighterRow
                {
                    Id = i + 1,
                    Team = (MatchRules.MatchTeam)r.GetProperty("team").GetInt32(),
                    Name = "F" + i,
                    DamageDealt = r.GetProperty("dealt").GetSingle(),
                    DamageTaken = r.GetProperty("taken").GetSingle(),
                    DamageMitigated = r.GetProperty("mit").GetSingle(),
                    HitsLanded = r.GetProperty("hits").GetInt32(),
                    Blocks = r.GetProperty("blocks").GetInt32(),
                    Misses = r.GetProperty("misses").GetInt32(),
                    Dodges = r.GetProperty("dodges").GetInt32(),
                    EliminationIndex = r.GetProperty("elim").GetInt32(),
                };
                before[i] = r.GetProperty("before").GetSingle();
                matches[i] = r.GetProperty("matches").GetInt32();
                mask[i] = r.GetProperty("mask").GetInt32() != 0;
            }
            MmrRating.ApplyMatch(snap, before, matches, mask, after, wins, losses);
            for (int i = 0; i < n; ++i)
            {
                GoldenFile.Near(f[i].GetProperty("after").GetSingle(), after[i], $"{name} case {ci} after[{i}]");
                Assert.Equal(f[i].GetProperty("win").GetInt32(), wins[i]);
                Assert.Equal(f[i].GetProperty("loss").GetInt32(), losses[i]);
            }
            ++ci;
        }
    }

    [Fact]
    public void ApplyMatch_matches_the_original_on_2000_matches()
    {
        Assert.True(BalanceTuning.Get().ZeroSumRating);   // mmr.json was made with the shipped levers
        Check("mmr");
    }

    [Fact]
    public void ApplyMatch_without_zero_sum_matches_the_original_on_2000_matches()
    {
        using (BalanceTuning.UseForTest(new BalanceTuning.Values { ZeroSumRating = false }))
            Check("mmr_nozs");
        Assert.True(BalanceTuning.Get().ZeroSumRating);   // restored
    }
}
