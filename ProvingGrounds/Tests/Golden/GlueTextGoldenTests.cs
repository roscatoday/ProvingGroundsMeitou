using System.Text.Json;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Task 1.13's pure glue parts against Tests/Golden/gluetext.json (golden/gen_gluetext.cpp: verbatim copies of
/// ArenaCombatProfile.cpp, RosterStatus.cpp, TownSpectators.cpp and TownBookie.cpp over plain values; its header lists the case kinds).
/// Strings, ints and seeds exactly; doubles and floats bit for bit (the copies run the same IEEE operations in the same order).</summary>
public class GlueTextGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("gluetext"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static uint U(JsonElement c, string n) => c.GetProperty(n).GetUInt32();
    static bool Flag(JsonElement c, string n) => c.GetProperty(n).GetInt32() != 0;
    static string Str(JsonElement c, string n) => c.GetProperty(n).GetString()!;
    static double D(JsonElement e) => BitConverter.Int64BitsToDouble(unchecked((long)e.GetUInt64()));
    static float F(JsonElement e) => BitConverter.Int32BitsToSingle(unchecked((int)e.GetUInt32()));
    static float F(JsonElement c, string n) => F(c.GetProperty(n));
    static void Same(double expected, double actual, string what) =>
        Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual), $"{what}: expected {expected:R}, actual {actual:R}");
    static void Same(float expected, float actual, string what) =>
        Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual), $"{what}: expected {expected:R}, actual {actual:R}");

    // Equality inside a case loop names the field and the whole case (inputs included), so a failure says which case broke.
    static void Eq<T>(T expected, T actual, JsonElement c, string field)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail($"{field}: expected {expected}, actual {actual}; case {c.GetRawText()}");
    }
    static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail($"{what}: expected {expected}, actual {actual}");
    }

    static ArenaCombatProfile.Profile ReadProfile(JsonElement c)
    {
        var p = new ArenaCombatProfile.Profile();
        p.Valid = Flag(c, "valid"); p.Martial = Flag(c, "martial"); p.Style = Str(c, "style");
        JsonElement s = c.GetProperty("stats");
        p.Attack = D(s[0]); p.Defence = D(s[1]); p.Strength = D(s[2]); p.Toughness = D(s[3]); p.Dexterity = D(s[4]); p.WeaponSkill = D(s[5]);
        return p;
    }

    static List<string> Strings(JsonElement a) => a.EnumerateArray().Select(e => e.GetString()!).ToList();

    static SparPodium.Snapshot ReadResult(JsonElement c)
    {
        var s = new SparPodium.Snapshot();
        s.Outcome = (SparPodium.OutcomeKind)Int(c, "outcome");
        s.LastStandingId = Int(c, "last");
        foreach (JsonElement f in c.GetProperty("fighters").EnumerateArray())
        {
            s.Fighters[s.FighterCount].Id = f[0].GetInt32();
            s.Fighters[s.FighterCount].Team = (MatchRules.MatchTeam)f[1].GetInt32();
            s.Fighters[s.FighterCount].Name = f[2].GetString();
            ++s.FighterCount;
        }
        return s;
    }

    static ArenaPerformance.Policy Policy(JsonElement c, float min, float max, bool enabled) =>
        new ArenaPerformance.Policy(120, 0, min, max, Int(c, "voices"), enabled);   // (cap, maintenanceMs, chatterMin, chatterMax, voices, chatter)

    static List<TownBookie.FighterView> ReadViews(JsonElement a) =>
        a.EnumerateArray().Select(v => new TownBookie.FighterView
            { Name = v[0].GetString()!, Style = v[1].GetString()!, Record = v[2].GetString()!, Stats = v[3].GetString()! }).ToList();

    static void SameViews(JsonElement expected, IReadOnlyList<TownBookie.FighterView> actual, string what)
    {
        var e = ReadViews(expected);
        Assert.True(e.Count == actual.Count, $"{what}: count {e.Count} vs {actual.Count}");
        for (int i = 0; i < e.Count; ++i)
        {
            Eq(e[i].Name, actual[i].Name, $"{what} [{i}] name"); Eq(e[i].Style, actual[i].Style, $"{what} [{i}] style");
            Eq(e[i].Record, actual[i].Record, $"{what} [{i}] record"); Eq(e[i].Stats, actual[i].Stats, $"{what} [{i}] stats");
        }
    }

    [Fact]
    public void GoldenHasEveryCase()
    {
        Assert.Equal(2800, All.Value.GetArrayLength());
        var expected = new Dictionary<string, int>
        {
            ["view"] = 500, ["eval"] = 400, ["roster"] = 300, ["chatter"] = 300, ["winners"] = 300, ["read"] = 200, ["cycle"] = 200,
            ["react"] = 200, ["capture"] = 200, ["result"] = 60, ["rand"] = 40, ["settle"] = 40, ["seed"] = 30, ["wager"] = 20, ["style"] = 10,
        };
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    [Fact]
    public void ReadDecisionsMatchTheOriginal()
    {
        int i = 0;
        foreach (JsonElement c in Cases("read"))
        {
            bool usable = Flag(c, "usable");
            var category = (ArenaCombatProfile.WeaponCategory)Int(c, "category");
            JsonElement g = c.GetProperty("getters");
            // getters: the nine category skills in WeaponCategory order, getEquippedWeaponSkill, unarmed attack, melee attack, dodge,
            // melee defence, strength, toughness, dexterity. The glue's side of Read: the getters IsMartial selects, and SkillFor's skill.
            bool martial = ArenaCombatProfile.IsMartial(usable, category);
            double skill = D(g[(int)category]);   // Other (9) = getEquippedWeaponSkill, SkillFor's default
            ArenaCombatProfile.Profile p = ArenaCombatProfile.FromStats(usable, category, martial ? D(g[10]) : D(g[11]),
                martial ? D(g[12]) : D(g[13]), D(g[14]), D(g[15]), D(g[16]), skill);
            ArenaCombatProfile.Profile e = ReadProfile(c);
            string what = $"read {i}";
            Assert.True(e.Valid == p.Valid, what + " valid");
            Assert.True(e.Martial == p.Martial, what + " martial");
            Eq(e.Style, p.Style, c, what + " style");
            Same(e.Attack, p.Attack, what + " attack"); Same(e.Defence, p.Defence, what + " defence");
            Same(e.Strength, p.Strength, what + " strength"); Same(e.Toughness, p.Toughness, what + " toughness");
            Same(e.Dexterity, p.Dexterity, what + " dexterity"); Same(e.WeaponSkill, p.WeaponSkill, what + " skill");
            ++i;
        }
    }

    [Fact]
    public void EvaluateAndStatTextMatchTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int i = 0;
        foreach (JsonElement c in Cases("eval"))
        {
            ArenaCombatProfile.Profile p = ReadProfile(c);
            Same(D(c.GetProperty("score")), ArenaCombatProfile.Evaluate(p), $"eval {i} score");
            Eq(Str(c, "text"), ArenaCombatProfile.StatText(p), c, $"eval {i} text");
            ++i;
        }
    }

    [Fact]
    public void StyleNamesMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("style"))
            Eq(Str(c, "text"), ArenaCombatProfile.StyleFor((ArenaCombatProfile.WeaponCategory)Int(c, "category")), c, "style");
    }

    [Fact]
    public void RosterStatusReadMatchesTheOriginal()
    {
        int i = 0;
        foreach (JsonElement c in Cases("roster"))
        {
            var parts = c.GetProperty("parts").EnumerateArray().Select(p => p[0].GetInt32() != 0 ?
                (RosterStatus.Part?)new RosterStatus.Part(F(p[1]), F(p[2]), F(p[3])) : null).ToList();
            bool ok = RosterStatus.Read(Flag(c, "valid"), Flag(c, "medical"), F(c, "maxBlood"), F(c, "blood"), parts,
                out RosterStatus.Snapshot s, out string error);
            Assert.True(Flag(c, "ok") == ok, $"roster {i} ok");
            Same(F(c, "lowest"), s.LowestLimb, $"roster {i} lowest");
            Same(F(c, "outBlood"), s.Blood, $"roster {i} blood");
            Same(F(c, "recovery"), s.Recovery, $"roster {i} recovery");
            Eq(Int(c, "band"), (int)s.Band, c, $"roster {i} band");
            Eq(Str(c, "error"), error, c, $"roster {i} error");
            ++i;
        }
    }

    [Fact]
    public void ChatterRandomMatchesTheOriginal()
    {
        foreach (JsonElement c in Cases("rand"))
        {
            uint seed = U(c, "seed"), tick = U(c, "tick");
            foreach (JsonElement o in c.GetProperty("out").EnumerateArray())
                Eq(o.GetUInt32(), TownSpectators.ChatterRandom(ref seed, tick), c, "rand");
        }
    }

    [Fact]
    public void ChatterStartSeedMatchesTheOriginal()
    {
        foreach (JsonElement c in Cases("seed"))
            Eq(U(c, "out"), TownSpectators.ChatterStartSeed(U(c, "tick"), Int(c, "guests")), c, "seed");
    }

    [Fact]
    public void ChatterLinesMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("chatter"))
        {
            uint seed = U(c, "seed");
            Eq(Str(c, "line"), TownSpectators.ChatterLine(Strings(c.GetProperty("fighters")), ref seed, U(c, "tick")), c, "chatter line");
            Eq(U(c, "seedOut"), seed, c, "chatter seed");
        }
    }

    [Fact]
    public void ResultWinnersAndLinesMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("winners"))
        {
            SparPodium.Snapshot s = ReadResult(c);
            List<string> winners = TownSpectators.ResultWinners(s);
            Eq(string.Join("|", Strings(c.GetProperty("winners"))), string.Join("|", winners), c, "winners");
            uint seed = U(c, "seed");
            Eq(Str(c, "line"), TownSpectators.ResultChatterLine(s, winners, ref seed, U(c, "tick")), c, "winners line");
            Eq(U(c, "seedOut"), seed, c, "winners seed");
        }
    }

    [Fact]
    public void ChatterCyclesMatchTheOriginal()
    {
        int i = 0;
        foreach (JsonElement c in Cases("cycle"))
        {
            ArenaPerformance.Policy p = Policy(c, F(c, "min"), F(c, "max"), true);
            uint seed = U(c, "seed");
            TownSpectators.Cycle cycle = TownSpectators.ScheduleChatterCycle(p, Int(c, "seated"), Int(c, "last"), F(c, "elapsed"),
                ref seed, U(c, "tick"));
            Same(F(c, "next"), cycle.NextChatterCycle, $"cycle {i} next");
            Eq(U(c, "seedOut"), seed, c, $"cycle {i} seed");
            var cues = c.GetProperty("cues").EnumerateArray().ToList();
            Eq(cues.Count, cycle.Cues.Count, c, $"cycle {i} cue count");
            for (int k = 0; k < cues.Count; ++k)
            {
                Eq(cues[k][0].GetInt32(), cycle.Cues[k].SeatedIndex, c, $"cycle {i} cue {k} speaker");
                Same(F(cues[k][1]), cycle.Cues[k].Due, $"cycle {i} cue {k}");
            }
            ++i;
        }
    }

    [Fact]
    public void ResultReactionsMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("react"))
        {
            ArenaPerformance.Policy p = Policy(c, 4.0f, 8.0f, Flag(c, "enabled"));
            uint seed = U(c, "seed");
            List<TownSpectators.Saying> said = TownSpectators.ReactToResult(p, ReadResult(c), Int(c, "guests"), Int(c, "seated"),
                Int(c, "speaker"), ref seed, U(c, "tick"));
            Eq(U(c, "seedOut"), seed, c, "react seed");
            var expected = c.GetProperty("said").EnumerateArray().ToList();
            Eq(expected.Count, said.Count, c, "react count");
            for (int k = 0; k < expected.Count; ++k)
            {
                Eq(expected[k][0].GetInt32(), said[k].SeatedIndex, c, $"react {k} speaker");
                Eq(expected[k][1].GetString(), said[k].Line, c, $"react {k} line");
            }
            // the C++ leaves lastChatterSpeaker on the last speaker (unchanged when nobody spoke)
            Eq(Int(c, "lastOut"), said.Count > 0 ? said[^1].SeatedIndex : Int(c, "speaker"), c, "react last speaker");
        }
    }

    [Fact]
    public void BookieCaptureMatchesTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        foreach (JsonElement c in Cases("capture"))
        {
            var standings = c.GetProperty("standings").EnumerateArray().Select(s => new LeaderboardData.Standing
                { Id = s[0].GetString()!, Mmr = F(s[1]), Wins = s[2].GetInt32(), Losses = s[3].GetInt32(), Matches = s[4].GetInt32() }).ToList();
            TownBookie.FighterView v = TownBookie.Capture(Flag(c, "cvalid"), Str(c, "name"), ReadProfile(c), Str(c, "identity"), standings);
            SameViews(JsonDocument.Parse("[" + c.GetProperty("view").GetRawText() + "]").RootElement, new List<TownBookie.FighterView> { v }, "capture " + c.GetRawText());
        }
    }

    static readonly PGConfig.Bookie[] Bookies =
    {
        new PGConfig.Bookie { Minimum = 10, Maximum = 10000 }, new PGConfig.Bookie { Minimum = 100, Maximum = 500 },
        new PGConfig.Bookie { Minimum = 50, Maximum = 50 },
    };

    [Fact]
    public void BookieViewMatchesTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int i = 0;
        foreach (JsonElement c in Cases("view"))
        {
            using var _ = PGConfig.UseBookieForTest(Bookies[Int(c, "bi")]);
            JsonElement t = c.GetProperty("ticket"), teams = c.GetProperty("teams"), ab = c.GetProperty("ability"), views = c.GetProperty("views");
            var g = new TownBookie.ViewInputs
            {
                HasWallet = Flag(c, "wallet"), Money = Int(c, "money"), SelectedSide = Int(c, "sel"), Market = Flag(c, "market"),
                Eligible = Flag(c, "eligible"), Fighting = Flag(c, "fighting"), Completed = Flag(c, "completed"),
                ResultScheduled = Flag(c, "scheduled"), ResultReady = Flag(c, "ready"), Nearby = Flag(c, "nearby"),
                SameTown = Flag(c, "sameTown"), HasActiveSave = Flag(c, "save"), BookieCredit = Int(c, "credit"), Stake = Int(c, "stake"),
                Ticket = new TownBettingPolicy.Ticket { Side = t[0].GetInt32(), Stake = t[1].GetInt32(), Payout = t[2].GetInt32(), Pending = t[3].GetInt32() != 0 },
                TeamCounts = new[] { teams[0].GetInt32(), teams[1].GetInt32() }, Ability = new[] { D(ab[0]), D(ab[1]) },
                FighterViews = new[] { ReadViews(views[0]), ReadViews(views[1]) },
                PlannedBout = Flag(c, "planned"), PlannedStartHours = D(c.GetProperty("start")), NowHours = D(c.GetProperty("now")),
                ActionMessage = Str(c, "action"), Message = Str(c, "message"),
            };
            TownBookie.View v = TownBookie.MakeView(g, out int stake);
            string what = $"view {i}";
            Assert.True(Int(c, "stakeOut") == stake, what + " stake");
            Eq(Int(c, "vsel"), v.SelectedSide, c, what + " selected side");
            Assert.True(Int(c, "vstake") == v.Stake, what + " view stake");
            JsonElement f = c.GetProperty("flags");
            Assert.True(f[0].GetInt32() != 0 == v.CanSelect, what + " canSelect");
            Assert.True(f[1].GetInt32() != 0 == v.CanStake, what + " canStake");
            Assert.True(f[2].GetInt32() != 0 == v.CanBet, what + " canBet");
            Assert.True(f[3].GetInt32() != 0 == v.CanArrange, what + " canArrange");
            Eq(Str(c, "header"), v.Header, c, what + " header");
            Eq(Str(c, "timer"), v.Timer, c, what + " timer");
            Eq(Str(c, "status"), v.Status, c, what + " status");
            Eq(Str(c, "slip"), v.Slip, c, what + " slip");
            Eq(c.GetProperty("odds")[0].GetString(), v.Odds[0], c, what + " odds A");
            Eq(c.GetProperty("odds")[1].GetString(), v.Odds[1], c, what + " odds B");
            SameViews(c.GetProperty("vviews")[0], v.Fighters[0], what + " fighters A");
            SameViews(c.GetProperty("vviews")[1], v.Fighters[1], what + " fighters B");
            ++i;
        }
    }

    [Fact]
    public void BookieResultViewMatchesTheOriginal()
    {
        foreach (JsonElement c in Cases("result"))
        {
            var r = new TownBettingPolicy.Result
            {
                Outcome = (TownBettingPolicy.ResultOutcome)Int(c, "outcome"), WinningSide = Int(c, "win"), SelectedSide = Int(c, "sel"),
                Stake = Int(c, "stake"), Returned = Int(c, "returned"), Net = Int(c, "net"),
            };
            JsonElement views = c.GetProperty("views");
            TownBookie.ResultView v = TownBookie.MakeResultView(r, new IReadOnlyList<TownBookie.FighterView>[] { ReadViews(views[0]), ReadViews(views[1]) });
            Eq(Int(c, "vwin"), v.WinningSide, c, "result winning side");
            Eq(Int(c, "vsel"), v.SelectedSide, c, "result selected side");
            Eq(Int(c, "tone"), v.Tone, c, "result tone");
            Eq(Str(c, "title"), v.Title, c, "result title");
            Eq(Str(c, "context"), v.Context, c, "result context");
            Eq(Str(c, "payout"), v.Payout, c, "result payout");
            SameViews(c.GetProperty("vviews")[0], v.Fighters[0], "result A " + c.GetRawText());
            SameViews(c.GetProperty("vviews")[1], v.Fighters[1], "result B " + c.GetRawText());
        }
    }

    [Fact]
    public void BookieWagerAndSettleTextsMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("wager"))
        {
            Eq(Str(c, "message"), TownBookie.AcceptedMessage(Int(c, "stake"), Int(c, "payout")), c, "wager message");
            Eq(Str(c, "fields"), TownBookie.WagerFields(Int(c, "side"), Int(c, "stake"), Int(c, "payout")), c, "wager fields");
        }
        foreach (JsonElement c in Cases("settle"))
            Eq(Str(c, "message"), TownBookie.SettleMessage(Flag(c, "pending"), Int(c, "winner"), Int(c, "side"), Int(c, "credit")), c, "settle message");
    }
}
