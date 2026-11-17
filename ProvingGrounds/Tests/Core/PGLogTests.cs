using ProvingGrounds.Core;
using Xunit;

namespace ProvingGrounds.Tests.Core;

/// <summary>Records what PGLog hands the glue sink. Shared with CombatBalanceLogTests.</summary>
sealed class RecordingSink : IPgLogSink
{
    public string Dir = "C:\\Kenshi\\mods\\ProvingGrounds\\";
    public PgSystemTime Time = new PgSystemTime(2026, 11, 11, 3, 4, 5, 6);
    public bool Fail;
    public readonly List<(string Path, string Line)> Lines = new();
    public readonly List<string> Warnings = new();
    public string Directory => Dir;
    public PgSystemTime UtcNow() => Time;
    public bool Append(string path, string line) { Lines.Add((path, line)); return !Fail; }
    public void Warn(string text) => Warnings.Add(text);
}

/// <summary>A sink whose members throw on demand (a faulty glue sink).</summary>
sealed class ThrowingSink : IPgLogSink
{
    public bool ThrowDirectory, ThrowClock, ThrowAppend, ThrowWarn;
    public readonly List<string> Lines = new();
    public int WarnCalls;
    public string Directory => ThrowDirectory ? throw new IOException("dir") : "D\\";
    public PgSystemTime UtcNow() => ThrowClock ? throw new InvalidOperationException("clock") : new PgSystemTime(2026, 1, 2, 3, 4, 5, 6);
    public bool Append(string path, string line) { if (ThrowAppend) throw new IOException("append"); Lines.Add(line); return true; }
    public void Warn(string text) { ++WarnCalls; if (ThrowWarn) throw new IOException("warn"); }
}

/// <summary>Core/PGLog.cs by hand (PGLog.cpp uses Windows APIs; no golden vectors). Every expected text is worked from the C++
/// format strings at the line cited.</summary>
public class PGLogTests
{
    const string Warning = "Proving Grounds: could not write pg_debug.log, pg_combat_debug.log or pg_challenge_debug.log beside ProvingGrounds.dll in the mod folder\n";

    static (IDisposable, RecordingSink) Installed()
    {
        var sink = new RecordingSink();
        IDisposable scope = PGLog.UseForTest(PGLog.State.Shipped());
        PGLog.Install(sink, 0x01DB2F3Au, 0x0000ABCDu, 1234u);
        return (scope, sink);
    }

    [Fact]
    public void Session_id_is_two_lowercase_hex_dwords_and_the_decimal_pid()
    {
        // PGLog.cpp:25 "%08lx%08lx-%lu"
        Assert.Equal("01db2f3a0000abcd-1234", PGLog.SessionId(0x01DB2F3Au, 0x0000ABCDu, 1234u));
        Assert.Equal("00000000ffffffff-4294967295", PGLog.SessionId(0, uint.MaxValue, uint.MaxValue));
        Assert.Equal("abcdef0000000001-0", PGLog.SessionId(0xABCDEF00u, 1, 0));
    }

    [Fact]
    public void Quote_escapes_quote_backslash_and_control_bytes_only()
    {
        // PGLog.cpp:54-63
        Assert.Equal("\"\"", PGLog.Quote(""));
        Assert.Equal("\"\"", PGLog.Quote(null));
        Assert.Equal("\"a\\\"b\\\\c\"", PGLog.Quote("a\"b\\c"));
        Assert.Equal("\"\\u0000\\u0001\\u0009\\u000a\\u000d\\u001b\\u001f \"", PGLog.Quote("\0\u0001\t\n\r\u001b\u001f "));
        // 127 and non-ASCII are copied unescaped; '/' is not escaped
        Assert.Equal("\"\u007f/é日\"", PGLog.Quote("\u007f/é日"));
    }

    [Fact]
    public void Debug_and_error_lines()
    {
        var (scope, sink) = Installed();
        using (scope)
        {
            PGLog.Debug("hello world");
            PGLog.Error("Proving Grounds: bad");
            sink.Time = new PgSystemTime(999, 1, 2, 23, 59, 9, 999);
            PGLog.Debug("");
        }
        // PGLog.cpp:35-36 "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ", :66 / :70
        Assert.Equal(new[]
        {
            ("C:\\Kenshi\\mods\\ProvingGrounds\\pg_debug.log", "2026-11-11T03:04:05.006Z [01db2f3a0000abcd-1234] DEBUG hello world\r\n"),
            ("C:\\Kenshi\\mods\\ProvingGrounds\\pg_debug.log", "2026-11-11T03:04:05.006Z [01db2f3a0000abcd-1234] ERROR Proving Grounds: bad\r\n"),
            ("C:\\Kenshi\\mods\\ProvingGrounds\\pg_debug.log", "0999-01-02T23:59:09.999Z [01db2f3a0000abcd-1234] DEBUG \r\n"),
        }, sink.Lines);
        Assert.Empty(sink.Warnings);
    }

    [Fact]
    public void Combat_and_challenge_envelope()
    {
        var (scope, sink) = Installed();
        using (scope)
        {
            PGLog.Combat("\"event\":\"x\"");
            PGLog.Challenge("\"a\":1");
        }
        // PGLog.cpp:75-85
        Assert.Equal(new[]
        {
            ("C:\\Kenshi\\mods\\ProvingGrounds\\pg_combat_debug.log",
                "{\"utc\":\"2026-11-11T03:04:05.006Z\",\"session\":\"01db2f3a0000abcd-1234\",\"event\":\"x\"}\r\n"),
            ("C:\\Kenshi\\mods\\ProvingGrounds\\pg_challenge_debug.log",
                "{\"utc\":\"2026-11-11T03:04:05.006Z\",\"session\":\"01db2f3a0000abcd-1234\",\"a\":1}\r\n"),
        }, sink.Lines);
    }

