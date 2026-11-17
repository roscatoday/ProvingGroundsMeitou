using System.Text;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task M2.6: the real-disk IConfigFiles (PGConfig.cpp ReadText :436-464, default write :614-619, MoveFileExW / DeleteFileW
/// :1060-1068): Missing only when the file does not exist, a locked or failing read is Unreadable, atomic writes leave no temporary file.</summary>
public sealed class PgConfigFilesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "pg-config-files-" + Guid.NewGuid().ToString("N"));
    readonly PgConfigFiles files = new();

    public PgConfigFilesTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    string P(string name) => Path.Combine(dir, name);
    string[] Leftovers() => Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n!.Contains(".tmp")).ToArray()!;

    [Fact]
    public void MissingFileAndMissingFolderAreMissing()
    {
        Assert.Equal(ConfigReadStatus.Missing, files.Read(P("pg_config.json")).Status);
        Assert.Equal(ConfigReadStatus.Missing, files.Read(Path.Combine(dir, "no-such-folder", "pg_config.json")).Status);
    }

    [Fact]
    public void ReadGivesTheRawBytes()
    {
        byte[] bytes = { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' };
        File.WriteAllBytes(P("pg_config.json"), bytes);
        ConfigRead read = files.Read(P("pg_config.json"));
        Assert.Equal(ConfigReadStatus.Read, read.Status);
        Assert.Equal(bytes, read.Bytes);
    }

    [Fact]
    public void AFolderWithTheNameIsUnreadable()
    {
        Directory.CreateDirectory(P("pg_config.json"));
        Assert.Equal(ConfigReadStatus.Unreadable, files.Read(P("pg_config.json")).Status);
    }

    [Fact]
    public void ALockedFileIsUnreadableNotMissing()
    {
        File.WriteAllText(P("pg_config.json"), "{}");
        if (!OperatingSystem.IsWindows()) return;   // file locks are advisory elsewhere
        using (new FileStream(P("pg_config.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(ConfigReadStatus.Unreadable, files.Read(P("pg_config.json")).Status);
    }

    [Fact]
    public void AFileOverOneMegabyteIsUnreadable()
    {
        File.WriteAllBytes(P("pg_config.json"), new byte[1024 * 1024 + 1]);
        Assert.Equal(ConfigReadStatus.Unreadable, files.Read(P("pg_config.json")).Status);
    }

    [Fact]
    public void WriteCreatesTheFileAndLeavesNoTemporary()
    {
        Assert.Equal(ConfigWriteStatus.Written, files.Write(P("pg_config.json"), Encoding.UTF8.GetBytes("{\"version\": 1}")));
        Assert.Equal("{\"version\": 1}", File.ReadAllText(P("pg_config.json")));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void TheDefaultFileNeverReplacesAFileThatAppeared()
    {
        File.WriteAllText(P("pg_config.json"), "player's");
        Assert.Equal(ConfigWriteStatus.Incomplete, files.Write(P("pg_config.json"), Encoding.UTF8.GetBytes("{}")));
        Assert.Equal("player's", File.ReadAllText(P("pg_config.json")));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void CoresTemporaryFileIsTruncatedLikeWb()
    {
        File.WriteAllText(P("pg_config.json.tmp"), "old and longer");
        Assert.Equal(ConfigWriteStatus.Written, files.Write(P("pg_config.json.tmp"), Encoding.UTF8.GetBytes("new")));
        Assert.Equal("new", File.ReadAllText(P("pg_config.json.tmp")));
        Assert.Equal(new[] { "pg_config.json.tmp" }, Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void WriteIntoAMissingFolderIsNotOpened()
    {
        Assert.Equal(ConfigWriteStatus.NotOpened, files.Write(Path.Combine(dir, "no-such-folder", "pg_config.json"), new byte[] { 1 }));
    }

    [Fact]
    public void MoveReplaceAndDelete()
    {
        File.WriteAllText(P("pg_config.json"), "old");
        File.WriteAllText(P("pg_config.json.tmp"), "new");
        Assert.True(files.MoveReplace(P("pg_config.json.tmp"), P("pg_config.json")));
        Assert.Equal("new", File.ReadAllText(P("pg_config.json")));
        Assert.False(files.MoveReplace(P("absent.tmp"), P("pg_config.json")));
        files.Delete(P("pg_config.json"));
        files.Delete(P("pg_config.json"));   // a second delete is ignored, as DeleteFileW's result is
        Assert.False(File.Exists(P("pg_config.json")));
    }

    [Fact]
    public void LoadOverTheRealDiskWritesTheShippedFileOnce()
    {
        var log = new ProvingGrounds.Tests.Core.FakeLog();
        using (PGConfig.UseIsolatedForTest())
            PGConfig.Load(dir + Path.DirectorySeparatorChar, files, log);
        Assert.Contains("// true lets F8 toggle the debug menu; false disables the F8 action", File.ReadAllText(P("pg_config.json")));
        Assert.Empty(Leftovers());
        Assert.Empty(log.Errors);
    }
}
