using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.DebugMenuNames;
using T = ProvingGrounds.Core.Glue.DebugMenuTexts;

namespace ProvingGrounds.Tests.Glue;

/// <summary>DebugMenu.cpp's rules (task 4.6b) over a fake host: characters are ints (0 = none).</summary>
public class DebugMenuLogicTests
{
    sealed class Fake : IDebugMenuHost<int>
    {
        public List<int> Squad = [];
        public List<int> Roster = [];
        public List<int> Cage = [];
        public bool Registry, Activity;
        public HashSet<int> Invalid = [], Dead = [], RosterPrisoner = [];
        public Dictionary<int, float> Ratings = [];
        public Dictionary<int, int> Marks = [];
        public bool Ready = true;
        public List<string> Calls = [];
        public TownDiagnosticData.State Diag = new();
        public bool DiagOk;
        public string DiagError = "";
        public bool Schedule = true;
        public uint Now;

        public int None => 0;
        public bool IsNone(int c) => c == 0;
        public bool IsValid(int c) => c != 0 && !Invalid.Contains(c);
        public bool IsDead(int c) => Dead.Contains(c);
        public string Name(int c) => Names.TryGetValue(c, out var n) ? n : "Fighter " + c;
        public Dictionary<int, string> Names = [];
        public string Key(int c) => "id" + c;
        public void CollectPlayerSquad(List<int> into) => into.AddRange(Squad);
        public bool HasBoundRegistry() => Registry;
        public bool PrisonersHaveRuntimeActivity() => Activity;
        public void CollectNearbyCagePrisoners(List<int> into) => into.AddRange(Cage);
        public IReadOnlyList<int> RosterPrisoners() => Roster;
        public bool IsRosterPrisoner(int c) => RosterPrisoner.Contains(c);
        public float GetRating(int c) { Calls.Add("rating " + c); return Ratings.TryGetValue(c, out var r) ? r : 150f; }
        public int GetMarks(int c) { Calls.Add("marks " + c); return Marks.TryGetValue(c, out var m) ? m : 0; }
        public bool SetRating(int c, float rating)
        {
            Calls.Add("set rating " + c + " " + rating.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!Ready) return false;
            Ratings[c] = Math.Clamp(rating, 0f, 9999f);
            return true;
        }
        public bool SetMarks(int c, int marks)
        {
            Calls.Add("set marks " + c + " " + marks);
            if (!Ready) return false;
            Marks[c] = Math.Clamp(marks, 0, 9999);
            return true;
        }
        public void TownBeginTrial() => Calls.Add("trial");
        public bool TownIsScheduleEnabled() => Schedule;
        public void TownSetScheduleEnabled(bool enabled) { Schedule = enabled; Calls.Add("schedule " + enabled); }
        public void TownCancel() => Calls.Add("cancel");
        public string TownStatus() => "Town trial idle";
        public TownDiagnosticData.State Diagnostic() => Diag;
        public string StatusText = "status text";
        public string DiagnosticStatusText() => StatusText;
        public string DiagnosticValidationError() => DiagError;
        bool D(string what) { Calls.Add(what); return DiagOk; }
        public bool DiagnosticStartPreset(bool reset) => D("preset " + reset);
        public bool DiagnosticStartConfigured(bool reset) => D("configured " + reset);
        public bool DiagnosticPause() => D("pause");
        public bool DiagnosticResume() => D("resume");
        public bool DiagnosticEnd() => D("end");
        public bool DiagnosticSetTarget(int target) => D("target " + target);
        public bool DiagnosticToggleModel(int model) => D("model " + model);
        public bool DiagnosticCycleReducedInfluence() => D("influence");
        public bool DiagnosticToggleCoverage(int coverage) => D("coverage " + coverage);
        public bool DiagnosticCycleUnderdogPercent() => D("underdog");
        public bool DiagnosticToggleStyleTierBalance() => D("balance");
        public uint NowMs => Now;
    }

    static (DebugMenuLogic<int> L, Fake H) Open(params int[] squad)
    {
        var h = new Fake { Squad = [.. squad] };
        var l = new DebugMenuLogic<int>(0);
        l.Toggle(h);
        return (l, h);
    }

    [Fact]
    public void StartsClosedWithTheCreationTexts()
    {
        var l = new DebugMenuLogic<int>(0);
        var v = l.BuildView();
        Assert.False(v.RequestOpen);
        Assert.Equal(0, v.RequestSerial);
        Assert.Equal(T.StatusStart, v.Status);
        Assert.Equal(T.SelectCharacter, v.SelectedName);
        Assert.Equal(T.DiagnosticOff, v.DiagnosticSummary);
        Assert.All(v.Rows, r => Assert.False(r.Visible));
        Assert.All(v.DiagnosticLabels, s => Assert.Equal("", s));
    }

