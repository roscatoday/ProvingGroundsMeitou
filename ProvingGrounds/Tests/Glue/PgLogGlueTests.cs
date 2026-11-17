using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Core/Glue/PgLogGlue.cs: the pure half of the real log sink (task 2.7): the folder text, the FILETIME split of the session id,
/// the __DATE__ __TIME__ build text, the startPlugin lines (Plugin.cpp:15-16) and which pg_debug.log lines also go to Meitou's error log.</summary>
public class PgLogGlueTests
{
    sealed class Sink : IPgLogSink
    {
        public readonly List<(string Path, string Line)> Lines = [];
        public string Directory => "C:\\mods\\PG\\";
        public PgSystemTime UtcNow() => new(2026, 11, 12, 1, 2, 3, 4);
        public bool Append(string path, string line) { Lines.Add((path, line)); return true; }
        public void Warn(string text) { }
    }

    [Theory]
    [InlineData("C:\\mods\\PG", "C:\\mods\\PG\\")]
    [InlineData("C:\\mods\\PG\\", "C:\\mods\\PG\\")]
    [InlineData("C:/mods/PG/", "C:/mods/PG/")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void FolderEndsWithOneSeparator(string? folder, string expected) =>
        Assert.Equal(expected, PgLogGlue.Folder(folder, '\\'));

    [Fact]
    public void FileTimeIsTheTwoDwordsOfGetSystemTimeAsFileTime()
    {
        var t = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        long ft = t.ToFileTimeUtc();
        var (high, low) = PgLogGlue.FileTime(t);
        Assert.Equal((uint)(ft >> 32), high);
        Assert.Equal((uint)(ft & 0xFFFFFFFF), low);
        Assert.Equal(((ulong)high << 32) | low, (ulong)ft);
    }

    [Fact]
    public void SessionIdFromTheSplitIsTheCFormat()
    {
        var (high, low) = PgLogGlue.FileTime(DateTime.FromFileTimeUtc(0x01DB2F3A0000ABCDL));
        Assert.Equal("01db2f3a0000abcd-1234", PGLog.SessionId(high, low, 1234));
    }

    [Theory]
    [InlineData(2026, 11, 2, 9, 5, 7, "Nov  2 2026 09:05:07")]   // __DATE__ pads a one-digit day with a space
    [InlineData(2026, 1, 12, 23, 59, 0, "Jan 12 2026 23:59:00")]
    [InlineData(2026, 9, 30, 0, 0, 1, "Sep 30 2026 00:00:01")]
    public void BuildStampIsDateAndTimeMacros(int y, int mo, int d, int h, int mi, int s, string expected) =>
        Assert.Equal(expected, PgLogGlue.BuildStamp(new DateTime(y, mo, d, h, mi, s)));

    [Fact]
    public void SystemTimeCopiesEveryField()
    {
        var t = PgLogGlue.SystemTime(new DateTime(2026, 11, 12, 13, 14, 15, 678, DateTimeKind.Utc));
        Assert.Equal((2026, 11, 12, 13, 14, 15, 678), ((int)t.Year, (int)t.Month, (int)t.Day, (int)t.Hour, (int)t.Minute, (int)t.Second, (int)t.Milliseconds));
    }

    [Fact]
    public void WallMs64IsTheSystemTickCount()
    {
        // CombatBalanceLog.cpp:167, :199 GetTickCount64 (task 8.7c): the real millisecond counter since boot, not the sim clock (which starts at
        // SimClock.StartTicks for every world and stops while paused). Bracketed by Environment.TickCount64 (= GetTickCount64 on Windows).
        long before = Environment.TickCount64;
        ulong wall = PgLogGlue.WallMs64();
        long after = Environment.TickCount64;
        Assert.InRange(wall, (ulong)before, (ulong)after);
    }

    [Fact]
    public void StartPluginWritesTheTwoLinesOfPluginCpp()
    {
        var sink = new Sink();
        using var _ = PGLog.UseForTest(PGLog.State.Shipped());
        PGLog.Install(sink, 0x01DB2F3Au, 0x0000ABCDu, 1234u);
        PgLogGlue.StartPlugin("Nov 12 2026 10:00:00");
        Assert.Equal(2, sink.Lines.Count);
        Assert.Equal(("C:\\mods\\PG\\pg_debug.log",
            "2026-11-12T01:02:03.004Z [01db2f3a0000abcd-1234] DEBUG Proving Grounds: startPlugin; logs beside ProvingGrounds.dll in the mod folder; build Nov 12 2026 10:00:00\r\n"),
            sink.Lines[0]);
        Assert.Equal(("C:\\mods\\PG\\pg_combat_debug.log",
            "{\"utc\":\"2026-11-12T01:02:03.004Z\",\"session\":\"01db2f3a0000abcd-1234\",\"event\":\"session_start\",\"schema\":1,\"build\":\"Nov 12 2026 10:00:00\"}\r\n"),
            sink.Lines[1]);
    }

    sealed class NoFolderSink : IPgLogSink
    {
        public int Appends;
        public string Directory => "";
        public PgSystemTime UtcNow() => new(2026, 11, 12, 1, 2, 3, 4);
        public bool Append(string path, string line) { Appends++; return true; }
        public void Warn(string text) { }
    }

    [Fact]
    public void AnErrorReachesMeitouAndTheFile()
    {
        var sink = new Sink();
        var meitou = new List<string>();
        using var _ = PGLog.UseForTest(PGLog.State.Shipped());
        PGLog.Install(sink, 1u, 2u, 3u);
        var route = new PgLogRoute(meitou.Add);
        route.Error("Proving Grounds: x");
        route.Debug("Proving Grounds: a debug line");
        Assert.Equal(["Proving Grounds: x"], meitou);
        Assert.Equal(2, sink.Lines.Count);
        Assert.EndsWith("] ERROR Proving Grounds: x\r\n", sink.Lines[0].Line);
        Assert.EndsWith("] DEBUG Proving Grounds: a debug line\r\n", sink.Lines[1].Line);
    }

    [Fact]
    public void AnErrorReachesMeitouWithNoFolder()
    {
        var sink = new NoFolderSink();
        var meitou = new List<string>();
        using var _ = PGLog.UseForTest(PGLog.State.Shipped());
        PGLog.Install(sink, 1u, 2u, 3u);
        new PgLogRoute(meitou.Add).Error("Proving Grounds: y");
        Assert.Equal(["Proving Grounds: y"], meitou);
        Assert.Equal(0, sink.Appends);   // PGLog.cpp:43: nothing written without a folder
    }

    [Fact]
    public void AnErrorReachesMeitouBeforeAnyInstall()
    {
        var meitou = new List<string>();
        using var _ = PGLog.UseForTest(PGLog.State.Shipped());   // never installed (Install failed)
        new PgLogRoute(meitou.Add).Error("Proving Grounds: z");
        Assert.Equal(["Proving Grounds: z"], meitou);
    }

    [Fact]
    public void AThrowingMeitouLogNeverReachesTheCaller()
    {
        using var _ = PGLog.UseForTest(PGLog.State.Shipped());
        new PgLogRoute(_ => throw new InvalidOperationException("closed")).Error("Proving Grounds: w");
    }

    [Fact]
    public void PGConfigErrorsTakeTheRoute()
    {
        var meitou = new List<string>();
        using var _ = PGLog.UseForTest(PGLog.State.Shipped());
        IConfigLog log = new PgLogRoute(meitou.Add);
        log.Error("Proving Grounds: pg_config.json exists but could not be read");
        log.Debug("Proving Grounds: pg_config.json read");
        Assert.Equal(["Proving Grounds: pg_config.json exists but could not be read"], meitou);
    }
}
