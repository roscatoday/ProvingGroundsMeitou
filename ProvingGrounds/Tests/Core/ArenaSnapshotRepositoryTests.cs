using System.Text;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaSnapshotRepository.cs (task 1.7a): every branch of LoadSnapshot, RecoverSnapshot and PublishSnapshot
// (ArenaSnapshotRepository.cpp:8-178) over a fake storage. Tests/Golden/SnapshotGoldenTests.cs runs 400 scripted cases against the
// real C++ as well.
public class ArenaSnapshotRepositoryTests
{
    sealed class FakeStorage : IStorage
    {
        public readonly Dictionary<string, (ReadCode Code, byte[] Text, string? Error)> Files = new(StringComparer.Ordinal);
        public List<string>? Backups = new();   // null: ListBackups fails
        public string? ListError;               // null: ListBackups leaves error alone
        public bool PreserveOk = true, ReplaceOk = true;
        public string? PreserveError, ReplaceError;
        public readonly List<string> Calls = new();

        public void Put(string path, byte[] text) => Files[path] = (ReadCode.ReadOk, text, null);

        public ReadCode Read(string path, ref byte[] text, ref string error)
        {
            Calls.Add("read " + path);
            if (!Files.TryGetValue(path, out var f)) return ReadCode.NotFound;
            if (f.Code == ReadCode.ReadOk) text = f.Text;
            if (f.Error != null) error = f.Error;
            return f.Code;
        }

        public bool ListBackups(string path, ref List<string> paths, ref string error)
        {
            Calls.Add("list " + path);
            if (ListError != null) error = ListError;
            if (Backups == null) return false;
            paths = new List<string>(Backups);
            return true;
        }

        public bool Preserve(string path, ref string backupPath, ref string error)
        {
            Calls.Add("preserve " + path);
            if (PreserveError != null) error = PreserveError;
            if (PreserveOk) backupPath = path + ".backup";
            return PreserveOk;
        }

        public bool Replace(string path, byte[] text, ref string error)
        {
            Calls.Add("replace " + path);
            if (ReplaceError != null) error = ReplaceError;
            if (ReplaceOk) Put(path, text);
            return ReplaceOk;
        }
    }

    static byte[] Text(string saveKey, string generation, int credit = 0)
    {
        var s = new Snapshot { SaveKey = saveKey, Generation = generation, ChallengeCredit = credit };
        byte[] json = System.Array.Empty<byte>();
        Assert.True(EncodeSnapshot(s, ref json, out string error), error);
        return json;
    }

    static void Is(LoadResult r, LoadCode code, string message, string saveKey, string generation)
    {
        Assert.Equal((code, message), (r.Code, r.Message));
        Assert.Equal((saveKey, generation), (r.Snapshot.SaveKey, r.Snapshot.Generation));
    }

    // ---- LoadSnapshot -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_matching_sidecar_loads_as_it_is()
    {
        var f = new FakeStorage(); f.Put("main", Text("k", "g", 7));
        LoadResult r = LoadSnapshot(f, "main", "k", "g");
        Is(r, LoadCode.Ready, "", "k", "g");
        Assert.Equal(7, r.Snapshot.ChallengeCredit);
        Assert.Equal(new[] { "read main" }, f.Calls);
    }