    [Fact]
    public void ToggleOpensAndRefreshesThenCloses()
    {
        var (l, h) = Open(1, 2, 3);
        var v = l.BuildView();
        Assert.True(v.RequestOpen);
        Assert.Equal(1, v.RequestSerial);
        Assert.Equal(T.HeadingFighters, v.Heading);
        Assert.Equal("Page 1 / 1", v.PageLabel);
        Assert.False(v.PrevEnabled);
        Assert.False(v.NextEnabled);
        Assert.Equal("Fighter 1    R:150  M:0", v.Rows[0].Text);
        Assert.True(v.Rows[2].Visible);
        Assert.False(v.Rows[3].Visible);
        Assert.Equal(T.SelectCharacter, v.SelectedName);
        Assert.Equal(T.RatingNone, v.Rating);
        Assert.Equal(T.MarksNone, v.Marks);
        // C++ :346-348: the rating is read before the marks, row by row
        Assert.Equal(["rating 1", "marks 1", "rating 2", "marks 2", "rating 3", "marks 3"], h.Calls);
        l.Toggle(h);
        Assert.False(l.BuildView().RequestOpen);
        Assert.Equal(2, l.BuildView().RequestSerial);
    }

    [Fact]
    public void RowTextRoundsTheRatingAsTheCxx()
    {
        Assert.Equal("A [PRISONER]    R:1235  M:7", T.RowText("A", true, 1234.5f, 7));
        Assert.Equal("B    R:0  M:0", T.RowText("B", false, 0.49f, 0));
        Assert.Equal(1234, T.RoundRating(1234.49f));
        Assert.Equal("Rating: 100", T.Rating(99.5f));
        Assert.Equal("Arena Marks: 12", T.Marks(12));
    }

    [Fact]
    public void PrisonersFollowTheSquadOnceEachLivingAndTheCageWhenARegistryIsBound()
    {
        var h = new Fake { Squad = [1, 2], Roster = [2, 5, 6, 7, 0], Cage = [9] };
        h.Dead.Add(6);
        h.Invalid.Add(7);
        h.RosterPrisoner.Add(5);
        var l = new DebugMenuLogic<int>(0);
        l.Toggle(h);
        Assert.Equal([1, 2, 5], l.Characters);
        Assert.Equal("Fighter 5 [PRISONER]    R:150  M:0", l.BuildView().Rows[2].Text);
        h.Registry = true;
        l.Press(h, N.FightersTab);
        Assert.Equal([1, 2, 9], l.Characters);   // CollectNearbyCagePrisoners
        h.Activity = true;
        l.Press(h, N.FightersTab);
        Assert.Equal([1, 2, 5], l.Characters);   // runtime activity: the roster prisoners
    }

    [Fact]
    public void PagesHoldEightRowsAndClamp()
    {
        var (l, h) = Open([.. Enumerable.Range(1, 19)]);
        Assert.Equal("Page 1 / 3", l.BuildView().PageLabel);
        Assert.True(l.BuildView().NextEnabled);
        Assert.False(l.Press(h, N.Prev));   // disabled: MyGUI sends no click
        Assert.True(l.Press(h, N.Next));
        Assert.True(l.Press(h, N.Next));
        var v = l.BuildView();
        Assert.Equal("Page 3 / 3", v.PageLabel);
        Assert.False(v.NextEnabled);
        Assert.True(v.PrevEnabled);
        Assert.Equal("Fighter 17    R:150  M:0", v.Rows[0].Text);
        Assert.True(v.Rows[2].Visible);
        Assert.False(v.Rows[3].Visible);
        Assert.False(l.Press(h, N.Row(3)));   // a hidden row
        h.Squad = [1, 2];                     // the squad shrank: the page clamps at the next refresh
        Assert.True(l.Press(h, N.Prev));
        Assert.Equal("Page 1 / 1", l.BuildView().PageLabel);
    }

    [Fact]
    public void SelectingARowUsesItsIndexFromTheLastRefresh()
    {
        var (l, h) = Open(4, 5);
        Assert.True(l.Press(h, N.Row(1)));
        Assert.Equal(5, l.SelectedCharacter);
        var v = l.BuildView();
        Assert.Equal(T.ChangesSaved, v.Status);
        Assert.Equal("Fighter 5", v.SelectedName);
        Assert.Equal("Rating: 150", v.Rating);
        Assert.Equal("Arena Marks: 0", v.Marks);
        Assert.True(v.Rows[1].Selected);
        Assert.False(v.Rows[0].Selected);
        // the squad changed after that refresh: the row still names index 1 of the list the last refresh collected (g_characters)
        h.Squad = [7, 8, 9];
        Assert.True(l.Press(h, N.Row(0)));
        Assert.Equal(4, l.SelectedCharacter);
        Assert.True(l.Press(h, N.Row(1)));   // that press's refresh collected the new squad
        Assert.Equal(8, l.SelectedCharacter);
    }

