using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The recruitment window's host over <see cref="RecruitFake"/>: the roster is the fake store's roster list, the recruit the real
/// PrisonerRecruitmentGlue over the same fake (so a recruit pays from the real ledger).</summary>
sealed class RecruitUiFake(RecruitFake f) : IPrisonerRecruitmentUIHost<int>
{
    public readonly RecruitFake F = f;
    public readonly PrisonerRecruitmentGlue<int> Glue = new(f);
    public bool RegistryValid = true, Throw;
    public int ArenaShows;
    public readonly List<string> Debugs = [], Errors = [];
    public Action? OnArenaShow;

    public IReadOnlyList<int> RosterPrisoners() => Throw ? throw new InvalidOperationException("roster") : F.Store.RosterList.ToList();
    public bool Valid(int c) => F.Valid(c);
    public bool IsDead(int c) => F.IsDead(c);
    public bool IsUnconscious(int c) => F.IsUnconscious(c);
    public bool IsRosterPrisoner(int c) => F.IsRosterPrisoner(c);
    public bool IngressPending() => F.Ingress;
    public bool SparActive() => F.Spar;
    public bool PrisonerRuntimeActivity() => F.Runtime;
    public bool PlayerReady() => F.Player;
    public bool RecruitAvailable() => F.Available;
    public int GetMarks(int c) => F.GetMarks(c);
    public float AverageCombatStat(int c) => Glue.AverageCombatStat(c);
    public int RequiredMarks(int c) => Glue.RequiredMarks(c);
    public string Name(int c) => F.Name(c);
    public bool Recruit(int c, out string status) => Glue.Recruit(c, out status);
    public bool BoundRegistryValid() => RegistryValid;
    public void ShowArenaFromBoundRegistry() { ArenaShows++; OnArenaShow?.Invoke(); }
    public void Debug(string line) => Debugs.Add(line);
    public void Error(string line) => Errors.Add(line);
}

/// <summary>PrisonerRecruitmentUI's rules half (Core/Glue/PrisonerRecruitmentUILogic.cs, task 6.4a): the rows, the selection, the captions and
/// status lines, the button and its "why not", the recruit command with its re-check, Show / Close / Tick / AbandonWorldState.</summary>
public class PrisonerRecruitmentUILogicTests
{
    const int B = RecruitFake.Bandit, C2 = 4;

    static (RecruitUiFake H, PrisonerRecruitmentUILogic<int> L) Open(int marks = 130, Action<RecruitFake>? set = null)
    {
        var f = new RecruitFake();
        Assert.True(f.S.SetMarks(B, marks));
        set?.Invoke(f);
        var h = new RecruitUiFake(f);
        var l = new PrisonerRecruitmentUILogic<int>();
        l.Show(h);
        return (h, l);
    }

    static void AddPrisoner(RecruitFake f, int id, string name, float skill)
    {
        f.Store.Keys[id] = "k" + id; f.Store.Names[id] = name; f.Skill[id] = skill;
        f.Store.Roster.Add(id); f.Store.RosterList.Add(id);
    }

