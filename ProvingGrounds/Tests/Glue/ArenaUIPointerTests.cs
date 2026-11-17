using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.5b: the Arena window's row pointer events (MyGUI's release / click / double click on a row button, OnPortraitRowDrag
/// :1221-1238, OnPortraitRowRelease :1240-1300, RefreshPointerHighlights :1201-1219) and the drag look (ApplyDragHighlights :1171-1191).</summary>
public class ArenaUIPointerTests
{
    const int None = 0;
    static ArenaRowPointer<int> P() => new();

    [Fact]
    public void PlainClickIsReleaseThenClick()
    {
        var p = P();
        p.LeftPress(ArenaList.Roster, 2, 7, 10, 10);
        var a = p.LeftRelease((ArenaList.Roster, 2), ArenaDropTarget.Roster, None, None, 1.0);
        Assert.Equal([new(ArenaRowActionKind.Release, ArenaList.Roster, 7, false, ArenaDropTarget.Roster, None),
            new(ArenaRowActionKind.Click, ArenaList.Roster, 7, false, ArenaDropTarget.None, None)], a);
        Assert.False(p.DragActive);
    }

    [Fact]
    public void SecondClickWithinAQuarterSecondAddsTheDoubleClick()
    {
        var p = P();
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 10.0);
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        var a = p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 10.24);
        Assert.Equal([ArenaRowActionKind.Release, ArenaRowActionKind.Click, ArenaRowActionKind.DoubleClick], a.Select(x => x.Kind));
        // MyGUI leaves the timer running after a double click (injectMouseRelease :310-315): a third click inside the time doubles again
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Equal(3, p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 10.249).Count);
    }

    [Fact]
    public void SlowSecondClickIsNoDoubleClick()
    {
        var p = P();
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0);
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Equal(2, p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0.25).Count);
    }

    [Fact]
    public void ChangingTheWidgetUnderThePointerExpiresTheTimer()
    {
        // a click on row 0 then a quick click on row 1: the pointer moved to another widget, MyGUI resets mTimerDoubleClick (:211-214)
        var p = P();
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0);
        p.LeftPress(ArenaList.Roster, 1, 6, 1, 60);
        Assert.Equal(2, p.LeftRelease((ArenaList.Roster, 1), ArenaDropTarget.Roster, None, None, 0.1).Count);
        // away and back between the clicks on the same row also expires it
        p.LeftPress(ArenaList.Roster, 1, 6, 1, 60);
        p.LeftRelease((ArenaList.Roster, 1), ArenaDropTarget.Roster, None, None, 1);
        p.Hover(null, held: false);
        p.LeftPress(ArenaList.Roster, 1, 6, 1, 60);
        Assert.Equal(2, p.LeftRelease((ArenaList.Roster, 1), ArenaDropTarget.Roster, None, None, 1.1).Count);
        // while the button is held the pressed widget keeps the focus: no expiry
        p.LeftPress(ArenaList.Roster, 1, 6, 1, 60);
        p.Hover("PG_TeamA", held: true);
        Assert.Equal(3, p.LeftRelease((ArenaList.Roster, 1), ArenaDropTarget.Roster, None, None, 1.2).Count);
    }

    [Fact]
    public void DoubleClickFiresOnThePressedRowWhereverTheReleaseIs()
    {
        var p = P();
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0);
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        var a = p.LeftRelease(null, ArenaDropTarget.TeamA, None, None, 0.1);
        Assert.Equal([ArenaRowActionKind.Release, ArenaRowActionKind.Click, ArenaRowActionKind.DoubleClick], a.Select(x => x.Kind));
    }

    [Fact]
    public void ReleaseOffThePressedRowGivesNoClickButStartsTheTimer()
    {
        var p = P();
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Single(p.LeftRelease(null, ArenaDropTarget.None, None, None, 0));
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Equal(3, p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0.1).Count);
    }

    [Fact]
    public void DragStartsOnTheFirstMoveAndDropsWithoutAClick()
    {
        var p = P();
        p.LeftPress(ArenaList.Roster, 1, 9, 100, 100);
        p.Move(100, 100, held: true);
        Assert.False(p.DragActive);
        p.Move(101, 100, held: true);
        Assert.True(p.DragActive);
        Assert.Equal(9, p.DragFighter);
        var a = p.LeftRelease((ArenaList.TeamB, 0), ArenaDropTarget.TeamB, 4, None, 3);
        Assert.Equal([new ArenaRowAction<int>(ArenaRowActionKind.Release, ArenaList.Roster, 9, false, ArenaDropTarget.TeamB, 4)], a);
        Assert.False(p.DragActive);
    }

    [Fact]
    public void ButtonUpWithoutAReleaseEndsTheDrag()
    {
        var p = P();
        p.LeftPress(ArenaList.Pool, 0, 3, 0, 0);
        p.Move(5, 5, held: true);
        p.Move(6, 5, held: false);
        Assert.False(p.DragActive);
        Assert.Empty(p.LeftRelease(null, ArenaDropTarget.None, None, None, 1));
    }

    [Fact]
    public void RightPressAndReleaseIsTheRightRelease()
    {
        var p = P();
        Assert.Null(p.RightRelease(ArenaDropTarget.None, None));
        p.RightPress(ArenaList.TeamA, 1, 8);
        Assert.Equal(new ArenaRowAction<int>(ArenaRowActionKind.Release, ArenaList.TeamA, 8, true, ArenaDropTarget.Roster, None),
            p.RightRelease(ArenaDropTarget.Roster, None));
        Assert.Null(p.RightRelease(ArenaDropTarget.None, None));
    }

    [Fact]
    public void LeftReleaseWithoutPressIsNothing() => Assert.Empty(P().LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0));

    [Fact]
    public void DragOverAPrisonerRowAsksForTheHandler()
    {
        var ok = ArenaDragHighlight.Look(true, true, true, ArenaDropTarget.TeamA, true, "Team A", "Team B", "Fighter pool");
        Assert.Equal(new ArenaDragLook("Team A", "Team B", "Fighter pool", true, "Release to assign handler."), ok);
        var no = ArenaDragHighlight.Look(true, false, true, ArenaDropTarget.TeamA, true, "Team A", "Team B", "Fighter pool");
        Assert.Equal(new ArenaDragLook("Team A", "Team B", "Fighter pool", false, "Cannot assign this handler."), no);
    }

    [Fact]
    public void DragOverAListSuffixesItsHeading()
    {
        Assert.Equal("Team B\nRelease to assign", ArenaDragHighlight.Look(true, false, false, ArenaDropTarget.TeamB, true, "Team A", "Team B", "P").TeamBLabel);
        Assert.Equal("P\nCannot assign", ArenaDragHighlight.Look(false, false, false, ArenaDropTarget.Pool, false, "Team A", "Team B", "P").PoolLabel);
        // a team list is no drop zone in pool mode, the pool none in team mode, the roster never gets a suffix
        var l = ArenaDragHighlight.Look(true, false, false, ArenaDropTarget.TeamA, false, "Team A", "Team B", "P");
        Assert.Equal(("Team A", "Team B", "P", (string?)null), (l.TeamALabel, l.TeamBLabel, l.PoolLabel, l.Status));
        Assert.Equal("P", ArenaDragHighlight.Look(true, false, false, ArenaDropTarget.Roster, false, "Team A", "Team B", "P").PoolLabel);
    }

    [Fact]
    public void TooltipShowsAfterHalfASecondAtRest()
    {
        var t = new TooltipTimer();
        Assert.False(t.Update(true, false, 10, 10, 0.0));    // entered
        Assert.False(t.Update(true, false, 10, 10, 0.3));
        Assert.True(t.Update(true, false, 10, 10, 0.5));
        Assert.True(t.Update(true, false, 30, 12, 0.6));     // shown: follows the pointer
        Assert.False(t.Update(false, false, 30, 12, 0.7));   // left the button
    }

    [Fact]
    public void TooltipMoveBeforeShowingRestartsTheWait()
    {
        var t = new TooltipTimer();
        t.Update(true, false, 10, 10, 0.0);
        t.Update(true, false, 10, 10, 0.4);
        Assert.False(t.Update(true, false, 11, 10, 0.45));   // moved: wait from here
        Assert.False(t.Update(true, false, 11, 10, 0.9));
        Assert.True(t.Update(true, false, 11, 10, 0.96));
    }

    [Fact]
    public void TooltipHiddenWhileHeldAndBackAtOnceWhenReleasedInPlace()
    {
        var t = new TooltipTimer();
        t.Update(true, false, 10, 10, 0.0);
        Assert.True(t.Update(true, false, 10, 10, 0.5));
        Assert.False(t.Update(true, true, 10, 10, 0.6));    // a button held (MyGUI capture): hidden
        Assert.False(t.Update(true, true, 10, 10, 2.0));
        Assert.True(t.Update(true, false, 10, 10, 2.01));   // the wait was not reset: shows at once
        // held before it showed: the wait does not run during the capture
        var u = new TooltipTimer();
        u.Update(true, false, 5, 5, 0.0);
        u.Update(true, false, 5, 5, 0.2);
        u.Update(true, true, 5, 5, 1.0);
        Assert.False(u.Update(true, false, 5, 5, 1.1));
        Assert.True(u.Update(true, false, 5, 5, 1.4));
    }

    [Fact]
    public void TooltipMovedWhileShownRestartsAfterACapture()
    {
        var t = new TooltipTimer();
        t.Update(true, false, 10, 10, 0.0);
        t.Update(true, false, 10, 10, 0.5);
        t.Update(true, false, 40, 10, 0.6);                  // moved while shown: the rest point stays 10,10
        t.Update(true, true, 40, 10, 0.7);
        Assert.False(t.Update(true, false, 40, 10, 0.8));    // released away from the rest point: wait again
        Assert.True(t.Update(true, false, 40, 10, 1.3));
    }

    [Fact]
    public void HoverWhileHeldKeepsTheDoubleClickTimer()
    {
        // MyGUI keeps the mouse focus on the pressed widget during a capture (injectMouseMove :99-117): a hover change while a button is held
        // does not expire mTimerDoubleClick; the same change with the buttons up does
        var p = P();
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0);
        p.Hover("PG_TeamA", held: true);
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Equal(3, p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0.1).Count);
        p.Hover("PG_TeamA", held: false);
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Equal(2, p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0.2).Count);
    }

    [Fact]
    public void MoveWithoutAPressDoesNothing()
    {
        var p = P();
        p.Move(5, 5, held: true);
        p.Move(9, 9, held: false);
        Assert.False(p.DragActive);
        Assert.Empty(p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 0));
        // nor does it touch the double-click timer of an earlier click
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 1);
        p.Move(40, 40, held: false);
        p.Move(41, 41, held: true);
        p.LeftPress(ArenaList.Roster, 0, 5, 1, 1);
        Assert.Equal(3, p.LeftRelease((ArenaList.Roster, 0), ArenaDropTarget.Roster, None, None, 1.1).Count);
    }

    [Fact]
    public void TooltipLeftAndReEnteredWhileShownWaitsAgain()
    {
        var t = new TooltipTimer();
        t.Update(true, false, 10, 10, 0.0);
        Assert.True(t.Update(true, false, 10, 10, 0.5));
        Assert.False(t.Update(false, false, 60, 10, 0.6));   // left the widget: hidden
        Assert.False(t.Update(true, false, 12, 10, 0.7));    // back: a new wait from here
        Assert.False(t.Update(true, false, 12, 10, 1.1));
        Assert.True(t.Update(true, false, 12, 10, 1.2));
    }

    [Fact]
    public void TooltipResetThenReEnteredWaitsAgain()
    {
        // a window closed with the pointer resting on the widget reopens without the tip (ResetWindowState calls Reset)
        var t = new TooltipTimer();
        t.Update(true, false, 10, 10, 0.0);
        Assert.True(t.Update(true, false, 10, 10, 0.5));
        t.Reset();
        Assert.False(t.Update(true, false, 10, 10, 5.0));    // entered again
        Assert.False(t.Update(true, false, 10, 10, 5.4));
        Assert.True(t.Update(true, false, 10, 10, 5.5));
    }
}
