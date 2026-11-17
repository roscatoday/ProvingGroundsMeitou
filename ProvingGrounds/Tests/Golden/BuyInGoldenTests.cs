using System.Text.Json;
using ProvingGrounds.Core;
using Q = ProvingGrounds.Core.TownChallengeBuyInPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownChallengeBuyInPolicy.cs against Tests/Golden/buyin.json (golden/gen_buyin.cpp; its header lists the case kinds). The
/// challenge base per division is switched per case through the PGConfig test seam, so the C# reads it live.</summary>
public class BuyInGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("buyin"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static double Bits(JsonElement c, string n) => BitConverter.Int64BitsToDouble(unchecked((long)c.GetProperty(n).GetUInt64()));
    static string Ctx(JsonElement c) => c.GetRawText();

    static Q.Quote ReadQuote(JsonElement a) { var q = new Q.Quote(); q.BuyIn = a[0].GetInt32(); q.Payout = a[1].GetInt32(); return q; }
    static Q.Account ReadAccount(JsonElement a) { var x = new Q.Account(); x.Credit = a[0].GetInt32(); x.Escrow = a[1].GetInt32(); return x; }
    static Q.Ticket ReadTicket(JsonElement a)
    {
        var t = new Q.Ticket();
        t.Quote.BuyIn = a[0].GetInt32(); t.Quote.Payout = a[1].GetInt32(); t.Pending = a[2].GetInt32() != 0;
        return t;
    }

    static int[][] Bases() =>
        Cases("cb")[0].GetProperty("v").EnumerateArray().Select(r => new[] { r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32() }).ToArray();

    // A regenerated buyin.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["acc"] = 1296, ["ca"] = 1296, ["cb"] = 1, ["price"] = 15680, ["save"] = 9, ["settle"] = 864, ["valid"] = 108,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(19254, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Price_matches_under_every_challenge_base()
    {
        var bases = Bases();
        foreach (var c in Cases("price"))
        {
            int[] b = bases[Int(c, "ci")];
            using var _ = PGConfig.UseChallengeBaseForTest(b[0], b[1], b[2]);
            Q.Quote q = Q.Price(Int(c, "d"), Bits(c, "ob"), Bits(c, "eb"));
            Q.Quote e = ReadQuote(c.GetProperty("q"));
            Assert.True(q.BuyIn == e.BuyIn, "buyIn " + Ctx(c));
            Assert.True(q.Payout == e.Payout, "payout " + Ctx(c));
            Assert.True(q.Valid() == (Int(c, "v") != 0), "valid " + Ctx(c));
        }
    }

    [Fact]
    public void Valid_matches()
    {
        foreach (var c in Cases("valid"))
            Assert.True(ReadQuote(c.GetProperty("q")).Valid() == (Int(c, "v") != 0), Ctx(c));
    }

    [Fact]
    public void CanAccept_and_Accept_match()
    {
        foreach (var c in Cases("ca"))
        {
            var a = ReadAccount(c.GetProperty("a")); var t = ReadTicket(c.GetProperty("t")); var q = ReadQuote(c.GetProperty("q"));
            Assert.True(Q.CanAccept(a, t, q, Int(c, "bal")) == (Int(c, "r") != 0), Ctx(c));
        }
        foreach (var c in Cases("acc"))
        {
            var a = ReadAccount(c.GetProperty("a")); var t = ReadTicket(c.GetProperty("t")); var q = ReadQuote(c.GetProperty("q"));
            bool ok = Q.Accept(ref a, ref t, q, Int(c, "bal"));
            var ea = ReadAccount(c.GetProperty("a2")); var et = ReadTicket(c.GetProperty("t2"));
            string w = Ctx(c);
            Assert.True(ok == (Int(c, "r") != 0), "result " + w);
            Assert.True(a.Credit == ea.Credit && a.Escrow == ea.Escrow, "account " + w);
            Assert.True(t.Quote.BuyIn == et.Quote.BuyIn && t.Quote.Payout == et.Quote.Payout && t.Pending == et.Pending, "ticket " + w);
        }
    }

    [Fact]
    public void Settle_and_SaveCredit_match()
    {
        foreach (var c in Cases("settle"))
        {
            var a = ReadAccount(c.GetProperty("a")); var t = ReadTicket(c.GetProperty("t"));
            int r = Q.Settle(ref a, ref t, Int(c, "o"));
            var ea = ReadAccount(c.GetProperty("a2")); var et = ReadTicket(c.GetProperty("t2"));
            string w = Ctx(c);
            Assert.True(r == Int(c, "r"), "credit " + w);
            Assert.True(a.Credit == ea.Credit && a.Escrow == ea.Escrow, "account " + w);
            Assert.True(t.Quote.BuyIn == et.Quote.BuyIn && t.Quote.Payout == et.Quote.Payout && t.Pending == et.Pending, "ticket " + w);
        }
        foreach (var c in Cases("save"))
            Assert.True(Q.SaveCredit(ReadAccount(c.GetProperty("a"))) == Int(c, "r"), Ctx(c));
    }
}
