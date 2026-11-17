using System.Text;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/PGConfig.cs (tasks 1.5, 1.6); each expected value and message is read off PGConfig.cpp. The reader cannot be
// golden-tested (PGConfig.cpp needs rapidjson, which the source release does not ship); its text output is pinned by
// Tests/Golden/PGConfigTextGoldenTests.cs. Every test that loads isolates both globals with PGConfig.UseIsolatedForTest().

sealed class FakeFiles : IConfigFiles
{
    public readonly Dictionary<string, byte[]> Disk = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    public readonly HashSet<string> Unreadable = new HashSet<string>(StringComparer.Ordinal);
    public readonly HashSet<string> CannotOpen = new HashSet<string>(StringComparer.Ordinal);
    public readonly HashSet<string> WriteIncomplete = new HashSet<string>(StringComparer.Ordinal);
    public bool MoveFails;
    public readonly List<string> Ops = new List<string>();

    public ConfigRead Read(string path)
    {
        Ops.Add("read " + path);
        if (Unreadable.Contains(path)) return new ConfigRead(ConfigReadStatus.Unreadable, null);
        return Disk.TryGetValue(path, out byte[]? bytes)
            ? new ConfigRead(ConfigReadStatus.Read, bytes)
            : new ConfigRead(ConfigReadStatus.Missing, null);
    }

    public ConfigWriteStatus Write(string path, byte[] bytes)
    {
        Ops.Add("write " + path);
        if (CannotOpen.Contains(path)) return ConfigWriteStatus.NotOpened;
        Disk[path] = bytes;
        return WriteIncomplete.Contains(path) ? ConfigWriteStatus.Incomplete : ConfigWriteStatus.Written;
    }

    public bool MoveReplace(string source, string destination)
    {
        Ops.Add("move " + source + " " + destination);
        if (MoveFails || !Disk.TryGetValue(source, out byte[]? bytes)) return false;
        Disk.Remove(source);
        Disk[destination] = bytes;
        return true;
    }

    public void Delete(string path)
    {
        Ops.Add("delete " + path);
        Disk.Remove(path);
    }

    public string Text(string path) => Encoding.UTF8.GetString(Disk[path]);
}

sealed class FakeLog : IConfigLog
{
    public readonly List<string> Debugs = new List<string>();
    public readonly List<string> Errors = new List<string>();
    public void Debug(string message) => Debugs.Add(message);
    public void Error(string message) => Errors.Add(message);
}

public class PGConfigTests
{
    const string Dir = @"C:\Mods\PG\";
    const string Path = Dir + "pg_config.json";

    static string Ignored(string where, string reason) =>
        "Proving Grounds: pg_config.json " + where + " ignored (" + reason + "); the shipped value stays in force";

    // Loads `json` (UTF-8, as is) under isolation and hands back the log; `check` runs inside the isolation.
    static FakeLog LoadText(string json, Action<FakeLog> check) => LoadBytes(Encoding.UTF8.GetBytes(json), check);

    static FakeLog LoadBytes(byte[] bytes, Action<FakeLog> check)
    {
        var files = new FakeFiles();
        files.Disk[Path] = bytes;
        var log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
        {
            PGConfig.Load(Dir, files, log);
            check(log);
        }
        Assert.Equal(bytes, files.Disk[Path]);   // a read file is never rewritten
        return log;
    }

    static void AssertShippedValues()
    {
        var shipped = PGConfig.ShippedEditableSettings();
        var now = PGConfig.GetEditableSettings();
        Assert.Equal(shipped.Recruitment, now.Recruitment);
        Assert.Equal(shipped.Bookie, now.Bookie);
        Assert.Equal(shipped.Challenges, now.Challenges);
        for (int i = 0; i < 3; ++i) Assert.Equal(shipped.ChallengeBase[i], now.ChallengeBase[i]);
        Assert.Equal(shipped.PerformanceProfile, now.PerformanceProfile);
        Assert.Equal(shipped.F8OpensDebugMenu, now.F8OpensDebugMenu);
        Assert.Equal(1.0, PGConfig.Current.Multiplier);
    }

    static int HarnessStandard() => ArenaRewards.Get(ArenaRewards.Piece.PitfighterHarness, ArenaRewards.Tier.Standard).Cost;

    [Fact]   // PGConfig.cpp:37-41, 66, 945-948
    public void Shipped_recruitment_values()
    {
        var r = PGConfig.State.Shipped().Recruitment;
        Assert.Equal(25, r.Minimum);
        Assert.Equal(250, r.Maximum);
        Assert.Equal(5, r.Step);
        Assert.Equal(2.5, r.CombatScale);
        if (!PGConfig.HasTestOverride)
            Assert.Equal(r, PGConfig.RecruitmentValues());
        using (PGConfig.UseRecruitmentForTest(new PGConfig.Recruitment { Minimum = 1, Maximum = 2, Step = 3, CombatScale = 4.0 }))
            Assert.Equal(3, PGConfig.RecruitmentValues().Step);
    }

    [Fact]   // PGConfig.cpp:42-55, 69-74, 960-970
    public void Shipped_editable_settings()
    {
        var s = PGConfig.ShippedEditableSettings();
        Assert.Equal(10, s.Bookie.Minimum);
        Assert.Equal(10000, s.Bookie.Maximum);
        Assert.Equal(500, s.ChallengeBase[0]);
        Assert.Equal(1000, s.ChallengeBase[1]);
        Assert.Equal(2500, s.ChallengeBase[2]);
        Assert.Equal(PGConfig.ChallengeDifficulty.Normal, s.Challenges.Difficulty);
        Assert.Equal(12, s.Challenges.RefreshHours);
        Assert.Equal(500, s.Challenges.RefreshBaseCostCats);
        Assert.Equal(2.0, s.Challenges.RefreshCostMultiplier);
        Assert.Equal(24, s.Challenges.CooldownHours);
        Assert.Equal(1.0, s.Challenges.WinMarksMultiplier);
        Assert.Equal(1.0, s.Challenges.UniqueBonusMultiplier);
        Assert.Equal(1.0, s.Challenges.SkarnBonusMultiplier);
        Assert.Equal(ArenaPerformance.Profile.Normal, s.PerformanceProfile);
        Assert.False(s.F8OpensDebugMenu);
        Assert.Equal(10, PGConfig.StakeStep);
        // a copy of the struct does not share its challenge bases
        var copy = s;
        copy.ChallengeBase[0] = 1;
        Assert.Equal(500, s.ChallengeBase[0]);
    }

