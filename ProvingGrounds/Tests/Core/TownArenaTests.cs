using ProvingGrounds.Core;
using TA = ProvingGrounds.Core.TownArena;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownArena*.cs: the number sites of TownArena.cpp as the shipped DLL prints them (msvcr100, task 1.14).
/// Every golden test runs in glibc mode (g++/glibc printed the goldens), so these are the only checks of the original's own text. Each
/// expected string was measured on the real msvcr100.dll with <c>_crtprobe/out/CrtProbe.exe fmt</c> (2026-11-12); the glibc strings with
/// WSL's printf. A tie value is used wherever one is reachable, and the same call under <see cref="CMath.UseGlibcForTest"/> must differ,
/// which shows the test really checks msvcr100.</summary>
public class TownArenaTests
{
    static string Glibc(Func<string> build)
    {
        using var glibc = CMath.UseGlibcForTest();
        return build();
    }

    // CrtProbe: %.3f 0.0625 -> 0.063, 0.3125 -> 0.313 (glibc 0.062, 0.312)
    [Fact]
    public void Ready_detail_line_prints_health_and_blood_with_msvcr100_ties()
    {
        var c = new TA.Actor
        {
            RosterOk = true, RosterError = "",
            Health = new RosterStatus.Snapshot { LowestLimb = 0.0625f, Blood = 0.3125f },
        };
        string? Detail()
        {
            TA.Ready(c, false, false, true, out _, out string? detail);
            return detail;
        }
        string expected = "Town candidate unknown: ready=0 health=0.063 blood=0.313 player=0 dead=0 ko=0 combat=0 aid=0";
        Assert.Equal(expected, Detail());
        string glibc = Glibc(() => Detail()!);
        Assert.Equal("Town candidate unknown: ready=0 health=0.062 blood=0.312 player=0 dead=0 ko=0 combat=0 aid=0", glibc);
        Assert.NotEqual(expected, glibc);
    }

    // CrtProbe: %.2f 0.75 -> 0.75, 1 -> 1.00, 1.25 -> 1.25 (glibc the same)
    [Fact]
    public void Direct_caption_marks_multiplier_has_no_reachable_tie()
    {
        // ArenaMarks::DivisionMultiplier is 0.75f / 1.0f / 1.25f: all exact at %.2f, so this site prints the same text under both CRTs.
        var s = new TA.DisplayState { DirectPreview = new TownChallengePolicy.Offer { Opponent = "beep", Name = "Beep" } };
        Assert.Equal("Book Easy 1v1 vs Beep | Marks: 0.75x", TA.DirectCaption(s, 0));
        Assert.Equal("Book Medium 1v1 vs Beep | Marks: 1.00x", TA.DirectCaption(s, 1));
        Assert.Equal("Book Hard 1v1 vs Beep | Marks: 1.25x", TA.DirectCaption(s, 2));
        for (int division = 0; division < 3; ++division)
            Assert.Equal(TA.DirectCaption(s, division), Glibc(() => TA.DirectCaption(s, division)));
    }

    // CrtProbe: %.1f 0.25 -> 0.3, -0.25 -> -0.3 (glibc 0.2, -0.2)
    [Fact]
    public void Skarn_cooldown_rounds_a_tie_away_from_zero()
    {
        string expected = "Skarn cooldown: 0.3 in-game hours remaining";
        Assert.Equal(expected, TA.SkarnCooldownMessage(24, 100.0, 76.25));       // 24 - 23.75 = 0.25
        string glibc = Glibc(() => TA.SkarnCooldownMessage(24, 100.0, 76.25));
        Assert.Equal("Skarn cooldown: 0.2 in-game hours remaining", glibc);
        Assert.NotEqual(expected, glibc);
        Assert.Equal("Skarn cooldown: -0.3 in-game hours remaining", TA.SkarnCooldownMessage(24, 100.0, 75.75));   // -0.25
        Assert.Equal("Skarn cooldown: -0.2 in-game hours remaining", Glibc(() => TA.SkarnCooldownMessage(24, 100.0, 75.75)));
    }

    // CrtProbe: %.2f 1.125 -> 1.13, 0.625 -> 0.63 (glibc 1.12, 0.62)
    [Fact]
    public void Ambient_matchup_log_rounds_ties_away_from_zero()
    {
        string expected = "Proving Grounds: ambient matchup 2v1 scores 1.13 / 0.63";
        Assert.Equal(expected, TA.AmbientMatchupLog(2, 1, 1.125, 0.625));
        string glibc = Glibc(() => TA.AmbientMatchupLog(2, 1, 1.125, 0.625));
        Assert.Equal("Proving Grounds: ambient matchup 2v1 scores 1.12 / 0.62", glibc);
        Assert.NotEqual(expected, glibc);
    }
}