    [Fact]
    public void A_failed_write_warns_once_and_the_next_line_is_still_tried()
    {
        var (scope, sink) = Installed();
        using (scope)
        {
            sink.Fail = true;
            PGLog.Debug("a");
            PGLog.Combat("\"b\":1");
            sink.Fail = false;
            PGLog.Debug("c");
            sink.Fail = true;
            PGLog.Challenge("\"d\":1");
        }
        // PGLog.cpp:47-50: one warning per process, never reset by a later success
        Assert.Equal(4, sink.Lines.Count);
        Assert.Equal(new[] { Warning }, sink.Warnings);
    }

    [Fact]
    public void An_empty_directory_writes_nothing_and_warns()
    {
        var (scope, sink) = Installed();
        using (scope)
        {
            sink.Dir = "";
            PGLog.Error("x");
            PGLog.Combat("\"y\":1");
            Assert.Equal("", PGLog.Directory);
        }
        Assert.Empty(sink.Lines);
        Assert.Equal(new[] { Warning }, sink.Warnings);
    }

    [Fact]
    public void NextId_is_one_counter_from_1_that_wraps()
    {
        using (PGLog.UseForTest(PGLog.State.Shipped()))
        {
            Assert.Equal(1u, PGLog.NextId());
            Assert.Equal(2u, PGLog.NextId());
        }
        var s = PGLog.State.Shipped();
        s.Sequence = uint.MaxValue;
        using (PGLog.UseForTest(s))
            Assert.Equal(0u, PGLog.NextId());
    }

    [Fact]
    public void Before_install_nothing_is_written_and_ids_still_count()
    {
        using (PGLog.UseForTest(PGLog.State.Shipped()))
        {
            PGLog.Debug("a"); PGLog.Error("b"); PGLog.Combat("c"); PGLog.Challenge("d");
            Assert.Equal("", PGLog.Directory);
            Assert.Equal(1u, PGLog.NextId());
        }
    }

    [Fact]
    public void Directory_is_the_sinks()
    {
        var (scope, _) = Installed();
        using (scope) Assert.Equal("C:\\Kenshi\\mods\\ProvingGrounds\\", PGLog.Directory);
    }

    [Fact]
    public void PGConfig_log_goes_to_pg_debug_log_with_its_exact_text()
    {
        var (scope, sink) = Installed();
        using (scope)
        using (PGConfig.UseIsolatedForTest())
            PGConfig.Load(null, new FakeFiles(), PGLog.ConfigLog);
        // PGConfig.cpp:824-889: the null-directory error, routed through PGLog::Error unchanged
        Assert.Equal(new[]
        {
            ("C:\\Kenshi\\mods\\ProvingGrounds\\pg_debug.log",
                "2026-11-11T03:04:05.006Z [01db2f3a0000abcd-1234] ERROR Proving Grounds: the plugin directory could not be resolved; pg_config.json cannot be read and the shipped prices stay in force\r\n"),
        }, sink.Lines);

        var (scope2, sink2) = Installed();
        using (scope2) PGLog.ConfigLog.Debug("d");
        Assert.Equal("2026-11-11T03:04:05.006Z [01db2f3a0000abcd-1234] DEBUG d\r\n", sink2.Lines.Single().Line);
    }

    [Fact]
    public void A_throwing_sink_is_a_failed_write_never_rethrown()
    {
        var sink = new ThrowingSink { ThrowAppend = true, ThrowWarn = true };
        using (PGLog.UseForTest(PGLog.State.Shipped()))
        {
            PGLog.Install(sink, 0, 0, 0);
            PGLog.Debug("a");            // Append throws: warn once (Warn throws too: contained)
            PGLog.Combat("\"b\":1");
            sink.ThrowAppend = false; sink.ThrowDirectory = true;
            PGLog.Error("c");            // Directory throws
            Assert.Equal("", PGLog.Directory);
            sink.ThrowDirectory = false; sink.ThrowClock = true;
            PGLog.Challenge("\"d\":1");     // the clock throws: the line is skipped
            PGLog.Debug("e");
            sink.ThrowClock = false;
            PGLog.Debug("f");
            Assert.Equal(1u, PGLog.NextId());
        }
        Assert.Equal(1, sink.WarnCalls);
        Assert.Equal(new[] { "2026-01-02T03:04:05.006Z [0000000000000000-0] DEBUG f\r\n" }, sink.Lines);
    }

    [Fact]
    public void A_throwing_clock_alone_warns()
    {
        var sink = new ThrowingSink { ThrowClock = true };
        using (PGLog.UseForTest(PGLog.State.Shipped()))
        {
            PGLog.Install(sink, 0, 0, 0);
            PGLog.Combat("\"x\":1");
            PGLog.Debug("y");
        }
        Assert.Empty(sink.Lines);
        Assert.Equal(1, sink.WarnCalls);
    }

    [Fact]
    public void Install_refuses_null_and_a_second_call()
    {
        using (PGLog.UseForTest(PGLog.State.Shipped()))
        {
            Assert.Throws<ArgumentNullException>(() => PGLog.Install(null!, 0, 0, 0));
            var first = new RecordingSink();
            PGLog.Install(first, 1, 2, 3);
            Assert.Throws<InvalidOperationException>(() => PGLog.Install(new RecordingSink(), 4, 5, 6));
            PGLog.Debug("x");
            Assert.Equal("2026-11-11T03:04:05.006Z [0000000100000002-3] DEBUG x\r\n", first.Lines.Single().Line);
        }
    }
}
