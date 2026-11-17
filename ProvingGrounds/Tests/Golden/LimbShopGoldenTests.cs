using System.Text;
using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownLimbShopPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownLimbShopPolicy.cs against Tests/Golden/limbshop.json (golden/gen_limbshop.cpp; its header lists the case kinds).
/// Grades, qualities, prices, seeds and every trader / fighter line exactly; the approach tracker's floats exactly too (they are
/// plain float sums, so equal bits are expected), with GoldenFile.Near as the first check.</summary>
public class LimbShopGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("limbshop"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static uint UInt(JsonElement c, string n) => c.GetProperty(n).GetUInt32();
    static bool Bool(JsonElement c, string n) => c.GetProperty(n).GetInt32() != 0;
    static float F(JsonElement c, string n) => c.GetProperty(n).GetSingle();
    static string? Str(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

    static void Eq<T>(T expected, T actual, string ctx)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail($"{ctx}: expected {expected?.ToString() ?? "null"}, actual {actual?.ToString() ?? "null"}");
    }

    static void NearExact(float expected, float actual, string ctx)
    {
        GoldenFile.Near(expected, actual, ctx);
        Eq(expected, actual, ctx + " (exact)");
    }

    // A regenerated limbshop.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["app"] = 34, ["g"] = 8, ["grade"] = 884, ["kind"] = 41, ["line"] = 504, ["oname"] = 4, ["order"] = 240, ["seed"] = 240,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(1955, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        // every grade reached by the fighter-class step, and both legendary values
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Cases("order").Select(c => Int(c, "g")).Distinct().OrderBy(x => x).ToArray());
        Assert.Equal(2, Cases("order").Select(c => Bool(c, "leg")).Distinct().Count());
        Assert.Equal(1435, Cases("app").Sum(c => c.GetProperty("steps").GetArrayLength()));
    }

    [Fact]
    public void GradeFor_matches()
    {
        foreach (var c in Cases("grade"))
            Eq((Grade)Int(c, "r"), GradeFor(Int(c, "div"), Bool(c, "leg"), UInt(c, "roll")), $"grade {c.GetRawText()}");
    }

    [Fact]
    public void Quality_names_and_prices()
    {
        foreach (var c in Cases("g"))
        {
            Grade g = (Grade)Int(c, "g");
            string ctx = $"g {(int)g}";
            Eq(Int(c, "q"), QualityLevel(g), ctx + " QualityLevel");
            Eq(c.GetProperty("name").GetString(), GradeName(g), ctx + " GradeName");
            JsonElement ns = c.GetProperty("ns"), price = c.GetProperty("price");
            for (int i = 0; i < ns.GetArrayLength(); ++i)
                Eq(price[i].GetInt32(), Price(g, ns[i].GetInt32()), $"{ctx} Price({ns[i].GetInt32()})");
        }
    }

    [Fact]
    public void KindFor_and_OrderName()
    {
        foreach (var c in Cases("kind"))
        {
            string ctx = $"kind {c.GetRawText()}";
            OrderKind k = KindFor(UInt(c, "mask"));
            Eq((OrderKind)Int(c, "kind"), k, ctx);
            Eq(c.GetProperty("name").GetString(), OrderName(k), ctx + " name");
        }
        foreach (var c in Cases("oname"))
            Eq(c.GetProperty("r").GetString(), OrderName((OrderKind)Int(c, "kind")), $"oname {c.GetRawText()}");
    }

    [Fact]
    public void Trader_and_fighter_lines()
    {
        foreach (var c in Cases("line"))
        {
            Grade g = (Grade)Int(c, "g");
            uint v = UInt(c, "var");
            int kv = Int(c, "kv");
            string ctx = $"line g={(int)g} var={v} kv={kv}";
            Eq(c.GetProperty("t").GetString(), TraderLine(g, v, kv), ctx + " TraderLine");
            Eq(c.GetProperty("f").GetString(), FighterLine(g, v, kv), ctx + " FighterLine");
        }
    }

    /// <summary>The shop's Seed is Hashing.LimbShopSeed (task 1.1): the same bits for every identity (raw UTF-8 bytes, as printed).</summary>
    [Fact]
    public void Seed_is_Hashing_LimbShopSeed()
    {
        foreach (var c in Cases("seed"))
        {
            string id = c.GetProperty("id").GetString()!;
            int m = Int(c, "m");
            uint mask = UInt(c, "mask");
            string ctx = $"seed {c.GetRawText()}";
            Eq(UInt(c, "r"), Seed(id, m, mask), ctx + " Seed(string)");
            Eq(UInt(c, "r"), Seed(Encoding.UTF8.GetBytes(id), m, mask), ctx + " Seed(bytes)");
            Eq(UInt(c, "r"), Hashing.LimbShopSeed(id, m, mask), ctx + " Hashing.LimbShopSeed");
        }
    }

    /// <summary>The queue step of TownLimbShop.cpp:415-431 (glue), rebuilt from the Core functions exactly as the generator wrote it:
    /// role -&gt; catalog tier or named division -&gt; division / legendary -&gt; seed -&gt; grade, variant, quality, lines.</summary>
    [Fact]
    public void Fighter_class_orders()
    {
        foreach (var c in Cases("order"))
        {
            string? role = Str(c.GetProperty("role"));
            string ctx = $"order {c.GetRawText()}";
            TownFighterCatalog.Entry? regular = role != null ? TownFighterCatalog.Find(role) : null;
            int namedDivision = TownChallengePolicy.NamedRoleDivision(role ?? "");
            int division = regular != null ? regular.Tier : namedDivision >= 0 ? namedDivision : 0;
            bool legendary = regular == null && namedDivision == 2;
            Eq(Int(c, "div"), division, ctx + " division");
            Eq(Bool(c, "leg"), legendary, ctx + " legendary");
            uint mask = UInt(c, "mask");
            uint seed = Seed(c.GetProperty("id").GetString()!, Int(c, "m"), mask);
            Eq(UInt(c, "seed"), seed, ctx + " seed");
            Grade grade = GradeFor(division, legendary, seed);
            Eq((Grade)Int(c, "g"), grade, ctx + " grade");
            uint variant = (seed >> 8) % 3u;
            Eq(UInt(c, "var"), variant, ctx + " variant");
            Eq(Int(c, "q"), QualityLevel(grade), ctx + " quality");
            Eq(c.GetProperty("t").GetString(), TraderLine(grade, variant, (int)KindFor(mask)), ctx + " trader");
            Eq(c.GetProperty("f").GetString(), FighterLine(grade, variant, (int)KindFor(mask)), ctx + " fighter");
        }
    }

    [Fact]
    public void Approach_sequences()
    {
        int seq = 0;
        foreach (var c in Cases("app"))
        {
            var pr = new ApproachProgress();
            Eq(0.0f, pr.Elapsed, "default elapsed");
            Eq(1.0e30f, pr.BestDistance, "default bestDistance");
            Eq(0.0f, pr.Stalled, "default stalled");
            int step = 0;
            foreach (var s in c.GetProperty("steps").EnumerateArray())
            {
                string ctx = $"app seq {seq} step {step} {s.GetRawText()}";
                if (s.GetProperty("op").GetString() == "reset") ResetApproach(ref pr, F(s, "d"));
                else
                {
                    UpdateApproach(ref pr, F(s, "d"), F(s, "dt"));
                    Eq(Bool(s, "st"), ApproachStalled(pr), ctx + " stalled?");
                }
                JsonElement st = s.GetProperty("s");
                NearExact(st[0].GetSingle(), pr.Elapsed, ctx + " elapsed");
                NearExact(st[1].GetSingle(), pr.BestDistance, ctx + " bestDistance");
                NearExact(st[2].GetSingle(), pr.Stalled, ctx + " stalled");
                ++step;
            }
            ++seq;
        }
    }
}
