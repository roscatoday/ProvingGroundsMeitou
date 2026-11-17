using ProvingGrounds.Core;
using ProvingGrounds.Tests.Fidelity;
using static ProvingGrounds.Core.TownAnnouncerPolicy;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand tests for Core/TownAnnouncerPolicy.cs beside announcer.json: the script tables against the upstream header's own
/// string literals (so the lines no cue ever reaches are pinned byte for byte too), and the struct defaults.</summary>
public class TownAnnouncerPolicyTests
{
    [Fact]
    public void Script_tables_equal_the_upstream_literals()
    {
        Assert.Equal(UpstreamLiterals.Read("TownAnnouncerPolicy.h", 238, 263), HouseScripts.SelectMany(x => x).ToArray());
        Assert.Equal(UpstreamLiterals.Read("TownAnnouncerPolicy.h", 268, 289), SkarnScripts.SelectMany(x => x).ToArray());
        Assert.Equal(UpstreamLiterals.Read("TownAnnouncerPolicy.h", 294, 303), SennScripts.SelectMany(x => x).ToArray());
        Assert.Equal(UpstreamLiterals.Read("TownAnnouncerPolicy.h", 304, 313), TorkaScripts.SelectMany(x => x).ToArray());
        Assert.Equal(new[] { 8, 5, 4, 4 }, new[] { HouseScripts.Length, SkarnScripts.Length, SennScripts.Length, TorkaScripts.Length });
        Assert.All(HouseScripts, s => Assert.Equal(3, s.Length));
        Assert.All(SkarnScripts, s => Assert.Equal(4, s.Length));
        // every byte is ASCII, so the UTF-8 the glue hands to Character::say is the C++'s narrow string
        Assert.All(HouseScripts.Concat(SkarnScripts).Concat(SennScripts).Concat(TorkaScripts).SelectMany(x => x),
            s => Assert.True(s.All(ch => ch >= 0x20 && ch < 0x7f), s));
        Assert.Equal(new[] { "FIGHT!" }, UpstreamLiterals.Read("TownAnnouncerPolicy.h", 234, 234));
    }

    /// <summary>The ids the header writes as literals are the PgRecords ones (so a PgRecords drift is caught here too).</summary>
    [Fact]
    public void Record_ids_are_the_PgRecords_ids()
    {
        Assert.True(IsSpot(PgRecords.AnnouncerSpot));
        Assert.Equal(PgRecords.ScratchAnnouncer, SpeakerRole(Program.HouseAnnouncer, 0));
        Assert.Equal(PgRecords.Skarn, SpeakerRole(Program.SkarnAnnouncer, 0));
        Assert.Equal(PgRecords.Senn, SpeakerRole(Program.SennAndTorkaAnnouncers, 0));
        Assert.Equal(PgRecords.Torka, SpeakerRole(Program.SennAndTorkaAnnouncers, 1));
        Assert.Equal(Program.SkarnAnnouncer, Select(true, 0, new string?[] { PgRecords.Senn }, 1));
        Assert.Equal(Program.SkarnAnnouncer, Select(true, 0, new string?[] { PgRecords.Torka }, 1));
        Assert.Equal(Program.HouseAnnouncer, Select(true, 5, new string?[] { PgRecords.Torka }, 1));
        Assert.Equal(TownSpectatorPolicy.AudienceRole.Civilian, TownSpectatorPolicy.RoleFor(PgRecords.ScratchCivilian));
        Assert.Equal(TownSpectatorPolicy.AudienceRole.BarResident, TownSpectatorPolicy.RoleFor(PgRecords.ScratchBarResident));
        Assert.Equal(TownSpectatorPolicy.AudienceRole.CageHandler, TownSpectatorPolicy.RoleFor(PgRecords.CageHandler));
    }

    [Fact]
    public void Unspoken_lines_stay_unspoken()
    {
        // House even variants have 2 setup cues and Skarn even variants 3: their last table line is never returned (as shipped).
        Assert.Equal("FIGHT!", CueText(Program.HouseAnnouncer, 0, 2));
        Assert.Equal(HouseScripts[1][2], CueText(Program.HouseAnnouncer, 1, 2));
        Assert.Equal("FIGHT!", CueText(Program.SkarnAnnouncer, 2, 3));
        Assert.Equal(SkarnScripts[3][3], CueText(Program.SkarnAnnouncer, 3, 3));
        // a negative variant is an unsigned roll: -1 = 0xffffffff, % 8 = 7, % 5 = 0
        Assert.Equal(HouseScripts[7][0], CueText(Program.HouseAnnouncer, -1, 0));
        Assert.Equal(SkarnScripts[0][0], CueText(Program.SkarnAnnouncer, -1, 0));
        Assert.Null(CueText(Program.LegacyCountdown, 0, 0));
    }

    [Fact]
    public void Struct_defaults_match_the_cpp()
    {
        var p = new ApproachProgress();
        Assert.Equal(0.0f, p.BestDistance);
        Assert.Equal(0.0f, p.StalledElapsed);
        ApproachProgress d = default;   // the C++ default is all zeros, so default(...) is the same here
        Assert.Equal(p.BestDistance, d.BestDistance);
        Assert.Equal(p.StalledElapsed, d.StalledElapsed);
    }

    [Fact]
    public void Duration_of_each_program()
    {
        // (cueCount - 1) * 3.25 + 0.75: House 2 setup = 7.25, 3 setup = 10.5; Skarn 3 = 10.5, 4 = 13.75; Senn and Torka 13.75
        Assert.Equal(7.25f, Duration(Program.HouseAnnouncer, 0));
        Assert.Equal(10.5f, Duration(Program.HouseAnnouncer, 1));
        Assert.Equal(10.5f, Duration(Program.SkarnAnnouncer, 0));
        Assert.Equal(13.75f, Duration(Program.SkarnAnnouncer, 1));
        Assert.Equal(13.75f, Duration(Program.SennAndTorkaAnnouncers, 0));
        Assert.Equal(0.0f, Duration(Program.LegacyCountdown));
    }
}