    [Fact]
    public void RatingButtonsNeedASelectionAndUseTheStore()
    {
        var (l, h) = Open(1);
        h.Calls.Clear();
        int revision = l.BuildView().Revision;
        Assert.True(l.Press(h, N.Actions[3]));   // Rating +100 without a selection
        Assert.Equal(T.SelectFirst, l.BuildView().Status);
        Assert.Empty(h.Calls);                   // no refresh (C++ :402-403 returns)
        Assert.Equal(revision, l.BuildView().Revision);
        l.Press(h, N.Row(0));
        h.Calls.Clear();
        l.Press(h, N.Actions[3]);
        Assert.Equal("set rating 1 250", h.Calls[1]);
        Assert.Equal(T.RatingUpdated, l.BuildView().Status);
        Assert.Equal("Rating: 250", l.BuildView().Rating);
        l.Press(h, N.Actions[0]);
        l.Press(h, N.Actions[0]);
        l.Press(h, N.Actions[0]);
        Assert.Equal("Rating: 0", l.BuildView().Rating);   // the store clamps
        l.Press(h, N.Actions[2]);
        Assert.Equal("Rating: 10", l.BuildView().Rating);
        l.Press(h, N.Actions[4]);
        Assert.Equal(T.RatingReset, l.BuildView().Status);
        Assert.Equal("Rating: 0", l.BuildView().Rating);
        h.Ready = false;
        l.Press(h, N.Actions[1]);
        Assert.Equal(T.RatingFailed, l.BuildView().Status);
        l.Press(h, N.Actions[4]);
        Assert.Equal(T.RatingFailed, l.BuildView().Status);
    }

    [Fact]
    public void MarksButtonsNeedASelectionAndUseTheStore()
    {
        var (l, h) = Open(1);
        l.Press(h, N.Actions[7]);
        Assert.Equal(T.SelectOrPrisonerFirst, l.BuildView().Status);
        l.Press(h, N.Actions[8]);
        Assert.Equal(T.SelectOrPrisonerFirst, l.BuildView().Status);
        l.Press(h, N.Row(0));
        l.Press(h, N.Actions[7]);
        l.Press(h, N.Actions[6]);
        Assert.Equal(T.MarksUpdated, l.BuildView().Status);
        Assert.Equal("Arena Marks: 110", l.BuildView().Marks);
        Assert.Equal("Fighter 1    R:150  M:110", l.BuildView().Rows[0].Text);
        l.Press(h, N.Actions[5]);
        Assert.Equal("Arena Marks: 100", l.BuildView().Marks);
        l.Press(h, N.Actions[8]);
        Assert.Equal(T.MarksReset, l.BuildView().Status);
        Assert.Equal("Arena Marks: 0", l.BuildView().Marks);
        h.Ready = false;
        l.Press(h, N.Actions[6]);
        Assert.Equal(T.MarksFailed, l.BuildView().Status);
    }

    [Fact]
    public void TownButtonsCallTheTownArenaAndShowItsStatus()
    {
        var (l, h) = Open(1);
        h.Calls.Clear();
        l.Press(h, N.Actions[9]);
        l.Press(h, N.Actions[10]);
        l.Press(h, N.Actions[11]);
        Assert.Equal(["trial", "cancel", "schedule False"], h.Calls);
        Assert.Equal("Town trial idle", l.BuildView().Status);
    }

    [Fact]
    public void AnInvalidSelectionIsDroppedAtTheNextRefresh()
    {
        var (l, h) = Open(1, 2);
        l.Press(h, N.Row(0));
        h.Invalid.Add(1);
        h.Squad = [2];
        l.Press(h, N.FightersTab);
        Assert.Equal(0, l.SelectedCharacter);
        Assert.Equal(T.SelectCharacter, l.BuildView().SelectedName);
    }

    [Fact]
    public void DiagnosticsTabShowsTheRunStateAndHidesTheFighterWidgets()
    {
        var (l, h) = Open(1, 2);
        int revision = l.BuildView().Revision;
        Assert.True(l.Press(h, N.DiagnosticsTab));
        var v = l.BuildView();
        Assert.False(v.Fighters);
        Assert.Equal(T.HeadingDiagnostics, v.Heading);
        Assert.All(v.Rows, r => Assert.False(r.Visible));
        Assert.Equal("status text", v.DiagnosticSummary);
        Assert.Equal("Start Fresh 150 (resets town records)", v.DiagnosticLabels[0]);
        Assert.Equal("Pause Diagnostic Selection", v.DiagnosticLabels[4]);
        Assert.Equal("Target preset: 150", v.DiagnosticLabels[6]);
        Assert.Equal("Stats-only model: ON", v.DiagnosticLabels[9]);
        Assert.Equal("Reduced MMR influence: 25%", v.DiagnosticLabels[12]);
        Assert.Equal("Moderate-underdog share: 20%", v.DiagnosticLabels[17]);
        Assert.Equal("Style/tier balancing: ON", v.DiagnosticLabels[18]);
        // idle run: the starts and settings enabled, Pause / End not (C++ :322-326)
        Assert.True(v.DiagnosticEnabled[0]);
        Assert.False(v.DiagnosticEnabled[4]);
        Assert.False(v.DiagnosticEnabled[5]);
        Assert.True(v.DiagnosticEnabled[18]);
        Assert.NotEqual(revision, v.Revision);
        Assert.False(l.Press(h, N.Actions[0]));   // a fighter action is hidden on this tab
        Assert.False(l.Press(h, N.Row(0)));
        Assert.False(l.Press(h, N.Prev));
        Assert.False(l.Press(h, N.Diagnostics[4]));   // disabled
    }