    [Fact]   // State.Clone is deep
    public void State_clone_is_deep()
    {
        var a = PGConfig.State.Shipped();
        var b = a.Clone();
        b.ChallengeBase[1] = 7;
        b.Gear["gear.x.standard"] = 1;
        Assert.Equal(1000, a.ChallengeBase[1]);
        Assert.Empty(a.Gear);
    }

    // ---- Load: the file states --------------------------------------------------------------------------------------------

    [Fact]   // PGConfig.cpp:847-851, 517-620, 864-886
    public void Missing_file_writes_the_defaults_and_keeps_the_shipped_values()
    {
        var files = new FakeFiles();
        var log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
        {
            string expected = PGConfig.DefaultsTextForTest(PGConfig.State.Shipped());
            PGConfig.Load(Dir, files, log);
            Assert.Equal(expected, files.Text(Path));
            Assert.Equal(new[] { "read " + Path, "write " + Path }, files.Ops);
            AssertShippedValues();
            Assert.Equal(25, HarnessStandard());
            Assert.Empty(log.Errors);
            Assert.Equal(2, log.Debugs.Count);
            Assert.Equal("Proving Grounds: wrote pg_config.json beside ProvingGrounds.dll from the shipped values; edit that file to change one", log.Debugs[0]);
            Assert.Equal("Proving Grounds: pg_config.json written from the shipped defaults (" + Path + "); applied catalogue=0 supplies=0 licences=0 marks_multiplier=1; " +
                "recruitment=25/250/5 combat_scale=2.5; bookie=10/10000; challenge_base=500/1000/2500; " +
                "challenge_difficulty=normal refresh_hours=12 refresh_base_cats=500 refresh_multiplier=2 " +
                "cooldown_hours=24 marks=1/1/1; performance=normal f8_debug=disabled; rejected=0", log.Debugs[1]);
        }
        Assert.DoesNotContain("\r", files.Text(Path));   // LF line endings, as the C++ writes "\n" in binary mode
    }

    [Fact]   // the written default file reads back as the shipped values, every entry claimed, nothing rejected
    public void Default_file_parses_back_to_the_defaults()
    {
        var files = new FakeFiles();
        using (PGConfig.UseIsolatedForTest())
            PGConfig.Load(Dir, files, new FakeLog());
        LoadBytes(files.Disk[Path], log =>
        {
            AssertShippedValues();
            Assert.Empty(log.Errors);
            Assert.Contains("applied catalogue=92 supplies=8 licences=4 marks_multiplier=1;", log.Debugs[0]);
            Assert.EndsWith("rejected=0", log.Debugs[0]);
            Assert.Contains(" read (", log.Debugs[0]);
        });
    }

    [Fact]   // PGConfig.cpp:852-856
    public void Missing_file_that_cannot_be_written()
    {
        var files = new FakeFiles();
        files.CannotOpen.Add(Path);
        var log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
        {
            PGConfig.Load(Dir, files, log);
            AssertShippedValues();
        }
        Assert.Equal(new[] { "Proving Grounds: no pg_config.json and it could not be written; the shipped prices stay in force and there is no file to edit" }, log.Errors);
        Assert.StartsWith("Proving Grounds: pg_config.json absent and not writable, shipped prices used (", log.Debugs.Single());

        // an incomplete write (fwrite or fclose failed) is the same failure
        files = new FakeFiles();
        files.WriteIncomplete.Add(Path);
        log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
            PGConfig.Load(Dir, files, log);
        Assert.Single(log.Errors);
    }

    [Fact]   // PGConfig.cpp:845-846, 452-453
    public void Unreadable_file_is_left_alone()
    {
        var files = new FakeFiles();
        files.Unreadable.Add(Path);
        var log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
            PGConfig.Load(Dir, files, log);
        Assert.Equal(new[] { "Proving Grounds: pg_config.json exists but could not be read; it is left untouched and the shipped prices stay in force" }, log.Errors);
        Assert.StartsWith("Proving Grounds: pg_config.json could not be read, shipped prices used (" + Path + ")", log.Debugs.Single());
        Assert.Equal(new[] { "read " + Path }, files.Ops);

        // over 1 MiB is refused as unreadable; exactly 1 MiB is read
        byte[] big = new byte[1024 * 1024 + 1];
        Array.Fill(big, (byte)' ');
        big[0] = (byte)'{'; big[^1] = (byte)'}';
        files = new FakeFiles();
        files.Disk[Path] = big;
        log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
            PGConfig.Load(Dir, files, log);
        Assert.Contains("exists but could not be read", log.Errors.Single());
        byte[] exact = new byte[1024 * 1024];
        Array.Fill(exact, (byte)' ');
        exact[0] = (byte)'{'; exact[^1] = (byte)'}';
        LoadBytes(exact, l => Assert.Empty(l.Errors));
    }

    [Fact]   // PGConfig.cpp:826-834
    public void No_directory_and_second_load()
    {
        var files = new FakeFiles();
        var log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
        {
            PGConfig.Load("", files, log);
            Assert.Equal(new[] { "Proving Grounds: the plugin directory could not be resolved; pg_config.json cannot be read and the shipped prices stay in force" }, log.Errors);
            Assert.Empty(log.Debugs);
            Assert.Empty(files.Ops);
            // only the first call does anything
            PGConfig.Load(Dir, files, log);
            Assert.Empty(files.Ops);
            Assert.Single(log.Errors);
        }
        using (PGConfig.UseIsolatedForTest())
        {
            PGConfig.Load(null, files, log);
            Assert.Equal(2, log.Errors.Count);
        }
    }

    // ---- Load: parse failures ------------------------------------------------------------------------------------------------

    [Fact]   // PGConfig.cpp:351-362: the file is left untouched, everything shipped, one error
    public void Bad_json_keeps_the_shipped_values()
    {
        LoadText("{ \"marks_multiplier\": 2.0, \"recruitment\": { \"minimum\": 1 ", log =>
        {
            AssertShippedValues();
            string error = log.Errors.Single();
            Assert.StartsWith("Proving Grounds: pg_config.json is not valid JSON at offset ", error);
            Assert.EndsWith("; it is left untouched and the shipped prices stay in force", error);
            Assert.Contains(" read (", log.Debugs.Single());
            Assert.EndsWith("rejected=0", log.Debugs.Single());
        });
    }

