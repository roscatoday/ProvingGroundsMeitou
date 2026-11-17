using System.Globalization;
using System.Text.Json;
using ProvingGrounds.Core;
using Xunit.Abstractions;
using static ProvingGrounds.Core.TownMatchmakingPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownMatchmakingPolicy.cs (and TownDiagnosticData's name functions) against Tests/Golden/matchmaking.json
/// (golden/gen_matchmaking.cpp; its header lists the case kinds and layouts). Ints, ids and sizes exactly; doubles within 1e-4
/// (relative above 1), equal infinities and -1 sentinels exactly.
/// <para>Accepted mismatches (task 1.10): a search whose lineup (valid, side A ids, side B ids) differs from the C++'s is accepted ONLY
/// when that case's printed best and second-best candidates sit in adjacent fit buckets and one of their distances is within 1e-5
/// relative of the boundary they share (on the fallback path: the two ratios within 1e-5 relative of each other, or one within 1e-5
/// relative of the band edge), see
/// <see cref="NearTie"/>: float rounding of <c>std::pow</c> between glibc and .NET can
/// reorder them. The search test reports how many such cases occurred and fails if they exceed 0.5 % of the searches. Any other
/// mismatch fails.</para></summary>
public class MatchmakingGoldenTests
{
    readonly ITestOutputHelper _out;
    public MatchmakingGoldenTests(ITestOutputHelper output) { _out = output; }

    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("matchmaking"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static uint UInt(JsonElement c, string n) => c.GetProperty(n).GetUInt32();
    static bool Bool(JsonElement c, string n) => c.GetProperty(n).GetInt32() != 0;
    static double D(JsonElement e) => BitConverter.Int64BitsToDouble(unchecked((long)e.GetUInt64()));
    static double D(JsonElement c, string n) => D(c.GetProperty(n));
    static double[] Ds(JsonElement a) => a.EnumerateArray().Select(D).ToArray();

    /// <summary>|e - a| &lt;= 1e-4 * max(1, |e|), or exactly equal (infinities); NaN never passes.</summary>
    static void Near(double expected, double actual, string what)
    {
        if (expected == actual) return;
        double tolerance = 1e-4 * Math.Max(1.0, Math.Abs(expected));
        if (!(Math.Abs(expected - actual) <= tolerance))
            Assert.Fail(string.Format(CultureInfo.InvariantCulture, "{0}: expected {1:R}, actual {2:R}", what, expected, actual));
    }

    static List<string> Ids(JsonElement a) => a.EnumerateArray().Select(x => x.GetString()!).ToList();
    static List<List<string>> Hist(JsonElement a) => a.EnumerateArray().Select(Ids).ToList();
    static List<Fighter> Fighters(JsonElement a) => a.EnumerateArray().Select(f => new Fighter
    {
        Id = f[0].GetString()!, Role = f[1].GetString()!, Ability = D(f[2]), Mmr = D(f[3]),
    }).ToList();

    static Dictionary<int, JsonElement> Table(string kind) => Cases(kind).ToDictionary(c => Int(c, "id"));
    static readonly Lazy<Dictionary<int, JsonElement>> Tunes = new(() => Table("tune"));
    static readonly Lazy<Dictionary<int, List<Fighter>>> Rosters = new(() => Cases("roster").ToDictionary(c => Int(c, "id"), c => Fighters(c.GetProperty("f"))));
    static readonly Lazy<Dictionary<int, List<List<string>>>> Hists = new(() => Cases("hist").ToDictionary(c => Int(c, "id"), c => Hist(c.GetProperty("h"))));

    static BalanceTuning.Values Tune(int id)
    {
        JsonElement t = Tunes.Value[id];
        return new BalanceTuning.Values
        {
            AbilityExponent = D(t, "ae"), RatingInfluence = D(t, "ri"), ShowcasePercent = Int(t, "sp"), AmbientCloseCap = D(t, "cc"),
            AmbientCloseSpan = D(t, "cs"), AmbientRatioCap = D(t, "rc"), AmbientUnderdogSpan = D(t, "us"),
        };
    }

    static IDisposable UseTune(JsonElement c) => BalanceTuning.UseForTest(c.TryGetProperty("tune", out JsonElement t) ? Tune(t.GetInt32()) : new BalanceTuning.Values());

    static bool SameLineup(JsonElement m, Match x) =>
        (m[0].GetInt32() != 0) == x.Valid && Ids(m[1]).SequenceEqual(x.A, StringComparer.Ordinal) && Ids(m[2]).SequenceEqual(x.B, StringComparer.Ordinal);

    static void AssertMatch(JsonElement m, Match x, string what)
    {
        Assert.True((m[0].GetInt32() != 0) == x.Valid, what + " valid");
        Assert.True(Ids(m[1]).SequenceEqual(x.A, StringComparer.Ordinal), $"{what} a: [{string.Join(",", Ids(m[1]))}] vs [{string.Join(",", x.A)}]");
        Assert.True(Ids(m[2]).SequenceEqual(x.B, StringComparer.Ordinal), $"{what} b: [{string.Join(",", Ids(m[2]))}] vs [{string.Join(",", x.B)}]");
        Near(D(m[3]), x.ScoreA, what + " scoreA");
        Near(D(m[4]), x.ScoreB, what + " scoreB");
        Assert.True((m[5].GetInt32() != 0) == x.Diagnostic, what + " diagnostic");
        Assert.True((m[6].GetInt32() != 0) == x.DiagnosticUnderdog, what + " diagnosticUnderdog");
        Assert.True((m[7].GetInt32() != 0) == x.BestAvailable, what + " bestAvailable");
        Assert.True(m[8].GetInt32() == (int)x.Failure, what + " failure");
        Assert.True(m[9].GetInt32() == x.DiagnosticModel, what + " diagnosticModel");
        Assert.True(m[10].GetInt32() == x.RequestedCoverage, what + " requestedCoverage");
        Assert.True(m[11].GetInt32() == x.ActualCoverage, what + " actualCoverage");
        Assert.True(m[12].GetInt32() == x.Relaxation, what + " relaxation");
        double[] sa = Ds(m[13]), sb = Ds(m[14]), pa = Ds(m[15]);
        for (int i = 0; i < 3; ++i)
        {
            Near(sa[i], x.ModelScoreA[i], $"{what} modelScoreA[{i}]");
            Near(sb[i], x.ModelScoreB[i], $"{what} modelScoreB[{i}]");
            Near(pa[i], x.ModelProbabilityA[i], $"{what} modelProbabilityA[{i}]");
        }
    }

    // TownMatchmakingPolicy.h:271-273 and :277-280: the under-band fallback's edge is rarityLow x .80 x the difficulty scale.
    static readonly double[,] RarityLowCopy = { { .25, .45, .70, 1.05 }, { .50, .75, 1.00, 1.35 }, { .85, 1.10, 1.40, 1.80 } };
    static double FallbackEdge(JsonElement c)
    {
        int diff = Int(c, "diff");
        double scale = diff == 0 ? .85 : diff == 2 ? 1.15 : 1.0;
        return RarityLowCopy[Int(c, "div"), Int(c, "rarity")] * .80 * scale;
    }

    static bool Rel(double x, double bound) => Math.Abs(x - bound) <= 1e-5 * Math.Abs(bound);

    /// <summary>Whether a lineup mismatch can come from float rounding (std::pow in glibc vs .NET). A candidate is ranked by its fit
    /// bucket <c>(int)(distance / .025)</c> first, then novelty, then the tie hash (after the coverage and style penalties on the
    /// diagnostic path). Novelty and the hash are computed from ids alone, identically in both builds, and the penalties are integers,
    /// so the only float-sensitive step is the bucket: the C++'s best and second-best must be in ADJACENT buckets (with equal
    /// penalties; buckets further apart cannot be reordered by a rounding error) and one of their distances must lie within 1e-5
    /// relative of the boundary they share, so that rounding could move it across. On the "fallback" path (Rare/Legendary strongest
    /// under-band lineup, TownMatchmakingPolicy.h:369-389) the C++ ranks by the ratio itself with a 1e-12 tie window, so a mismatch is
    /// excused when the best and second-best ratios are within 1e-5 relative of each other (and not equal), or when either lies within
    /// 1e-5 relative of the band minimum, so that rounding could move it into the band. Admission and category edge flips (the band
    /// minimum/maximum +- 1e-12, the favourable .80 band, the ambient closeCap/ratioCap, the diagnostic 1.30/1.15) are deliberately NOT
    /// excused: such a mismatch fails the test and is to be reported, not covered by widening this rule.</summary>
    static bool NearTie(JsonElement c)
    {
        JsonElement x = c.GetProperty("X");
        if (!Bool(x, "has")) return false;
        double d1 = D(x, "d"), d2 = D(x, "d2");
        if (x.GetProperty("src").GetString() == "fallback")
        {
            double edge = FallbackEdge(c);
            bool has2 = Bool(x, "has2");
            return Rel(d1, edge) || (has2 && Rel(d2, edge)) || (has2 && d1 != d2 && Rel(d1, d2));
        }
        if (!Bool(x, "has2")) return false;
        int b1 = Int(x, "fit"), b2 = Int(x, "fit2");
        if (Int(x, "cp") != Int(x, "cp2") || Int(x, "sp") != Int(x, "sp2")) return false;
        int lo = Math.Min(b1, b2), hi = Math.Max(b1, b2);
        if (hi != lo + 1) return false;
        double boundary = hi * .025;
        return Rel(d1, boundary) || Rel(d2, boundary);
    }

    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["abil"] = 300, ["amb"] = 285, ["cov"] = 49, ["diag"] = 320, ["dnames"] = 10, ["fin"] = 60, ["fm"] = 240, ["fp"] = 600,
        ["hist"] = 21, ["ht"] = 200, ["keyempty"] = 1, ["named"] = 10, ["nov"] = 200, ["rec"] = 40, ["rf"] = 300, ["ros"] = 120,
        ["roster"] = 135, ["sid"] = 240, ["sp"] = 413, ["tc"] = 1, ["tidx"] = 12, ["ts"] = 320, ["tune"] = 4,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(3881, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(3881, ExpectedCounts.Values.Sum());
    }

    // ---- small functions ----

    [Fact]
    public void Finite_ability_rating_factor()
    {
        foreach (var c in Cases("fin")) Assert.True(Finite(D(c, "vb")) == Bool(c, "o"), "fin " + c);
        foreach (var c in Cases("abil"))
        {
            double[] v = Ds(c.GetProperty("vb"));
            Near(D(c, "ob"), Ability(v[0], v[1], v[2], v[3], v[4]), "abil " + c);
        }
        foreach (var c in Cases("rf"))
        {
            Near(D(c, "ob"), RatingFactor(D(c, "mb"), D(c, "ib")), "rf " + c);
            Near(D(c, "o1b"), RatingFactor(D(c, "mb")), "rf1 " + c);
        }
    }

    [Fact]
    public void Fighter_power_and_team_score()
    {
        foreach (var c in Cases("fp"))
        {
            using var _ = UseTune(c);
            Near(D(c, "ob"), FighterPower(D(c, "ab"), D(c, "mb"), D(c, "ib")), "fp " + c);
            Near(D(c, "o2b"), FighterPower(D(c, "ab"), D(c, "mb")), "fp2 " + c);
        }
        foreach (var c in Cases("ts"))
        {
            using var _ = UseTune(c);
            double[] ab = Ds(c.GetProperty("ab")), mb = Ds(c.GetProperty("mb"));
            int n = Int(c, "n");
            Near(D(c, "o4b"), TeamScore(ab, mb, n, D(c, "ib")), "ts4 " + c);
            Near(D(c, "o3b"), TeamScore(ab, mb, n), "ts3 " + c);
            Near(D(c, "o2b"), TeamScore(ab, n), "ts2 " + c);
            Near(D(c, "onullb"), TeamScore(null, n), "tsnull " + c);
        }
    }

    [Fact]
    public void Record_history()
    {
        foreach (var c in Cases("rec"))
        {
            var h = Hist(c.GetProperty("h0"));
            foreach (var step in c.GetProperty("steps").EnumerateArray())
            {
                RecordHistory(h, Ids(step.GetProperty("ids")));
                var expected = Hist(step.GetProperty("h"));
                Assert.Equal(expected.Count, h.Count);
                for (int i = 0; i < h.Count; ++i) Assert.Equal(expected[i], h[i]);
            }
        }
    }

    [Fact]
    public void Novelty_key_types()
    {
        foreach (var c in Cases("nov"))
        {
            var pool = Fighters(c.GetProperty("pool"));
            var ids = Ids(c.GetProperty("ids"));
            var types = Detail.Types(pool);
            var expectedTypes = c.GetProperty("types").EnumerateArray().Select(p => (p[0].GetString()!, p[1].GetString()!)).ToList();
            Assert.Equal(expectedTypes, types.Select(p => (p.Key, p.Value)).ToList());
            Near(D(c, "ob"), Detail.Novelty(ids, Hist(c.GetProperty("h")), types), "nov " + c);
            Assert.Equal(c.GetProperty("key").GetString(), Detail.Key(ids));
        }
        Assert.Equal(Cases("keyempty")[0].GetProperty("key").GetString(), Detail.Key(new List<string>()));
    }

    [Fact]
    public void Coverage_type_index_named()
    {
        foreach (var c in Cases("cov")) Assert.True(Detail.CoverageFor(Int(c, "a"), Int(c, "b")) == Int(c, "o"), "cov " + c);
        foreach (var c in Cases("tidx"))
        {
            JsonElement name = c.GetProperty("name");
            Assert.True(Detail.TypeIndex(name.ValueKind == JsonValueKind.Null ? null : name.GetString()) == Int(c, "o"), "tidx " + c);
        }
        Assert.Equal(Int(Cases("tc")[0], "o"), Detail.TypeCount());
        foreach (var c in Cases("named"))
        {
            string role = c.GetProperty("role").GetString()!;
            Assert.True(Detail.Named(role) == Bool(c, "named"), "named " + c);
            Assert.True(Detail.NamedDivision(role) == Int(c, "div"), "namedDiv " + c);
        }
        foreach (var c in Cases("dnames"))
        {
            int v = Int(c, "v");
            Assert.Equal(c.GetProperty("status").GetString(), TownDiagnosticData.StatusName(v));
            Assert.Equal(c.GetProperty("model").GetString(), TownDiagnosticData.ModelName(v));
            Assert.Equal(c.GetProperty("coverage").GetString(), TownDiagnosticData.CoverageName(v));
        }
    }

    [Fact]
    public void Has_target_score_ids_fill_models()
    {
        foreach (var c in Cases("ht"))
        {
            var pool = Fighters(c.GetProperty("pool"));
            var a = c.GetProperty("a").EnumerateArray().Select(x => x.GetInt32()).ToList();
            var b = c.GetProperty("b").EnumerateArray().Select(x => x.GetInt32()).ToList();
            Assert.True(Detail.HasTarget(pool, a, b, Int(c, "type"), Int(c, "tier")) == Bool(c, "o"), "ht " + c);
        }
        foreach (var c in Cases("sid"))
        {
            using var _ = UseTune(c);
            Near(D(c, "ob"), Detail.ScoreIds(Fighters(c.GetProperty("pool")), Ids(c.GetProperty("ids")), D(c, "ib")), "sid " + c);
        }
        foreach (var c in Cases("fm"))
        {
            using var _ = UseTune(c);
            var m = new Match { A = Ids(c.GetProperty("a")), B = Ids(c.GetProperty("b")) };
            Detail.FillModels(m, Fighters(c.GetProperty("pool")), D(c, "rb"));
            AssertMatch(c.GetProperty("M"), m, "fm " + c);
        }
    }

    [Fact]
    public void Roster_sampling()
    {
        foreach (var c in Cases("ros"))
        {
            using var _ = UseTune(c);
            var input = Fighters(c.GetProperty("in"));
            var excluded = new HashSet<string>(Ids(c.GetProperty("ex")), StringComparer.Ordinal);
            string anchor = c.GetProperty("anchor").GetString()!;
            uint seed = UInt(c, "seed");
            Assert.Equal(Ids(c.GetProperty("o")), Detail.Roster(input, seed, excluded, anchor, D(c, "ib")).Select(f => f.Id).ToList());
            Assert.Equal(Ids(c.GetProperty("o2")), Detail.Roster(input, seed, excluded, anchor).Select(f => f.Id).ToList());
        }
    }

    // ---- searches ----

    [Fact]
    public void Searches_match_the_original()
    {
        int searches = 0, accepted = 0;
        var failures = new List<string>();
        void Check(JsonElement c, Match actual, string what)
        {
            ++searches;
            JsonElement m = c.GetProperty("M");
            if (!SameLineup(m, actual))
            {
                if (NearTie(c)) { ++accepted; _out.WriteLine("accepted near-tie: " + what); return; }
                failures.Add($"{what}: lineup [{string.Join(",", Ids(m[1]))}] v [{string.Join(",", Ids(m[2]))}] expected, got " +
                    $"[{string.Join(",", actual.A)}] v [{string.Join(",", actual.B)}] (valid {actual.Valid}); X {c.GetProperty("X")}");
                return;
            }
            try { AssertMatch(m, actual, what); }
            catch (Exception e) { failures.Add(e.Message); }
        }

        int index = 0;
        foreach (var c in Cases("sp"))
        {
            using var _ = UseTune(c);
            Match actual = SelectPlayer(Fighters(c.GetProperty("p")), Rosters.Value[Int(c, "r")], (Division)Int(c, "div"), UInt(c, "seed"),
                Hists.Value[Int(c, "h")], Int(c, "maxE"), Int(c, "maxT"), c.GetProperty("role").GetString()!, Int(c, "rarity"),
                Bool(c, "solo"), Bool(c, "t11"), (PGConfig.ChallengeDifficulty)Int(c, "diff"));
            Check(c, actual, $"sp #{index++}");
        }
        index = 0;
        foreach (var c in Cases("amb"))
        {
            using var _ = UseTune(c);
            uint seed = UInt(c, "seed");
            Match actual = SelectAmbient(Rosters.Value[Int(c, "r")], ref seed, Hists.Value[Int(c, "h")], Int(c, "maxT"));
            if (seed != UInt(c, "seedOut")) failures.Add($"amb #{index} seed out {seed} vs {UInt(c, "seedOut")}");
            Check(c, actual, $"amb #{index++}");
        }
        index = 0;
        foreach (var c in Cases("diag"))
        {
            using var _ = UseTune(c);
            JsonElement q = c.GetProperty("q");
            var request = new DiagnosticRequest
            {
                Active = q[0].GetInt32() != 0, Underdog = q[1].GetInt32() != 0, BalanceStyleTier = q[2].GetInt32() != 0,
                Model = q[4].GetInt32(), Coverage = q[5].GetInt32(), DesiredType = q[6].GetInt32(), DesiredTier = q[7].GetInt32(),
                Influence = D(q[8]), ReducedInfluence = D(q[9]), Seed = q[10].GetUInt32(),
            };
            for (int i = 0; i < 4; ++i) request.EnabledCoverage[i] = q[3][i].GetInt32() != 0;
            uint seed = UInt(c, "seed");
            Match actual = SelectAmbientDiagnostic(Rosters.Value[Int(c, "r")], ref seed, Hists.Value[Int(c, "h")], request, Int(c, "maxT"));
            if (seed != UInt(c, "seedOut")) failures.Add($"diag #{index} seed out {seed} vs {UInt(c, "seedOut")}");
            Check(c, actual, $"diag #{index++}");
        }

        Assert.Equal(1018, searches);   // 413 sp + 285 amb + 320 diag
        _out.WriteLine($"searches {searches}, accepted near-tie lineup mismatches {accepted} ({100.0 * accepted / searches:0.###} %)");
        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(20)));
        Assert.True(accepted * 1000 <= searches * 5, $"accepted near-tie mismatches {accepted} of {searches} exceed 0.5 %");
    }
}