    [Fact]
    public void DiagnosticEnabledFollowsTheRunStatus()
    {
        var s = new TownDiagnosticData.State { Status = (int)TownDiagnosticData.RunStatus.Active };
        var e = T.DiagnosticEnabled(s);
        Assert.False(e[0]);
        Assert.True(e[4]);
        Assert.True(e[5]);
        Assert.False(e[6]);
        s.Pending.Active = true;
        e = T.DiagnosticEnabled(s);
        Assert.False(e[4]);
        Assert.False(e[5]);
        s.Status = (int)TownDiagnosticData.RunStatus.Paused;
        e = T.DiagnosticEnabled(s);
        Assert.True(e[4]);
        Assert.False(e[5]);
        Assert.Equal("Resume Diagnostic Run", T.DiagnosticLabels(s, 0)[4]);
    }

    [Fact]
    public void FreshStartsAskForAConfirmationClickFirst()
    {
        var (l, h) = Open(1);
        l.Press(h, N.DiagnosticsTab);
        h.Calls.Clear();
        l.Press(h, N.Diagnostics[0]);
        Assert.Equal(1, l.ConfirmFresh);
        Assert.Equal(T.ConfirmFresh150, l.BuildView().Status);
        Assert.Equal("CONFIRM: reset town records + start 150", l.BuildView().DiagnosticLabels[0]);
        Assert.Empty(h.Calls);
        l.Press(h, N.Diagnostics[2]);   // the other confirmation replaces it
        Assert.Equal(2, l.ConfirmFresh);
        Assert.Equal(T.ConfirmConfigured, l.BuildView().Status);
        Assert.Equal("Start Fresh 150 (resets town records)", l.BuildView().DiagnosticLabels[0]);
        Assert.Equal("CONFIRM: reset + start configured", l.BuildView().DiagnosticLabels[2]);
        l.Press(h, N.Diagnostics[2]);   // confirmed: the action runs; refused by the fake run with no validation error: the C++ fallback text, confirmation kept
        Assert.Equal(["configured True"], h.Calls);
        Assert.Equal(T.DiagnosticUnavailable, l.BuildView().Status);
        Assert.Equal(2, l.ConfirmFresh);
        h.DiagError = "Load a game first.";
        l.Press(h, N.Diagnostics[1]);
        Assert.Equal("Load a game first.", l.BuildView().Status);
        h.DiagOk = true;
        l.Press(h, N.Diagnostics[1]);
        Assert.Equal(T.Current150Started, l.BuildView().Status);
        Assert.Equal(0, l.ConfirmFresh);   // a success clears it
        l.Press(h, N.Diagnostics[0]);
        l.Press(h, N.FightersTab);         // a tab change clears it
        Assert.Equal(0, l.ConfirmFresh);
    }

    [Fact]
    public void DiagnosticActionsCallTheirSetters()
    {
        var (l, h) = Open(1);
        l.Press(h, N.DiagnosticsTab);
        h.DiagOk = true;
        h.Calls.Clear();
        for (int i = 3; i < N.Diagnostics.Count; ++i)
            if (i != 4 && i != 5) l.Press(h, N.Diagnostics[i]);   // 4 and 5 are disabled while idle
        Assert.Equal(["configured False", "target 300", "target 140", "target 160", "model 0", "model 1", "model 2", "influence",
            "coverage 0", "coverage 1", "coverage 2", "coverage 3", "underdog", "balance"], h.Calls);
        Assert.Equal(T.BalanceChanged, l.BuildView().Status);
        // the preset target cycles 6 -> 50 -> 150 -> 300 -> 6 (C++ :495)
        foreach (var (from, to) in new[] { (1, 6), (6, 50), (49, 50), (50, 150), (150, 300), (300, 6), (999, 6) })
        {
            h.Diag.Config.Target = from;
            h.Calls.Clear();
            l.Press(h, N.Diagnostics[6]);
            Assert.Equal("target " + to, h.Calls[0]);
        }
        h.Diag.Config.Target = 5;
        h.Calls.Clear();
        l.Press(h, N.Diagnostics[7]);
        Assert.Equal("target 1", h.Calls[0]);   // Max(1, value - 10)
    }