    [Fact]   // rapidjson kParseErrorDocumentEmpty "The document is empty." (offset after leading whitespace/comments)
    public void Empty_file()
    {
        LoadText("", log => Assert.Equal("Proving Grounds: pg_config.json is not valid JSON at offset 0 (The document is empty.); it is left untouched and the shipped prices stay in force", log.Errors.Single()));
        LoadText("  // nothing\n ", log => Assert.Contains("at offset 14 (The document is empty.)", log.Errors.Single()));
        LoadBytes(new byte[] { 0xEF, 0xBB, 0xBF }, log => Assert.Contains("at offset 0 (The document is empty.)", log.Errors.Single()));
    }

    [Fact]   // PGConfig.cpp:363-368
    public void Root_must_be_an_object()
    {
        LoadText("[1, 2]", log =>
        {
            Assert.Equal(new[] { Ignored("$", "expected an object"), "Proving Grounds: pg_config.json was not applied in full; every rejected entry is named above" }, log.Errors);
            Assert.EndsWith("rejected=1", log.Debugs.Single());
        });
    }

    [Fact]   // PGConfig.cpp:371-380: version must be int 1; anything else takes nothing from the file
    public void Version_must_be_one()
    {
        foreach (string version in new[] { "2", "\"1\"", "1.0", "1e0", "0", "true" })
            LoadText("{ \"version\": " + version + ", \"marks_multiplier\": 2.0, \"bookie\": { \"maximum\": 20 } }", log =>
            {
                AssertShippedValues();
                Assert.Equal(Ignored("version", "expected 1; no prices were loaded"), log.Errors[0]);
                Assert.Equal(2, log.Errors.Count);
            });
        LoadText("{ \"version\": 1, \"marks_multiplier\": 2.0 }", log => { Assert.Equal(2.0, PGConfig.Current.Multiplier); Assert.Empty(log.Errors); });
        LoadText("{ \"marks_multiplier\": 2.0 }", log => Assert.Equal(2.0, PGConfig.Current.Multiplier));
        // duplicate keys: rapidjson's FindMember returns the FIRST member
        LoadText("{ \"version\": 1, \"version\": 2, \"marks_multiplier\": 2.0 }", log => Assert.Equal(2.0, PGConfig.Current.Multiplier));
        LoadText("{ \"version\": 2, \"version\": 1 }", log => Assert.Contains(Ignored("version", "expected 1; no prices were loaded"), log.Errors));
    }

