using System.Text;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using ProvingGrounds.Tests.Core;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task M2.6: the Meitou settings page as the original's ui_settings overlay (PGConfig.cpp:285-322 via Core's own reader),
/// the Save button's checks (:991-1024), the at-once values (:1069-1070), and the probe JSON.</summary>
public class PgSettingsPageTests
{
    const string Dir = @"C:\Mods\PG\";
    const string Path = Dir + "pg_config.json";

    [ThreadStatic] static PgSettingsValues? LastShown;   // what the page shows after ApplyPage

    static PgSettingsValues Shipped => PgSettingsPage.From(PGConfig.ShippedEditableSettings(), "Off");

    // Load `json` (or no file), then the overlay of `chosen` over the file's values; `check` runs inside the isolation.
    static FakeLog LoadWithPage(string? json, Func<PgSettingsValues, PgSettingsValues> chosen, Action<FakeLog, PgSettingsValues> check,
        bool unreadable = false)
    {
        var files = new FakeFiles();
        if (json != null) files.Disk[Path] = Encoding.UTF8.GetBytes(json);
        if (unreadable) files.Unreadable.Add(Path);
        var log = new FakeLog();
        using (PGConfig.UseIsolatedForTest())
        {
            // the mod's own start-up order (src/Ui/PGSettingsUI.cs Start): load, Meitou sets the saved values, apply
            PgSettingsValues file = PgSettingsStartup.LoadFile(Dir, files, log);
            PgSettingsValues page = chosen(file);
            LastShown = PgSettingsStartup.ApplyPage(file, page, Dir, log);
            check(log, file);
        }
        return log;
    }

    [Fact]
    public void ShippedValuesMapToTheOriginalRowTexts()
    {
        PgSettingsValues v = Shipped;
        Assert.Equal((25, 250, 5, 2.5), (v.RecruitmentMinimum, v.RecruitmentMaximum, v.RecruitmentStep, v.RecruitmentCombatScale));
        Assert.Equal((10, 10000), (v.BookieMinimum, v.BookieMaximum));
        Assert.Equal((500, 1000, 2500), (v.ChallengeBuyInEasy, v.ChallengeBuyInMedium, v.ChallengeBuyInHard));
        Assert.Equal("Normal", v.ChallengeDifficulty);
        Assert.Equal((12, 500, 2.0, 24), (v.RefreshHours, v.RefreshBaseCostCats, v.RefreshCostMultiplier, v.CooldownHours));
        Assert.Equal((1.0, 1.0, 1.0), (v.WinMarksMultiplier, v.UniqueBonusMultiplier, v.SkarnBonusMultiplier));
        Assert.Equal("Normal", v.PerformanceProfile);
        Assert.Equal("Off", v.DebugMenuKey);
    }

    [Fact]
    public void ToEditableRoundTripsEveryValue()
    {
        var v = new PgSettingsValues(1, 2, 3, 4.25, 20, 30, 40, 50, 60, "Hard", 7, 8, 9.5, 10, 11.5, 12.25, 13.75, "Potato", "N");
        PGConfig.EditableSettings s = PgSettingsPage.ToEditable(v);
        Assert.Equal(v, PgSettingsPage.From(s, "N"));
        Assert.Equal(PGConfig.ChallengeDifficulty.Hard, s.Challenges.Difficulty);
        Assert.Equal(ArenaPerformance.Profile.Potato, s.PerformanceProfile);
        Assert.True(s.F8OpensDebugMenu);
        Assert.Equal("Easy", PgSettingsPage.From(PgSettingsPage.ToEditable(v with { ChallengeDifficulty = "Easy" }), "Off").ChallengeDifficulty);
        Assert.Equal("Normal", PgSettingsPage.From(PgSettingsPage.ToEditable(v with { ChallengeDifficulty = "bogus" }), "Off").ChallengeDifficulty);
    }

    [Theory]
    [InlineData("Off", false)]
    [InlineData("O", true)]
    [InlineData("N", true)]
    [InlineData("V", true)]
    [InlineData("F8", false)]   // not an option: Off
    public void DebugKeyDecidesF8(string key, bool f8)
    {
        Assert.Equal(f8, PgSettingsPage.Live(Shipped with { DebugMenuKey = key }).F8OpensDebugMenu);
    }

