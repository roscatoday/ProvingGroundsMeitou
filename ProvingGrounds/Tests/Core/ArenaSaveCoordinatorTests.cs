using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaSaveCoordinator.cs (ArenaSaveCoordinator.h:1-27), one per branch: empty target, empty generation,
// native failure, publish failure / success, AbandonWorld, SetSaveKey before and after BeginSave. Tests/Golden/SaveCoordGoldenTests.cs
// runs 300 random sequences against the real header and codec as well.
public class ArenaSaveCoordinatorTests
{
    sealed class Mem : IStorage
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public bool ReplaceOk = true;
        public readonly List<string> Calls = new();
        public ReadCode Read(string path, ref byte[] text, ref string error)
        {
            Calls.Add("read " + path);
            if (!Files.TryGetValue(path, out byte[]? t)) return ReadCode.NotFound;
            text = t; return ReadCode.ReadOk;
        }
        public bool ListBackups(string path, ref List<string> paths, ref string error) { Calls.Add("list " + path); return true; }
        public bool Preserve(string path, ref string backupPath, ref string error) { Calls.Add("keep " + path); backupPath = path + ".bak"; return true; }
        public bool Replace(string path, byte[] text, ref string error)
        {
            Calls.Add("write " + path);
            if (!ReplaceOk) { error = "disk full"; return false; }
            Files[path] = text; return true;
        }
    }

    static Snapshot Valid(string key = "save1") => new Snapshot { SaveKey = key, Generation = "ignored" };

    static Snapshot Stored(Mem m, string path)
    {
        LoadResult r = DecodeSnapshot(m.Files[path]);
        Assert.Equal(LoadCode.Ready, r.Code);
        return r.Snapshot;
    }

    [Fact]
    public void Finish_without_a_begin_does_nothing()
    {
        var m = new Mem(); var c = new SaveCoordinator(m);
        Assert.False(c.FinishSave(true)); Assert.Equal("", c.Error()); Assert.Empty(m.Calls);
    }

    [Theory]
    [InlineData("", "g1")]
    [InlineData("save/a.json", "")]
    public void An_empty_target_or_generation_is_not_pending(string target, string generation)
    {
        var m = new Mem(); var c = new SaveCoordinator(m);
        c.BeginSave(target, generation, Valid());
        Assert.False(c.PendingForTest);
        Assert.False(c.FinishSave(true)); Assert.Equal("", c.Error()); Assert.Empty(m.Calls);
        Assert.Equal(generation, c.CapturedForTest.Generation);
    }

    [Fact]
    public void Native_failure_writes_nothing_and_says_so()
    {
        var m = new Mem(); var c = new SaveCoordinator(m);
        c.BeginSave("save/a.json", "g1", Valid());
        Assert.False(c.FinishSave(false));
        Assert.Equal("Native save did not complete successfully", c.Error());
        Assert.Empty(m.Calls);
        Assert.False(c.FinishSave(true), "a finished save is no longer pending");
        c.BeginSave("save/a.json", "g1", Valid());
        Assert.True(c.Error() == "", "BeginSave clears the error");
    }

    [Fact]
    public void Success_publishes_the_capture_with_the_given_generation()
    {
        var m = new Mem(); var c = new SaveCoordinator(m);
        Snapshot s = Valid();
        s.BookieCredit = 0;
        c.BeginSave("save/a.json", "g1", s);
        s.SaveKey = "changed later"; s.Generation = "changed later";
        Assert.True(c.FinishSave(true));
        Assert.Equal("", c.Error());
        Assert.Equal(new[] { "read save/a.json", "write save/a.json" }, m.Calls);
        Snapshot w = Stored(m, "save/a.json");
        Assert.True(w.SaveKey == "save1" && w.Generation == "g1", "the caller's later edits do not reach the publish");
        m.Calls.Clear();
        c.BeginSave("save/a.json", "g2", Valid());
        Assert.True(c.FinishSave(true));
        Assert.Equal(new[] { "read save/a.json", "keep save/a.json", "write save/a.json" }, m.Calls);
    }

    [Fact]
    public void Publish_failure_reports_the_storage_error()
    {
        var m = new Mem { ReplaceOk = false }; var c = new SaveCoordinator(m);
        c.BeginSave("save/a.json", "g1", Valid());
        Assert.False(c.FinishSave(true)); Assert.Equal("disk full", c.Error());
        c.BeginSave("save/a.json", "g1", new Snapshot());
        Assert.False(c.FinishSave(true));
        Assert.Equal("$.saveKey: expected a nonempty identifier", c.Error());
    }

    [Fact]
    public void SetSaveKey_before_BeginSave_is_overwritten_and_after_it_is_published()
    {
        var m = new Mem(); var c = new SaveCoordinator(m);
        c.SetSaveKey("early");
        c.BeginSave("save/a.json", "g1", Valid("from snapshot"));
        Assert.Equal("from snapshot", c.CapturedForTest.SaveKey);
        c.SetSaveKey("late");
        Assert.True(c.FinishSave(true));
        Assert.Equal("late", Stored(m, "save/a.json").SaveKey);
    }

    [Fact]
    public void AbandonWorld_drops_the_pending_save_but_keeps_the_error()
    {
        var m = new Mem(); var c = new SaveCoordinator(m);
        c.BeginSave("save/a.json", "g1", Valid());
        Assert.False(c.FinishSave(false));
        c.BeginSave("save/a.json", "g1", Valid());
        c.AbandonWorld();
        Assert.False(c.PendingForTest); Assert.Equal("", c.TargetForTest); Assert.Equal("", c.CapturedForTest.SaveKey);
        Assert.False(c.FinishSave(true)); Assert.Empty(m.Calls);
        c.BeginSave("save/a.json", "g1", Valid()); Assert.False(c.FinishSave(false));
        c.AbandonWorld();
        Assert.Equal("Native save did not complete successfully", c.Error());
        c.SetSaveKey("after abandon");
        Assert.Equal("after abandon", c.CapturedForTest.SaveKey);
    }
}
