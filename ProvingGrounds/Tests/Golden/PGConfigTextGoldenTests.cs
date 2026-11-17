using System.Text.Json;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Golden;

// Tests/Golden/pgconfig_text.json, made by golden/gen_pgconfig_text.cpp: verbatim copies of PGConfig.cpp's text builders compiled
// with g++ and linked with the real ArenaRewards.cpp. Every text is compared byte for byte.
public class PGConfigTextGoldenTests
{
    static IEnumerable<JsonElement> Cases(string kind)
    {
        foreach (JsonElement c in GoldenFile.Load("pgconfig_text").EnumerateArray())
            if (c.GetProperty("k").GetString() == kind)
                yield return c;
    }

    static double X(JsonElement c)
    {
        double x = c.GetProperty("x").GetDouble();
        return c.GetProperty("neg").GetBoolean() && x == 0.0 ? -0.0 : x;
    }

    static PGConfig.EditableSettings Settings(JsonElement c)
    {
        PGConfig.EditableSettings s = default;
        s.Recruitment = new PGConfig.Recruitment
        {
            Minimum = c.GetProperty("rmin").GetInt32(), Maximum = c.GetProperty("rmax").GetInt32(),
            Step = c.GetProperty("rstep").GetInt32(), CombatScale = c.GetProperty("rscale").GetDouble(),
        };
        s.Bookie = new PGConfig.Bookie { Minimum = c.GetProperty("bmin").GetInt32(), Maximum = c.GetProperty("bmax").GetInt32() };
        s.Challenges = new PGConfig.Challenges
        {
            Difficulty = (PGConfig.ChallengeDifficulty)c.GetProperty("diff").GetInt32(),
            RefreshHours = c.GetProperty("rh").GetInt32(), RefreshBaseCostCats = c.GetProperty("rbc").GetInt32(),
            RefreshCostMultiplier = c.GetProperty("rcm").GetDouble(), CooldownHours = c.GetProperty("cd").GetInt32(),
            WinMarksMultiplier = c.GetProperty("win").GetDouble(), UniqueBonusMultiplier = c.GetProperty("uniq").GetDouble(),
            SkarnBonusMultiplier = c.GetProperty("skarn").GetDouble(),
        };
        for (int i = 0; i < 3; ++i) s.ChallengeBase[i] = c.GetProperty("base")[i].GetInt32();
        s.PerformanceProfile = (ArenaPerformance.Profile)c.GetProperty("perf").GetInt32();
        s.F8OpensDebugMenu = c.GetProperty("f8").GetBoolean();
        return s;
    }

    [Fact]
    public void FormatNumber_matches_printf_g()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int n = 0;
        foreach (JsonElement c in Cases("g"))
        {
            Assert.Equal(c.GetProperty("text").GetString(), PGConfig.FormatNumberForTest(X(c)));
            ++n;
        }
        Assert.True(n > 200);
    }

    [Fact]
    public void Default_file_text()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int n = 0;
        foreach (JsonElement c in Cases("defaults"))
        {
            var state = PGConfig.State.Shipped();
            state.Multiplier = c.GetProperty("m").GetDouble();
            state.Recruitment = new PGConfig.Recruitment
            {
                Minimum = c.GetProperty("rmin").GetInt32(), Maximum = c.GetProperty("rmax").GetInt32(),
                Step = c.GetProperty("rstep").GetInt32(), CombatScale = c.GetProperty("rscale").GetDouble(),
            };
            state.Bookie = new PGConfig.Bookie { Minimum = c.GetProperty("bmin").GetInt32(), Maximum = c.GetProperty("bmax").GetInt32() };
            for (int i = 0; i < 3; ++i) state.ChallengeBase[i] = c.GetProperty("base")[i].GetInt32();
            // the catalogue the default file lists is the shipped, unstamped one (WriteDefaults runs before ApplyPriceOverrides)
            using (PGConfig.UseIsolatedForTest())
                Assert.Equal(c.GetProperty("text").GetString(), PGConfig.DefaultsTextForTest(state));
            ++n;
        }
        Assert.Equal(3, n);
    }

    [Fact]
    public void Overlay_text()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int n = 0;
        foreach (JsonElement c in Cases("overlay"))
        {
            Assert.Equal(c.GetProperty("text").GetString(), PGConfig.OverlayTextForTest(Settings(c)));
            ++n;
        }
        Assert.Equal(41, n);
    }

    [Fact]
    public void Load_summary_text()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int n = 0;
        foreach (JsonElement c in Cases("summary"))
        {
            PGConfig.EditableSettings s = Settings(c);
            var state = PGConfig.State.Shipped();
            state.Multiplier = c.GetProperty("m").GetDouble();
            state.Recruitment = s.Recruitment;
            state.Bookie = s.Bookie;
            state.Challenges = s.Challenges;
            for (int i = 0; i < 3; ++i) state.ChallengeBase[i] = s.ChallengeBase[i];
            state.PerformanceProfile = s.PerformanceProfile;
            state.F8OpensDebugMenu = s.F8OpensDebugMenu;
            state.AppliedGear = c.GetProperty("ag").GetInt32();
            state.AppliedSupplies = c.GetProperty("as").GetInt32();
            state.AppliedLicences = c.GetProperty("al").GetInt32();
            state.Rejected = c.GetProperty("rej").GetInt32();
            Assert.Equal(c.GetProperty("text").GetString(),
                PGConfig.SummaryForTest(state, c.GetProperty("origin").GetString()!, c.GetProperty("path").GetString()!));
            ++n;
        }
        Assert.Equal(12, n);
    }

    [Fact]
    public void Range_texts()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int n = 0;
        foreach (JsonElement c in Cases("range"))
        {
            string actual = c.GetProperty("whole").GetBoolean()
                ? PGConfig.RangeWholeForTest(c.GetProperty("min").GetInt32(), c.GetProperty("max").GetInt32())
                : PGConfig.RangeNumberForTest(c.GetProperty("min").GetDouble(), c.GetProperty("max").GetDouble());
            Assert.Equal(c.GetProperty("text").GetString(), actual);
            ++n;
        }
        Assert.Equal(10, n);
    }

    [Fact]
    public void Quote_text()
    {
        foreach (JsonElement c in Cases("quote"))
            Assert.Equal(c.GetProperty("text").GetString(), PGConfig.QuoteForTest(c.GetProperty("s").GetString()!));
    }
}
