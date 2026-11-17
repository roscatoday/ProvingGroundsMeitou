using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The rules half of ResultsUI.cpp (task 4.6a): the 1.0 s delay on the clock (ScheduleShow / Tick :607-683), Cancel / Close / IsVisible,
/// and every text ShowFromSnapshot / FillSlot write (:460-602), byte for byte.</summary>
public class ResultsUILogicTests
{
    sealed class Host : IResultsHost<int>
    {
        public uint Now;
        public SparPodium.Snapshot? Snap;
        public readonly Dictionary<int, int> Characters = [];
        public readonly List<string> Lines = [];
        public uint NowMs => Now;
        public bool HasSnapshot() => Snap is not null && Snap.FighterCount > 0;
        public SparPodium.Snapshot GetSnapshot() => Snap!;
        public bool TryPodiumCharacter(int index, out int id) => Characters.TryGetValue(index, out id);
        public void Debug(string line) => Lines.Add(line);
    }

    static SparPodium.FighterRow Row(int id, string name, MatchRules.MatchTeam team, int elim = -1) => new()
    {
        Id = id, Name = name, Team = team, DamageDealt = 1046.4f, DamageTaken = 0.4f, DamageMitigated = 12.4f, HitsLanded = 11, Blocks = 6,
        Misses = 0, Dodges = 3, EliminationIndex = elim,
    };