    [Fact]
    public void FileF8StandsForTheFirstKey()
    {
        Assert.Equal("O", PgSettingsPage.DebugKeyFor(true));
        Assert.Equal("Off", PgSettingsPage.DebugKeyFor(false));
    }

    [Fact]
    public void OnlyPerformanceAndKeyApplyAtOnce()
    {
        string[] keys = typeof(PgSettingsValues).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").ToArray();
        Assert.Equal(19, keys.Length);
        Assert.Equal(new[] { "PerformanceProfile", "DebugMenuKey" }, keys.Where(PgSettingsPage.AppliesAtOnce));
    }

    [Fact]
    public void UnchangedPageWritesNoOverlay()
    {
        Assert.Null(PgSettingsPage.Overlay(Shipped, Shipped));
        // a different key with the same enablement is not an overlay value (the key is the port's, published at once)
        Assert.Null(PgSettingsPage.Overlay(Shipped with { DebugMenuKey = "O" }, Shipped with { DebugMenuKey = "V" }));
    }

    [Fact]
    public void OverlayHoldsOnlyTheChangedKeysInTheOriginalNames()
    {
        string? overlay = PgSettingsPage.Overlay(Shipped, Shipped with { RecruitmentMaximum = 300, WinMarksMultiplier = 2, ChallengeDifficulty = "Hard", PerformanceProfile = "Potato", DebugMenuKey = "N" });
        Assert.Equal("{\n        \"recruitment\": {\"maximum\": 300},\n        \"challenges\": {\"difficulty\": \"hard\", \"marks\": {\"win_multiplier\": 2.0}},\n"
            + "        \"performance_profile\": \"potato\",\n        \"f8_opens_debug_menu\": true\n    }", overlay);
    }

    [Fact]
    public void NoFileThePageAppliesOverShippedValues()
    {
        LoadWithPage(null, f => f with { RecruitmentMinimum = 40, BookieMaximum = 5000, ChallengeBuyInHard = 2000, RefreshCostMultiplier = 3.5,
            ChallengeDifficulty = "Easy", PerformanceProfile = "High", DebugMenuKey = "V" }, (log, _) =>
        {
            PGConfig.EditableSettings e = PGConfig.GetEditableSettings();
            Assert.Equal(40, e.Recruitment.Minimum);
            Assert.Equal(250, e.Recruitment.Maximum);
            Assert.Equal(5000, e.Bookie.Maximum);
            Assert.Equal(2000, e.ChallengeBase[2]);
            Assert.Equal(3.5, e.Challenges.RefreshCostMultiplier);
            Assert.Equal(PGConfig.ChallengeDifficulty.Easy, e.Challenges.Difficulty);
            Assert.Equal(ArenaPerformance.Profile.High, e.PerformanceProfile);
            Assert.True(e.F8OpensDebugMenu);
            Assert.Empty(log.Errors);
            Assert.StartsWith("Proving Grounds: pg_config.json with the settings page values applied (" + Path + "); ", log.Debugs[^1]);
        });
    }

    [Fact]
    public void AnUnchangedPageValueLeavesTheFileValue()
    {
        const string file = "{\"recruitment\": {\"minimum\": 30, \"maximum\": 400}, \"ui_settings\": {\"bookie\": {\"maximum\": 2000}}}";
        LoadWithPage(file, f => f with { RecruitmentMaximum = 500 }, (log, defaults) =>
        {
            Assert.Equal(30, defaults.RecruitmentMinimum);   // the page's default is the file's value
            Assert.Equal(2000, defaults.BookieMaximum);      // ui_settings in the file still reads
            PGConfig.EditableSettings e = PGConfig.GetEditableSettings();
            Assert.Equal((30, 500), (e.Recruitment.Minimum, e.Recruitment.Maximum));
            Assert.Equal(2000, e.Bookie.Maximum);
        });
    }

