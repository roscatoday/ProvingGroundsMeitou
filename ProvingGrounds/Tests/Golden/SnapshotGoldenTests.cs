using System.Globalization;
using System.Text;
using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaSnapshotJson.cs, ArenaSnapshotRepository.cs and RapidJson.cs against Tests/Golden/snapshot.json
/// (golden/gen_snapshot.cpp: the REAL codec compiled with rapidjson v1.1.0; its header lists the case kinds and layouts).
/// Texts are byte-exact; numbers bit-exact; strings and messages exact.</summary>
public class SnapshotGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("snapshot"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    // ---- golden byte strings: one code unit per byte (gen_snapshot.cpp Q) -----------------------------------------------------
    static byte[] Bytes(string s)
    {
        var b = new byte[s.Length];
        for (int i = 0; i < s.Length; ++i)
        {
            if (s[i] > 0xFF) Assert.Fail("golden byte string holds a code unit above 0xFF");
            b[i] = (byte)s[i];
        }
        return b;
    }

    static byte[] Bytes(JsonElement e) => Bytes(e.GetString()!);
    static string Utf8(JsonElement e) => new UTF8Encoding(false, true).GetString(Bytes(e));

    internal static string Q(string s) => Q(ArenaPersistence.Utf8Bytes(s));

    internal static string Q(ReadOnlySpan<byte> v)
    {
        var o = new StringBuilder("\"");
        foreach (byte c in v)
        {
            if (c >= 0x20 && c <= 0x7e && c != '"' && c != '\\') o.Append((char)c);
            else o.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }
        return o.Append('"').ToString();
    }

    // ---- texts --------------------------------------------------------------------------------------------------------------
    static readonly Lazy<Dictionary<int, byte[]>> Bases = new(() =>
        Cases("base").Count == 2 ? Cases("base").ToDictionary(c => c.GetProperty("i").GetInt32(), c => Bytes(c.GetProperty("text"))) : throw new InvalidOperationException("expected 2 base texts in snapshot.json"));
    static readonly Lazy<Dictionary<int, byte[]>> EncTexts = new(() =>
        Cases("enc").ToDictionary(c => c.GetProperty("i").GetInt32(), c => Bytes(c.GetProperty("text"))));

    static byte[] Text(JsonElement t)
    {
        if (t.TryGetProperty("e", out JsonElement e)) return EncTexts.Value[e.GetInt32()];
        int b = t.GetProperty("b").GetInt32();
        byte[] baseText = b < 0 ? System.Array.Empty<byte>() : Bases.Value[b];
        int at = t.GetProperty("at").GetInt32(), del = t.GetProperty("del").GetInt32(), pad = t.GetProperty("pad").GetInt32();
        byte[] ins = Bytes(t.GetProperty("ins"));
        var o = new byte[baseText.Length - del + ins.Length + pad];
        System.Array.Copy(baseText, 0, o, 0, at);
        System.Array.Copy(ins, 0, o, at, ins.Length);
        System.Array.Copy(baseText, at + del, o, at + ins.Length, baseText.Length - at - del);
        for (int i = 0; i < pad; ++i) o[o.Length - pad + i] = (byte)' ';
        return o;
    }

    // ---- snapshot dump (gen_snapshot.cpp Dump) ---------------------------------------------------------------------------------
    static string U(ulong v) => v.ToString(CultureInfo.InvariantCulture);
    static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
    static string B(bool v) => v ? "true" : "false";
    static string Db(double d) => U(unchecked((ulong)BitConverter.DoubleToInt64Bits(d)));
    static string Fb(float f) => U(BitConverter.SingleToUInt32Bits(f));
    static string Strs(List<string> v) => "[" + string.Join(",", v.Select(Q)) + "]";
    static string Hist(List<List<string>> h) => "[" + string.Join(",", h.Select(Strs)) + "]";
    static string Rows(List<LeaderboardData.Standing> v) =>
        "[" + string.Join(",", v.Select(r => "[" + Q(r.Id) + "," + Q(r.Name) + "," + Fb(r.Mmr) + "," + I(r.Wins) + "," + I(r.Losses) + "," + I(r.Matches) + "]")) + "]";

    static string DumpOffer(TownChallengePolicy.Offer o)
    {
        var r = "[" + I(o.Epoch) + "," + I(o.Encounter) + "," + I(o.Format) + "," + I(o.Mode) + "," + I((int)o.State) + "," + B(o.Generated) +
            "," + U(o.Draw) + "," + I(o.Division) + "," + I(o.PlayerCount) + "," + I(o.EnemyCount) + "," + Q(o.Context);
        for (int i = 0; i < 4; ++i)
            r += ",[" + Q(TownChallengePolicy.OpponentIdConst(o, i)) + "," + Q(TownChallengePolicy.OpponentNameConst(o, i)) + "," + Q(o.Roles[i]) + "]";
        return r + "]";
    }

    internal static string Dump(Snapshot s)
    {
        var o = new StringBuilder();
        o.Append("{\"version\":").Append(I(s.Version)).Append(",\"saveKey\":").Append(Q(s.SaveKey)).Append(",\"generation\":").Append(Q(s.Generation));
        o.Append(",\"ps\":").Append(Rows(s.Fighters.PlayerStandings)).Append(",\"ts\":").Append(Rows(s.Fighters.TownStandings)).Append(",\"pr\":[");
        o.Append(string.Join(",", s.Fighters.Progression.Select(p => "[" + Q(p.Id) + "," + Q(p.Name) + "," + I(p.Marks) + ",[" +
            string.Join(",", p.RewardTiers.Select(t => "[" + Q(t.Id) + "," + I(t.Tier) + "]")) + "]]")));
        o.Append("],\"fu\":").Append(Strs(s.Fighters.FactionUnlocks));
        TownChallengePolicy.Card c = s.Challenges;
        o.Append(",\"ch\":[").Append(U(c.Seed)).Append(',').Append(U(c.AmbientSeed)).Append(',').Append(I(c.Division)).Append(',').Append(U(c.Milestones)).Append(',')
            .Append(U(c.UniqueWins)).Append(',').Append(U(c.ChallengeWins)).Append(',').Append(Hist(c.PlayerHistory)).Append(',').Append(Hist(c.AmbientHistory)).Append(",[");
        o.Append(string.Join(",", c.Offers.Select(DumpOffer)));
        o.Append("],").Append(DumpOffer(c.SkarnOffer)).Append(',').Append(B(c.SkarnGenerated)).Append(',').Append(B(c.SkarnWon)).Append(',').Append(B(c.SkarnAttempted))
            .Append(',').Append(B(c.SkarnInProgress)).Append(',').Append(Db(c.SkarnLastEnd)).Append(',').Append(Db(c.LastChallengeEndHours)).Append(',')
            .Append(B(c.HasChallengeEnd)).Append(',').Append(I(c.PaidRefreshCount)).Append(',').Append(U(c.PaidRefreshNonce)).Append(']');
        TownDiagnosticData.State d = s.Diagnostic;
        o.Append(",\"dg\":[").Append(I(d.Status)).Append(',').Append(U(d.RunId)).Append(',').Append(U(d.NextSequence)).Append(',').Append(I(d.Config.Target)).Append(",[");
        for (int i = 0; i < TownDiagnosticData.ModelCount; ++i) o.Append(i != 0 ? "," : "").Append(B(d.Config.Models[i]));
        o.Append("],[");
        for (int i = 0; i < TownDiagnosticData.CoverageCount; ++i) o.Append(i != 0 ? "," : "").Append(B(d.Config.Coverage[i]));
        o.Append("],").Append(Db(d.Config.ReducedInfluence)).Append(',').Append(I(d.Config.UnderdogPercent)).Append(',').Append(B(d.Config.BalanceStyleTier));
        TownDiagnosticData.Counters k = d.Counters;
        foreach (int v in new[] { k.Opened, k.Completed, k.Cancelled, k.Aborted, k.TeamAWins, k.TeamBWins, k.LargerWins, k.SmallerWins, k.EqualWins, k.FavoriteWins })
            o.Append(',').Append(I(v));
        o.Append(",[");
        for (int i = 0; i < TownDiagnosticData.ModelCount; ++i) o.Append(i != 0 ? "," : "").Append(I(k.ModelCompleted[i]));
        o.Append("],[");
        for (int i = 0; i < TownDiagnosticData.CoverageCount; ++i) o.Append(i != 0 ? "," : "").Append(I(k.CoverageCompleted[i]));
        o.Append("],").Append(Db(k.SchedulingHours)).Append(',').Append(Db(k.LastCompletedHours)).Append(',').Append(I(k.SchedulingSamples));
        TownDiagnosticData.Pending q = d.Pending;
        o.Append(',').Append(B(q.Active)).Append(',').Append(B(q.Underdog)).Append(',').Append(U(q.Sequence)).Append(',').Append(I(q.Model)).Append(',')
            .Append(I(q.RequestedCoverage)).Append(',').Append(I(q.ActualCoverage)).Append(',').Append(I(q.Relaxation)).Append(',').Append(I(q.SizeA)).Append(',').Append(I(q.SizeB));
        for (int a = 0; a < 3; ++a)
        {
            o.Append(",[");
            for (int i = 0; i < TownDiagnosticData.ModelCount; ++i)
                o.Append(i != 0 ? "," : "").Append(Db(a == 0 ? q.ScoreA[i] : a == 1 ? q.ScoreB[i] : q.ProbabilityA[i]));
            o.Append(']');
        }
        o.Append(',').Append(Db(q.AnnouncedHours)).Append(']');
        o.Append(",\"bc\":").Append(I(s.BookieCredit)).Append(",\"cc\":").Append(I(s.ChallengeCredit));
        PlannedNpcBout b = s.PlannedNpc;
        o.Append(",\"np\":[").Append(B(b.Active)).Append(',').Append(Q(b.RegistryId)).Append(',').Append(Db(b.StartHours)).Append(',').Append(Db(b.ScoreA)).Append(',')
            .Append(Db(b.ScoreB)).Append(',').Append(Strs(b.TeamA)).Append(',').Append(Strs(b.TeamB)).Append(',').Append(B(b.Market)).Append(',').Append(B(b.WagerPending))
            .Append(',').Append(I(b.WagerSide)).Append(',').Append(I(b.Stake)).Append(',').Append(I(b.Payout)).Append(']');
        o.Append(",\"de\":").Append(Q(s.DiagnosticError)).Append('}');
        return o.ToString();
    }

    static double Dbl(JsonElement e) => BitConverter.Int64BitsToDouble(unchecked((long)e.GetUInt64()));
    static List<string> StrList(JsonElement a) => a.EnumerateArray().Select(Utf8).ToList();

    static TownChallengePolicy.Offer ReadOffer(JsonElement a)
    {
        var o = new TownChallengePolicy.Offer
        {
            Epoch = a[0].GetInt32(), Encounter = a[1].GetInt32(), Format = a[2].GetInt32(), Mode = a[3].GetInt32(),
            State = (TownArenaPolicy.OfferState)a[4].GetInt32(), Generated = a[5].GetBoolean(), Draw = a[6].GetUInt32(),
            Division = a[7].GetInt32(), PlayerCount = a[8].GetInt32(), EnemyCount = a[9].GetInt32(), Context = Utf8(a[10]),
        };
        for (int i = 0; i < 4; ++i)
        {
            TownChallengePolicy.OpponentId(ref o, i) = Utf8(a[11 + i][0]);
            TownChallengePolicy.OpponentName(ref o, i) = Utf8(a[11 + i][1]);
            o.Roles[i] = Utf8(a[11 + i][2]);
        }
        return o;
    }

    static List<LeaderboardData.Standing> ReadRows(JsonElement a) => a.EnumerateArray().Select(r => new LeaderboardData.Standing
    {
        Id = Utf8(r[0]), Name = Utf8(r[1]), Mmr = BitConverter.UInt32BitsToSingle(r[2].GetUInt32()), Wins = r[3].GetInt32(), Losses = r[4].GetInt32(), Matches = r[5].GetInt32(),
    }).ToList();

    internal static Snapshot FromDump(JsonElement j)
    {
        var s = new Snapshot { Version = j.GetProperty("version").GetInt32(), SaveKey = Utf8(j.GetProperty("saveKey")), Generation = Utf8(j.GetProperty("generation")) };
        s.Fighters.PlayerStandings = ReadRows(j.GetProperty("ps"));
        s.Fighters.TownStandings = ReadRows(j.GetProperty("ts"));
        foreach (JsonElement p in j.GetProperty("pr").EnumerateArray())
        {
            var row = new LeaderboardData.Progression { Id = Utf8(p[0]), Name = Utf8(p[1]), Marks = p[2].GetInt32() };
            foreach (JsonElement t in p[3].EnumerateArray()) row.RewardTiers.Add(new RewardProgression.TierEntry(Utf8(t[0]), t[1].GetInt32()));
            s.Fighters.Progression.Add(row);
        }
        s.Fighters.FactionUnlocks = StrList(j.GetProperty("fu"));
        JsonElement ch = j.GetProperty("ch");
        TownChallengePolicy.Card c = s.Challenges;
        c.Seed = ch[0].GetUInt32(); c.AmbientSeed = ch[1].GetUInt32(); c.Division = ch[2].GetInt32(); c.Milestones = ch[3].GetUInt32();
        c.UniqueWins = ch[4].GetUInt32(); c.ChallengeWins = ch[5].GetUInt32();
        foreach (JsonElement bout in ch[6].EnumerateArray()) c.PlayerHistory.Add(StrList(bout));
        foreach (JsonElement bout in ch[7].EnumerateArray()) c.AmbientHistory.Add(StrList(bout));
        for (int i = 0; i < 5; ++i) c.Offers[i] = ReadOffer(ch[8][i]);
        c.SkarnOffer = ReadOffer(ch[9]);
        c.SkarnGenerated = ch[10].GetBoolean(); c.SkarnWon = ch[11].GetBoolean(); c.SkarnAttempted = ch[12].GetBoolean(); c.SkarnInProgress = ch[13].GetBoolean();
        c.SkarnLastEnd = Dbl(ch[14]); c.LastChallengeEndHours = Dbl(ch[15]); c.HasChallengeEnd = ch[16].GetBoolean(); c.PaidRefreshCount = ch[17].GetInt32();
        c.PaidRefreshNonce = ch[18].GetUInt32();
        JsonElement dg = j.GetProperty("dg");
        var d = new TownDiagnosticData.State();
        d.Status = dg[0].GetInt32(); d.RunId = dg[1].GetUInt32(); d.NextSequence = dg[2].GetUInt32(); d.Config.Target = dg[3].GetInt32();
        for (int i = 0; i < TownDiagnosticData.ModelCount; ++i) d.Config.Models[i] = dg[4][i].GetBoolean();
        for (int i = 0; i < TownDiagnosticData.CoverageCount; ++i) d.Config.Coverage[i] = dg[5][i].GetBoolean();
        d.Config.ReducedInfluence = Dbl(dg[6]); d.Config.UnderdogPercent = dg[7].GetInt32(); d.Config.BalanceStyleTier = dg[8].GetBoolean();
        d.Counters.Opened = dg[9].GetInt32(); d.Counters.Completed = dg[10].GetInt32(); d.Counters.Cancelled = dg[11].GetInt32(); d.Counters.Aborted = dg[12].GetInt32();
        d.Counters.TeamAWins = dg[13].GetInt32(); d.Counters.TeamBWins = dg[14].GetInt32(); d.Counters.LargerWins = dg[15].GetInt32(); d.Counters.SmallerWins = dg[16].GetInt32();
        d.Counters.EqualWins = dg[17].GetInt32(); d.Counters.FavoriteWins = dg[18].GetInt32();
        for (int i = 0; i < TownDiagnosticData.ModelCount; ++i) d.Counters.ModelCompleted[i] = dg[19][i].GetInt32();
        for (int i = 0; i < TownDiagnosticData.CoverageCount; ++i) d.Counters.CoverageCompleted[i] = dg[20][i].GetInt32();
        d.Counters.SchedulingHours = Dbl(dg[21]); d.Counters.LastCompletedHours = Dbl(dg[22]); d.Counters.SchedulingSamples = dg[23].GetInt32();
        d.Pending.Active = dg[24].GetBoolean(); d.Pending.Underdog = dg[25].GetBoolean(); d.Pending.Sequence = dg[26].GetUInt32(); d.Pending.Model = dg[27].GetInt32();
        d.Pending.RequestedCoverage = dg[28].GetInt32(); d.Pending.ActualCoverage = dg[29].GetInt32(); d.Pending.Relaxation = dg[30].GetInt32();
        d.Pending.SizeA = dg[31].GetInt32(); d.Pending.SizeB = dg[32].GetInt32();
        for (int i = 0; i < TownDiagnosticData.ModelCount; ++i) { d.Pending.ScoreA[i] = Dbl(dg[33][i]); d.Pending.ScoreB[i] = Dbl(dg[34][i]); d.Pending.ProbabilityA[i] = Dbl(dg[35][i]); }
        d.Pending.AnnouncedHours = Dbl(dg[36]);
        s.Diagnostic = d;
        s.BookieCredit = j.GetProperty("bc").GetInt32(); s.ChallengeCredit = j.GetProperty("cc").GetInt32();
        JsonElement np = j.GetProperty("np");
        PlannedNpcBout b = s.PlannedNpc;
        b.Active = np[0].GetBoolean(); b.RegistryId = Utf8(np[1]); b.StartHours = Dbl(np[2]); b.ScoreA = Dbl(np[3]); b.ScoreB = Dbl(np[4]);
        b.TeamA = StrList(np[5]); b.TeamB = StrList(np[6]); b.Market = np[7].GetBoolean(); b.WagerPending = np[8].GetBoolean();
        b.WagerSide = np[9].GetInt32(); b.Stake = np[10].GetInt32(); b.Payout = np[11].GetInt32();
        s.DiagnosticError = Utf8(j.GetProperty("de"));
        return s;
    }

    static IDisposable? Bookie(JsonElement c)
    {
        int bm = c.GetProperty("bm").GetInt32();
        return bm == 10000 ? null : PGConfig.UseBookieForTest(new PGConfig.Bookie { Minimum = 10, Maximum = bm });
    }

    static void SameBytes(byte[] expected, byte[] actual, string what)
    {
        if (expected.AsSpan().SequenceEqual(actual)) return;
        int at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) ++at;
        string Around(byte[] b) => Encoding.UTF8.GetString(b, Math.Max(0, at - 40), Math.Min(b.Length, at + 40) - Math.Max(0, at - 40));
        Assert.Fail($"{what}: bytes differ at {at} (lengths {expected.Length} / {actual.Length})\nexpected ...{Around(expected)}...\nactual   ...{Around(actual)}...");
    }

    // ---- the codec ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Base_texts_are_pinned()
    {
        Assert.Equal(2, Cases("base").Count);
        Assert.Equal(new[] { 0, 1 }, Bases.Value.Keys.OrderBy(k => k));
    }

    [Fact]
    public void FromDump_and_Dump_round_trip_the_golden_snapshots()
    {
        // the test's own reader and writer of the C++ dump agree, so the comparisons below compare the snapshots
        foreach (JsonElement c in Cases("enc"))
            Assert.Equal(c.GetProperty("in").GetRawText(), Dump(FromDump(c.GetProperty("in"))));
    }

    [Fact]
    public void EncodeSnapshot_matches_the_original_byte_for_byte()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        var cases = Cases("enc");
        Assert.Equal(226, cases.Count);
        int ok = 0;
        foreach (JsonElement c in cases)
        {
            int i = c.GetProperty("i").GetInt32();
            using IDisposable? bookie = Bookie(c);
            Snapshot s = FromDump(c.GetProperty("in"));
            byte[] sentinel = { (byte)'u' };
            byte[] json = sentinel;
            bool done = EncodeSnapshot(s, ref json, out string error);
            Assert.True(c.GetProperty("ok").GetBoolean() == done, $"enc {i}: ok (error '{error}')");
            Assert.True(Utf8(c.GetProperty("err")) == error, $"enc {i}: error '{error}', expected '{Utf8(c.GetProperty("err"))}'");
            if (done) { SameBytes(Bytes(c.GetProperty("text")), json, $"enc {i}"); ++ok; }
            else Assert.Same(sentinel, json);
        }
        Assert.Equal(139, ok);
    }

    [Fact]
    public void DecodeSnapshot_matches_the_original()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        var cases = Cases("dec");
        Assert.Equal(573, cases.Count);
        // rules reached only by the valid-JSON mutations of the spec review: keep them in the golden
        var messages = cases.Select(c => Utf8(c.GetProperty("msg"))).ToHashSet();
        foreach (string rule in new[] { "expected 3 or 4 opponent slots", "opponent count exceeds stored opponent slots",
                     "expected an object mapping reward IDs to numeric tiers" })
            Assert.Contains(rule, messages);
        foreach (JsonElement c in cases)
        {
            int i = c.GetProperty("i").GetInt32();
            using IDisposable? bookie = Bookie(c);
            byte[] text = Text(c.GetProperty("t"));
            LoadResult r = DecodeSnapshot(text);
            string what = $"dec {i} ({c.GetProperty("t").GetRawText()})";
            Assert.True(c.GetProperty("code").GetInt32() == (int)r.Code, $"{what}: code {r.Code} ({r.Field}: {r.Message})");
            Assert.True(Utf8(c.GetProperty("field")) == r.Field, $"{what}: field '{r.Field}', expected '{Utf8(c.GetProperty("field"))}'");
            Assert.True(Utf8(c.GetProperty("msg")) == r.Message, $"{what}: message '{r.Message}', expected '{Utf8(c.GetProperty("msg"))}'");
            Assert.True(c.GetProperty("snap").GetRawText() == Dump(r.Snapshot), $"{what}: snapshot\nexpected {c.GetProperty("snap").GetRawText()}\nactual   {Dump(r.Snapshot)}");
        }
    }

    // ---- the repository over the scripted storage of gen_snapshot.cpp (struct Fake) --------------------------------------------

    sealed class FakeFile
    {
        public ReadCode Code;
        public byte[] Text = System.Array.Empty<byte>();
        public bool SetError;
        public string Error = "";
    }

    internal sealed class Fake : IStorage
    {
        public readonly SortedDictionary<string, object> Files = new(StringComparer.Ordinal);
        public bool ListOk = true, ListSetError, PreserveOk = true, PreserveSetError, ReplaceOk = true, ReplaceSetError;
        public List<string> List = new();
        public string ListError = "", PreserveError = "", BackupPath = "bak", ReplaceError = "";
        public readonly List<string> Log = new();

        public void Add(string path, ReadCode code, byte[] text, bool setError, string error) =>
            Files[path] = new FakeFile { Code = code, Text = text, SetError = setError, Error = error };

        public ReadCode Read(string path, ref byte[] text, ref string error)
        {
            Log.Add("R:" + path);
            if (!Files.TryGetValue(path, out object? o)) return ReadCode.NotFound;
            var f = (FakeFile)o;
            if (f.Code == ReadCode.ReadOk) text = f.Text;
            if (f.SetError) error = f.Error;
            return f.Code;
        }

        public bool ListBackups(string path, ref List<string> paths, ref string error)
        {
            Log.Add("L:" + path);
            if (ListSetError) error = ListError;
            if (ListOk) paths = new List<string>(List);
            return ListOk;
        }

        public bool Preserve(string path, ref string backupPath, ref string error)
        {
            Log.Add("P:" + path);
            if (PreserveSetError) error = PreserveError;
            if (PreserveOk) backupPath = BackupPath;
            return PreserveOk;
        }

        public bool Replace(string path, byte[] text, ref string error)
        {
            Log.Add("W:" + path + ":" + text.Length.ToString(CultureInfo.InvariantCulture));
            if (ReplaceSetError) error = ReplaceError;
            if (ReplaceOk) Add(path, ReadCode.ReadOk, text, false, "");
            return ReplaceOk;
        }

        public byte[] MainText() => Files.TryGetValue("main", out object? o) && ((FakeFile)o).Code == ReadCode.ReadOk ? ((FakeFile)o).Text : System.Array.Empty<byte>();
    }

    static Fake FakeFrom(JsonElement c)
    {
        var f = new Fake();
        foreach (JsonElement file in c.GetProperty("files").EnumerateArray())
            f.Add(Utf8(file[0]), (ReadCode)file[1].GetInt32(), Text(file[2]), file[3].GetBoolean(), Utf8(file[4]));
        JsonElement list = c.GetProperty("list");
        f.ListOk = list[0].GetBoolean(); f.List = StrList(list[1]); f.ListSetError = list[2].GetBoolean(); f.ListError = Utf8(list[3]);
        JsonElement preserve = c.GetProperty("preserve");
        f.PreserveOk = preserve[0].GetBoolean(); f.BackupPath = Utf8(preserve[1]); f.PreserveSetError = preserve[2].GetBoolean(); f.PreserveError = Utf8(preserve[3]);
        JsonElement replace = c.GetProperty("replace");
        f.ReplaceOk = replace[0].GetBoolean(); f.ReplaceSetError = replace[1].GetBoolean(); f.ReplaceError = Utf8(replace[2]);
        return f;
    }

    [Fact]
    public void LoadSnapshot_matches_the_original()
    {
        var cases = Cases("load");
        Assert.Equal(201, cases.Count);
        Assert.Contains(cases, c => Utf8(c.GetProperty("msg")) == "Recovered generation-matched snapshot copied from save 'other' at b1");
        int n = 0;
        foreach (JsonElement c in cases)
        {
            Fake f = FakeFrom(c);
            LoadResult r = LoadSnapshot(f, "main", Utf8(c.GetProperty("key")), Utf8(c.GetProperty("gen")));
            string what = $"load {n++}";
            Assert.True(c.GetProperty("code").GetInt32() == (int)r.Code, $"{what}: code {r.Code} ({r.Message})");
            Assert.True(Utf8(c.GetProperty("field")) == r.Field, $"{what}: field '{r.Field}'");
            Assert.True(Utf8(c.GetProperty("msg")) == r.Message, $"{what}: message '{r.Message}', expected '{Utf8(c.GetProperty("msg"))}'");
            Assert.True(c.GetProperty("snap").GetRawText() == Dump(r.Snapshot), $"{what}: snapshot");
            Assert.Equal(StrList(c.GetProperty("log")), f.Log);
        }
    }

    [Fact]
    public void PublishSnapshot_matches_the_original()
    {
        var cases = Cases("pub");
        Assert.Equal(200, cases.Count);
        int n = 0;
        foreach (JsonElement c in cases)
        {
            Fake f = FakeFrom(c);
            string error = Utf8(c.GetProperty("e0"));
            bool ok = PublishSnapshot(f, "main", FromDump(c.GetProperty("in")), ref error);
            string what = $"pub {n++}";
            Assert.True(c.GetProperty("ok").GetBoolean() == ok, $"{what}: ok ({error})");
            Assert.True(Utf8(c.GetProperty("err")) == error, $"{what}: error '{error}', expected '{Utf8(c.GetProperty("err"))}'");
            SameBytes(Text(c.GetProperty("main")), f.MainText(), what + " main");
            Assert.Equal(StrList(c.GetProperty("log")), f.Log);
        }
    }

    // ---- rapidjson probes ---------------------------------------------------------------------------------------------------

    sealed class Recorder : RapidJson.IHandler
    {
        public string Ev = "";
        bool Add(string s) { if (Ev.Length != 0) Ev += " "; Ev += s; return true; }
        public bool Null() => Add("n");
        public bool Bool(bool b) => Add(b ? "t" : "f");
        public bool Int(int i) => Add("?int");
        public bool Uint(uint u) => Add("?uint");
        public bool Int64(long i) => Add("?int64");
        public bool Uint64(ulong u) => Add("?uint64");
        public bool Double(double d) => Add("?double");
        public bool RawNumber(ReadOnlySpan<byte> s) => Add("#" + Encoding.ASCII.GetString(s));
        public bool String(ReadOnlySpan<byte> s) => Add("s" + Q(s));
        public bool Key(ReadOnlySpan<byte> s) => Add("k" + Q(s));
        public bool StartObject() => Add("{");
        public bool EndObject(uint c) => Add("}" + c.ToString(CultureInfo.InvariantCulture));
        public bool StartArray() => Add("[");
        public bool EndArray(uint c) => Add("]" + c.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Reader_sends_the_events_and_errors_of_rapidjson()
    {
        var cases = Cases("rdr");
        Assert.Equal(81, cases.Count);
        const RapidJson.ParseFlags flags = RapidJson.ParseFlags.ValidateEncoding | RapidJson.ParseFlags.FullPrecision |
            RapidJson.ParseFlags.Iterative | RapidJson.ParseFlags.NumbersAsStrings;
        foreach (JsonElement c in cases)
        {
            byte[] text = Bytes(c.GetProperty("t"));
            var rec = new Recorder();
            var reader = new RapidJson.Reader();
            reader.Parse(text, 0, text.Length, flags, rec);
            string what = $"rdr {c.GetProperty("t").GetRawText()}";
            Assert.True(c.GetProperty("ev").GetString() == rec.Ev, $"{what}: events '{rec.Ev}', expected '{c.GetProperty("ev").GetString()}'");
            Assert.True(c.GetProperty("code").GetInt32() == (int)reader.GetParseErrorCode(), $"{what}: code {reader.GetParseErrorCode()}");
            Assert.True(c.GetProperty("off").GetInt64() == reader.GetErrorOffset(), $"{what}: offset {reader.GetErrorOffset()}");
        }
    }

    // The C++ probe (gen_snapshot.cpp Num) calls Document::Parse<kParseFullPrecisionFlag>, rapidjson's RECURSIVE parser; the port has
    // only the iterative one. For a document that is one number literal both send the same single event and the same errors.
    [Fact]
    public void Number_values_and_types_match_rapidjson()
    {
        var cases = Cases("num");
        Assert.Equal(4558, cases.Count);
        foreach (JsonElement c in cases)
        {
            byte[] text = Bytes(c.GetProperty("t"));
            var doc = new RapidJson.Document().Parse(text, 0, text.Length, RapidJson.ParseFlags.FullPrecision | RapidJson.ParseFlags.Iterative);
            string what = $"num '{c.GetProperty("t").GetString()}'";
            Assert.True(c.GetProperty("code").GetInt32() == (int)doc.GetParseError(), $"{what}: code {doc.GetParseError()}");
            Assert.True(c.GetProperty("off").GetInt64() == doc.GetErrorOffset(), $"{what}: offset {doc.GetErrorOffset()}");
            if (doc.HasParseError()) continue;
            RapidJson.Value v = doc.Root;
            string ty = (v.IsInt ? "i" : "") + (v.IsUint ? "u" : "") + (v.IsInt64 ? "I" : "") + (v.IsUint64 ? "U" : "") + (v.IsDouble ? "d" : "") + (v.IsNumber ? "" : "?");
            if (v.IsInt) ty += ":" + v.GetInt().ToString(CultureInfo.InvariantCulture);
            if (v.IsUint) ty += ":" + v.GetUint().ToString(CultureInfo.InvariantCulture);
            Assert.True(c.GetProperty("ty").GetString() == ty, $"{what}: type '{ty}', expected '{c.GetProperty("ty").GetString()}'");
            Assert.True(c.GetProperty("d").GetUInt64() == unchecked((ulong)BitConverter.DoubleToInt64Bits(v.GetDouble())),
                $"{what}: value {v.GetDouble():R}, expected {Dbl(c.GetProperty("d")):R}");
        }
    }

    [Fact]
    public void Double_text_matches_rapidjson_Grisu2()
    {
        var cases = Cases("dbl");
        Assert.Equal(4041, cases.Count);
        foreach (JsonElement c in cases)
        {
            double d = Dbl(c.GetProperty("d"));
            var w = new RapidJson.PrettyWriter();
            bool ok = w.Double(d);
            Assert.True(c.GetProperty("ok").GetBoolean() == ok, $"dbl {d:R}: ok");
            string text = Encoding.ASCII.GetString(w.GetBytes());
            Assert.True(c.GetProperty("w").GetString() == text, $"dbl {d:R}: '{text}', expected '{c.GetProperty("w").GetString()}'");
        }
    }
}
