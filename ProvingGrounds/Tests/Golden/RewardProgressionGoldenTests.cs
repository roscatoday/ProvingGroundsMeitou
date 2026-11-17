using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.RewardProgression;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/RewardProgression.cs and Core/LeaderboardData.cs against Tests/Golden/rewardprog.json (golden/gen_rewardprog.cpp):
/// parse results, every table and every op result exactly; mmr within GoldenFile.Near (it is only stored, so it is exact anyway).</summary>
public class RewardProgressionGoldenTests
{
    static List<JsonElement> Cases(string kind)
    {
        var list = GoldenFile.Load("rewardprog").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(list);
        return list;
    }

    static List<TierEntry> TiersOf(JsonElement a) =>
        a.EnumerateArray().Select(e => new TierEntry(e[0].GetString()!, e[1].GetInt32())).ToList();

    static List<string> StringsOf(JsonElement a) => a.EnumerateArray().Select(e => e.GetString()!).ToList();

    static void SameTiers(List<TierEntry> expected, List<TierEntry> actual, string what)
    {
        Assert.True(expected.Count == actual.Count, $"{what}: count expected {expected.Count}, got {actual.Count}");
        for (int i = 0; i < expected.Count; ++i)
            Assert.True(expected[i].Id == actual[i].Id && expected[i].Tier == actual[i].Tier,
                $"{what}[{i}]: expected {expected[i].Id}={expected[i].Tier}, got {actual[i].Id}={actual[i].Tier}");
    }

    [Fact]
    public void DecodeTiers_matches()
    {
        foreach (JsonElement c in Cases("dt"))
        {
            string text = c.GetProperty("text").GetString()!;
            var e = new List<TierEntry> { new TierEntry("marker", 9) };
            bool ok = DecodeTiers(text, e);
            string w = $"dt {JsonSerializer.Serialize(text)}";
            Assert.True(ok == (c.GetProperty("ok").GetInt32() != 0), w + " ok");
            SameTiers(TiersOf(c.GetProperty("out")), e, w);
            Assert.True(EncodeTiers(e) == c.GetProperty("enc").GetString(), w + " encode");
        }
    }

    [Fact]
    public void DecodeUnlocks_matches()
    {
        foreach (JsonElement c in Cases("du"))
        {
            string text = c.GetProperty("text").GetString()!;
            var e = new List<string> { "marker" };
            bool ok = DecodeUnlocks(text, e);
            string w = $"du {JsonSerializer.Serialize(text)}";
            Assert.True(ok == (c.GetProperty("ok").GetInt32() != 0), w + " ok");
            Assert.True(StringsOf(c.GetProperty("out")).SequenceEqual(e), w + " out");
            Assert.True(EncodeUnlocks(e) == c.GetProperty("enc").GetString(), w + " encode");
        }
    }

    [Fact]
    public void Encode_matches()
    {
        foreach (JsonElement c in Cases("et"))
        {
            Assert.Equal(c.GetProperty("te").GetString(), EncodeTiers(TiersOf(c.GetProperty("tiers"))));
            Assert.Equal(c.GetProperty("ue").GetString(), EncodeUnlocks(StringsOf(c.GetProperty("unl"))));
        }
    }

    [Fact]
    public void Tier_and_unlock_ops_match()
    {
        int ci = 0;
        foreach (JsonElement c in Cases("op"))
        {
            var e = new List<TierEntry>();
            var u = new List<string>();
            int oi = 0;
            foreach (JsonElement op in c.GetProperty("ops").EnumerateArray())
            {
                int kind = op[0].GetInt32();
                string id = op[1].GetString()!;
                int a = op[2].GetInt32(), b = op[3].GetInt32(), expected = op[4].GetInt32();
                int r = kind switch
                {
                    0 => HighestTier(e, id),
                    1 => SetHighestTier(e, id, a, b) ? 1 : 0,
                    2 => RestoreHighestTier(e, id, a) ? 1 : 0,
                    3 => HasUnlock(u, id) ? 1 : 0,
                    4 => AddUnlock(u, id) ? 1 : 0,
                    _ => RemoveUnlock(u, id) ? 1 : 0,
                };
                Assert.True(r == expected, $"op case {ci} op {oi} ({kind} '{id}' {a} {b}): expected {expected}, got {r}");
                ++oi;
            }
            SameTiers(TiersOf(c.GetProperty("tiers")), e, $"op case {ci} tiers");
            Assert.True(StringsOf(c.GetProperty("unl")).SequenceEqual(u), $"op case {ci} unlocks");
            ++ci;
        }
    }