    [Fact]
    public void PauseOrResumeFollowsTheRunAndEndNeedsALiveRun()
    {
        var (l, h) = Open(1);
        l.Press(h, N.DiagnosticsTab);
        h.DiagOk = true;
        h.Diag.Status = (int)TownDiagnosticData.RunStatus.Active;
        l.Press(h, N.FightersTab);
        l.Press(h, N.DiagnosticsTab);   // refresh: Pause / End enabled now
        h.Calls.Clear();
        l.Press(h, N.Diagnostics[4]);
        Assert.Equal(T.Paused, l.BuildView().Status);
        h.Diag.Status = (int)TownDiagnosticData.RunStatus.Paused;
        l.Press(h, N.Diagnostics[4]);
        Assert.Equal(T.Resumed, l.BuildView().Status);
        Assert.Equal(["pause", "resume"], h.Calls);
    }

    [Fact]
    public void DiagnosticsRefreshEverySecondWhileOpenOnThatTab()
    {
        var (l, h) = Open(1);
        h.Calls.Clear();
        h.Now = 5000;
        l.Tick(h);
        Assert.Empty(h.Calls);   // the fighters tab: no refresh
        l.Press(h, N.DiagnosticsTab);
        h.Diag.Config.Target = 42;
        l.Tick(h);
        Assert.Equal("Target preset: 42", l.BuildView().DiagnosticLabels[6]);
        h.Diag.Config.Target = 43;
        h.Now = 5999;
        l.Tick(h);
        Assert.Equal("Target preset: 42", l.BuildView().DiagnosticLabels[6]);
        h.Now = 6000;
        l.Tick(h);
        Assert.Equal("Target preset: 43", l.BuildView().DiagnosticLabels[6]);
        l.Close();
        h.Diag.Config.Target = 44;
        h.Now = 9000;
        l.Tick(h);
        Assert.Equal("Target preset: 43", l.BuildView().DiagnosticLabels[6]);
    }

    [Fact]
    public void ClosedWindowIgnoresPressesAndAClosedReportKeepsTheSerial()
    {
        var (l, h) = Open(1);
        int serial = l.BuildView().RequestSerial;
        Assert.False(l.OnClosedFromWindow(serial - 1));
        Assert.True(l.OnClosedFromWindow(serial));
        Assert.False(l.IsVisible);
        Assert.Equal(serial, l.BuildView().RequestSerial);
        Assert.False(l.OnClosedFromWindow(serial));
        Assert.False(l.Press(h, N.Row(0)));
        Assert.False(l.Press(h, N.FightersTab));
        Assert.False(l.Press(h, "nothing"));
    }

    [Fact]
    public void AbandonWorldStateHidesAndClearsTheSelection()
    {
        var (l, h) = Open(1, 2);
        l.Press(h, N.Row(1));
        l.Press(h, N.Next);
        l.AbandonWorldState(h);
        var v = l.BuildView();
        Assert.False(v.RequestOpen);
        Assert.Equal(0, l.SelectedCharacter);
        Assert.Empty(l.Characters);
        Assert.Equal(0, l.Page);
        Assert.Equal(T.SelectCharacter, v.SelectedName);
        Assert.Equal(T.RatingNone, v.Rating);
        Assert.Equal(T.MarksNone, v.Marks);
        Assert.Equal(T.ChangesSaved, v.Status);   // the status line keeps its text (C++ does not touch it)
    }

    [Fact]
    public void RevisionMovesOnlyWhenALaidOutTextChanges()
    {
        var (l, h) = Open(1);
        int r0 = l.BuildView().Revision;
        l.Press(h, N.FightersTab);   // same content
        Assert.Equal(r0, l.BuildView().Revision);
        l.Press(h, N.Actions[9]);    // only the status changes (no refresh)
        Assert.Equal(r0, l.BuildView().Revision);
        l.Press(h, N.Row(0));        // selection: the detail texts change
        Assert.NotEqual(r0, l.BuildView().Revision);
    }

    // ---------- task 8.7b: the drawn-model clicks (a click carries what the window drew; a stale one is refused) ----------

    static DebugMenuPressResult PressDrawn(DebugMenuLogic<int> l, Fake h, DebugMenuView drawn, string widget) =>
        l.Press(h, widget, DebugMenuDrawn.Of(drawn, widget));

    static void AssertRefusedUnchanged(DebugMenuLogic<int> l, Fake h, DebugMenuView drawn, string widget)
    {
        var before = l.BuildView();
        int calls = h.Calls.Count;
        int selected = l.SelectedCharacter, page = l.Page, confirm = l.ConfirmFresh;
        Assert.Equal(DebugMenuPressResult.Stale, PressDrawn(l, h, drawn, widget));
        Assert.Equal(before, l.BuildView());   // no status text, no refresh
        Assert.Equal(calls, h.Calls.Count);
        Assert.Equal(selected, l.SelectedCharacter);
        Assert.Equal(page, l.Page);
        Assert.Equal(confirm, l.ConfirmFresh);
    }