    [Fact]   // rapidjson flags: comments (both kinds) and trailing commas accepted; BOM skipped once
    public void Comments_trailing_commas_and_bom()
    {
        const string json = "/* head */ {\n  // a line comment\n  \"marks_multiplier\": 2.0, /* inline */\n  \"bookie\": { \"maximum\": 500, },\n}\n// tail";
        LoadText(json, log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(2.0, PGConfig.Current.Multiplier);
            Assert.Equal(500, PGConfig.BookieValues().Maximum);
        });
        byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        LoadBytes(bom, log => { Assert.Empty(log.Errors); Assert.Equal(2.0, PGConfig.Current.Multiplier); });
        byte[] twoBoms = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bom).ToArray();
        LoadBytes(twoBoms, log => { Assert.Contains("is not valid JSON", log.Errors.Single()); AssertShippedValues(); });
        // a comma with nothing before it is not a trailing comma
        LoadText("{ , }", log => Assert.Contains("is not valid JSON", log.Errors.Single()));
    }

    [Fact]   // rapidjson without kParseNanAndInfFlag rejects NaN/Infinity; a number too big for a double is a parse error
    public void Nan_infinity_and_huge_numbers()
    {
        foreach (string bad in new[] { "NaN", "Infinity", "-Infinity", "1e400", "-1e999" })
            LoadText("{ \"marks_multiplier\": " + bad + " }", log =>
            {
                Assert.Contains("is not valid JSON", log.Errors.Single());
                AssertShippedValues();
            });
        // an underflow is just zero
        LoadText("{ \"marks_multiplier\": 1e-400 }", log => { Assert.Empty(log.Errors); Assert.Equal(0.0, PGConfig.Current.Multiplier); });
    }

    [Fact]   // kParseCommentsFlag: a comment may stand wherever whitespace may, including between a name and its ':'
    public void Comments_anywhere_whitespace_is()
    {
        LoadText("{ \"marks_multiplier\" /* x */ : 2.0, \"bookie\" // line\n : { \"maximum\": /* y */ 500 // z\n } }", log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(2.0, PGConfig.Current.Multiplier);
            Assert.Equal(500, PGConfig.BookieValues().Maximum);
        });
        // "//" and "/*" inside a string are text, not comments
        LoadText("{ \"supplies\": { \"supply.a//b\": 1, \"supply./*c*/\": 2 } }", log => Assert.Equal(new[]
        {
            Ignored("supplies.supply./*c*/", "does not name an entry in the shipped catalogue"),
            Ignored("supplies.supply.a//b", "does not name an entry in the shipped catalogue"),
            "Proving Grounds: pg_config.json was not applied in full; every rejected entry is named above",
        }, log.Errors));
        // rapidjson ends a line comment at '\n' only: in a CR-only file it swallows the rest of the document
        LoadText("{\r// note\r\"marks_multiplier\": 2.0\r}", log =>
        {
            Assert.Contains("is not valid JSON", log.Errors.Single());
            AssertShippedValues();
        });
        LoadText("{\r\n// note\r\n\"marks_multiplier\": 2.0\r\n}", log => Assert.Equal(2.0, PGConfig.Current.Multiplier));
        // unterminated block comment, and a '/' that starts no comment
        LoadText("{ \"marks_multiplier\": 2.0 /* never closed", log =>
        {
            Assert.Contains("is not valid JSON", log.Errors.Single());
            AssertShippedValues();
        });
        LoadText("{ \"marks_multiplier\": 2.0 / }", log => Assert.Contains("is not valid JSON", log.Errors.Single()));
        LoadText("{ \"marks_multiplier\": 2.0 }/*/", log => Assert.Contains("is not valid JSON", log.Errors.Single()));
    }

    [Fact]   // rapidjson kParseErrorStringUnicodeSurrogateInvalid; invalid UTF-8 (a recorded divergence: rapidjson accepts it)
    public void Bad_string_escapes_and_encoding()
    {
        LoadText("{ \"challenges\": { \"difficulty\": \"\\uD800\" } }", log => Assert.Contains("is not valid JSON", log.Errors.Single()));
        LoadBytes(new byte[] { (byte)'{', (byte)'"', 0xFF, (byte)'"', (byte)':', (byte)'1', (byte)'}' },
            log => Assert.Contains("is not valid JSON", log.Errors.Single()));
    }

    [Fact]   // Parse(text.c_str() + start): the document ends at the first NUL byte
    public void Nul_byte_ends_the_document()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{ \"marks_multiplier\": 2.0 }\0 this is not json {{{");
        LoadBytes(bytes, log => { Assert.Empty(log.Errors); Assert.Equal(2.0, PGConfig.Current.Multiplier); });
    }

    // ---- Load: sections and keys ---------------------------------------------------------------------------------------------

    [Fact]   // PGConfig.cpp:391-404
    public void Unknown_sections_and_non_object_sections()
    {
        LoadText("{ \"marks_multiplier\": 2.0, \"surprise\": 1, \"recruitment\": 5, \"ui_settings\": [] }", log =>
        {
            Assert.Equal(new[]
            {
                Ignored("surprise", "unknown section"),
                Ignored("recruitment", "expected an object"),
                Ignored("ui_settings", "expected an object"),
                "Proving Grounds: pg_config.json was not applied in full; every rejected entry is named above",
            }, log.Errors);
            Assert.Equal(2.0, PGConfig.Current.Multiplier);   // the rest still loads
            Assert.Equal(25, PGConfig.RecruitmentValues().Minimum);
            Assert.EndsWith("rejected=3", log.Debugs.Single());
        });
        // marks_multiplier is a number, not a section: no "expected an object" for it
        LoadText("{ \"marks_multiplier\": \"x\" }", log => Assert.Equal(Ignored("$.marks_multiplier", "expected a number"), log.Errors[0]));
    }

    [Fact]   // PGConfig.cpp:205-219 and ReadWhole/OptionalNumber ranges
    public void Recruitment_ranges()
    {
        LoadText("{ \"recruitment\": { \"minimum\": 0, \"maximum\": 1000000, \"step\": 1, \"combat_scale\": 100 } }", log =>
        {
            Assert.Empty(log.Errors);
            var r = PGConfig.RecruitmentValues();
            Assert.Equal((0, 1000000, 1, 100.0), (r.Minimum, r.Maximum, r.Step, r.CombatScale));
        });
        LoadText("{ \"recruitment\": { \"minimum\": -1, \"maximum\": 1000001, \"step\": 0, \"combat_scale\": 100.5 } }", log =>
        {
            Assert.Equal(new[]
            {
                Ignored("recruitment.minimum", "expected 0 to 1000000"),
                Ignored("recruitment.maximum", "expected 0 to 1000000"),
                Ignored("recruitment.step", "expected 1 to 1000000"),
                Ignored("recruitment.combat_scale", "expected 0 to 100"),
            }, log.Errors.Take(4));
            Assert.Equal(PGConfig.State.Shipped().Recruitment, PGConfig.RecruitmentValues());
        });
        LoadText("{ \"recruitment\": { \"combat_scale\": 0, \"step\": 1000000 } }", log =>
        {
            Assert.Equal(0.0, PGConfig.RecruitmentValues().CombatScale);
            Assert.Equal(1000000, PGConfig.RecruitmentValues().Step);
        });
        // minimum above maximum: the whole candidate is dropped, including the valid step
        LoadText("{ \"recruitment\": { \"minimum\": 300, \"step\": 7 } }", log =>
        {
            Assert.Equal(Ignored("recruitment", "minimum exceeds maximum; the shipped range is kept"), log.Errors[0]);
            Assert.Equal(5, PGConfig.RecruitmentValues().Step);
        });
    }

    [Fact]   // rapidjson IsInt / IsNumber: a literal with '.' or an exponent is a double; beyond int32 is not an int
    public void Whole_number_types()
    {
        foreach (string value in new[] { "30.0", "3e1", "3E1", "2147483648", "-2147483649", "99999999999999999999" })
            LoadText("{ \"recruitment\": { \"minimum\": " + value + " } }", log =>
                Assert.Equal(Ignored("recruitment.minimum", "expected a whole number"), log.Errors[0]));
        foreach (string value in new[] { "\"30\"", "true", "null", "[30]", "{}" })
            LoadText("{ \"recruitment\": { \"minimum\": " + value + " } }", log =>
                Assert.Equal(Ignored("recruitment.minimum", "expected a number"), log.Errors[0]));
        LoadText("{ \"recruitment\": { \"minimum\": -0 } }", log => { Assert.Empty(log.Errors); Assert.Equal(0, PGConfig.RecruitmentValues().Minimum); });
        LoadText("{ \"recruitment\": { \"minimum\": -2147483648 } }", log => Assert.Equal(Ignored("recruitment.minimum", "expected 0 to 1000000"), log.Errors[0]));
        // a whole number is a number for a double field
        LoadText("{ \"recruitment\": { \"combat_scale\": 3 } }", log => Assert.Equal(3.0, PGConfig.RecruitmentValues().CombatScale));
    }

    [Fact]   // PGConfig.cpp:221-237: rounded to whole steps of 10, minimum up, maximum down
    public void Bookie_rounding_and_ranges()
    {
        LoadText("{ \"bookie\": { \"minimum\": 15, \"maximum\": 9995 } }", log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(20, PGConfig.BookieValues().Minimum);
            Assert.Equal(9990, PGConfig.BookieValues().Maximum);
        });
        LoadText("{ \"bookie\": { \"minimum\": 0, \"maximum\": 1000000 } }", log =>
        {
            Assert.Equal(0, PGConfig.BookieValues().Minimum);
            Assert.Equal(1000000, PGConfig.BookieValues().Maximum);
            Assert.Equal(5000000, PGConfig.BookieCreditCeiling());
        });
        LoadText("{ \"bookie\": { \"minimum\": 11, \"maximum\": 19 } }", log =>
        {
            Assert.Equal(Ignored("bookie", "minimum exceeds maximum; the shipped range is kept"), log.Errors[0]);
            Assert.Equal(PGConfig.State.Shipped().Bookie, PGConfig.BookieValues());
        });
        LoadText("{ \"bookie\": { \"minimum\": 20, \"maximum\": 20 } }", log => Assert.Equal(20, PGConfig.BookieValues().Maximum));
        LoadText("{ \"bookie\": { \"minimum\": -10, \"maximum\": 1000010 } }", log =>
            Assert.Equal(new[] { Ignored("bookie.minimum", "expected 0 to 1000000"), Ignored("bookie.maximum", "expected 0 to 1000000") }, log.Errors.Take(2)));
    }

    [Fact]   // PGConfig.cpp:239-255: values outside 200..2500 are applied but warned about (not a rejection)
    public void Challenge_buy_in_and_its_warning()
    {
        LoadText("{ \"challenge_buy_in\": { \"easy\": 199, \"medium\": 200, \"hard\": 2501 } }", log =>
        {
            Assert.Equal(new[]
            {
                "Proving Grounds: pg_config.json challenge_buy_in.easy is outside 200 to 2500; quotes built from it may be rejected as invalid",
                "Proving Grounds: pg_config.json challenge_buy_in.hard is outside 200 to 2500; quotes built from it may be rejected as invalid",
            }, log.Errors);
            Assert.Equal(199, PGConfig.ChallengeBase(0));
            Assert.Equal(200, PGConfig.ChallengeBase(1));
            Assert.Equal(2501, PGConfig.ChallengeBase(2));
            Assert.EndsWith("rejected=0", log.Debugs.Single());
        });
        LoadText("{ \"challenge_buy_in\": { \"easy\": 0, \"hard\": 1000000, \"medium\": 1000001 } }", log =>
        {
            Assert.Equal(0, PGConfig.ChallengeBase(0));
            Assert.Equal(1000, PGConfig.ChallengeBase(1));
            Assert.Equal(1000000, PGConfig.ChallengeBase(2));
            Assert.Contains(Ignored("challenge_buy_in.medium", "expected 0 to 1000000"), log.Errors);
        });
        LoadText("{ \"challenge_buy_in\": { \"easy\": 2500 } }", log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(2500, PGConfig.ChallengeBase(-1));   // out-of-range divisions read division 0
            Assert.Equal(2500, PGConfig.ChallengeBase(3));
        });
    }

    [Fact]   // PGConfig.cpp:257-283
    public void Challenge_settings()
    {
        LoadText("{ \"challenges\": { \"difficulty\": \"hard\", \"refresh_hours\": 168, \"refresh_base_cost_cats\": 1000000, \"refresh_cost_multiplier\": 10, \"cooldown_hours\": 720, \"marks\": { \"win_multiplier\": 100, \"unique_bonus_multiplier\": 0, \"skarn_bonus_multiplier\": 0.25 } } }", log =>
        {
            Assert.Empty(log.Errors);
            var c = PGConfig.ChallengeValues();
            Assert.Equal(PGConfig.ChallengeDifficulty.Hard, c.Difficulty);
            Assert.Equal((168, 1000000, 10.0, 720), (c.RefreshHours, c.RefreshBaseCostCats, c.RefreshCostMultiplier, c.CooldownHours));
            Assert.Equal((100.0, 0.0, 0.25), (c.WinMarksMultiplier, c.UniqueBonusMultiplier, c.SkarnBonusMultiplier));
            Assert.Contains("challenge_difficulty=hard refresh_hours=168 refresh_base_cats=1000000 refresh_multiplier=10 cooldown_hours=720 marks=100/0/0.25;", log.Debugs.Single());
        });
        LoadText("{ \"challenges\": { \"difficulty\": \"Hard\", \"refresh_hours\": 0, \"refresh_base_cost_cats\": 0, \"refresh_cost_multiplier\": 0.99, \"cooldown_hours\": 721, \"marks\": { \"win_multiplier\": -1 } } }", log =>
        {
            Assert.Equal(new[]
            {
                Ignored("challenges.difficulty", "expected easy, normal or hard"),
                Ignored("challenges.refresh_hours", "expected 1 to 168"),
                Ignored("challenges.refresh_base_cost_cats", "expected 1 to 1000000"),
                Ignored("challenges.refresh_cost_multiplier", "expected 1 to 10"),
                Ignored("challenges.cooldown_hours", "expected 0 to 720"),
                Ignored("challenges.marks.win_multiplier", "expected 0 to 100"),
            }, log.Errors.Take(6));
            Assert.Equal(PGConfig.State.Shipped().Challenges, PGConfig.ChallengeValues());
        });
        LoadText("{ \"challenges\": { \"difficulty\": 1, \"marks\": 5, \"refresh_hours\": 1, \"cooldown_hours\": 0 } }", log =>
        {
            Assert.Equal(new[] { Ignored("challenges.difficulty", "expected easy, normal or hard"), Ignored("challenges.marks", "expected an object") }, log.Errors.Take(2));
            Assert.Equal(1, PGConfig.ChallengeValues().RefreshHours);   // the rest of the section still applies
            Assert.Equal(0, PGConfig.ChallengeValues().CooldownHours);
        });
        // strcmp on GetString(): the value is cut at an embedded NUL
        LoadText("{ \"challenges\": { \"difficulty\": \"easy\\u0000ish\" } }", log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(PGConfig.ChallengeDifficulty.Easy, PGConfig.ChallengeValues().Difficulty);
        });
    }

    [Fact]   // PGConfig.cpp:285-322, 429-430: ui_settings is read last, over the file's own sections
    public void Ui_settings_overlay()
    {
        LoadText("{ \"recruitment\": { \"minimum\": 30 }, \"bookie\": { \"maximum\": 500 }, \"challenge_buy_in\": { \"easy\": 300 }, \"challenges\": { \"cooldown_hours\": 1 }," +
            " \"ui_settings\": { \"recruitment\": { \"minimum\": 40 }, \"bookie\": { \"minimum\": 100 }, \"challenge_buy_in\": { \"medium\": 900 }, \"challenges\": { \"refresh_hours\": 6 }," +
            " \"performance_profile\": \"potato\", \"f8_opens_debug_menu\": true } }", log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(40, PGConfig.RecruitmentValues().Minimum);
            Assert.Equal(100, PGConfig.BookieValues().Minimum);
            Assert.Equal(500, PGConfig.BookieValues().Maximum);      // the file's value survives where the overlay is silent
            Assert.Equal(300, PGConfig.ChallengeBase(0));
            Assert.Equal(900, PGConfig.ChallengeBase(1));
            Assert.Equal(1, PGConfig.ChallengeValues().CooldownHours);
            Assert.Equal(6, PGConfig.ChallengeValues().RefreshHours);
            Assert.Equal(ArenaPerformance.Profile.Potato, PGConfig.PerformanceProfile());
            Assert.True(PGConfig.F8OpensDebugMenu());
            Assert.EndsWith("performance=potato f8_debug=enabled; rejected=0", log.Debugs.Single());
        });
        LoadText("{ \"ui_settings\": { \"recruitment\": 1, \"bookie\": \"x\", \"challenge_buy_in\": [], \"challenges\": null, \"performance_profile\": \"Potato\", \"f8_opens_debug_menu\": \"true\" } }", log =>
        {
            Assert.Equal(new[]
            {
                Ignored("ui_settings.recruitment", "expected an object"),
                Ignored("ui_settings.bookie", "expected an object"),
                Ignored("ui_settings.challenge_buy_in", "expected an object"),
                Ignored("ui_settings.challenges", "expected an object"),
                Ignored("ui_settings.performance_profile", "expected high, normal or potato"),
                Ignored("ui_settings.f8_opens_debug_menu", "expected true or false"),
            }, log.Errors.Take(6));
            AssertShippedValues();
        });
        LoadText("{ \"ui_settings\": { \"performance_profile\": 2 } }", log =>
            Assert.Equal(Ignored("ui_settings.performance_profile", "expected high, normal or potato"), log.Errors[0]));
        // the challenge_buy_in warning runs again for the overlay (the base is re-checked even when the key is absent)
        LoadText("{ \"challenge_buy_in\": { \"easy\": 100 }, \"ui_settings\": { \"challenge_buy_in\": { } } }", log =>
            Assert.Equal(2, log.Errors.Count(e => e.Contains("challenge_buy_in.easy is outside 200 to 2500"))));
    }

    // ---- Load: prices ------------------------------------------------------------------------------------------------------

    [Fact]   // PGConfig.cpp:931-943: the multiplier scales every catalogue price, rounded half up
    public void Marks_multiplier_rounds_half_up()
    {
        LoadText("{ \"marks_multiplier\": 1.5 }", log =>
        {
            Assert.Equal(38, HarnessStandard());   // 25 * 1.5 = 37.5 -> 38
            Assert.Equal(38, PGConfig.ScaleMarks(25));
            Assert.Equal(0, PGConfig.ScaleMarks(0));
            Assert.Contains("marks_multiplier=1.5;", log.Debugs.Single());
        });
        LoadText("{ \"marks_multiplier\": 0.5 }", log => Assert.Equal(13, HarnessStandard()));   // 12.5 -> 13
        LoadText("{ \"marks_multiplier\": 0 }", log => Assert.Equal(0, HarnessStandard()));
        LoadText("{ \"marks_multiplier\": 100 }", log => Assert.Equal(2500, HarnessStandard()));
        LoadText("{ \"marks_multiplier\": 100.1 }", log =>
        {
            Assert.Equal(Ignored("$.marks_multiplier", "expected 0 to 100"), log.Errors[0]);
            Assert.Equal(25, HarnessStandard());
        });
        LoadText("{ \"marks_multiplier\": -0.5 }", log => Assert.Equal(Ignored("$.marks_multiplier", "expected 0 to 100"), log.Errors[0]));
    }

    [Fact]   // PGConfig.cpp:151-203, 622-631, 891-929
    public void Catalogue_overrides_and_leftovers()
    {
        LoadText("{ \"marks_multiplier\": 2,\n" +
            " \"armour\": { \"armour.pitfighter.harness\": { \"standard\": 30, \"bogus\": 1 }, \"weapon.katana\": { \"high\": 1 }, \"armour.retainer.boots\": 7 },\n" +
            " \"weapons\": { \"weapon.katana\": { \"high\": 61, \"standard\": 5 } },\n" +
            " \"gear\": { \"armour.pitfighter.harness\": { \"standard\": 99, \"high\": 51 }, \"no.such\": { \"high\": 3 } },\n" +
            " \"supplies\": { \"supply.splint\": 9, \"supply.nothing\": 1, \"supply.skeleton_repair\": 10000001 },\n" +
            " \"licences\": { \"weapon.all\": 40, \"armour.warlord\": 2.5 } }", log =>
        {
            Assert.Equal(60, HarnessStandard());                                                               // 30 * 2
            Assert.Equal(102, ArenaRewards.Get(ArenaRewards.Piece.PitfighterHarness, ArenaRewards.Tier.HighGrade).Cost);  // gear 51 * 2
            Assert.Equal(122, ArenaRewards.Get(ArenaRewards.Piece.Katana, ArenaRewards.Tier.HighGrade).Cost);
            Assert.Equal(18, ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.SplintKit).Cost);
            Assert.Equal(20, ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.SkeletonRepair).Cost);         // shipped 10 * 2
            Assert.Equal(80, ArenaRewards.GetWeaponLicence().Cost);
            Assert.Equal(300, ArenaRewards.GetLicence(ArenaRewards.ArmourSet.T3).Cost);                       // shipped 150 * 2
            Assert.Equal(new[]
            {
                Ignored("armour.armour.pitfighter.harness.bogus", "unknown grade; use standard, high, specialist or masterwork"),
                Ignored("armour.weapon.katana", "expected an item ID beginning with armour."),
                Ignored("armour.armour.retainer.boots", "expected an object of grade prices"),
                Ignored("gear.armour.pitfighter.harness.standard", "duplicate entry"),
                Ignored("supplies.supply.skeleton_repair", "expected 0 to 10000000"),
                Ignored("licences.armour.warlord", "expected a whole number"),
                // leftovers, in map order: gear, then supplies, then licences
                Ignored("gear.no.such.high", "does not name an entry in the shipped catalogue"),
                Ignored("gear.weapon.katana.standard", "does not name an entry in the shipped catalogue"),
                Ignored("supplies.supply.nothing", "does not name an entry in the shipped catalogue"),
                "Proving Grounds: pg_config.json was not applied in full; every rejected entry is named above",
            }, log.Errors);
            Assert.Contains("applied catalogue=3 supplies=1 licences=1 marks_multiplier=2;", log.Debugs.Single());
            Assert.EndsWith("rejected=9", log.Debugs.Single());
        });
    }

    [Fact]   // duplicate keys in one object: every member is iterated (the second is a duplicate entry); scalars take the first
    public void Duplicate_keys()
    {
        LoadText("{ \"supplies\": { \"supply.splint\": 9, \"supply.splint\": 11 }, \"recruitment\": { \"minimum\": 1, \"minimum\": 2 }, \"recruitment\": { \"minimum\": 3 } }", log =>
        {
            Assert.Equal(9, ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.SplintKit).Cost);
            Assert.Equal(1, PGConfig.RecruitmentValues().Minimum);
            Assert.Equal(Ignored("supplies.supply.splint", "duplicate entry"), log.Errors[0]);
        });
        // a section name with an embedded NUL counts as known (std::string from GetString()) but is not that section (HasMember)
        LoadText("{ \"recruitment\\u0000x\": { \"minimum\": 3 } }", log =>
        {
            Assert.Empty(log.Errors);
            Assert.Equal(25, PGConfig.RecruitmentValues().Minimum);
        });
    }

    // ---- SaveEditableSettings ------------------------------------------------------------------------------------------------

    static FakeFiles FilesWith(string text)
    {
        var files = new FakeFiles();
        files.Disk[Path] = Encoding.UTF8.GetBytes(text);
        return files;
    }

    static (bool ok, string error, FakeLog log) Save(FakeFiles files, PGConfig.EditableSettings settings, string? directory = Dir)
    {
        var log = new FakeLog();
        bool ok = PGConfig.SaveEditableSettings(settings, out string error, directory, files, log);
        return (ok, error, log);
    }

    [Fact]   // PGConfig.cpp:991-1021: the range checks, in order
    public void Save_validates_the_settings()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            var files = FilesWith("{}");
            void Fails(Func<PGConfig.EditableSettings, PGConfig.EditableSettings> change, string message)
            {
                var (ok, error, _) = Save(files, change(PGConfig.ShippedEditableSettings()));
                Assert.False(ok);
                Assert.Equal(message, error);
            }
            const string recruitment = "Recruitment settings are outside their supported ranges.";
            Fails(s => { s.Recruitment.Minimum = -1; return s; }, recruitment);
            Fails(s => { s.Recruitment.Maximum = 1000001; return s; }, recruitment);
            Fails(s => { s.Recruitment.Minimum = 251; return s; }, recruitment);
            Fails(s => { s.Recruitment.Step = 0; return s; }, recruitment);
            Fails(s => { s.Recruitment.CombatScale = double.NaN; return s; }, recruitment);
            Fails(s => { s.Recruitment.CombatScale = 100.01; return s; }, recruitment);
            const string bookie = "Bookie limits must be ordered multiples of 10 Cats.";
            Fails(s => { s.Bookie.Minimum = 15; return s; }, bookie);
            Fails(s => { s.Bookie.Maximum = 10005; return s; }, bookie);
            Fails(s => { s.Bookie.Minimum = 20000; return s; }, bookie);
            Fails(s => { s.Bookie.Maximum = 1000010; return s; }, bookie);
            Fails(s => { s.Challenges.Difficulty = (PGConfig.ChallengeDifficulty)3; return s; }, "Challenge difficulty is invalid.");
            Fails(s => { s.PerformanceProfile = (ArenaPerformance.Profile)(-1); return s; }, "Crowd performance profile is invalid.");
            Fails(s => { s.ChallengeBase[2] = 1000001; return s; }, "Challenge buy-in is outside its supported range.");
            const string challenges = "Challenge settings are outside their supported ranges.";
            Fails(s => { s.Challenges.RefreshHours = 169; return s; }, challenges);
            Fails(s => { s.Challenges.RefreshBaseCostCats = 0; return s; }, challenges);
            Fails(s => { s.Challenges.RefreshCostMultiplier = 0.5; return s; }, challenges);
            Fails(s => { s.Challenges.CooldownHours = -1; return s; }, challenges);
            Fails(s => { s.Challenges.SkarnBonusMultiplier = double.PositiveInfinity; return s; }, challenges);
            // an earlier check wins
            Fails(s => { s.Recruitment.Step = 0; s.Bookie.Minimum = 5; return s; }, recruitment);
            Assert.Empty(files.Ops);
        }
    }

    [Fact]   // PGConfig.cpp:1023-1041
    public void Save_needs_a_readable_valid_file()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            var s = PGConfig.ShippedEditableSettings();
            Assert.Equal("The Proving Grounds mod folder could not be resolved.", Save(new FakeFiles(), s, "").error);
            Assert.Equal("pg_config.json could not be read; it was not changed.", Save(new FakeFiles(), s).error);
            var unreadable = FilesWith("{}");
            unreadable.Unreadable.Add(Path);
            Assert.Equal("pg_config.json could not be read; it was not changed.", Save(unreadable, s).error);
            Assert.Equal("pg_config.json is invalid; it was left untouched.", Save(FilesWith("{ broken"), s).error);
            Assert.Equal("pg_config.json is invalid; it was left untouched.", Save(FilesWith("[1]"), s).error);
            // two ui_settings members: the scanner refuses to choose
            Assert.Equal("pg_config.json could not be safely updated; it was left untouched.",
                Save(FilesWith("{ \"ui_settings\": {}, \"ui_settings\": {} }"), s).error);
            // a NUL ends the parse (c_str) but the overlay scan reads the whole text; it stops at the root's '}', so the bytes after stay
            var nul = FilesWith("{ \"a\": 1 }\0 junk");
            Assert.True(Save(nul, s).ok);
            Assert.EndsWith(" \"a\": 1 }\0 junk", nul.Text(Path));
        }
    }

    [Fact]   // PGConfig.cpp:1042-1078: an existing overlay is replaced in place; everything else is kept byte for byte
    public void Save_replaces_the_overlay()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            const string before = "{\n    // keep me\n    \"marks_multiplier\": 2.0,\n    \"ui_settings\": { \"performance_profile\": \"high\" /* old */ },\n    \"bookie\": { \"maximum\": 500 }\n}\n";
            var files = FilesWith(before);
            var s = PGConfig.ShippedEditableSettings();
            s.PerformanceProfile = ArenaPerformance.Profile.Potato;
            s.F8OpensDebugMenu = true;
            s.Recruitment.Minimum = 30;
            var (ok, error, log) = Save(files, s);
            Assert.True(ok);
            Assert.Equal("", error);
            string overlay = PGConfig.OverlayTextForTest(s);
            Assert.Equal("{\n    // keep me\n    \"marks_multiplier\": 2.0,\n    \"ui_settings\": " + overlay + ",\n    \"bookie\": { \"maximum\": 500 }\n}\n", files.Text(Path));
            Assert.Equal(new[] { "read " + Path, "write " + Path + ".tmp", "move " + Path + ".tmp " + Path }, files.Ops);
            Assert.Equal(new[] { "Proving Grounds: saved in-game settings overlay to pg_config.json; crowd performance and F8 apply immediately" }, log.Debugs);
            // crowd performance and F8 apply at once; gameplay values wait for the next start
            Assert.Equal(ArenaPerformance.Profile.Potato, PGConfig.PerformanceProfile());
            Assert.True(PGConfig.F8OpensDebugMenu());
            Assert.Equal(25, PGConfig.RecruitmentValues().Minimum);

            // the next start reads the overlay over the file's sections
            var load = new FakeLog();
            using (PGConfig.UseIsolatedForTest())
            {
                PGConfig.Load(Dir, files, load);
                Assert.Empty(load.Errors);
                Assert.Equal(30, PGConfig.RecruitmentValues().Minimum);
                Assert.Equal(10000, PGConfig.BookieValues().Maximum);   // the overlay's bookie (shipped) wins over the file's 500
                Assert.Equal(2.0, PGConfig.Current.Multiplier);
                Assert.Equal(ArenaPerformance.Profile.Potato, PGConfig.PerformanceProfile());
            }
        }
    }

    [Fact]   // PGConfig.cpp:1046-1052: no overlay yet -> inserted right after the root's '{'
    public void Save_inserts_the_overlay()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            var s = PGConfig.ShippedEditableSettings();
            string overlay = PGConfig.OverlayTextForTest(s);
            var files = FilesWith("{ \"marks_multiplier\": 1.0 }");
            Assert.True(Save(files, s).ok);
            Assert.Equal("{\n    \"ui_settings\": " + overlay + ",\n     \"marks_multiplier\": 1.0 }", files.Text(Path));
            files = FilesWith("  {}");
            Assert.True(Save(files, s).ok);
            Assert.Equal("  {\n    \"ui_settings\": " + overlay + "\n    }", files.Text(Path));
            // a BOM is kept
            var bom = new FakeFiles();
            bom.Disk[Path] = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' };
            Assert.True(Save(bom, s).ok);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'\n' }, bom.Disk[Path].Take(5));
        }
    }

    [Fact]   // the splice works on the original bytes: non-ASCII text and comments before ui_settings survive byte for byte
    public void Save_keeps_non_ascii_text_and_comments()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            const string head = "{\n    // café prices\n    \"supplies\" /* é */ : { \"supply.é\": 3 },\n    \"ui_settings\" // here\n : ";
            const string tail = ",\n    \"bookie\": { \"maximum\": 500 }\n}\n";
            var files = FilesWith(head + "{ \"f8_opens_debug_menu\": false }" + tail);
            var s = PGConfig.ShippedEditableSettings();
            s.F8OpensDebugMenu = true;
            var (ok, error, _) = Save(files, s);
            Assert.True(ok, error);
            byte[] expected = Encoding.UTF8.GetBytes(head + PGConfig.OverlayTextForTest(s) + tail);
            Assert.Equal(expected, files.Disk[Path]);
            Assert.True(PGConfig.F8OpensDebugMenu());
            var log = new FakeLog();
            using (PGConfig.UseIsolatedForTest())
            {
                PGConfig.Load(Dir, files, log);
                Assert.True(PGConfig.F8OpensDebugMenu());
                Assert.Equal(10000, PGConfig.BookieValues().Maximum);   // the overlay's (shipped) bookie wins over the file's 500
                Assert.Contains(Ignored("supplies.supply.é", "does not name an entry in the shipped catalogue"), log.Errors);
            }
        }
    }

    [Fact]   // PGConfig.cpp:1061-1073
    public void Save_write_failures()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            var s = PGConfig.ShippedEditableSettings();
            s.F8OpensDebugMenu = true;
            var files = FilesWith("{}");
            files.CannotOpen.Add(Path + ".tmp");
            var (ok, error, _) = Save(files, s);
            Assert.False(ok);
            Assert.Equal("Could not create a temporary config file.", error);
            Assert.DoesNotContain(files.Ops, o => o.StartsWith("delete"));

            files = FilesWith("{}");
            files.MoveFails = true;
            (ok, error, _) = Save(files, s);
            Assert.False(ok);
            Assert.Equal("Could not replace pg_config.json; the previous file was kept.", error);
            Assert.Equal("delete " + Path + ".tmp", files.Ops[^1]);
            Assert.Equal("{}", files.Text(Path));

            files = FilesWith("{}");
            files.WriteIncomplete.Add(Path + ".tmp");
            (ok, error, _) = Save(files, s);
            Assert.Equal("Could not replace pg_config.json; the previous file was kept.", error);
            Assert.DoesNotContain(files.Ops, o => o.StartsWith("move"));
            Assert.False(PGConfig.F8OpensDebugMenu());   // nothing applied on failure
        }
    }

    // ---- accessors -----------------------------------------------------------------------------------------------------

    [Fact]   // PGConfig.cpp:1093-1109
    public void Clamp_stake_and_credit_ceiling()
    {
        var state = PGConfig.State.Shipped();
        using (PGConfig.UseForTest(state))
        {
            Assert.Equal(10, PGConfig.ClampStake(-5));
            Assert.Equal(10, PGConfig.ClampStake(10));
            Assert.Equal(15, PGConfig.ClampStake(15));   // not snapped to a step: the bounds are already steps
            Assert.Equal(10000, PGConfig.ClampStake(10001));
            Assert.Equal(50000, PGConfig.BookieCreditCeiling());   // 10000 * 5 = 50000, not above the shipped bound
            state.Bookie.Maximum = 10010;
            Assert.Equal(50050, PGConfig.BookieCreditCeiling());
            state.Bookie.Maximum = 0;
            Assert.Equal(50000, PGConfig.BookieCreditCeiling());
        }
    }

    [Fact]   // PGConfig.cpp:977-987
    public void Get_editable_settings_reads_the_globals()
    {
        var state = PGConfig.State.Shipped();
        state.ChallengeBase[2] = 42;
        state.PerformanceProfile = ArenaPerformance.Profile.High;
        state.F8OpensDebugMenu = true;
        state.Bookie.Minimum = 30;
        using (PGConfig.UseForTest(state))
        {
            var s = PGConfig.GetEditableSettings();
            Assert.Equal(42, s.ChallengeBase[2]);
            Assert.Equal(ArenaPerformance.Profile.High, s.PerformanceProfile);
            Assert.True(s.F8OpensDebugMenu);
            Assert.Equal(30, s.Bookie.Minimum);
            s.ChallengeBase[2] = 1;
            Assert.Equal(42, PGConfig.ChallengeBase(2));
        }
    }
}
