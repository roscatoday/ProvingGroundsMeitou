using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaSaveCoordinator.cs against Tests/Golden/savecoord.json (golden/gen_savecoord.cpp: the real header with the real
/// codec and repository, rapidjson v1.1.0): every step of 300 call sequences over a scripted in-memory storage: FinishSave's
/// result, Error(), the private pending / target / captured key and generation, the storage calls in order and the stored bytes.</summary>
public class SaveCoordGoldenTests
{
    static List<JsonElement> Cases(string kind) =>
        GoldenFile.Load("savecoord").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(300, Cases("sc").Count);
        Assert.Equal(2342, Cases("sc").Sum(c => c.GetProperty("steps").GetArrayLength()));
        Assert.Equal(15, Cases("txt").Count);
    }

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

    static string Latin(byte[] b) => new string(b.Select(x => (char)x).ToArray());

    /// <summary>gen_savecoord.cpp Fake (a std::map: ordinal order).</summary>
    sealed class Fake : IStorage
    {
        public readonly SortedDictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public bool ReadFail, PreserveFail, ReplaceFail, SetError;
        public string Log = "";

        public ReadCode Read(string path, ref byte[] text, ref string error)
        {
            Log += "R:" + path + ";";
            if (ReadFail) { if (SetError) error = "read broke"; return ReadCode.ReadFailed; }
            if (!Files.TryGetValue(path, out byte[]? t)) return ReadCode.NotFound;
            text = t; return ReadCode.ReadOk;
        }

        public bool ListBackups(string path, ref List<string> paths, ref string error) { Log += "L:" + path + ";"; return true; }

        public bool Preserve(string path, ref string backupPath, ref string error)
        {
            Log += "P:" + path + ";";
            if (PreserveFail) { if (SetError) error = "keep broke"; return false; }
            backupPath = path + ".bak";
            Files[backupPath] = Files.TryGetValue(path, out byte[]? t) ? t : System.Array.Empty<byte>();
            return true;
        }

        public bool Replace(string path, byte[] text, ref string error)
        {
            Log += "W:" + path + ";";
            if (ReplaceFail) { if (SetError) error = "write broke"; return false; }
            Files[path] = text; return true;
        }
    }

    // C++: gen_savecoord.cpp Base
    static Snapshot Base(int b)
    {
        var s = new Snapshot();
        if (b == 1)
        {
            s.SaveKey = "s1"; s.Generation = "old";
            var p = new LeaderboardData.Progression { Id = "a", Name = "Al", Marks = 5 };
            p.RewardTiers.Add(new RewardProgression.TierEntry(ArenaRewards.StableId(ArenaRewards.Piece.Katana), 1));
            s.Fighters.Progression.Add(p);
        }
        else if (b == 2)
        {
            s.PlannedNpc.Active = true; s.PlannedNpc.RegistryId = "r"; s.PlannedNpc.StartHours = double.NaN;
            s.PlannedNpc.TeamA.Add("x"); s.PlannedNpc.TeamB.Add("y");
        }
        else if (b == 3)
        {
            s.BookieCredit = 7; s.ChallengeCredit = 3; s.Fighters.FactionUnlocks.Add(ArenaRewards.GetWeaponLicence().StableId); s.SaveKey = "s3";
        }
        return s;
    }

    [Fact]
    public void Sequences_match()
    {
        Dictionary<int, byte[]> texts = Cases("txt").ToDictionary(c => c.GetProperty("id").GetInt32(), c => Bytes(c.GetProperty("t").GetString()!));
        int finishes = 0, published = 0;
        foreach (JsonElement c in Cases("sc"))
        {
            var files = new Fake();
            var coord = new SaveCoordinator(files);
            var held = new Snapshot();
            int k = 0;
            foreach (JsonElement st in c.GetProperty("steps").EnumerateArray())
            {
                files.Log = "";
                string op = st.GetProperty("op").GetString()!;
                string w = $"sc {c.GetProperty("i").GetInt32()} step {k} {op}";
                switch (op)
                {
                    case "begin":
                        held = Base(st.GetProperty("b").GetInt32());
                        coord.BeginSave(st.GetProperty("d").GetString()!, st.GetProperty("g").GetString()!, held);
                        break;
                    case "mut": held.SaveKey = "scribble"; held.Generation = "scribble"; held.Fighters.FactionUnlocks.Add("scribble"); break;
                    case "key": coord.SetSaveKey(st.GetProperty("v").GetString()!); break;
                    case "fin":
                    {
                        bool r = coord.FinishSave(st.GetProperty("n").GetBoolean());
                        Assert.True(st.GetProperty("r").GetBoolean() == r, $"{w}: result");
                        ++finishes; if (r) ++published;
                        break;
                    }
                    case "ab": coord.AbandonWorld(); break;
                    case "sto":
                        files.ReadFail = st.GetProperty("rf").GetBoolean(); files.PreserveFail = st.GetProperty("pf").GetBoolean();
                        files.ReplaceFail = st.GetProperty("wf").GetBoolean(); files.SetError = st.GetProperty("se").GetBoolean();
                        break;
                    case "put": files.Files[st.GetProperty("p").GetString()!] = texts[st.GetProperty("t").GetInt32()]; break;
                    case "del": files.Files.Remove(st.GetProperty("p").GetString()!); break;
                    default: throw new InvalidOperationException(w + ": unknown op");
                }
                if (op != "fin") Assert.True(st.GetProperty("r").ValueKind == JsonValueKind.Null, w);
                Assert.True(st.GetProperty("err").GetString() == coord.Error(), $"{w}: error expected {st.GetProperty("err")}, got {coord.Error()}");
                Assert.True(st.GetProperty("pend").GetBoolean() == coord.PendingForTest, $"{w}: pending");
                Assert.True(st.GetProperty("tgt").GetString() == coord.TargetForTest, $"{w}: target");
                Assert.True(st.GetProperty("key").GetString() == coord.CapturedForTest.SaveKey, $"{w}: captured saveKey");
                Assert.True(st.GetProperty("gen").GetString() == coord.CapturedForTest.Generation, $"{w}: captured generation");
                Assert.True(st.GetProperty("log").GetString() == files.Log, $"{w}: log expected {st.GetProperty("log")}, got {files.Log}");
                if (st.TryGetProperty("files", out JsonElement fe))
                {
                    var expected = fe.EnumerateArray().Select(f => (f[0].GetString()!, f[1].GetInt32())).ToList();
                    Assert.True(expected.Count == files.Files.Count, $"{w}: file count expected {expected.Count}, got {files.Files.Count}");
                    int j = 0;
                    foreach (KeyValuePair<string, byte[]> f in files.Files)
                    {
                        Assert.True(expected[j].Item1 == f.Key, $"{w}: file {j} path expected {expected[j].Item1}, got {f.Key}");
                        Assert.True(texts[expected[j].Item2].AsSpan().SequenceEqual(f.Value),
                            $"{w}: file {f.Key} bytes differ:\nexpected {Latin(texts[expected[j].Item2])}\ngot      {Latin(f.Value)}");
                        ++j;
                    }
                }
                else
                    Assert.True(!files.Log.Contains('P') && !files.Log.Contains('W'), $"{w}: storage changed on a step the golden says did not");
                ++k;
            }
        }
        Assert.True(finishes == 493 && published == 37, $"coverage: {finishes} finishes, {published} published (pinned: 493, 37)");
    }
}