    [Fact]
    public void RowsCarryTheKeyOfTheirCharacter()
    {
        var (l, _) = Open(4, 7);
        var v = l.BuildView();
        Assert.Equal("id4", v.Rows[0].Key);
        Assert.Equal("id7", v.Rows[1].Key);
        Assert.Equal("", v.SelectedKey);
        Assert.Equal(new DebugMenuDrawn(true, "id7", true), DebugMenuDrawn.Of(v, N.Row(1)));
    }

    [Fact]
    public void ARowClickDrawnBeforeAPagingIsRefused()
    {
        var (l, h) = Open([.. Enumerable.Range(1, 10)]);
        var drawn = l.BuildView();
        Assert.True(l.Press(h, N.Next));   // page 2: row 0 now holds fighter 9
        AssertRefusedUnchanged(l, h, drawn, N.Row(0));
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, l.BuildView(), N.Row(0)));
        Assert.Equal(9, l.SelectedCharacter);
    }

    [Fact]
    public void ARowWhoseCharacterChangedAfterACollectIsRefusedEvenWithTheSameText()
    {
        var (l, h) = Open(1, 2);
        h.Names[1] = h.Names[3] = "Same";
        l.Press(h, N.FightersTab);
        var drawn = l.BuildView();
        h.Squad = [3, 2];
        l.Press(h, N.FightersTab);   // the refresh collects fighter 3 into row 0, reading the same text
        Assert.Equal(drawn.Rows[0].Text, l.BuildView().Rows[0].Text);
        AssertRefusedUnchanged(l, h, drawn, N.Row(0));
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.Row(1)));   // row 1 still holds fighter 2
        Assert.Equal(2, l.SelectedCharacter);
        Assert.Equal(T.ChangesSaved, l.BuildView().Status);
    }

    [Fact]
    public void AFighterActionCarriesTheSelectionTheWindowDrew()
    {
        var (l, h) = Open(1, 2);
        l.Press(h, N.Row(0));
        var drawn = l.BuildView();
        Assert.Equal("id1", drawn.SelectedKey);
        l.Press(h, N.Row(1));   // another row selected before the redraw
        AssertRefusedUnchanged(l, h, drawn, N.Actions[3]);
        AssertRefusedUnchanged(l, h, drawn, N.Actions[7]);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, l.BuildView(), N.Actions[3]));
        Assert.Equal(250f, h.Ratings[2]);
        Assert.False(h.Ratings.ContainsKey(1));
    }

    [Fact]
    public void FighterActionsWithNoSelectionDrawnAndNoneNowAreAccepted()
    {
        var (l, h) = Open(1);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, l.BuildView(), N.Actions[0]));
        Assert.Equal(T.SelectFirst, l.BuildView().Status);
    }

    [Fact]
    public void TownButtonsNeedTheFighterTabDrawn()
    {
        var (l, h) = Open(1);
        l.Press(h, N.DiagnosticsTab);
        var diagnostics = l.BuildView();
        l.Press(h, N.FightersTab);
        AssertRefusedUnchanged(l, h, diagnostics, N.Actions[9]);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, l.BuildView(), N.Actions[9]));
        Assert.Contains("trial", h.Calls);
    }

    [Fact]
    public void AStalePageLabelIsRefused()
    {
        var (l, h) = Open([.. Enumerable.Range(1, 20)]);
        var drawn = l.BuildView();
        Assert.Equal("Page 1 / 3", drawn.PageLabel);
        Assert.True(l.Press(h, N.Next));
        AssertRefusedUnchanged(l, h, drawn, N.Next);   // a second Next before the redraw: page 2 stays
        Assert.Equal(1, l.Page);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, l.BuildView(), N.Prev));
        Assert.Equal(0, l.Page);
    }

    [Fact]
    public void TabsCarryNothingExtra()
    {
        var (l, h) = Open(1);
        var drawn = l.BuildView();
        l.Press(h, N.DiagnosticsTab);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.FightersTab));
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.DiagnosticsTab));
        Assert.False(l.OnFightersTab);
    }

    [Fact]
    public void ADoubleFreshClickBeforeTheRedrawNeverResetsTownRecords()
    {
        var (l, h) = Open(1);
        h.DiagOk = true;
        l.Press(h, N.DiagnosticsTab);
        var drawn = l.BuildView();
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.Diagnostics[0]));
        Assert.Equal(T.ConfirmFresh150, l.BuildView().Status);
        AssertRefusedUnchanged(l, h, drawn, N.Diagnostics[0]);   // the second click carries the old label: no "preset True"
        Assert.DoesNotContain("preset True", h.Calls);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, l.BuildView(), N.Diagnostics[0]));   // the CONFIRM label was drawn
        Assert.Contains("preset True", h.Calls);
        Assert.Equal(T.Fresh150Started, l.BuildView().Status);

        // the configured fresh confirmation the same way
        var (l2, h2) = Open(1);
        h2.DiagOk = true;
        l2.Press(h2, N.DiagnosticsTab);
        var drawn2 = l2.BuildView();
        PressDrawn(l2, h2, drawn2, N.Diagnostics[2]);
        AssertRefusedUnchanged(l2, h2, drawn2, N.Diagnostics[2]);
        Assert.DoesNotContain("configured True", h2.Calls);
    }

    [Fact]
    public void AStalePauseLabelIsRefused()
    {
        var (l, h) = Open(1);
        h.DiagOk = true;
        h.Diag.Status = (int)TownDiagnosticData.RunStatus.Active;
        l.Press(h, N.DiagnosticsTab);
        var drawn = l.BuildView();
        Assert.Equal("Pause Diagnostic Selection", drawn.DiagnosticLabels[4]);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.Diagnostics[4]));
        h.Diag.Status = (int)TownDiagnosticData.RunStatus.Paused;   // the run paused
        h.Now = 5000;
        Assert.True(l.Tick(h));
        Assert.Equal("Resume Diagnostic Run", l.BuildView().DiagnosticLabels[4]);
        AssertRefusedUnchanged(l, h, drawn, N.Diagnostics[4]);
        Assert.DoesNotContain("resume", h.Calls);
    }

    [Fact]
    public void ADiagnosticActionWhoseEnabledStateChangedIsIgnored()
    {
        var (l, h) = Open(1);
        h.DiagOk = true;
        h.Diag.Status = (int)TownDiagnosticData.RunStatus.Active;
        l.Press(h, N.DiagnosticsTab);
        var drawn = l.BuildView();
        Assert.True(drawn.DiagnosticEnabled[5]);
        h.Diag.Pending.Active = true;
        h.Now = 5000;
        l.Tick(h);   // End is disabled now: MyGUI would send nothing
        var before = l.BuildView();
        int calls = h.Calls.Count;
        Assert.Equal(DebugMenuPressResult.Ignored, PressDrawn(l, h, drawn, N.Diagnostics[5]));
        Assert.Equal(before, l.BuildView());
        Assert.Equal(calls, h.Calls.Count);
        Assert.DoesNotContain("end", h.Calls);
    }

    [Fact]
    public void ARowWhoseTextChangedButStillHoldsTheSameCharacterIsAccepted()
    {
        var (l, h) = Open(1, 2);
        var drawn = l.BuildView();
        h.Ratings[2] = 400f;
        l.Press(h, N.FightersTab);   // the refresh shows fighter 2's new rating
        Assert.NotEqual(drawn.Rows[1].Text, l.BuildView().Rows[1].Text);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.Row(1)));
        Assert.Equal(2, l.SelectedCharacter);
    }

    [Fact]
    public void ACharacterSwapWithTheSameTextKeepsTheRevision()
    {
        var (l, h) = Open(1, 2);
        h.Names[1] = h.Names[3] = "Same";
        l.Press(h, N.FightersTab);
        int revision = l.BuildView().Revision;
        h.Squad = [3, 2];
        l.Press(h, N.FightersTab);
        Assert.Equal("id3", l.BuildView().Rows[0].Key);
        Assert.Equal(revision, l.BuildView().Revision);
    }

    [Fact]
    public void ADiagnosticsRefreshThatChangesOnlyTheSummaryKeepsGoodClicks()
    {
        var (l, h) = Open(1);
        h.DiagOk = true;
        l.Press(h, N.DiagnosticsTab);
        var drawn = l.BuildView();
        h.StatusText = "status text 2";
        h.Now = 5000;
        Assert.True(l.Tick(h));
        Assert.NotEqual(drawn.DiagnosticSummary, l.BuildView().DiagnosticSummary);
        Assert.Equal(DebugMenuPressResult.Done, PressDrawn(l, h, drawn, N.Diagnostics[8]));
        Assert.Contains("target 160", h.Calls);
        Assert.Equal(T.TargetChanged, l.BuildView().Status);
    }

    [Fact]
    public void AClosedWindowOrHiddenWidgetIsIgnoredNotStale()
    {
        var (l, h) = Open(1);
        var drawn = l.BuildView();
        Assert.Equal(DebugMenuPressResult.Ignored, PressDrawn(l, h, drawn, N.Row(5)));
        Assert.Equal(DebugMenuPressResult.Ignored, PressDrawn(l, h, drawn, "nothing"));
        l.Toggle(h);
        Assert.Equal(DebugMenuPressResult.Ignored, PressDrawn(l, h, drawn, N.Row(0)));
    }
}

