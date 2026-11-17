using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.5a: the Arena window's rules (Core/Glue/ArenaUILogic.cs = ArenaUI.cpp's decisions) over a fake host: the roster and its
/// availability reasons (the 2,500-unit rule), search, assignment, handlers, Balance, presets, CanStart / ValidationMessage with every refusal
/// and its exact text, Start / Stop call order and the published view.</summary>
public class ArenaUILogicTests
{
    static (ArenaUIFakeHost H, ArenaUILogic<int> L) Open(params (int Id, string Name, float Combat)[] squad)
    {
        var h = new ArenaUIFakeHost { Registry = ArenaUIFakeHost.RegistryAt(0f, 0f) };
        foreach (var (id, name, combat) in squad) h.Add(id, name, combat);
        var l = new ArenaUILogic<int>(-1);
        l.ShowFromRegistry(h, h.Registry);
        return (h, l);
    }

    static void Assign(ArenaUIFakeHost h, ArenaUILogic<int> l, int id) => l.OnPortraitRowDoubleClick(h, ArenaList.Roster, id);

    // ---------- open, roster, availability ----------

    [Fact]
    public void ShowFromRegistryBindsOpensAndListsTheSquad()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        Assert.Equal(["TownBookieClose", "RecruitmentAbandon", "TownArenaClose", "BindRegistry"], h.Calls);
        Assert.True(l.Visible);
        var v = l.BuildView(h);
        Assert.True(v.RequestOpen);
        Assert.Equal(1, v.RequestSerial);
        Assert.Equal([1, 2], v.Roster.Select(r => r.Id));
        Assert.Equal("2 fighters", v.RosterCount);
        Assert.Equal("Alpha", v.Roster[0].Caption);   // unassigned, available: the name alone
        Assert.Equal("Idle", v.Status);               // ShowFromRegistry's last write: ArenaIngress::GetStatus
        Assert.Equal("arena:2/0/0/0/t1/b0", v.ShapeKey);
    }

    [Fact]
    public void TownRegistryOpensTheTownWindowInstead()
    {
        var h = new ArenaUIFakeHost();
        var l = new ArenaUILogic<int>(-1);
        l.ShowFromRegistry(h, ArenaUIFakeHost.RegistryAt(0, 0, PgRecords.TownRegistry));
        Assert.Contains("TownArenaShow", h.Calls);
        Assert.DoesNotContain("BindRegistry", h.Calls);
        Assert.False(l.Visible);
    }

    [Fact]
    public void WhilePendingTheWindowOpensWithoutBindingAndShowsTheIngressStatus()
    {
        var h = new ArenaUIFakeHost { Pending = true, IngressStatusText = "Moving to formation" };
        h.Add(1, "Alpha");
        var l = new ArenaUILogic<int>(-1);
        l.ShowFromRegistry(h, ArenaUIFakeHost.RegistryAt(0, 0));
        Assert.DoesNotContain("BindRegistry", h.Calls);
        Assert.Equal("Moving to formation", l.BuildView(h).Status);
    }

    [Fact]
    public void OpenerIsPreviewed()
    {
        var h = new ArenaUIFakeHost { Registry = ArenaUIFakeHost.RegistryAt(0, 0), Opener = 2 };
        h.Add(1, "Alpha", 61.4f); h.Add(2, "Beta", 45.6f);
        var l = new ArenaUILogic<int>(-1);
        l.ShowFromRegistry(h, h.Registry);
        var v = l.BuildView(h);
        Assert.Equal(2, v.Preview);
        Assert.True(v.PreviewImageVisible);
        Assert.Equal("Beta", v.PreviewName);
        Assert.Equal("Unassigned", v.PreviewMeta);
        Assert.Equal("Combat 46   Toughness 40\nStrength 30   Dexterity 20", v.PreviewStats);
        Assert.True(v.Roster[1].Selected);
        Assert.False(v.Roster[0].Selected);
    }

    [Fact]
    public void AvailabilityReasonsAreJoinedInTheCppOrder()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        Assert.Equal("", l.FighterUnavailableReason(h, 1));
        h.Down.Add(1);
        Assert.Equal("Unconscious", l.FighterUnavailableReason(h, 1));
        h.Dead.Add(1);
        Assert.Equal("Dead", l.FighterUnavailableReason(h, 1));   // dead wins over unconscious (else if)
        h.Pos[1] = new Vector3(2500.1f, 0f, 0f);
        Assert.Equal("Dead, Too far away", l.FighterUnavailableReason(h, 1));
        h.Gone.Add(1);
        Assert.Equal("Invalid", l.FighterUnavailableReason(h, 1));
        Assert.Equal("Invalid", l.FighterUnavailableReason(h, -1));
    }

    [Fact]
    public void TooFarIsHorizontalAndStrictlyOver2500AndNeedsARegistry()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        h.Pos[1] = new Vector3(1500f, 9000f, 2000f);   // exactly 2500 on x/z (3-4-5), height ignored
        Assert.Equal("", l.FighterUnavailableReason(h, 1));
        h.Pos[1] = new Vector3(1500f, 0f, 2000.01f);
        Assert.Equal("Too far away", l.FighterUnavailableReason(h, 1));
        h.Registry = null;   // no bound Registry: never too far
        Assert.Equal("", l.FighterUnavailableReason(h, 1));
    }

    [Fact]
    public void PrisonerWithoutCageIsUnavailableUnlessReleasedForThisMatch()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        h.Names[9] = "Prisoner"; h.Pos[9] = Vector3.Zero; h.Prisoners.Add(9);
        Assert.Equal("No cage", l.FighterUnavailableReason(h, 9));
        h.Caged.Add(9);
        Assert.Equal("", l.FighterUnavailableReason(h, 9));
        Assert.Equal("Unassigned · Prisoner", l.FighterDetailLabel(h, 9));
    }

    [Fact]
    public void RosterRowCaptionsAndDetailLabels()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        Assign(h, l, 1);
        h.Pos[2] = new Vector3(5000f, 0f, 0f);
        l.Tick(h, 0.2f);   // first tick after the open only starts the clock (dt 0)
        l.Tick(h, 0.2f);   // the 0.20 s refresh: labels again
        var v = l.BuildView(h);
        Assert.Equal("Alpha\nTeam A", v.Roster[0].Caption);
        Assert.Equal("Beta\nUnavailable - Too far away", v.Roster[1].Caption);
        Assert.Equal("Alpha", v.TeamA[0].Caption);
        h.Down.Add(1);
        l.Tick(h, 0.2f);
        v = l.BuildView(h);
        Assert.Equal("Alpha\nTeam A - Unconscious", v.Roster[0].Caption);
        Assert.Equal("Alpha\nUnavailable - Unconscious", v.TeamA[0].Caption);
    }

    [Fact]
    public void HealthBarOnRosterRowsOnly()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        h.HealthOf[1] = new RosterStatus.Snapshot { LowestLimb = 0.6f, Blood = 0.8f, Recovery = 0.6f, Band = RosterStatusPolicy.Band.Orange };
        l.Tick(h, 0f); l.Tick(h, 0.2f);
        Assign(h, l, 1);
        var v = l.BuildView(h);
        Assert.True(v.Roster[0].HealthVisible);
        Assert.Equal(0.6f, v.Roster[0].HealthRatio);
        Assert.Equal(RosterStatusPolicy.Band.Orange, v.Roster[0].HealthBand);
        Assert.False(v.TeamA[0].HealthVisible);
    }

    [Fact]
    public void SearchFiltersByLowercaseSubstringAsciiOnly()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f), (3, "ÄLPHA", 30f));
        l.OnRosterSearchChanged(h, "LP");
        var v = l.BuildView(h);
        Assert.Equal([1, 3], v.Roster.Select(r => r.Id));
        Assert.Equal("2 of 3 fighters", v.RosterCount);
        Assert.Equal("LP", v.RosterQuery);
        l.OnRosterSearchChanged(h, "äl");   // tolower in the "C" locale leaves Ä: "ÄLPHA" -> "Älpha", no match
        Assert.Empty(l.BuildView(h).Roster);
        l.OnRosterSearchChanged(h, "zz");
        Assert.Empty(l.BuildView(h).Roster);
        Assert.Equal("0 of 3 fighters", l.BuildView(h).RosterCount);
        l.OnRosterSearchChanged(h, "");
        Assert.Equal("3 fighters", l.BuildView(h).RosterCount);
        Assert.Equal("älpha", ArenaUILogic<int>.LowerText("äLPHA"));
    }

    // ---------- assignment ----------

    [Fact]
    public void DoubleClickAssignsToTheDestinationAndDestButtonsMoveTheSelected()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        Assign(h, l, 1);
        Assert.Equal([1], l.TeamAList);
        l.OnPortraitRowClick(h, ArenaList.Roster, 2);
        l.OnDest(h, ArenaDest.TeamB);
        Assert.Equal([2], l.TeamBList);
        var v = l.BuildView(h);
        Assert.Equal("Double-click assigns to Team B", v.DestLabel);
        Assert.True(v.DestB.Selected);
        Assert.False(v.DestA.Selected);
        Assert.Equal("Team A | 1 fighter | ~50 combat", v.TeamALabel);
        Assert.Equal("Team B | 1 fighter | ~40 combat", v.TeamBLabel);
        Assert.Equal("Teams   ·   A 1 / B 1\nArena   ·   Team knockout", v.Summary);
        Assert.Equal("Ready", v.Status);
        Assert.True(v.Start.Enabled);
        Assert.Equal("arena:2/1/1/0/t1/b0", v.ShapeKey);
    }

    [Fact]
    public void ClicksOnAFighterNoLongerInThatListAreIgnored()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        l.OnPortraitRowClick(h, ArenaList.TeamA, 1);   // not in team A
        Assert.Equal(-1, l.PreviewChar);
        l.OnPortraitRowDoubleClick(h, ArenaList.Roster, 7);   // never shown
        Assert.Empty(l.TeamAList);
        l.OnPortraitRowDoubleClick(h, ArenaList.TeamA, 1);   // double click acts on roster rows only
        Assert.Empty(l.TeamAList);
    }

    [Fact]
    public void UnavailableFighterIsNotAssigned()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        h.Down.Add(1);
        Assign(h, l, 1);
        Assert.Empty(l.TeamAList);
    }

    [Fact]
    public void AssignmentRefusedWhileSparOrIngressRuns()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        h.Spar = true;
        Assign(h, l, 1);
        Assert.Empty(l.TeamAList);
        h.Spar = false; h.Pending = true;
        l.OnRowRelease(h, 1, ArenaDropTarget.TeamA);
        Assert.Empty(l.TeamAList);
    }

    [Fact]
    public void DropsAndRightClickRelease()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        l.OnRowRelease(h, 1, ArenaDropTarget.TeamB);
        Assert.Equal([1], l.TeamBList);
        Assert.Equal(ArenaDest.TeamB, l.Dest);   // a drop sets the destination (without UpdateDestHighlight, as the C++)
        Assert.Equal("Double-click assigns to Team A", l.BuildView(h).DestLabel);
        l.OnPortraitRowRelease(h, ArenaList.TeamB, 1, false, ArenaDropTarget.Roster, -1);   // team row dropped on the roster: removed
        Assert.Empty(l.TeamBList);
        Assign(h, l, 2);   // dest is B now
        Assert.Equal([2], l.TeamBList);
        l.OnPortraitRowRelease(h, ArenaList.TeamB, 2, true, ArenaDropTarget.None, -1);   // right release: removed
        Assert.Empty(l.TeamBList);
    }

    [Fact]
    public void RemoveButtonAndItsPendingText()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        Assign(h, l, 1);
        h.Pending = true;
        l.OnRemove(h);
        Assert.Equal("Walk-in in progress — cannot change roster", l.Status);
        Assert.Equal([1], l.TeamAList);
        h.Pending = false;
        l.OnRemove(h);
        Assert.Empty(l.TeamAList);
    }

    [Fact]
    public void SwitchingModesMovesTeamsToThePoolAndBackClearsIt()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        Assign(h, l, 1);
        l.OnPortraitRowClick(h, ArenaList.Roster, 2);
        l.OnDest(h, ArenaDest.TeamB);
        l.OnMode(h, ArenaUiMode.LastStanding);
        Assert.Equal([1, 2], l.PoolList);
        Assert.Empty(l.TeamAList);
        var v = l.BuildView(h);
        Assert.False(v.TeamMode);
        Assert.Equal("Add Selected Fighter", v.DestA.Caption);
        Assert.False(v.DestB.Visible);
        Assert.False(v.Swap.Visible);
        Assert.Equal("Double-click adds to the pool", v.DestLabel);
        Assert.Equal("Fighter pool  |  2 fighters", v.PoolLabel);
        Assert.Equal("Last man standing   ·   2 fighters\nArena   ·   Last conscious fighter wins", v.Summary);
        l.OnMode(h, ArenaUiMode.Teams1v1);
        Assert.Empty(l.PoolList);
        Assert.Equal("Teams 1v1   ·   A 0 / B 0\nArena   ·   Sequential · Winner stays", l.BuildView(h).Summary);
    }

    // ---------- handlers ----------

    [Fact]
    public void PrisonerGetsTheFirstFreeSquadHandlerAndADroppedHandlerReplacesIt()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f), (3, "Gamma", 30f));
        h.Names[9] = "Bandit"; h.Pos[9] = Vector3.Zero; h.Prisoners.Add(9); h.Caged.Add(9); h.CagePrisoners.Add(9);
        l.ShowFromRegistry(h, h.Registry);   // re-open: the cage prisoner joins the roster
        Assert.Contains(9, l.RosterCacheList);
        Assign(h, l, 1);
        Assign(h, l, 9);   // team A: Alpha, Bandit; handler = first squad candidate not assigned: Beta
        Assert.Equal(2, l.HandlerOf(h, 9));
        var v = l.BuildView(h);
        Assert.True(v.TeamA[1].HasHandler);
        Assert.Equal("Handler: Beta", v.TeamA[1].HandlerLabel);
        Assert.False(v.TeamA[0].HasHandler);
        l.OnPortraitRowRelease(h, ArenaList.Roster, 3, false, ArenaDropTarget.TeamA, 9);
        Assert.Equal(3, l.HandlerOf(h, 9));
        Assert.Equal(9, l.PreviewChar);
        Assert.Equal("Handler: Gamma", l.BuildView(h).TeamA[1].HandlerLabel);
    }

    [Fact]
    public void ANonCandidateDroppedOnAPrisonerRowFallsThroughToThatRowsList()
    {
        // C++ :1267-1294: FindAssignedPrisonerAtPoint first; a fighter who cannot be a handler falls through to the list tests, and the
        // prisoner row lies inside its list, so the drop assigns to that list
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        h.Names[9] = "Bandit"; h.Pos[9] = Vector3.Zero; h.Prisoners.Add(9); h.Caged.Add(9); h.CagePrisoners.Add(9);
        l.ShowFromRegistry(h, h.Registry);
        Assign(h, l, 1);                                       // Alpha -> team A
        l.OnPortraitRowClick(h, ArenaList.Roster, 9);
        l.OnDest(h, ArenaDest.TeamB);                          // Bandit -> team B (handler Beta)
        Assert.Equal(2, l.HandlerOf(h, 9));
        // Alpha is assigned: not a handler candidate; released over Bandit's row in team B -> moved to team B
        l.OnPortraitRowRelease(h, ArenaList.TeamA, 1, false, ArenaDropTarget.TeamB, 9);
        Assert.Empty(l.TeamAList);
        Assert.Equal([9, 1], l.TeamBList);
        Assert.Equal(2, l.HandlerOf(h, 9));                    // the handler is unchanged

        // Last Standing: another prisoner (never a handler candidate) released over a pool prisoner row joins the pool
        h.Names[8] = "Thug"; h.Pos[8] = Vector3.Zero; h.Prisoners.Add(8); h.Caged.Add(8); h.CagePrisoners.Add(8);
        l.ShowFromRegistry(h, h.Registry);
        l.OnMode(h, ArenaUiMode.LastStanding);
        Assert.Contains(9, l.PoolList);
        l.OnPortraitRowRelease(h, ArenaList.Roster, 8, false, ArenaDropTarget.Pool, 9);
        Assert.Contains(8, l.PoolList);
        Assert.NotEqual(8, l.HandlerOf(h, 9));
    }

    [Fact]
    public void HandlerShortageRefusesStartWithItsText()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        h.Names[9] = "Bandit"; h.Pos[9] = Vector3.Zero; h.Prisoners.Add(9); h.Caged.Add(9); h.CagePrisoners.Add(9);
        h.Names[8] = "Thug"; h.Pos[8] = Vector3.Zero; h.Prisoners.Add(8); h.Caged.Add(8); h.CagePrisoners.Add(8);
        l.ShowFromRegistry(h, h.Registry);
        Assign(h, l, 9);
        l.OnPortraitRowClick(h, ArenaList.Roster, 8);
        l.OnDest(h, ArenaDest.TeamB);
        Assert.False(l.CanStart(h));
        Assert.Equal("Need 2 handlers for 2 prisoners (1 available)", l.ValidationMessage(h));
    }

    // ---------- CanStart / ValidationMessage: every refusal and its text ----------

    [Fact]
    public void ValidationMessagesInOrder()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        Assert.Equal("Assign at least one fighter to each team.", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        Assign(h, l, 1);
        Assert.Equal("Assign at least one fighter to each team.", l.ValidationMessage(h));
        l.OnPortraitRowClick(h, ArenaList.Roster, 2);
        l.OnDest(h, ArenaDest.TeamB);
        Assert.Equal("Ready", l.ValidationMessage(h));
        Assert.True(l.CanStart(h));

        h.Pos[2] = new Vector3(0f, 0f, 3000f);
        Assert.Equal("Beta unavailable: Too far away", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        h.Pos[2] = Vector3.Zero;

        h.Site = false;
        Assert.Equal("No arena placed near registry", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        h.Location = ArenaLocationMode.Banner;
        Assert.Equal("No banner placed near registry", l.ValidationMessage(h));
        h.Site = true;

        h.Returning = true;
        Assert.Equal("Returning prisoners to their cages...", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        h.Returning = false;

        h.Spar = true;
        Assert.Equal("[LIVE] A spar is currently running.", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        h.Pending = true;
        Assert.Equal("[...] Fighters are moving to the match location.", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        h.Spar = h.Pending = false;

        h.Town = true;
        Assert.False(l.CanStart(h));
        h.Town = false; h.Booking = true;
        Assert.False(l.CanStart(h));
        h.Booking = false;
        Assert.True(l.CanStart(h));

        h.Gone.Add(2);
        Assert.Equal("Fighter unavailable: Invalid", l.ValidationMessage(h));
    }

    [Fact]
    public void PoolModeNeedsTwo()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        l.OnMode(h, ArenaUiMode.LastStanding);
        Assign(h, l, 1);
        Assert.Equal("Assign at least two fighters.", l.ValidationMessage(h));
        Assert.False(l.CanStart(h));
        Assign(h, l, 2);
        Assert.True(l.CanStart(h));
    }

    [Fact]
    public void StatusLineAndButtonsDuringTownEventSparAndIngress()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        h.Town = true;
        l.Tick(h, 0f); l.Tick(h, 0.2f);
        var v = l.BuildView(h);
        Assert.Equal("Arena reserved by a town event.", v.Status);
        Assert.False(v.Start.Visible);
        Assert.True(v.Stop.Visible && v.Stop.Enabled);
        Assert.Equal("Stop Fight", v.Stop.Caption);

        h.Town = false; h.Pending = true; h.IngressStatusText = "Countdown";
        l.Tick(h, 0.2f);
        v = l.BuildView(h);
        Assert.Equal("Countdown", v.Status);
        Assert.Equal("Cancel Match Start", v.Stop.Caption);
        Assert.False(v.ModeAvB.Enabled);
        Assert.False(v.LocArena.Enabled);
        Assert.False(v.Clear.Enabled);

        h.Pending = false; h.Spar = true;
        l.Tick(h, 0.2f);
        v = l.BuildView(h);
        Assert.Equal("Match started", v.Status);
        Assert.Equal("Stop Fight", v.Stop.Caption);
        Assert.False(v.Start.Visible);
    }

    [Fact]
    public void RefreshRunsEvery020SecondsWhileVisibleOnly()
    {
        // C++: elapsed starts at 0.20 and the first dt after a show is 0, so the first frame refreshes at once
        var (h, l) = Open((1, "Alpha", 50f));
        Assert.True(l.Tick(h, 0.05f));
        Assert.False(l.Tick(h, 0.1f));
        Assert.True(l.Tick(h, 0.1f));
        l.HidePanel();
        Assert.False(l.Tick(h, 1f));
    }

    // ---------- Balance ----------

    [Fact]
    public void BalanceBySkillSplitsTheC_Way()
    {
        // scores 60, 50, 40, 30: A gets 60 (0 <= 0), B 50, A? scoreA 60 > scoreB 50 -> B 40, then A 30 (B full)
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f), (4, "Four", 30f));
        l.BalanceTeams(h, false);
        Assert.Equal([1, 4], l.TeamAList);
        Assert.Equal([2, 3], l.TeamBList);
        var v = l.BuildView(h);
        Assert.Equal("Team A | 2 fighters | ~45 combat", v.TeamALabel);
        Assert.Equal("Team B | 2 fighters | ~45 combat", v.TeamBLabel);
        Assert.False(v.BalanceMenuOpen);
    }

    [Fact]
    public void BalanceOddCountGivesAOneMoreAndTiesGoByName()
    {
        var (h, l) = Open((1, "Zed", 50f), (2, "Amy", 50f), (3, "Bob", 50f));
        l.BalanceTeams(h, false);
        // sorted by name on a tie: Amy, Bob, Zed; targets A 2, B 1: Amy -> A (0 <= 0), Bob -> B (50 > 0), Zed -> A (B full)
        Assert.Equal([2, 1], l.TeamAList);
        Assert.Equal([3], l.TeamBList);
    }

    [Fact]
    public void BalanceWithASelectionUsesOnlyTheSelectedAndSkipsTheUnavailable()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f));
        Assign(h, l, 1);
        Assign(h, l, 3);
        l.BalanceTeams(h, false);
        Assert.Equal([1], l.TeamAList);
        Assert.Equal([3], l.TeamBList);
        h.Down.Add(3);
        l.BalanceTeams(h, false);
        Assert.Equal([1], l.TeamAList);
        Assert.Empty(l.TeamBList);
    }

    [Fact]
    public void BalanceByRatingAndInPoolModeSelectsAll()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f), (4, "Four", 30f));
        h.RatingOf[4] = 500f; h.RatingOf[3] = 400f;
        l.BalanceTeams(h, true);   // 4 (500), 3 (400), then 1/2 at 100 by name: One, Two
        Assert.Equal([4, 2], l.TeamAList);
        Assert.Equal([3, 1], l.TeamBList);
        l.OnMode(h, ArenaUiMode.LastStanding);
        l.OnClearTeams(h);
        l.BalanceTeams(h, false);
        Assert.Equal([1, 2, 3, 4], l.PoolList);
    }

    [Fact]
    public void BalanceMenuToggleAndCaptions()
    {
        var (h, l) = Open((1, "One", 60f));
        Assert.Equal("Balance", l.BuildView(h).Balance.Caption);
        l.OnBalanceMenu(h);
        var v = l.BuildView(h);
        Assert.Equal("Back", v.Balance.Caption);
        Assert.True(v.BalanceSkill.Visible && v.BalanceRating.Visible);
        Assert.False(v.Swap.Visible || v.Clear.Visible);
        h.Spar = true;
        l.OnBalanceMenu(h);
        Assert.True(l.BalanceMenuOpen);
    }

    [Fact]
    public void AbandonClosesTheBalanceMenuButLeavesItsLookStale()
    {
        // C++ AbandonWorldState sets g_balanceMenuOpen = false without UpdateBalanceMenuVisibility, and a reopen skips CreatePanel
        var (h, l) = Open((1, "One", 60f));
        l.OnBalanceMenu(h);
        l.AbandonWorldState(h);
        l.ShowFromRegistry(h, h.Registry);
        var v = l.BuildView(h);
        Assert.False(v.BalanceMenuOpen);
        Assert.Equal("Back", v.Balance.Caption);
        Assert.True(v.BalanceSkill.Visible && v.BalanceRating.Visible);
        Assert.False(v.Swap.Visible || v.Clear.Visible);
        l.OnBalanceMenu(h);                                    // the next click opens it (the look it already shows)
        Assert.True(l.BalanceMenuOpen);
        Assert.Equal("Back", l.BuildView(h).Balance.Caption);
        l.OnBalanceMenu(h);
        Assert.Equal("Balance", l.BuildView(h).Balance.Caption);
        Assert.True(l.BuildView(h).Clear.Visible);
    }

    [Fact]
    public void BalanceCandidateOrderIsScoreThenUtf8Name()
    {
        Assert.True(ArenaUILogic<int>.CompareBalanceCandidates(2f, "b", 1f, "a") < 0);
        Assert.True(ArenaUILogic<int>.CompareBalanceCandidates(1f, "a", 1f, "b") < 0);
        Assert.True(ArenaUILogic<int>.CompareNames("Z", "a") < 0);       // bytes: 'Z' < 'a'
        Assert.True(ArenaUILogic<int>.CompareNames("ab", "abc") < 0);
        Assert.True(ArenaUILogic<int>.CompareNames("�", "\U0001F600") < 0);   // UTF-8 EF.. < F0.. (UTF-16 would say the opposite)
    }

    // ---------- clear, swap, presets ----------

    [Fact]
    public void ClearAndSwap()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f));
        l.BalanceTeams(h, false);
        l.OnSwapTeams(h);
        Assert.Equal([2], l.TeamAList);
        Assert.Equal([1], l.TeamBList);
        l.OnClearTeams(h);
        Assert.Empty(l.TeamAList);
        Assert.Empty(l.TeamBList);
        Assert.False(l.BuildView(h).Clear.Enabled);
    }

    [Fact]
    public void PresetsRoundTripAndDropMissingFighters()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f));
        l.BalanceTeams(h, false);   // A [1,3]? scores 60,50,40: A 60, B 50, A 40
        l.OnLocation(h, ArenaLocationMode.SmallArena);
        l.OnMedicalProtocolToggle(h);
        l.OnPreset(h, 1, save: true);
        Assert.Equal("Preset 2 *", l.BuildView(h).Presets[1].Caption);
        Assert.Equal("Preset 1", l.BuildView(h).Presets[0].Caption);

        l.OnClearTeams(h);
        l.OnMode(h, ArenaUiMode.LastStanding);
        l.OnLocation(h, ArenaLocationMode.Arena);
        l.OnMedicalProtocolToggle(h);
        h.Gone.Add(3);   // Three disappears: RefreshAll's roster no longer has it
        l.ShowFromRegistry(h, h.Registry);
        l.OnPreset(h, 1, save: false);
        Assert.Equal(ArenaUiMode.AvB, l.Mode);
        Assert.Equal([1], l.TeamAList);
        Assert.Equal([2], l.TeamBList);
        Assert.Equal(ArenaLocationMode.SmallArena, h.Location);
        Assert.Equal(ArenaMedicalPolicy.Protocol.Bloodsport, h.Protocol);
        var v = l.BuildView(h);
        Assert.Equal("Medical: Bloodsport", v.Medical.Caption);
        Assert.Equal("No treatment until the match ends. No auto prisoner aid.", v.MedicalTip);
        Assert.True(v.LocSmall.Selected);
        Assert.Equal(-1, l.PreviewChar);

        l.OnPreset(h, 0, save: false);   // empty preset: nothing
        Assert.Equal([1], l.TeamAList);
        h.Pending = true;
        l.OnPreset(h, 2, save: true);
        Assert.False(l.PresetValid(2));
    }

    [Fact]
    public void MedicalToggleCaptionsAndRefusals()
    {
        var (h, l) = Open((1, "One", 60f));
        var v = l.BuildView(h);
        Assert.Equal("Medical: Ringside Aid", v.Medical.Caption);
        Assert.Equal("No aid for active fighters; OK once eliminated. Prisoners get stabilized.", v.MedicalTip);
        h.Spar = true;
        l.OnMedicalProtocolToggle(h);
        Assert.Equal(ArenaMedicalPolicy.Protocol.RingsideAid, l.MedicalProtocol);
    }

    [Fact]
    public void LocationHighlightIsWrittenOnlyWhenTheCppUpdatesIt()
    {
        var (h, l) = Open((1, "One", 60f));
        Assert.True(l.BuildView(h).LocArena.Selected);
        h.Location = ArenaLocationMode.Banner;   // e.g. BindRegistry of a Banner: RefreshAll does not update the highlight
        l.ShowFromRegistry(h, h.Registry);
        var v = l.BuildView(h);
        Assert.True(v.LocArena.Selected);
        Assert.Contains("Banner   ·", v.Summary);
        l.OnLocation(h, ArenaLocationMode.Banner);
        Assert.True(l.BuildView(h).LocBanner.Selected);
    }

    // ---------- Start / Stop ----------

    [Fact]
    public void StartCallsIngressBeginWithTeamsAndHides()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f), (4, "Four", 30f));
        l.BalanceTeams(h, false);
        l.OnMode(h, ArenaUiMode.Teams1v1);   // team to team: kept
        h.Calls.Clear();
        int serial = l.RequestSerial;
        l.OnStart(h);
        Assert.Equal(["ClearMatchState", "SetProtocol RingsideAid", "IngressBegin"], h.Calls);
        var b = h.Begun!.Value;
        Assert.Equal(MatchRules.MatchMode.Teams1v1, b.Mode);
        Assert.Equal([1, 4, 2, 3], b.Fighters);
        Assert.Equal([MatchRules.MatchTeam.TeamA, MatchRules.MatchTeam.TeamA, MatchRules.MatchTeam.TeamB, MatchRules.MatchTeam.TeamB], b.Teams);
        Assert.Empty(b.Escorts);
        Assert.False(l.Visible);
        Assert.Equal(serial + 1, l.RequestSerial);
    }

    [Fact]
    public void StartInPoolModeAndRefusedStartDoesNothing()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f));
        h.Calls.Clear();
        l.OnStart(h);   // nobody assigned: CanStart false, nothing called
        Assert.Empty(h.Calls);
        l.OnMode(h, ArenaUiMode.LastStanding);
        l.BalanceTeams(h, false);
        l.OnStart(h);
        Assert.Equal(MatchRules.MatchMode.LastStanding, h.Begun!.Value.Mode);
        Assert.Equal([MatchRules.MatchTeam.None, MatchRules.MatchTeam.None], h.Begun!.Value.Teams);
    }

    [Fact]
    public void FailedBeginKeepsTheWindowAndShowsValidation()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f));
        l.BalanceTeams(h, false);
        h.BeginOk = false;
        l.OnStart(h);
        Assert.True(l.Visible);
        Assert.Equal("Ready", l.Status);   // the ingress status is written, then RefreshStatusAndButtons writes the validation (as the C++)
    }

    [Fact]
    public void PrisonerStartPreparesWithOrderedHandlersOrReturnsOnFailure()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f));
        h.Names[9] = "Bandit"; h.Pos[9] = Vector3.Zero; h.Prisoners.Add(9); h.Caged.Add(9); h.CagePrisoners.Add(9);
        l.ShowFromRegistry(h, h.Registry);
        Assign(h, l, 1);
        l.OnPortraitRowClick(h, ArenaList.Roster, 9);
        l.OnDest(h, ArenaDest.TeamB);
        h.Calls.Clear();
        h.PrepareOk = false; h.MatchHasPrisoner = true; h.ReturnStatus = "Returning prisoners";
        l.OnStart(h);
        Assert.Equal(["PrepareMatch 9 / 2", "ReturnAllToCages"], h.Calls);
        Assert.Null(h.Begun);
        h.Calls.Clear();
        h.PrepareOk = true; h.MatchHasPrisoner = false; h.MatchHandlerList.Add(2);
        l.OnStart(h);
        Assert.Equal(["PrepareMatch 9 / 2", "SetProtocol RingsideAid", "IngressBegin"], h.Calls);
        Assert.Equal([2], h.Begun!.Value.Escorts);
    }

    [Fact]
    public void StopCancelsTownEventOrIngressThenStopsTheSpar()
    {
        var (h, l) = Open((1, "One", 60f));
        h.Town = true;
        h.Calls.Clear();
        l.OnStop(h);
        Assert.Equal(["TownCancel"], h.Calls);
        h.Calls.Clear();
        h.Pending = true;
        l.OnStop(h);
        Assert.Equal(["IngressCancel", "SparStopManual"], h.Calls);
        h.Calls.Clear();
        l.OnStop(h);
        Assert.Equal(["SparStopManual"], h.Calls);
    }

    // ---------- pruning, close, abandon ----------

    [Fact]
    public void AFighterWhoLeavesTheRosterDropsOutAtTheNextRefreshAll()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f));
        Assign(h, l, 2);
        l.OnPortraitRowClick(h, ArenaList.TeamA, 2);
        h.Gone.Add(2);
        l.Tick(h, 0f); l.Tick(h, 0.2f);   // the idle refresh keeps the row (the C++ keeps its pointer), now "Invalid"
        var v = l.BuildView(h);
        Assert.Equal("Two\nUnavailable - Invalid", v.TeamA[0].Caption);
        Assert.Equal("Two", v.TeamA[0].Name);   // the last name seen
        l.ShowFromRegistry(h, h.Registry);   // RefreshAll prunes it
        Assert.Empty(l.TeamAList);
        Assert.Equal(-1, l.PreviewChar);
        Assert.Equal("No fighter selected", l.BuildView(h).PreviewName);
    }

    [Fact]
    public void ClosedFromTheWindowOnlyForItsSerial()
    {
        var (h, l) = Open((1, "One", 60f));
        Assert.False(l.OnClosedFromWindow(l.RequestSerial + 1));
        Assert.True(l.OnClosedFromWindow(l.RequestSerial));
        Assert.False(l.Visible);
        Assert.False(l.OnClosedFromWindow(l.RequestSerial));
    }

    [Fact]
    public void RecruitHidesAndOpensRecruitment()
    {
        var (h, l) = Open((1, "One", 60f));
        l.OnRecruitPrisoners(h);
        Assert.False(l.Visible);
        Assert.Contains("ShowRecruitment", h.Calls);
    }

    [Fact]
    public void AbandonWorldStateClearsAndHides()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f));
        l.BalanceTeams(h, false);
        l.OnPreset(h, 0, save: true);
        l.OnRosterSearchChanged(h, "o");
        l.AbandonWorldState(h);
        Assert.Contains("OtherWindowsAbandon", h.Calls);
        Assert.Empty(l.TeamAList);
        Assert.False(l.PresetValid(0));
        Assert.False(l.Visible);
        var v = l.BuildView(h);
        Assert.Equal("Idle", v.Status);
        Assert.False(v.PreviewImageVisible);
        Assert.Empty(v.Roster);
        Assert.Equal("", v.RosterQuery);
    }

    [Fact]
    public void ShapeKeyFollowsRowCountsTeamModeAndBalanceMenu()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f));
        l.BalanceTeams(h, false);
        Assert.Equal("arena:3/2/1/0/t1/b0", l.BuildView(h).ShapeKey);
        l.OnBalanceMenu(h);
        Assert.Equal("arena:3/2/1/0/t1/b1", l.BuildView(h).ShapeKey);
        l.OnBalanceMenu(h);
        l.OnMode(h, ArenaUiMode.LastStanding);
        Assert.Equal("arena:3/0/0/3/t0/b0", l.BuildView(h).ShapeKey);
        Assert.Equal(ArenaUiView<int>.ShapeOf(3, 0, 0, 3, false, false), l.BuildView(h).ShapeKey);
    }

    [Fact]
    public void HealthBandOfEachRosterRow()
    {
        var (h, l) = Open((1, "One", 60f), (2, "Two", 50f), (3, "Three", 40f));
        foreach (var (id, rec) in new[] { (1, 0.95f), (2, 0.5f), (3, 0.2f) })
        {
            RosterStatus.Read(true, true, 1f, rec, [new RosterStatus.Part(rec, 0f, 1f)], out var snap, out _);
            h.HealthOf[id] = snap;
        }
        l.Tick(h, 0f);
        var v = l.BuildView(h);
        Assert.Equal([RosterStatusPolicy.Band.Green, RosterStatusPolicy.Band.Orange, RosterStatusPolicy.Band.Red], v.Roster.Select(r => r.HealthBand));
        Assert.Equal([0.95f, 0.5f, 0.2f], v.Roster.Select(r => r.HealthRatio));
    }

    [Fact]
    public void UnavailableFighterInTeamAAndInThePool()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f), (3, "Gamma", 30f));
        Assign(h, l, 1);
        l.OnPortraitRowClick(h, ArenaList.Roster, 2);
        l.OnDest(h, ArenaDest.TeamB);
        h.Down.Add(1);
        Assert.False(l.CanStart(h));
        Assert.Equal("Alpha unavailable: Unconscious", l.ValidationMessage(h));
        l.Tick(h, 0f);
        var v = l.BuildView(h);
        Assert.Equal("Alpha\nUnavailable - Unconscious", v.TeamA[0].Caption);
        Assert.False(v.Start.Enabled);
        Assert.Equal("Alpha unavailable: Unconscious", v.Status);

        h.Down.Remove(1);
        l.OnMode(h, ArenaUiMode.LastStanding);
        Assign(h, l, 3);
        h.Dead.Add(3);
        Assert.False(l.CanStart(h));
        Assert.Equal("Gamma unavailable: Dead", l.ValidationMessage(h));
        l.Tick(h, 0.2f);
        Assert.Equal("Gamma\nUnavailable - Dead", l.BuildView(h).Pool.Single(r => r.Id == 3).Caption);
    }

    [Fact]
    public void CommandsAreAcceptedOnlyWhileTheWindowIsOpenExceptClose()
    {
        var l0 = new ArenaUILogic<int>(-1);
        Assert.False(l0.AcceptsCommand(false));   // never opened
        Assert.True(l0.AcceptsCommand(true));
        var (h, l) = Open((1, "Alpha", 50f));
        Assert.True(l.AcceptsCommand(false));
        l.HidePanel();
        Assert.False(l.AcceptsCommand(false));
        Assert.True(l.AcceptsCommand(true));
        _ = h;
    }

    [Fact]
    public void ARepeatShowOrHideBumpsNoRequestSerial()
    {
        var (h, l) = Open((1, "Alpha", 50f));
        int serial = l.RequestSerial;
        l.ShowFromRegistry(h, h.Registry);   // already open
        Assert.Equal(serial, l.RequestSerial);
        l.HidePanel();
        Assert.Equal(serial + 1, l.RequestSerial);
        l.OnRecruitPrisoners(h);             // already hidden
        Assert.Equal(serial + 1, l.RequestSerial);
        l.ShowFromRegistry(h, h.Registry);
        Assert.Equal(serial + 2, l.RequestSerial);
    }

    [Fact]
    public void RowsCarryTheDragFlags()
    {
        var (h, l) = Open((1, "Alpha", 50f), (2, "Beta", 40f));
        h.Names[9] = "Bandit"; h.Pos[9] = Vector3.Zero; h.Prisoners.Add(9); h.Caged.Add(9); h.CagePrisoners.Add(9);
        l.ShowFromRegistry(h, h.Registry);
        Assign(h, l, 1);
        Assign(h, l, 9);
        h.Down.Add(2);
        l.Tick(h, 0f);
        var v = l.BuildView(h);
        var alpha = v.Roster.Single(r => r.Id == 1);
        var beta = v.Roster.Single(r => r.Id == 2);
        var bandit = v.TeamA.Single(r => r.Id == 9);
        Assert.True(alpha.DragValid);
        Assert.False(alpha.HandlerCandidate);   // assigned
        Assert.False(beta.DragValid);           // unconscious
        Assert.False(beta.HandlerCandidate);
        Assert.True(bandit.Prisoner);           // a team row of a prisoner: the C++ "assigned prisoner row"
        Assert.False(bandit.HandlerCandidate);
        Assert.False(alpha.Prisoner);
        h.Down.Remove(2);
        h.Spar = true;
        l.Tick(h, 0.2f);
        v = l.BuildView(h);
        Assert.True(v.Roster.Single(r => r.Id == 2).HandlerCandidate);
        Assert.False(v.Roster.Single(r => r.Id == 2).DragValid);   // a spar runs
        Assert.Equal("\nRelease to assign", ArenaUiTexts.HeadingDropOk);
        Assert.Equal("\nCannot assign", ArenaUiTexts.HeadingDropNo);
    }

    [Fact]
    public void ViewEqualityIsByValue()
    {
        var (h, l) = Open((1, "One", 60f));
        Assert.Equal(l.BuildView(h), l.BuildView(h));
        var before = l.BuildView(h);
        Assign(h, l, 1);
        Assert.NotEqual(before, l.BuildView(h));
    }

    [Fact]
    public void SiteCheckCacheAsksAgainOnChangeTimeoutAndForget()
    {
        var c = new SiteCheckCache();
        int calls = 0;
        bool Compute() { calls++; return true; }
        var reg = new ArenaBuildingRef(ArenaBuildingSource.Player, "7", "45");
        c.Get(ArenaLocationMode.Arena, reg, 1000, Compute);
        c.Get(ArenaLocationMode.Arena, reg, 1999, Compute);
        Assert.Equal(1, calls);
        c.Get(ArenaLocationMode.Arena, reg, 2000, Compute);
        Assert.Equal(2, calls);
        c.Get(ArenaLocationMode.Banner, reg, 2001, Compute);
        Assert.Equal(3, calls);
        c.Get(ArenaLocationMode.Banner, null, 2002, Compute);
        Assert.Equal(4, calls);
        c.Forget();
        c.Get(ArenaLocationMode.Banner, null, 2003, Compute);
        Assert.Equal(5, calls);
    }
}

static class ArenaUILogicTestExtensions
{
    /// <summary>A roster row of <paramref name="id"/> released on <paramref name="target"/> (a drop).</summary>
    public static void OnRowRelease(this ArenaUILogic<int> l, ArenaUIFakeHost h, int id, ArenaDropTarget target) =>
        l.OnPortraitRowRelease(h, ArenaList.Roster, id, false, target, -1);
}
