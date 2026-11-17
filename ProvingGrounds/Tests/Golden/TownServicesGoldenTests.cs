using System.Text.Json;
using ProvingGrounds.Core;
using SP = ProvingGrounds.Core.TownSpectatorPolicy;
using CP = ProvingGrounds.Core.TownCorpseCleanupPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownSpectatorPolicy.cs and Core/TownCorpseCleanupPolicy.cs against Tests/Golden/town_services.json
/// (golden/gen_town_services.cpp; its header lists the case kinds). Boolean inputs are exhaustive; ints, enums and bits exactly,
/// floats exactly (no arithmetic beyond one float add) after GoldenFile.Near.</summary>
public class TownServicesGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("town_services"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static bool Bool(JsonElement e) => e.GetInt32() != 0;
    static bool Bool(JsonElement c, string n) => Bool(c.GetProperty(n));
    static bool[] Bools(JsonElement e) => e.EnumerateArray().Select(Bool).ToArray();
    static double D(JsonElement e) => e.ValueKind == JsonValueKind.String
        ? (e.GetString() == "inf" ? double.PositiveInfinity : e.GetString() == "-inf" ? double.NegativeInfinity : throw new FormatException(e.GetString()))
        : e.GetDouble();
    static float Fl(JsonElement e) => e.ValueKind == JsonValueKind.String
        ? (e.GetString() == "inf" ? float.PositiveInfinity : e.GetString() == "-inf" ? float.NegativeInfinity : throw new FormatException(e.GetString()))
        : e.GetSingle();

    static void Eq<T>(T expected, T actual, string ctx)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail($"{ctx}: expected {expected?.ToString() ?? "null"}, actual {actual?.ToString() ?? "null"}");
    }

    // A regenerated town_services.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["c.adv"] = 110, ["c.elig"] = 16, ["c.furnace"] = 32, ["c.handler"] = 64, ["c.next"] = 16, ["c.release"] = 256,
        ["c.select"] = 264, ["c.timed"] = 63, ["s.arena"] = 196, ["s.next"] = 256, ["s.release"] = 16, ["s.reserve"] = 224,
        ["s.role"] = 9, ["s.watch"] = 32,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(1554, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(new[] { -1, 0, 1, 2 }, Cases("s.next").Select(c => Int(c, "r")).Distinct().OrderBy(x => x).ToArray());
        Assert.Equal(new[] { -1, 0, 1, 2, 3, 4, 5, 6, 9 }, Cases("c.select").Select(c => Int(c, "r")).Distinct().OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Spectator_policy()
    {
        foreach (var c in Cases("s.role"))
        {
            JsonElement id = c.GetProperty("id");
            Eq((SP.AudienceRole)Int(c, "r"), SP.RoleFor(id.ValueKind == JsonValueKind.Null ? null : id.GetString()), $"s.role {c.GetRawText()}");
        }
        foreach (var c in Cases("s.watch"))
            Eq(Bool(c, "r"), SP.CanWatch((SP.AudienceRole)Int(c, "role"), Bool(c, "p"), Bool(c, "j"), Bool(c, "h")), $"s.watch {c.GetRawText()}");
        foreach (var c in Cases("s.reserve"))
            Eq(Bool(c, "r"), SP.CanReserve(Bool(c, "f"), Bool(c, "rs"), Int(c, "a"), Int(c, "t")), $"s.reserve {c.GetRawText()}");
        foreach (var c in Cases("s.next"))
            Eq((SP.AudienceRole)Int(c, "r"), SP.NextRole(Int(c, "c"), Int(c, "rs"), Int(c, "h"), (SP.AudienceRole)Int(c, "cur")),
                $"s.next {c.GetRawText()}");
        foreach (var c in Cases("s.release"))
            Eq((SP.ReleaseDisposition)Int(c, "r"), SP.ReleaseFor(Bool(c, "v"), Bool(c, "d"), Bool(c, "p"), Bool(c, "h")),
                $"s.release {c.GetRawText()}");
        foreach (var c in Cases("s.arena"))
            Eq(Bool(c, "r"), SP.BelongsToArena(Bool(c, "st"), D(c.GetProperty("d")), D(c.GetProperty("o"))), $"s.arena {c.GetRawText()}");
    }

    [Fact]
    public void Corpse_cleanup_policy()
    {
        foreach (var c in Cases("c.elig"))
        {
            bool[] a = Bools(c.GetProperty("a"));
            Eq(Bool(c, "r"), CP.EligibleCorpse(a[0], a[1], a[2], a[3]), $"c.elig {c.GetRawText()}");
        }
        foreach (var c in Cases("c.handler"))
        {
            bool[] a = Bools(c.GetProperty("a"));
            Eq(Bool(c, "r"), CP.HandlerReady(a[0], a[1], a[2], a[3], a[4], a[5]), $"c.handler {c.GetRawText()}");
        }
        foreach (var c in Cases("c.furnace"))
        {
            bool[] a = Bools(c.GetProperty("a"));
            Eq(Bool(c, "r"), CP.FurnaceReady(a[0], a[1], a[2], a[3], a[4]), $"c.furnace {c.GetRawText()}");
        }
        foreach (var c in Cases("c.next"))
        {
            bool[] a = Bools(c.GetProperty("a"));
            Eq((CP.Action)Int(c, "r"), CP.NextAction(a[0], a[1], a[2], a[3]), $"c.next {c.GetRawText()}");
        }
        foreach (var c in Cases("c.release"))
        {
            bool[] a = Bools(c.GetProperty("a"));
            Eq((CP.ReleaseDisposition)Int(c, "r"), CP.ReleaseFor(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7]), $"c.release {c.GetRawText()}");
        }
        foreach (var c in Cases("c.timed"))
        {
            float e = Fl(c.GetProperty("e")), l = Fl(c.GetProperty("l"));
            string ctx = $"c.timed {c.GetRawText()}";
            Eq(Bool(c, "r"), CP.TimedOut(e, l), ctx + " TimedOut");
            Eq(Bool(c, "b0"), CP.BatchTimedOut(false, e, l), ctx + " BatchTimedOut(false)");
            Eq(Bool(c, "b1"), CP.BatchTimedOut(true, e, l), ctx + " BatchTimedOut(true)");
        }
        foreach (var c in Cases("c.adv"))
        {
            string ctx = $"c.adv {c.GetRawText()}";
            float r = CP.AdvanceElapsed(Fl(c.GetProperty("e")), Fl(c.GetProperty("d")), Bool(c, "pz"));
            GoldenFile.Near(Fl(c.GetProperty("r")), r, ctx);
            Eq(Fl(c.GetProperty("r")), r, ctx + " (exact)");
        }
        foreach (var c in Cases("c.select"))
        {
            JsonElement el = c.GetProperty("el"), rs = c.GetProperty("rs");
            Eq(Int(c, "r"), CP.SelectCorpse(el.ValueKind == JsonValueKind.Null ? null : Bools(el),
                rs.ValueKind == JsonValueKind.Null ? null : Bools(rs), Int(c, "n"), Int(c, "cur")), $"c.select {c.GetRawText()}");
        }
    }
}