public class DebugMenuKeyRuleTests
{
    [Fact]
    public void TakesOnlyTheSettingsKeyLeftUnusedByTheGame()
    {
        Assert.True(DebugMenuKeyRule.Takes("O", "O", false, false, true, false, false, false));
        Assert.False(DebugMenuKeyRule.Takes("Off", "Off", false, false, true, false, false, false));
        Assert.False(DebugMenuKeyRule.Takes("O", "N", false, false, true, false, false, false));
        Assert.False(DebugMenuKeyRule.Takes("V", "V", true, false, true, false, false, false));    // Ctrl held
        Assert.False(DebugMenuKeyRule.Takes("V", "V", false, true, true, false, false, false));    // the game took it
        Assert.False(DebugMenuKeyRule.Takes("V", "V", false, false, false, false, false, false));  // interface hidden
        Assert.False(DebugMenuKeyRule.Takes("V", "V", false, false, true, true, false, false));    // title
        Assert.False(DebugMenuKeyRule.Takes("V", "V", false, false, true, false, true, false));    // reserved / bound
        Assert.False(DebugMenuKeyRule.Takes("N", "N", false, false, true, false, false, true));    // typing
    }
}

public class DebugMenuKeyPressesTests
{
    [Fact]
    public void OffClearsThePressesWithoutToggling()
    {
        var p = new DebugMenuKeyPresses();
        p.Press();
        Assert.Equal(0, p.Take(settingOn: false));
        Assert.Equal(0, p.Pending);
        Assert.Equal(0, p.Take(settingOn: true));   // nothing kept for later
    }