    static SparPodium.Snapshot TeamWin(int podium, bool rated)
    {
        var s = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins, Mode = MatchRules.MatchMode.Teams1v1, FighterCount = 6 };
        for (int i = 0; i < 6; i++) s.Fighters[i] = Row(i, "F" + i, i < 3 ? MatchRules.MatchTeam.TeamA : MatchRules.MatchTeam.TeamB);
        SparPodium.BuildPodium(s);
        s.PodiumCount = podium;
        if (rated)
            for (int p = 0; p < podium; p++)
            {
                ref var f = ref s.Podium[p].Fighter;
                f.RatingUpdated = true;
                f.RatingAfter = 120f;
                f.RatingDelta = 20f;
                f.MarksAfter = 58;
                f.MarksEarned = 10;
            }
        return s;
    }

    [Fact]
    public void ScheduleShowNeedsASnapshotAndWaitsOneSecondOnTheClock()
    {
        var h = new Host { Now = 1000 };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);   // no snapshot: nothing scheduled (:609-610)
        Assert.False(l.Pending);
        h.Snap = TeamWin(3, false);
        l.ScheduleShow(h, 1.0f);
        Assert.True(l.Pending);
        Assert.Contains("Proving Grounds: results UI scheduled", h.Lines);
        for (int t = 1; t <= 29; t++)
        {
            h.Now = 1000 + (uint)(t * 1000 / 30);
            Assert.False(l.Tick(h));
            Assert.False(l.IsVisible);
        }
        // the C++ sums float seconds of whole-millisecond deltas (:655-658): 30 ticks of 33/34 ms can sum to just under 1.0f, so the show comes on
        // tick 30 or 31 (a C++ frame later at most), never earlier
        int shownAt = 0;
        for (int t = 30; t <= 32 && shownAt == 0; t++)
        {
            h.Now = 1000 + (uint)(t * 1000 / 30);
            if (l.Tick(h)) shownAt = t;
        }
        Assert.InRange(shownAt, 30, 31);
        Assert.True(l.IsVisible);
        Assert.False(l.Pending);
        Assert.Contains("Proving Grounds: results podium shown", h.Lines);
        Assert.Equal(1, l.BuildView().RequestSerial);
        Assert.True(l.BuildView().RequestOpen);
    }

    [Fact]
    public void ANonPositiveDelayIsOneSecond()
    {
        var h = new Host { Now = 0, Snap = TeamWin(1, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 0f);
        h.Now = 999;
        Assert.False(l.Tick(h));
        h.Now = 1000;
        Assert.True(l.Tick(h));
    }

    [Fact]
    public void TheDelayIsMeasuredFromTheScheduleNotFromTheFirstTick()
    {
        var h = new Host { Now = 5000, Snap = TeamWin(1, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        h.Now = 6000;   // the first Tick after a whole second: shown at once
        Assert.True(l.Tick(h));
    }

    [Fact]
    public void ASnapshotClearedWhileWaitingShowsNothing()
    {
        var h = new Host { Now = 0, Snap = TeamWin(1, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        h.Snap = null;
        h.Now = 1000;
        Assert.False(l.Tick(h));
        Assert.False(l.Pending);
        Assert.False(l.IsVisible);
    }

    [Fact]
    public void CancelAndClose()
    {
        var h = new Host { Now = 0, Snap = TeamWin(2, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        l.Cancel();
        h.Now = 2000;
        Assert.False(l.Tick(h));
        l.ScheduleShow(h, 1.0f);
        h.Now = 3000;
        Assert.True(l.Tick(h));
        Assert.True(l.IsVisible);
        l.Close();
        Assert.False(l.IsVisible);
        var v = l.BuildView();
        Assert.Equal(2, v.RequestSerial);
        Assert.False(v.RequestOpen);
        l.Close();   // closing a closed window asks nothing new
        Assert.Equal(2, l.BuildView().RequestSerial);
    }

    [Fact]
    public void AStaleCloseResultsPressDoesNotCancelANewPendingShow()
    {
        var h = new Host { Now = 0, Snap = TeamWin(1, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        h.Now = 1000;
        Assert.True(l.Tick(h));
        l.Close();                        // closed (Escape), then a new match schedules a show
        l.ScheduleShow(h, 1.0f);
        Assert.False(l.OnCloseClicked());   // the press sent before the close arrives a tick later: ignored
        Assert.True(l.Pending);
        h.Now = 2000;
        Assert.True(l.Tick(h));
        Assert.True(l.OnCloseClicked());
        Assert.False(l.IsVisible);
    }

    [Fact]
    public void ClosedFromTheWindowOnlyForItsSerial()
    {
        var h = new Host { Now = 0, Snap = TeamWin(1, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        h.Now = 1000;
        l.Tick(h);
        Assert.False(l.OnClosedFromWindow(7));
        Assert.True(l.IsVisible);
        Assert.True(l.OnClosedFromWindow(1));
        Assert.False(l.IsVisible);
        Assert.Equal(1, l.BuildView().RequestSerial);
    }

    static ResultsUiView<int> Shown(SparPodium.Snapshot snap, Host? host = null)
    {
        var h = host ?? new Host();
        h.Snap = snap;
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        h.Now += 1000;
        Assert.True(l.Tick(h));
        return l.BuildView();
    }

    [Fact]
    public void TeamModeTexts()
    {
        var h = new Host();
        h.Characters[0] = 40;
        var v = Shown(TeamWin(3, false), h);
        Assert.Equal("Team A wins", v.Header);
        Assert.Equal("Teams 1v1  -  6 fighters", v.Context);
        Assert.False(v.EmptyVisible);
        Assert.Equal(3, v.Slots.Count);
        Assert.Equal(["MVP", "MVP #2", "MVP #3"], v.Slots.Select(s => s.RankCaption));
        Assert.Equal([1, 2, 3], v.Slots.Select(s => s.Place));
        Assert.Equal("F0  -  Team A", v.Slots[0].NameLine);
        Assert.Equal("F", v.Slots[0].Initial);
        Assert.True(v.Slots[0].HasCharacter);
        Assert.Equal(40, v.Slots[0].Character);
        Assert.False(v.Slots[1].HasCharacter);
        Assert.Equal("DEALT 1046  •  TAKEN 0  •  MITIGATED 12\nHITS 11  •  BLOCKS 6  •  MISSES 0  •  DODGES 3", v.Slots[0].Stats);
    }

    [Fact]
    public void RatedStatsCarryRatingAndMarks()
    {
        var snap = TeamWin(1, true);
        var v = Shown(snap);
        Assert.Equal("DEALT 1046  •  TAKEN 0  •  MITIGATED 12\nHITS 11  •  BLOCKS 6  •  MISSES 0  •  DODGES 3\n"
            + "RATING 120.0 (+20.0)  •  MARKS 58 (+10)", v.Slots[0].Stats);
    }

    [Theory]
    [InlineData(-12.4f, "RATING 87.6 (-12.4)")]
    [InlineData(0f, "RATING 100.0 (+0.0)")]
    [InlineData(-0.04f, "RATING 100.0 (-0.0)")]
    public void RatingDeltaIsPrintedAsPercentPlusPointOneF(float delta, string expected)
    {
        var snap = TeamWin(1, true);
        snap.Podium[0].Fighter.RatingAfter = 100f + (delta < -1 ? delta : 0f);
        snap.Podium[0].Fighter.RatingDelta = delta;
        var v = Shown(snap);
        Assert.Contains(expected + "  •  MARKS", v.Slots[0].Stats);
    }

    [Fact]
    public void LastStandingTextsAndSingularFighter()
    {
        var s = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.LastStanding, Mode = MatchRules.MatchMode.LastStanding, FighterCount = 1 };
        s.Fighters[0] = Row(3, "Kang", MatchRules.MatchTeam.None);
        s.LastStandingId = 3;
        SparPodium.BuildPodium(s);
        var v = Shown(s);
        Assert.Equal("Kang last standing", v.Header);
        Assert.Equal("Last man standing  -  1 fighter", v.Context);
        Assert.Equal("VICTOR", v.Slots[0].RankCaption);
        Assert.Equal("Kang", v.Slots[0].NameLine);   // no team: the name alone (:516-519)
    }

    [Fact]
    public void FreeForAllPlacesAndTeamAvBCaption()
    {
        var s = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.Stopped, Mode = MatchRules.MatchMode.LastStanding, FighterCount = 3 };
        for (int i = 0; i < 3; i++) s.Fighters[i] = Row(i, "N" + i, MatchRules.MatchTeam.None);
        SparPodium.BuildPodium(s);
        var v = Shown(s);
        Assert.Equal("Stopped", v.Header);
        Assert.Equal(["VICTOR", "SECOND PLACE", "THIRD PLACE"], v.Slots.Select(x => x.RankCaption));
        var avb = TeamWin(1, false);
        avb.Mode = MatchRules.MatchMode.TeamAvB;
        Assert.Equal("Team A vs Team B  -  6 fighters", Shown(avb).Context);
    }

    [Fact]
    public void DrawHasNoPodiumAndShowsTheEmptyText()
    {
        var s = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.Draw, Mode = MatchRules.MatchMode.TeamAvB, FighterCount = 2 };
        s.Fighters[0] = Row(0, "A", MatchRules.MatchTeam.TeamA);
        s.Fighters[1] = Row(1, "B", MatchRules.MatchTeam.TeamB);
        SparPodium.BuildPodium(s);
        var v = Shown(s);
        Assert.Equal("Draw", v.Header);
        Assert.Empty(v.Slots);
        Assert.True(v.EmptyVisible);
        Assert.Equal("No victor\nThe match ended without a podium.", ResultsUiTexts.Empty);
    }

    [Fact]
    public void AnEmptyHeaderReadsMatchEndedAndAnEmptyNameGivesAQuestionMark()
    {
        var s = TeamWin(1, false);
        s.Header = "";
        s.Podium[0].Fighter.Name = null;
        s.Podium[0].Fighter.Team = MatchRules.MatchTeam.None;
        var v = Shown(s);
        Assert.Equal("Match ended", v.Header);
        Assert.Equal("?", v.Slots[0].Initial);
        Assert.Equal("", v.Slots[0].NameLine);
    }

    [Fact]
    public void EachShowIsANewRevision()
    {
        var h = new Host { Snap = TeamWin(1, false) };
        var l = new ResultsUILogic<int>();
        l.ScheduleShow(h, 1.0f);
        h.Now = 1000;
        l.Tick(h);
        int first = l.BuildView().Revision;
        l.ScheduleShow(h, 1.0f);
        h.Now = 2000;
        l.Tick(h);
        Assert.Equal(first + 1, l.BuildView().Revision);
        Assert.Equal(1, l.BuildView().RequestSerial);   // already open: no new open request
    }
}
