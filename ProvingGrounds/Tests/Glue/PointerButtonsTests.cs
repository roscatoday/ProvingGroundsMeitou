using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.5b review: the window framework's held buttons, reconciled each frame against the operating system's button state (Meitou
/// does not always call PlayerInterface.MouseUp for a press it saw; the original polls GetAsyncKeyState, ArenaUI.cpp:1205).</summary>
public class PointerButtonsTests
{
    const int Left = 0, Right = 1;

    [Fact]
    public void PressAndReleaseTrackEachButton()
    {
        var b = new PointerButtons();
        Assert.False(b.Any);
        b.Press(Left, true);
        b.Press(Right, true);
        Assert.True(b.IsDown(Left) && b.IsDown(Right) && b.Any);
        b.Release(Left);
        Assert.False(b.IsDown(Left));
        Assert.True(b.Any);
        b.Release(Right);
        Assert.False(b.Any);
    }

    [Fact]
    public void AMissedReleaseIsForgottenOnlyTwoPassesAfterTheMark()
    {
        var b = new PointerButtons();
        b.Press(Left, true);
        Assert.False(b.Reconcile(true, 1));     // still physically held
        Assert.False(b.Reconcile(false, 2));    // first "up": marked (maybe inside pass 2's own DoEvents, its MouseUp queued behind)
        Assert.False(b.Reconcile(false, 2));    // the same pass again (the arrange after DoEvents): still marked only
        Assert.True(b.IsDown(Left));
        Assert.False(b.Reconcile(false, 3));    // mark + 1: pass 3's DoEvents may be the one bringing the MouseUp only now
        Assert.True(b.IsDown(Left));
        Assert.True(b.Reconcile(false, 4));     // mark + 2: a whole DoEvents ran after the mark and no MouseUp came: forgotten
        Assert.False(b.Any);
        Assert.False(b.Reconcile(false, 5));    // nothing left to forget
    }

    [Fact]
    public void AMouseUpBeforeMarkPlusTwoReleasesNormally()
    {
        var b = new PointerButtons();
        b.Press(Left, true);
        Assert.False(b.Reconcile(false, 5));    // released physically; the MouseUp still queued
        Assert.False(b.Reconcile(false, 6));
        b.Release(Left);                        // the next DoEvents brings the MouseUp
        Assert.False(b.Any);
        Assert.False(b.Reconcile(false, 7));    // nothing forgotten: the release came through
        b.Press(Left, true);                    // a new press starts with no mark
        Assert.False(b.Reconcile(false, 8));
        Assert.False(b.Reconcile(false, 9));
        Assert.True(b.IsDown(Left));
    }

    [Fact]
    public void ADownSightingClearsTheMark()
    {
        var b = new PointerButtons();
        b.Press(Right, true);
        b.Reconcile(false, 1);
        b.Reconcile(true, 2);                   // down again: the mark goes
        Assert.False(b.Reconcile(false, 3));    // a fresh first sighting
        Assert.False(b.Reconcile(false, 4));
        Assert.True(b.Reconcile(false, 5));
    }

    [Fact]
    public void APressWithoutAPhysicalButtonLivesUntilItsOwnRelease()
    {
        // the agent channel's click, pg window drag and a fast tap press through PlayerInterface.MouseDown with no button down at the system
        var b = new PointerButtons();
        b.Press(Left, false);
        Assert.False(b.Reconcile(false, 1));
        Assert.False(b.Reconcile(false, 2));
        Assert.True(b.IsDown(Left));
        b.Release(Left);
        Assert.False(b.Any);
        // a press while the system cannot be asked is not reconciled either, nor is any press while it cannot be asked
        b.Press(Right, null);
        Assert.False(b.Reconcile(false, 3));
        Assert.False(b.Reconcile(false, 4));
        b.Press(Left, true);
        Assert.False(b.Reconcile(null, 5));
        Assert.False(b.Reconcile(null, 6));
        Assert.False(b.Reconcile(false, 7));
        Assert.False(b.Reconcile(false, 8));
        Assert.True(b.Reconcile(false, 9));
        Assert.True(b.IsDown(Right) && !b.IsDown(Left));
    }

    [Fact]
    public void ClearForgetsEveryPressAndOutOfRangeButtonsAreIgnored()
    {
        var b = new PointerButtons();
        b.Press(Left, false);
        b.Press(Right, true);
        b.Press(99, true);
        b.Press(-1, true);
        Assert.False(b.IsDown(99));
        b.Clear();
        Assert.False(b.Any);
        b.Release(42);
        Assert.False(b.Any);
    }
}