    [Fact]   // Show: the roster prisoners as rows, the first selected, the details, "Grant Freedom - N Marks", the idle status line, an open request
    public void Show_lists_the_roster_and_selects_the_first()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        var v = l.BuildView();
        Assert.True(v.RequestOpen);
        Assert.Equal(1, v.RequestSerial);
        Assert.Equal(1, v.Shows);
        Assert.Equal("Proving Grounds - Prisoner Recruitment", v.Caption);
        Assert.Equal("CONTRACTS OF FREEDOM  |  Prisoners spend their own Arena Marks", v.Subtitle);
        Assert.Equal("PRISONERS", v.RosterHeading);
        Assert.Equal("Back to Arena", v.CloseCaption);
        var row = Assert.Single(v.Rows);
        Assert.Equal(new PrisonerRecruitmentRowView<int>(B, 0, "Dust Bandit", "130 Marks", true), row);
        Assert.True(v.HasSelected);
        Assert.Equal(B, v.Selected);
        Assert.True(v.PortraitVisible);
        Assert.Equal("Dust Bandit", v.SelectedName);
        Assert.Equal("Combat 40  |  130 / 100 Arena Marks", v.Stats);
        Assert.Equal("Grant Freedom - 100 Marks", v.ActionCaption);
        Assert.True(v.ActionEnabled);
        Assert.Equal("This prisoner can earn freedom now.", v.Explanation);
        Assert.Equal("Select a prisoner to review their freedom contract.", v.Status);
        Assert.Equal(100, v.ShownPrice);
        Assert.Contains("Proving Grounds: prisoner recruitment UI shown", h.Debugs);
    }

    [Fact]   // "Combat %.0f": msvcr100 rounding of the float (half away from zero), not .NET's
    public void Stats_line_rounds_like_msvcr100()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(set: f => f.Skill[B] = 40.5f);
        Assert.Equal("Combat 41  |  130 / 105 Arena Marks", l.BuildView().Stats);
    }

    [Fact]   // no prisoner: no selection, the C++ captions, the button "No Prisoner Selected" disabled
    public void No_prisoner()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(set: f => { f.Store.RosterList.Clear(); f.Store.Roster.Clear(); });
        var v = l.BuildView();
        Assert.Empty(v.Rows);
        Assert.False(v.HasSelected);
        Assert.False(v.PortraitVisible);
        Assert.Equal("No prisoner selected", v.SelectedName);
        Assert.Equal("No recruitment contract available.", v.Stats);
        Assert.Equal("No Prisoner Selected", v.ActionCaption);
        Assert.False(v.ActionEnabled);
        Assert.Equal("No nearby living prisoners are available.", v.Explanation);
        Assert.Equal(0, v.ShownPrice);
    }

    [Fact]   // RefreshCandidates: invalid, dead and repeated roster entries are skipped
    public void Candidates_skip_invalid_dead_and_repeats()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(set: f =>
        {
            AddPrisoner(f, C2, "Hungry Bandit", 20f);
            AddPrisoner(f, 5, "Dead Bandit", 20f); f.Store.Dead.Add(5);
            AddPrisoner(f, 6, "Gone Bandit", 20f); f.Store.Invalid.Add(6);
            f.Store.RosterList.Add(B);
        });
        Assert.Equal([B, C2], l.BuildView().Rows.Select(r => r.Character));
        Assert.Equal([0, 1], l.BuildView().Rows.Select(r => r.Index));
    }

    [Fact]   // the button's "why not", in the Presentation order, from the window's own reads
    public void Button_states()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        (string, bool, string) Of(Action<RecruitFake> set, int marks = 130)
        {
            var (_, l) = Open(marks, set);
            var v = l.BuildView();
            return (v.ActionCaption, v.ActionEnabled, v.Explanation);
        }
        Assert.Equal(("Arena Operation Active", false, "Finish the current arena operation before recruiting."), Of(f => f.Spar = true));
        Assert.Equal(("Arena Operation Active", false, "Finish the current arena operation before recruiting."), Of(f => f.Ingress = true));
        Assert.Equal(("Arena Operation Active", false, "Finish the current arena operation before recruiting."), Of(f => f.Runtime = true));
        Assert.Equal(("Prisoner Unconscious", false, "The prisoner must be conscious to accept freedom."), Of(f => f.Unconscious.Add(B)));
        Assert.Equal(("Player Faction Unavailable", false, "The player faction is not ready."), Of(f => f.Player = false));
        Assert.Equal(("More Marks Required", false, "This prisoner needs 1 more Arena Mark."), Of(_ => { }, 99));
        Assert.Equal(("More Marks Required", false, "This prisoner needs 100 more Arena Marks."), Of(_ => { }, 0));
        Assert.Equal(("Recruitment Unavailable", false, PrisonerRecruitmentGlue<int>.UnavailableText), Of(f => f.Available = false));
        // the part off shows only where the C++ rules would enable the button: a Marks deficit is still shown as the C++ shows it
        Assert.Equal(("More Marks Required", false, "This prisoner needs 1 more Arena Mark."), Of(f => f.Available = false, 99));
    }

    [Fact]   // a selected prisoner that left the roster but is still a candidate this refresh: "Prisoner Unavailable"
    public void Not_a_roster_prisoner()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        h.F.Store.Roster.Remove(B);   // the roster list still lists it (a stale list): IsRosterPrisoner says no
        l.Tick(h);
        var v = l.BuildView();
        Assert.Equal(("Prisoner Unavailable", false, "This fighter is not an available arena prisoner."), (v.ActionCaption, v.ActionEnabled, v.Explanation));
    }

    [Fact]   // a row click selects that prisoner and clears the message; a click on no row is ignored
    public void Row_click()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(set: f => AddPrisoner(f, C2, "Hungry Bandit", 20f));
        Assert.Equal(B, l.BuildView().Selected);
        Assert.True(l.OnRowClicked(h, C2));
        var v = l.BuildView();
        Assert.Equal(C2, v.Selected);
        Assert.Equal([false, true], v.Rows.Select(r => r.Selected));
        Assert.Equal("More Marks Required", v.ActionCaption);   // its own Marks: none yet
        Assert.Equal("This prisoner needs 50 more Arena Marks.", v.Explanation);
        Assert.Equal("Combat 20  |  0 / 50 Arena Marks", v.Stats);
        Assert.Equal(50, v.ShownPrice);
        Assert.False(l.OnRowClicked(h, 77));
        Assert.Equal(C2, l.BuildView().Selected);
    }

    [Fact]   // Grant Freedom: the recruit runs, its status is the line, the prisoner leaves the rows and the selection moves on
    public void Grant_freedom()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(set: f => AddPrisoner(f, C2, "Hungry Bandit", 20f));
        int builds = l.BuildView().RowBuilds;
        Assert.True(l.OnRecruitCommand(h, B, 100, 7));
        var v = l.BuildView();
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", v.Status);
        Assert.Equal(7, v.LastSeq);
        Assert.False(v.LastOfferChanged);
        Assert.Equal([C2], v.Rows.Select(r => r.Character));
        Assert.Equal(builds + 1, v.RowBuilds);
        Assert.Equal(C2, v.Selected);
        Assert.Equal(30, h.F.S.GetMarks(B));
        Assert.Contains(B, h.F.Joined);
        // the message stays through the refreshes (Tick preserves it) until a row click or Show clears it
        l.Tick(h);
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", l.BuildView().Status);
        l.OnRowClicked(h, C2);
        Assert.Equal("Select a prisoner to review their freedom contract.", l.BuildView().Status);
    }

    [Fact]   // a refused recruit (a stale click on a disabled button) shows the recruit's own C++ text
    public void Refused_recruit_status()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(marks: 99);
        Assert.True(l.OnRecruitCommand(h, B, 100, 1));
        Assert.Equal("This prisoner has not earned enough Arena Marks.", l.BuildView().Status);
        Assert.Equal(99, h.F.S.GetMarks(B));
        Assert.Single(l.BuildView().Rows);
    }

    [Fact]   // the offer changed since it was shown: another price or another prisoner selected -> not run, the line kept, LastOfferChanged
    public void Offer_changed_since_shown()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(set: f => AddPrisoner(f, C2, "Hungry Bandit", 20f));
        Assert.False(l.OnRecruitCommand(h, B, 95, 3));   // the price is 100 now
        var v = l.BuildView();
        Assert.True(v.LastOfferChanged);
        Assert.Equal(3, v.LastSeq);
        Assert.Equal("Select a prisoner to review their freedom contract.", v.Status);
        Assert.Empty(h.F.Joined);
        Assert.Equal(130, h.F.S.GetMarks(B));
        Assert.False(l.OnRecruitCommand(h, C2, 50, 4));   // C2 is not the selection
        Assert.True(l.BuildView().LastOfferChanged);
        Assert.Empty(h.F.Joined);
        Assert.Equal(2, h.Debugs.Count(d => d.StartsWith("Proving Grounds: prisoner recruitment skipped")));
    }

    [Fact]   // review fix: a command whose sequence is not above LastSeq is dropped (not run, not recorded)
    public void Old_sequence_is_dropped()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(marks: 500);
        Assert.False(l.OnRecruitCommand(h, B, 95, 5));   // recorded (offer changed)
        Assert.False(l.OnRecruitCommand(h, B, 100, 5));  // same sequence: dropped
        Assert.False(l.OnRecruitCommand(h, B, 100, 4));  // older: dropped
        Assert.Empty(h.F.Joined);
        Assert.Equal(5, l.BuildView().LastSeq);
        Assert.True(l.OnRecruitCommand(h, B, 100, 6));
        Assert.Contains(B, h.F.Joined);
    }

    [Fact]   // a command while the window is closed is recorded (the wait ends) but not run
    public void Command_while_closed()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        l.Close(h);
        Assert.False(l.OnRecruitCommand(h, B, 100, 9));
        Assert.Equal(9, l.BuildView().LastSeq);
        Assert.Empty(h.F.Joined);
    }

    [Fact]   // Close: hidden (a close request), then the Arena window back from the bound Registry when it is valid
    public void Close_reopens_the_arena()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        l.Close(h);
        Assert.False(l.IsVisible);
        Assert.Equal(2, l.BuildView().RequestSerial);
        Assert.False(l.BuildView().RequestOpen);
        Assert.Equal(1, h.ArenaShows);
        h.RegistryValid = false;
        l.Show(h);
        l.Close(h);
        Assert.Equal(1, h.ArenaShows);   // no valid registry: nothing shown
        Assert.Equal(4, l.BuildView().RequestSerial);
    }

    [Fact]   // the C++ re-entry: ArenaUI::ShowFromRegistry calls PrisonerRecruitmentUI::AbandonWorldState while Close runs; one close request
    public void Close_reentry_from_ShowFromRegistry()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        h.OnArenaShow = () => l.AbandonWorldState();
        l.Close(h);
        var v = l.BuildView();
        Assert.Equal(2, v.RequestSerial);
        Assert.False(v.HasSelected);
        Assert.Empty(v.Rows);
        l.Show(h);   // the next Show lists the prisoner again
        Assert.Single(l.BuildView().Rows);
        Assert.Equal(B, l.BuildView().Selected);
    }

    [Fact]   // the window's own close (Escape, the title bar) under the current request: hidden, the arena back, no second request
    public void Closed_from_the_window()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        Assert.False(l.OnClosedFromWindow(h, 0));   // an older request
        Assert.True(l.OnClosedFromWindow(h, 1));
        Assert.False(l.IsVisible);
        Assert.Equal(1, l.BuildView().RequestSerial);
        Assert.Equal(1, h.ArenaShows);
        Assert.False(l.OnClosedFromWindow(h, 1));
    }

    [Fact]   // Tick: only while visible; it follows the roster and the Marks
    public void Tick_refreshes_while_visible()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        h.F.S.SetMarks(B, 20);
        l.Tick(h);
        Assert.Equal("20 Marks", l.BuildView().Rows[0].Marks);
        Assert.Equal(("More Marks Required", "This prisoner needs 80 more Arena Marks."), (l.BuildView().ActionCaption, l.BuildView().Explanation));
        l.Close(h);
        h.F.S.SetMarks(B, 500);
        l.Tick(h);
        Assert.Equal("20 Marks", l.BuildView().Rows[0].Marks);
    }

    [Fact]   // a refresh that throws: the C++ error line, the window abandoned (a close request; the next Show makes it again)
    public void Refresh_failure_abandons_the_window()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        h.Throw = true;
        l.Tick(h);
        Assert.Equal(["Proving Grounds: prisoner recruitment UI refresh failed"], h.Errors);
        Assert.False(l.IsVisible);
        Assert.Equal(2, l.BuildView().RequestSerial);
        h.Throw = false;
        l.Show(h);
        Assert.True(l.IsVisible);
        Assert.Single(l.BuildView().Rows);
    }

    [Fact]   // AbandonWorldState: hidden, rows / selection / message gone
    public void AbandonWorldState_clears()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(marks: 99);
        l.OnRecruitCommand(h, B, 100, 1);
        l.AbandonWorldState();
        Assert.False(l.IsVisible);
        Assert.Equal("", l.Message);
        Assert.False(l.HasSelected);
        Assert.Empty(l.BuildView().Rows);
        l.AbandonWorldState();
        Assert.Equal(2, l.BuildView().RequestSerial);   // one close request
    }

    [Fact]   // Show clears the message and bumps Shows; a second Show while open sends no new open request
    public void Show_again()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open(marks: 99);
        l.OnRecruitCommand(h, B, 100, 1);
        l.Show(h);
        var v = l.BuildView();
        Assert.Equal("Select a prisoner to review their freedom contract.", v.Status);
        Assert.Equal(2, v.Shows);
        Assert.Equal(1, v.RequestSerial);
    }

    [Fact]   // the view's value equality: an unchanged refresh publishes nothing new
    public void View_equality()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var (h, l) = Open();
        var a = l.BuildView();
        l.Tick(h);
        Assert.Equal(a, l.BuildView());
    }
}
