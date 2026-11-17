using System.Text.Json;
using ProvingGrounds.Core;
using B = ProvingGrounds.Core.TownBettingPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownBettingPolicy.cs against Tests/Golden/betting.json (golden/gen_betting.cpp; its header lists the case kinds). The
/// bookie range and the pricing levers are switched per case through the PGConfig / BalanceTuning test seams, so the C# reads both live.</summary>
public class BettingGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("betting"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static double Bits(JsonElement c, string n) => BitConverter.Int64BitsToDouble(unchecked((long)c.GetProperty(n).GetUInt64()));
    static double BitsOf(JsonElement e) => BitConverter.Int64BitsToDouble(unchecked((long)e.GetUInt64()));
    static void Same(double expected, double actual, string what) =>
        Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
            $"{what}: expected {expected:R}, actual {actual:R}");

    // std::pow (glibc, the generator) and Math.Pow (the Windows CRT) may differ by one ULP: that is glibc vs .NET, and in the shipped game
    // both the original (MSVC CRT) and the port (.NET on Windows) run on Windows, so this tolerance is about the golden harness only.
    // Per case the test works out from the C# code's own condition whether pow runs (PowRuns): when it does not, the probability is compared
    // bit for bit; when it does, to 1e-12 relative (far inside the 1e-4 of GoldenFile.Near). Every integer result is compared exactly.
    static void NearPow(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-12 * Math.Max(1.0, Math.Abs(expected)),
            $"{what}: expected {expected:R}, actual {actual:R}");

    // True when PricedProbability reaches Math.Pow: not on a guard return, not on the exponent-2.0 pass-through / WinProbability shortcut,
    // not when the ratio is within a positive knee (exponent 2.0 then); otherwise pow(ratio, exponent) runs.
    static bool PowRuns(BalanceTuning.Values tune, double own, double other)
    {
        if (!(own > 0) || !(other > 0) || !TownMatchmakingPolicy.Finite(own) || !TownMatchmakingPolicy.Finite(other)) return false;
        if (tune.PricingExponent == 2.0) return false;
        double ratio = Math.Max(own, other) / Math.Min(own, other);
        return !(tune.PricingKnee > 0 && ratio <= tune.PricingKnee);
    }

    static PGConfig.Bookie[] Bookies() =>
        Cases("bk")[0].GetProperty("v").EnumerateArray().Select(r => new PGConfig.Bookie { Minimum = r[1].GetInt32(), Maximum = r[2].GetInt32() }).ToArray();

    static BalanceTuning.Values[] Tunes() =>
        Cases("tune")[0].GetProperty("v").EnumerateArray().Select(r =>
        {
            var v = new BalanceTuning.Values();
            v.PricingExponent = BitsOf(r[1]);
            v.PricingKnee = BitsOf(r[2]);
            return v;
        }).ToArray();

    static B.Ticket ReadTicket(JsonElement a)
    {
        var t = new B.Ticket();
        t.Side = a[0].GetInt32(); t.Stake = a[1].GetInt32(); t.Payout = a[2].GetInt32(); t.Pending = a[3].GetInt32() != 0;
        return t;
    }

    static string Ctx(JsonElement c) => c.GetRawText();

    // A regenerated betting.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["adj"] = 1078, ["bk"] = 1, ["ca"] = 6849, ["rc"] = 26170, ["res"] = 768, ["st"] = 768, ["ta"] = 421, ["tune"] = 1, ["wp"] = 6112,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(42168, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Resolve_matches()
    {
        foreach (var c in Cases("res"))
        {
            B.Ticket t = ReadTicket(c.GetProperty("t"));
            B.Result r = B.Resolve(t, Int(c, "w"));
            string w = Ctx(c);
            Assert.True((int)r.Outcome == Int(c, "o"), "outcome " + w);
            Assert.True(r.WinningSide == Int(c, "ws"), "winningSide " + w);
            Assert.True(r.SelectedSide == Int(c, "ss"), "selectedSide " + w);
            Assert.True(r.Stake == Int(c, "st"), "stake " + w);
            Assert.True(r.Returned == Int(c, "rt"), "returned " + w);
            Assert.True(r.Net == Int(c, "n"), "net " + w);
        }
    }

    [Fact]
    public void Settle_matches()
    {
        foreach (var c in Cases("st"))
        {
            B.Ticket t = ReadTicket(c.GetProperty("t"));
            int r = B.Settle(ref t, Int(c, "w"));
            Assert.True(r == Int(c, "r"), "return " + Ctx(c));
            Assert.True(t.Pending == (Int(c, "p") != 0), "pending " + Ctx(c));
        }
    }

    [Fact]
    public void AdjustStake_matches_under_every_bookie_range()
    {
        var bookies = Bookies();
        foreach (var c in Cases("adj"))
        {
            using var _ = PGConfig.UseBookieForTest(bookies[Int(c, "bi")]);
            Assert.True(B.AdjustStake(Int(c, "c"), Int(c, "d")) == Int(c, "r"), Ctx(c));
        }
    }

    [Fact]
    public void Probabilities_match_under_every_pricing_tune()
    {
        var tunes = Tunes();
        foreach (var c in Cases("wp"))
        {
            using var _ = BalanceTuning.UseForTest(tunes[Int(c, "ti")]);
            double own = Bits(c, "ob"), other = Bits(c, "tb");
            Same(Bits(c, "wb"), B.WinProbability(own, other), "WinProbability " + Ctx(c));
            var tune = tunes[Int(c, "ti")];
            double priced = B.PricedProbability(own, other);
            if (!PowRuns(tune, own, other)) Same(Bits(c, "pb"), priced, "PricedProbability " + Ctx(c));
            else NearPow(Bits(c, "pb"), priced, "PricedProbability " + Ctx(c));
        }
    }

    [Fact]
    public void ReturnCats_matches_under_every_bookie_range_and_tune()
    {
        var bookies = Bookies();
        var tunes = Tunes();
        foreach (var c in Cases("rc"))
        {
            using var b = PGConfig.UseBookieForTest(bookies[Int(c, "bi")]);
            using var t = BalanceTuning.UseForTest(tunes[Int(c, "ti")]);
            // Under the pow tunes a 1-ULP glibc-vs-.NET pow flip at an integer boundary would fail this exact compare after a regenerate:
            // investigate that case (on Windows MSVC and .NET agree), do not loosen the test.
            Assert.True(B.ReturnCats(Int(c, "sk"), Bits(c, "ob"), Bits(c, "tb")) == Int(c, "r"), Ctx(c));
        }
    }

    [Fact]
    public void TeamAbility_matches()
    {
        foreach (var c in Cases("ta"))
        {
            double[]? members = Int(c, "n") < 0 ? null : c.GetProperty("m").EnumerateArray().Select(BitsOf).ToArray();
            Same(Bits(c, "r"), B.TeamAbility(members, Int(c, "count")), "TeamAbility " + Ctx(c));
        }
    }

    [Fact]
    public void CanAccept_matches_under_every_bookie_range()
    {
        var bookies = Bookies();
        foreach (var c in Cases("ca"))
        {
            using var _ = PGConfig.UseBookieForTest(bookies[Int(c, "bi")]);
            var t = new B.Ticket();
            t.Pending = Int(c, "p") != 0;
            bool r = B.CanAccept(t, Int(c, "np") != 0, Int(c, "bc") != 0, Int(c, "sd"), Int(c, "sk"), Int(c, "bal"));
            Assert.True(r == (Int(c, "r") != 0), Ctx(c));
        }
    }
}