    [Fact]
    public void EveryPressBeforeOnePollTogglesOnce()
    {
        var p = new DebugMenuKeyPresses();
        p.Press();
        p.Press();
        Assert.Equal(2, p.Take(settingOn: true));
        Assert.Equal(0, p.Take(settingOn: true));
    }

    [Fact]
    public void KeyNameIsTheSettingsLetterOrNull()
    {
        Assert.Equal("O", DebugMenuKeyRule.KeyName("O"));
        Assert.Equal("N", DebugMenuKeyRule.KeyName("N"));
        Assert.Equal("V", DebugMenuKeyRule.KeyName("V"));
        Assert.Null(DebugMenuKeyRule.KeyName("Off"));
        Assert.Null(DebugMenuKeyRule.KeyName("F8"));
        Assert.Null(DebugMenuKeyRule.KeyName("o"));
    }
}

public class DebugMenuViewTests
{
    [Fact]
    public void ViewsOfTheSameStateAreEqualSoTheSimPublishesNothingNew()
    {
        var l = new DebugMenuLogic<int>(0);
        Assert.Equal(l.BuildView(), l.BuildView());
        var a = l.BuildView();
        l.Close();   // already closed: no change
        Assert.Equal(a, l.BuildView());
    }

    [Fact]
    public void TickSaysWhetherItRefreshed()
    {
        var l = new DebugMenuLogic<int>(0);
        var h = new FakeTickHost();
        Assert.False(l.Tick(h));   // closed
        l.Toggle(h);
        Assert.False(l.Tick(h));   // fighters tab
        l.Press(h, DebugMenuNames.DiagnosticsTab);
        h.Now = 2000;
        Assert.True(l.Tick(h));
        h.Now = 2500;
        Assert.False(l.Tick(h));
        h.Now = 3000;
        Assert.True(l.Tick(h));
    }

    sealed class FakeTickHost : IDebugMenuHost<int>
    {
        public uint Now;
        public int None => 0;
        public bool IsNone(int c) => c == 0;
        public bool IsValid(int c) => c != 0;
        public bool IsDead(int c) => false;
        public string Name(int c) => "F";
        public string Key(int c) => "id" + c;
        public void CollectPlayerSquad(List<int> into) { }
        public bool HasBoundRegistry() => false;
        public bool PrisonersHaveRuntimeActivity() => false;
        public void CollectNearbyCagePrisoners(List<int> into) { }
        public IReadOnlyList<int> RosterPrisoners() => [];
        public bool IsRosterPrisoner(int c) => false;
        public float GetRating(int c) => 100f;
        public int GetMarks(int c) => 0;
        public bool SetRating(int c, float rating) => true;
        public bool SetMarks(int c, int marks) => true;
        public void TownBeginTrial() { }
        public bool TownIsScheduleEnabled() => true;
        public void TownSetScheduleEnabled(bool enabled) { }
        public void TownCancel() { }
        public string TownStatus() => "";
        public ProvingGrounds.Core.TownDiagnosticData.State Diagnostic() => new();
        public string DiagnosticStatusText() => "";
        public string DiagnosticValidationError() => "";
        public bool DiagnosticStartPreset(bool r) => false;
        public bool DiagnosticStartConfigured(bool r) => false;
        public bool DiagnosticPause() => false;
        public bool DiagnosticResume() => false;
        public bool DiagnosticEnd() => false;
        public bool DiagnosticSetTarget(int t) => false;
        public bool DiagnosticToggleModel(int m) => false;
        public bool DiagnosticCycleReducedInfluence() => false;
        public bool DiagnosticToggleCoverage(int c) => false;
        public bool DiagnosticCycleUnderdogPercent() => false;
        public bool DiagnosticToggleStyleTierBalance() => false;
        public uint NowMs => Now;
    }
}