    [Fact]
    public void MinimumAboveMaximumIsRejectedAsInAUiSettingsBlock()
    {
        LoadWithPage(null, f => f with { RecruitmentMinimum = 300 }, (log, _) =>
        {
            PGConfig.EditableSettings e = PGConfig.GetEditableSettings();
            Assert.Equal((25, 250), (e.Recruitment.Minimum, e.Recruitment.Maximum));
            Assert.Contains("Proving Grounds: pg_config.json recruitment ignored (minimum exceeds maximum; the shipped range is kept); the shipped value stays in force", log.Errors);
            Assert.Equal("Proving Grounds: the settings page values were not applied in full; every rejected entry is named above", log.Errors[^1]);
            Assert.Equal(1, PGConfig.Report().Rejected);
        });
    }

    [Fact]
    public void BookieBoundsAreNormalisedLikeTheFile()
    {
        // Meitou's slider steps by 10 already; a hand-set value still goes through ReadBookie's whole-tens rule (PGConfig.cpp:228-229).
        LoadWithPage(null, f => f with { BookieMinimum = 15, BookieMaximum = 995 }, (log, _) =>
        {
            PGConfig.EditableSettings e = PGConfig.GetEditableSettings();
            Assert.Equal((20, 990), (e.Bookie.Minimum, e.Bookie.Maximum));
        });
    }

    [Fact]
    public void BuyInOutsideTheQuoteBandWarnsAsTheFileDoes()
    {
        LoadWithPage(null, f => f with { ChallengeBuyInEasy = 100 }, (log, _) =>
        {
            Assert.Equal(100, PGConfig.ChallengeBase(0));
            Assert.Contains("Proving Grounds: pg_config.json challenge_buy_in.easy is outside 200 to 2500; quotes built from it may be rejected as invalid", log.Errors);
        });
    }

    [Theory]
    [InlineData(300, 250, 10, 10000, "Recruitment settings are outside their supported ranges.")]
    [InlineData(25, 250, 20, 10, "Bookie limits must be ordered multiples of 10 Cats.")]
    [InlineData(25, 250, 15, 10000, "Bookie limits must be ordered multiples of 10 Cats.")]
    [InlineData(25, 250, 10, 10000, "")]
    public void ValidateUsesTheSaveButtonsMessages(int rMin, int rMax, int bMin, int bMax, string message)
    {
        bool ok = PgSettingsPage.Validate(Shipped with { RecruitmentMinimum = rMin, RecruitmentMaximum = rMax, BookieMinimum = bMin, BookieMaximum = bMax }, out string error);
        Assert.Equal(message.Length == 0, ok);
        Assert.Equal(message, error);
    }

    [Fact]
    public void LiveSettingsApplyOnlyThePresetAndF8()
    {
        using (PGConfig.UseIsolatedForTest())
        {
            PGConfig.ApplyLiveSettings(ArenaPerformance.Profile.Potato, true);
            Assert.Equal(ArenaPerformance.Profile.Potato, PGConfig.PerformanceProfile());
            Assert.True(PGConfig.F8OpensDebugMenu());
            Assert.Equal(250, PGConfig.RecruitmentValues().Maximum);
        }
    }

    [Fact]
    public void ProbeJsonIsOneValidLineWithEveryValue()
    {
        LoadWithPage(null, f => f with { RecruitmentCombatScale = 3.1, DebugMenuKey = "N" }, (log, _) =>
        {
            string json = PgSettingsPage.ProbeJson(PGConfig.GetEditableSettings(), PGConfig.Report(), "N", Path);
            Assert.DoesNotContain('\n', json);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            Assert.Equal("probe-config", r.GetProperty("verb").GetString());
            Assert.Equal(Path, r.GetProperty("path").GetString());
            Assert.True(r.GetProperty("loaded").GetBoolean());
            Assert.Equal(3.1, r.GetProperty("recruitment").GetProperty("combat_scale").GetDouble());
            Assert.Equal(25, r.GetProperty("recruitment").GetProperty("minimum").GetInt32());
            Assert.Equal(10000, r.GetProperty("bookie").GetProperty("maximum").GetInt32());
            Assert.Equal(2500, r.GetProperty("challenge_buy_in").GetProperty("hard").GetInt32());
            Assert.Equal("normal", r.GetProperty("challenges").GetProperty("difficulty").GetString());
            Assert.Equal(1.0, r.GetProperty("challenges").GetProperty("marks").GetProperty("skarn_bonus_multiplier").GetDouble());
            Assert.Equal("normal", r.GetProperty("performance_profile").GetString());
            Assert.True(r.GetProperty("f8_opens_debug_menu").GetBoolean());
            Assert.Equal("N", r.GetProperty("debug_menu_key").GetString());
            Assert.Equal(1.0, r.GetProperty("marks_multiplier").GetDouble());
            Assert.Contains("\"combat_scale\":3.1}", json);   // %g, as the Load summary
        });
    }

