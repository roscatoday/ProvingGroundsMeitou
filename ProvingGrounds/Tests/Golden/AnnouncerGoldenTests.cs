using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownAnnouncerPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownAnnouncerPolicy.cs against Tests/Golden/announcer.json (golden/gen_announcer.cpp; its header lists the case
/// kinds). Ints, enums, strings (every script line, byte for byte) and bits exactly; floats with GoldenFile.Near.</summary>
public class AnnouncerGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("announcer"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static uint UInt(JsonElement c, string n) => c.GetProperty(n).GetUInt32();
    static bool Bool(JsonElement e) => e.GetInt32() != 0;
    static bool Bool(JsonElement c, string n) => Bool(c.GetProperty(n));
    static float F(JsonElement c, string n) => c.GetProperty(n).GetSingle();
    static string? Str(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();
    static Program P(JsonElement c, string n) => (Program)Int(c, n);

    static void Eq<T>(T expected, T actual, string ctx)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail($"{ctx}: expected {expected?.ToString() ?? "null"}, actual {actual?.ToString() ?? "null"}");
    }

    // A regenerated announcer.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["adv"] = 40, ["at"] = 5400, ["chain"] = 128, ["eq"] = 100, ["prog"] = 144, ["rel"] = 8, ["res"] = 56, ["sel"] = 3100,
        ["stall"] = 31, ["start"] = 200, ["vroll"] = 136,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(9343, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        // every program reached by Select, by the chain, and every Select branch with a NULL roles pointer
        Assert.Equal(new[] { 0, 1, 2, 3 }, Cases("sel").Select(c => Int(c, "r")).Distinct().OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, Cases("chain").Select(c => Int(c, "r")).Distinct().OrderBy(x => x).ToArray());
        Assert.Equal(826, Cases("stall").Sum(c => c.GetProperty("steps").GetArrayLength()));
    }

    [Fact]
    public void Equals_and_IsSpot()
    {
        foreach (var c in Cases("eq"))
        {
            string? a = Str(c.GetProperty("a")), b = Str(c.GetProperty("b"));
            string ctx = $"eq {c.GetRawText()}";
            Eq(Bool(c, "r"), TownAnnouncerPolicy.Equals(a, b), ctx + " Equals");
            Eq(Bool(c, "sa"), IsSpot(a), ctx + " IsSpot(a)");
            Eq(Bool(c, "sb"), IsSpot(b), ctx + " IsSpot(b)");
        }
    }

    [Fact]
    public void Select_matches()
    {
        foreach (var c in Cases("sel"))
        {
            JsonElement rj = c.GetProperty("roles");
            // An empty list was passed to the C++ as NULL (a std::vector's empty data()); the C++ loop never reads it.
            string?[]? roles = rj.ValueKind == JsonValueKind.Null || rj.GetArrayLength() == 0
                ? null : rj.EnumerateArray().Select(Str).ToArray();
            Eq((Program)Int(c, "r"), Select(Bool(c, "pm"), Int(c, "slot"), roles, Int(c, "n")), $"sel {c.GetRawText()}");
        }
    }

    [Fact]
    public void Programs_cues_texts_and_durations()
    {
        foreach (var c in Cases("prog"))
        {
            Program p = P(c, "p");
            int v = Int(c, "v");
            string ctx = $"prog p={(int)p} v={v}";
            Eq(Int(c, "rs"), RequiredSpeakers(p), ctx + " RequiredSpeakers");
            Eq(Int(c, "vc"), VariantCount(p), ctx + " VariantCount");
            Eq(Int(c, "var"), Variant(p, unchecked((uint)v)), ctx + " Variant");
            Eq(Int(c, "sc"), SetupCueCount(p, v), ctx + " SetupCueCount");
            Eq(Int(c, "cc"), CueCount(p, v), ctx + " CueCount");
            if (v == 0) Eq(Int(c, "cc"), CueCount(p), ctx + " CueCount(default variant)");
            JsonElement sp = c.GetProperty("sp"), cs = c.GetProperty("cs"), ct = c.GetProperty("ct");
            for (int i = -1; i <= 3; ++i) Eq(Str(sp[i + 1]), SpeakerRole(p, i), $"{ctx} SpeakerRole({i})");
            for (int cue = -1; cue <= 6; ++cue)
            {
                Eq(cs[cue + 1].GetInt32(), CueSpeaker(p, cue, v), $"{ctx} CueSpeaker({cue})");
                if (v == 0) Eq(cs[cue + 1].GetInt32(), CueSpeaker(p, cue), $"{ctx} CueSpeaker({cue}, default)");
                Eq(Str(ct[cue + 1]), CueText(p, v, cue), $"{ctx} CueText({cue})");
            }
            GoldenFile.Near(F(c, "du"), Duration(p, v), ctx + " Duration");
            Eq(F(c, "du"), Duration(p, v), ctx + " Duration (exact)");
            if (v == 0) Eq(F(c, "du"), Duration(p), ctx + " Duration(default)");
        }
    }

    [Fact]
    public void Variant_rolls()
    {
        foreach (var c in Cases("vroll"))
            Eq(Int(c, "r"), Variant(P(c, "p"), UInt(c, "roll")), $"vroll {c.GetRawText()}");
    }

    [Fact]
    public void Cue_starts()
    {
        foreach (var c in Cases("start"))
        {
            float r = CueStart(P(c, "p"), Int(c, "cue"), Int(c, "v"));
            Eq(F(c, "r"), r, $"start {c.GetRawText()}");
            if (Int(c, "v") == 0) Eq(F(c, "r"), CueStart(P(c, "p"), Int(c, "cue")), $"start {c.GetRawText()} default");
        }
    }

    [Fact]
    public void CueAt_and_CuesDue()
    {
        foreach (var c in Cases("at"))
        {
            Program p = P(c, "p");
            int v = Int(c, "v"), last = Int(c, "last");
            float e = F(c, "e");
            string ctx = $"at {c.GetRawText()}";
            Eq(Int(c, "at"), CueAt(p, last, e, v), ctx + " CueAt");
            Eq(Int(c, "due"), CuesDue(p, last, e, v), ctx + " CuesDue");
            if (v == 0)
            {
                Eq(Int(c, "at"), CueAt(p, last, e), ctx + " CueAt default");
                Eq(Int(c, "due"), CuesDue(p, last, e), ctx + " CuesDue default");
            }
        }
        // -0 is printed as "-0" and must read back as negative zero (CueStart 0 == -0, so CueAt fires)
        Assert.Contains(Cases("at"), c => c.GetProperty("e").GetRawText() == "-0");
    }

    [Fact]
    public void Resolve_and_NextFallback()
    {
        foreach (var c in Cases("res"))
        {
            string ctx = $"res {c.GetRawText()}";
            Eq((Program)Int(c, "r"), Resolve(P(c, "p"), Bool(c, "spot"), Int(c, "n")), ctx + " Resolve");
            Eq((Program)Int(c, "nf"), NextFallback(P(c, "p")), ctx + " NextFallback");
        }
    }

    /// <summary>The cast-fallback loop of TownAnnouncer.cpp:300-317 (glue), rebuilt from the policy functions exactly as the
    /// generator wrote it: the same candidates must be tried and the same program must result.</summary>
    [Fact]
    public void Cast_fallback_chain()
    {
        // the generator's castIds, in its bit order (bit 0 = House announcer 260, 1 = Skarn 167, 2 = Senn 169, 3 = Torka 168)
        string[] castIds = { PgRecords.ScratchAnnouncer, PgRecords.Skarn, PgRecords.Senn, PgRecords.Torka };
        Assert.Equal(new[] { "260-Proving Grounds.mod", "167-Proving Grounds.mod", "169-Proving Grounds.mod", "168-Proving Grounds.mod" }, castIds);
        foreach (var c in Cases("chain"))
        {
            bool spot = Bool(c, "spot");
            uint have = UInt(c, "have");
            var tried = new List<int>();
            Program candidate = P(c, "req");
            int found = 0;
            while (spot && candidate != Program.LegacyCountdown)
            {
                tried.Add((int)candidate);
                found = 0;
                int candidateCount = RequiredSpeakers(candidate);
                for (int i = 0; i < candidateCount; ++i)
                {
                    string? role = SpeakerRole(candidate, i);
                    for (int k = 0; k < 4; ++k)
                        if ((have & (1u << k)) != 0 && TownAnnouncerPolicy.Equals(role, castIds[k])) { ++found; break; }
                }
                if (found == candidateCount) break;
                candidate = NextFallback(candidate);
            }
            string ctx = $"chain {c.GetRawText()}";
            Assert.True(c.GetProperty("tried").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(tried),
                $"{ctx}: tried [{string.Join(",", tried)}]");
            Eq((Program)Int(c, "r"), Resolve(candidate, spot, found), ctx + " result");
        }
    }

    [Fact]
    public void AdvanceApproach_matches()
    {
        foreach (var c in Cases("adv"))
            Eq(F(c, "r"), AdvanceApproach(F(c, "e"), F(c, "d"), Bool(c, "pz")), $"adv {c.GetRawText()}");
    }

    [Fact]
    public void ApproachStalled_sequences()
    {
        int seq = 0;
        foreach (var c in Cases("stall"))
        {
            var pr = new ApproachProgress();
            Eq(0.0f, pr.BestDistance, "default bestDistance");
            Eq(0.0f, pr.StalledElapsed, "default stalledElapsed");
            int step = 0;
            foreach (var s in c.GetProperty("steps").EnumerateArray())
            {
                string ctx = $"stall seq {seq} step {step} {s.GetRawText()}";
                if (s.GetProperty("op").GetString() == "reset") ResetApproachProgress(ref pr, F(s, "d"));
                else Eq(Bool(s, "r"), ApproachStalled(ref pr, F(s, "d"), F(s, "dt"), Bool(s, "pz")), ctx + " result");
                JsonElement st = s.GetProperty("s");
                Eq(st[0].GetSingle(), pr.BestDistance, ctx + " bestDistance");
                Eq(st[1].GetSingle(), pr.StalledElapsed, ctx + " stalledElapsed");
                ++step;
            }
            ++seq;
        }
    }

    [Fact]
    public void Release_rules()
    {
        foreach (var c in Cases("rel"))
        {
            string ctx = $"rel {c.GetRawText()}";
            bool o = Bool(c, "o"), d = Bool(c, "d"), s = Bool(c, "s");
            ReleasePlan plan = PlanRelease(o, d, s);
            JsonElement r = c.GetProperty("r");
            Eq(Bool(r[0]), plan.ReleaseOwnership, ctx + " releaseOwnership");
            Eq(Bool(r[1]), plan.ResumeNative, ctx + " resumeNative");
            Eq(Bool(r[2]), plan.DeferNative, ctx + " deferNative");
            Eq((PendingReleaseDisposition)Int(c, "pd"), PendingReleaseFor(o, d, s), ctx + " PendingReleaseFor");
            Eq(F(c, "cf"), CountdownElapsedAfterFallback(o, F(c, "pv")), ctx + " CountdownElapsedAfterFallback");
        }
    }
}