    [Fact]
    public void A_read_failure_is_an_IO_failure()
    {
        var f = new FakeStorage(); f.Files["main"] = (ReadCode.ReadFailed, System.Array.Empty<byte>(), null);
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "Could not read arena snapshot", "k", "g");
        f.Files["main"] = (ReadCode.ReadFailed, System.Array.Empty<byte>(), "disk on fire");
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "disk on fire", "k", "g");
    }

    [Fact]
    public void A_missing_sidecar_is_fresh_without_a_generation_and_searched_for_with_one()
    {
        var f = new FakeStorage();
        Is(LoadSnapshot(f, "main", "k", ""), LoadCode.Missing, "No arena snapshot exists for a fresh world", "k", "");
        Assert.Equal(new[] { "read main" }, f.Calls);
        f.Calls.Clear();
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.WrongSnapshot, "Native snapshot has no matching arena sidecar", "k", "g");
        Assert.Equal(new[] { "read main", "list main" }, f.Calls);
    }

    [Fact]
    public void Listing_backups_can_fail_and_keeps_an_earlier_error()
    {
        var f = new FakeStorage { Backups = null };
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "Could not inspect arena snapshot backups", "k", "g");
        f.ListError = "no dir";
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "no dir", "k", "g");
        // a Read that succeeded but wrote an error leaves it in the shared string: a silent ListBackups failure reports it
        f.ListError = null;
        f.Files["main"] = (ReadCode.ReadOk, Text("other", "g"), "read note");
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "read note", "k", "g");
    }

    sealed class NullTextStorage : IStorage
    {
        public ReadCode Read(string path, ref byte[] text, ref string error) { text = null!; return ReadCode.ReadOk; }
        public bool ListBackups(string path, ref List<string> paths, ref string error) { paths = new List<string> { "b" }; return true; }
        public bool Preserve(string path, ref string backupPath, ref string error) => true;
        public bool Replace(string path, byte[] text, ref string error) => true;
    }

    [Fact]
    public void A_null_text_on_ReadOk_reads_as_an_empty_text()
    {
        LoadResult r = LoadSnapshot(new NullTextStorage(), "main", "k", "g");
        Assert.Equal((LoadCode.InvalidJson, "$", "The document is empty. at byte 0"), (r.Code, r.Field, r.Message));
        string error = "";
        Assert.False(PublishSnapshot(new NullTextStorage(), "main", new Snapshot { SaveKey = "k", Generation = "g" }, ref error));
        Assert.Equal("$: The document is empty. at byte 0", error);
        Assert.Throws<ArgumentNullException>(() => DecodeSnapshot((byte[])null!));
        Assert.Throws<ArgumentNullException>(() => DecodeSnapshot((string)null!));
    }

    [Fact]
    public void An_invalid_sidecar_returns_its_decode_result()
    {
        var f = new FakeStorage(); f.Put("main", Encoding.UTF8.GetBytes("{\"version\":3}"));
        LoadResult r = LoadSnapshot(f, "main", "k", "g");
        Assert.Equal((LoadCode.LegacyReset, "$.version"), (r.Code, r.Field));
        Assert.Equal("", r.Snapshot.SaveKey);   // the decode's default snapshot, not the requested identity
        Assert.Equal(new[] { "read main" }, f.Calls);
    }

    [Fact]
    public void Backups_recover_an_exact_match_skipping_unreadable_invalid_and_other_generations()
    {
        var f = new FakeStorage();
        f.Put("main", Text("other", "x"));
        f.Put("b-badjson", Encoding.UTF8.GetBytes("{"));
        f.Put("b-othergen", Text("k", "h"));
        f.Put("b-match", Text("k", "g", 3));
        f.Put("b-later", Text("k", "g", 9));
        f.Backups = new List<string> { "b-missing", "b-badjson", "b-othergen", "b-match", "b-later" };
        LoadResult r = LoadSnapshot(f, "main", "k", "g");
        Is(r, LoadCode.Ready, "Recovered matching snapshot from b-match", "k", "g");
        Assert.Equal(3, r.Snapshot.ChallengeCredit);
        Assert.Equal(new[] { "read main", "list main", "read b-missing", "read b-badjson", "read b-othergen", "read b-match" }, f.Calls);
    }

    [Fact]
    public void A_backup_read_failure_stops_the_search()
    {
        var f = new FakeStorage();
        f.Files["b1"] = (ReadCode.ReadFailed, System.Array.Empty<byte>(), null);
        f.Put("b2", Text("k", "g"));
        f.Backups = new List<string> { "b1", "b2" };
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "Could not read arena snapshot backup b1", "k", "g");
        f.Files["b1"] = (ReadCode.ReadFailed, System.Array.Empty<byte>(), "bad sector");
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.IoFailure, "bad sector", "k", "g");
    }

    [Fact]
    public void A_generation_match_from_another_save_is_rebound_when_nothing_matches_exactly()
    {
        // the main sidecar itself was copied from another slot (emergency save): it is the fallback
        var f = new FakeStorage();
        f.Put("main", Text("old-slot", "g", 5));
        LoadResult r = LoadSnapshot(f, "main", "k", "g");
        Is(r, LoadCode.Ready, "Recovered generation-matched snapshot copied from save 'old-slot'", "old-slot", "g");
        Assert.Equal(5, r.Snapshot.ChallengeCredit);
        // an exact backup wins over it
        f.Put("b1", Text("k", "g", 6));
        f.Backups = new List<string> { "b1" };
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.Ready, "Recovered matching snapshot from b1", "k", "g");
        // a backup from another slot only when the main sidecar gave none; the first one found wins
        var g = new FakeStorage();
        g.Put("b1", Text("slot-a", "g", 1)); g.Put("b2", Text("slot-b", "g", 2));
        g.Backups = new List<string> { "b1", "b2" };
        r = LoadSnapshot(g, "main", "k", "g");
        Is(r, LoadCode.Ready, "Recovered generation-matched snapshot copied from save 'slot-a' at b1", "slot-a", "g");
        // with an empty native generation nothing is rebound
        var h = new FakeStorage();
        h.Put("main", Text("other", "g"));
        Is(LoadSnapshot(h, "main", "k", ""), LoadCode.WrongSnapshot, "Arena sidecar belongs to a different native save", "k", "");
    }

    [Fact]
    public void The_mismatch_message_names_what_differs()
    {
        var f = new FakeStorage(); f.Put("main", Text("k", "old"));
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.WrongSnapshot, "Arena sidecar generation does not match the native snapshot", "k", "g");
        f.Put("main", Text("other", "old"));
        Is(LoadSnapshot(f, "main", "k", "g"), LoadCode.WrongSnapshot, "Arena sidecar belongs to a different native save", "k", "g");
    }

    // ---- PublishSnapshot --------------------------------------------------------------------------------------------------------

    static Snapshot Snap(int credit = 0) => new Snapshot { SaveKey = "k", Generation = "g", ChallengeCredit = credit };

    [Fact]
    public void Publishing_writes_a_new_file_without_a_backup()
    {
        var f = new FakeStorage();
        string error = "stale";
        Assert.True(PublishSnapshot(f, "main", Snap(4), ref error));
        Assert.Equal("", error);
        Assert.Equal(new[] { "read main", "replace main" }, f.Calls);
        Assert.Equal(Text("k", "g", 4), f.Files["main"].Text);
    }

    [Fact]
    public void Publishing_over_a_valid_or_legacy_file_preserves_it_first()
    {
        foreach (byte[] existing in new[] { Text("k", "g"), Encoding.UTF8.GetBytes("{\"version\":2}") })
        {
            var f = new FakeStorage(); f.Put("main", existing);
            string error = "";
            Assert.True(PublishSnapshot(f, "main", Snap(1), ref error));
            Assert.Equal(new[] { "read main", "preserve main", "replace main" }, f.Calls);
        }
    }

    [Fact]
    public void Publishing_refuses_to_replace_an_unreadable_or_invalid_file()
    {
        var f = new FakeStorage(); f.Put("main", Encoding.UTF8.GetBytes("{\"version\":99}"));
        string error = "";
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("$.version: snapshot schema is newer than supported version 16", error);
        f.Put("main", Encoding.UTF8.GetBytes("{"));
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("$: Missing a name for object member. at byte 1", error);
        f.Files["main"] = (ReadCode.ReadFailed, System.Array.Empty<byte>(), null);
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("Could not inspect the existing arena snapshot", error);
        f.Files["main"] = (ReadCode.ReadFailed, System.Array.Empty<byte>(), "locked");
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("locked", error);
        Assert.DoesNotContain("replace main", f.Calls);
    }

    [Fact]
    public void Publishing_reports_encode_preserve_and_replace_failures()
    {
        var f = new FakeStorage();
        string error = "";
        Snapshot bad = Snap(); bad.Version = 15;
        Assert.False(PublishSnapshot(f, "main", bad, ref error));
        Assert.Equal("$.version: only schema version 16 can be written", error);
        Assert.Empty(f.Calls);

        f.Put("main", Text("k", "g"));
        f.PreserveOk = false;
        error = "stale";
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("", error);   // EncodeSnapshot emptied it and the storage wrote nothing
        f.PreserveError = "no space";
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("no space", error);

        f.PreserveOk = true; f.PreserveError = null; f.ReplaceOk = false; f.ReplaceError = "rename failed";
        Assert.False(PublishSnapshot(f, "main", Snap(), ref error));
        Assert.Equal("rename failed", error);
    }
}