    [Theory]
    [InlineData("High", ArenaPerformance.Profile.High)]
    [InlineData("Normal", ArenaPerformance.Profile.Normal)]
    [InlineData("Potato", ArenaPerformance.Profile.Potato)]
    [InlineData("potato", ArenaPerformance.Profile.Normal)]   // not an option (Meitou ignores such a saved value): the shipped profile
    public void CrowdPerformanceShowsTheOriginalCaptions(string option, ArenaPerformance.Profile profile)
    {
        Assert.Equal(new[] { "High", "Normal", "Potato" }, PgSettingsPage.PerformanceOptions);
        Assert.Equal(profile, PgSettingsPage.Live(Shipped with { PerformanceProfile = option }).PerformanceProfile);
        Assert.Equal(ArenaPerformance.Name(profile), PgSettingsPage.From(PgSettingsPage.ToEditable(Shipped with { PerformanceProfile = option }), "Off").PerformanceProfile);
    }

    const string NotRead = "Proving Grounds: pg_config.json was not read, so the settings page values are not applied either; the shipped values stay in force";

    [Fact]
    public void AnUnreadableFileTakesNoPageValues()
    {
        // C++ PGConfig.cpp:841-847: an unreadable file is never parsed, so no ui_settings: only the shipped values.
        LoadWithPage("{}", f => f with { RecruitmentMinimum = 40, PerformanceProfile = "Potato" }, (log, defaults) =>
        {
            Assert.Equal(Shipped, defaults);
            Assert.False(PGConfig.SettingsPageOverlayAllowed());
            PGConfig.EditableSettings e = PGConfig.GetEditableSettings();
            Assert.Equal(25, e.Recruitment.Minimum);
            Assert.Equal(ArenaPerformance.Profile.Normal, e.PerformanceProfile);
            Assert.Equal(NotRead, log.Errors[^1]);   // the refusal is logged (the player's values reached the reader)
            Assert.Equal(Shipped, LastShown);         // and the page shows the values in force
        }, unreadable: true);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"version\": 2, \"ui_settings\": {\"performance_profile\": \"potato\"}}")]
    public void AFileParseRejectsTakesNoPageValues(string json)
    {
        LoadWithPage(json, f => f with { BookieMaximum = 5000 }, (log, _) =>
        {
            Assert.False(PGConfig.SettingsPageOverlayAllowed());
            Assert.Equal(10000, PGConfig.BookieValues().Maximum);
            Assert.Equal(NotRead, log.Errors[^1]);
        });
    }

    [Fact]
    public void AMissingFileStillTakesThePageValues()
    {
        LoadWithPage(null, f => f with { BookieMaximum = 5000 }, (log, _) =>
        {
            Assert.True(PGConfig.SettingsPageOverlayAllowed());
            Assert.Equal(ConfigReadStatus.Missing, PGConfig.Report().FileStatus);
            Assert.Equal(5000, PGConfig.BookieValues().Maximum);
        });
    }

    [Fact]
    public void AnUnreadableFileWithAnUnchangedPageLogsNothingExtra()
    {
        LoadWithPage("{}", f => f, (log, _) => Assert.DoesNotContain(NotRead, log.Errors), unreadable: true);
    }

    [Fact]
    public void ApplyPagePutsTheShownLiveValuesInForce()
    {
        LoadWithPage(null, f => f with { PerformanceProfile = "Potato", DebugMenuKey = "V" }, (log, _) =>
        {
            Assert.Equal(ArenaPerformance.Profile.Potato, PGConfig.PerformanceProfile());
            Assert.True(PGConfig.F8OpensDebugMenu());
            Assert.Equal("Potato", LastShown!.PerformanceProfile);
        });
    }
}