    [Fact]
    public void Leaderboard_ops_match()
    {
        int ci = 0;
        foreach (JsonElement c in Cases("lb"))
        {
            var data = new LeaderboardData.Collections();
            foreach (JsonElement op in c.GetProperty("ops").EnumerateArray())
            {
                int kind = op[0].GetInt32();
                var k = (LeaderboardData.Kind)op[1].GetInt32();
                string id = op[2].GetString()!, name = op[3].GetString()!;
                float mmr = op[4].GetSingle();
                int a = op[5].GetInt32(), b = op[6].GetInt32();
                if (kind <= 2) LeaderboardData.ApplyCompetitiveResult(data, k, id, name, mmr, a, b);
                else if (kind <= 4) LeaderboardData.AddMarks(data, id, name, a);
                else if (kind == 5) LeaderboardData.EnsureStanding(data, k, id, name).Wins = a;
                else if (kind == 6) LeaderboardData.EnsureProgression(data, id, name).Marks = a;
                else if (kind == 7) { if (a != 0) LeaderboardData.ResetFighters(data); }   // the generator prints a = whether it reset
                else data.FactionUnlocks.Add(id);
            }
            for (int t = 0; t < 2; ++t)
            {
                var rows = LeaderboardData.Standings(data, (LeaderboardData.Kind)t);
                var exp = c.GetProperty(t == 0 ? "player" : "town").EnumerateArray().ToList();
                Assert.True(exp.Count == rows.Count, $"lb case {ci} table {t} count: expected {exp.Count}, got {rows.Count}");
                for (int i = 0; i < exp.Count; ++i)
                {
                    string w = $"lb case {ci} table {t} row {i}";
                    Assert.True(rows[i].Id == exp[i][0].GetString() && rows[i].Name == exp[i][1].GetString(), w + " id/name");
                    GoldenFile.Near(exp[i][2].GetSingle(), rows[i].Mmr, w + " mmr");
                    Assert.True(rows[i].Wins == exp[i][3].GetInt32() && rows[i].Losses == exp[i][4].GetInt32() &&
                        rows[i].Matches == exp[i][5].GetInt32(), w + " record");
                }
            }
            var prog = c.GetProperty("prog").EnumerateArray().ToList();
            Assert.True(prog.Count == data.Progression.Count, $"lb case {ci} progression count");
            for (int i = 0; i < prog.Count; ++i)
            {
                string w = $"lb case {ci} progression {i}";
                Assert.True(data.Progression[i].Id == prog[i][0].GetString() && data.Progression[i].Name == prog[i][1].GetString(), w);
                Assert.True(data.Progression[i].Marks == prog[i][2].GetInt32(), w + " marks");
                SameTiers(TiersOf(prog[i][3]), data.Progression[i].RewardTiers, w + " tiers");
            }
            Assert.True(StringsOf(c.GetProperty("fu")).SequenceEqual(data.FactionUnlocks), $"lb case {ci} unlocks");
            ++ci;
        }
    }

    [Fact]
    public void Ids_versions_and_totals_match()
    {
        foreach (JsonElement c in Cases("id"))
        {
            JsonElement idv = c.GetProperty("id");
            string? id = idv.ValueKind == JsonValueKind.Null ? null : idv.GetString();
            Assert.True(LeaderboardData.IsLeaderboardId(id) == (c.GetProperty("lb").GetInt32() != 0), $"id {id}");
            Assert.True((int)LeaderboardData.KindForId(id) == c.GetProperty("kind").GetInt32(), $"kind {id}");
        }
        foreach (JsonElement c in Cases("ver"))
            Assert.True(LeaderboardData.ShouldResetFighters(c.GetProperty("v").GetInt32()) == (c.GetProperty("reset").GetInt32() != 0), $"ver {c}");
        JsonElement fm = Cases("fm")[0];
        Assert.Equal(fm.GetProperty("t0").GetInt32(), (int)LeaderboardData.ForMatch(false));
        Assert.Equal(fm.GetProperty("t1").GetInt32(), (int)LeaderboardData.ForMatch(true));
        foreach (JsonElement c in Cases("at"))
            Assert.True(LeaderboardData.AwardedTotal(c.GetProperty("v").GetInt32(), c.GetProperty("inc").GetInt32()) == c.GetProperty("r").GetInt32(), $"at {c}");
    }
}
