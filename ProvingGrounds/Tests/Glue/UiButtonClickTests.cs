using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.4b: a button bound to eventMouseButtonClick acts on the left release over the widget it was pressed on (MyGUI 3.2.2
/// MyGUI_InputManager.cpp injectMouseRelease :283-333); one bound to eventMouseButtonPressed keeps acting on the press.</summary>
public class UiButtonClickTests
{
    const string S = "pg.x", A = "BtnA", B = "BtnB";
    static bool Shown(string screen, string widget) => true;
    static bool Never(string screen, string widget) => false;

    [Fact]
    public void APressButtonActsOnThePress()
    {
        var c = new UiButtonClick();
        Assert.True(c.Activated(S, A, UiButtonEvent.Press, fromLeftPress: true, 0));
        Assert.Null(c.Release(true, (S, A), Shown, 0.1));   // nothing is waiting for its release
    }

    [Fact]
    public void ThePointerKindIsSentOnThePress()
    {
        var c = new UiButtonClick();
        Assert.True(c.Activated(S, A, UiButtonEvent.Pointer, fromLeftPress: true, 0));
    }

    [Fact]
    public void AClickButtonWaitsForTheReleaseOverTheSameWidgetAndFiresOnce()
    {
        var c = new UiButtonClick();
        Assert.False(c.Activated(S, A, UiButtonEvent.Click, fromLeftPress: true, 0));
        Assert.Equal((S, A), c.Release(true, (S, A), Shown, 0.1));
        Assert.Null(c.Release(true, (S, A), Shown, 0.2));   // a second release with no press: nothing
    }

    [Fact]
    public void PressDragOffReleaseDoesNothing()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        Assert.Null(c.Release(true, null, Shown, 0.1));
    }

    [Fact]
    public void AReleaseOverAnotherOfOurButtonsDoesNothing()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        Assert.Null(c.Release(true, (S, B), Shown, 0.1));
        Assert.Null(c.Release(true, ("pg.other", A), Shown, 0.2));
    }

    [Fact]
    public void ARightReleaseDoesNothingAndKeepsTheLeftPress()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        Assert.Null(c.Release(false, (S, A), Shown, 0.1));
        Assert.Equal((S, A), c.Release(true, (S, A), Shown, 0.2));   // MyGUI: the click is a left release only; the left capture stays
    }

    [Fact]
    public void AHiddenOrDisabledWidgetAtTheReleaseGetsNoClick()
    {
        // MyGUI: a hidden widget loses the mouse focus (Widget::_updateVisible :986-987), a disabled one returns before the click (:286-287)
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        Assert.Null(c.Release(true, (S, A), Never, 0.1));
    }

    [Fact]
    public void AClosedWindowForgetsItsPress()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.ForgetScreen("pg.other");
        Assert.True(c.Waiting);
        c.ForgetScreen(S);
        Assert.False(c.Waiting);
        Assert.Null(c.Release(true, (S, A), Shown, 0.1));
    }

    [Fact]
    public void ForgetDropsThePressAndTheTimer()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.Release(true, (S, A), Shown, 0.1);
        c.Activated(S, A, UiButtonEvent.Click, true, 0.15);
        c.Forget();
        Assert.False(c.Waiting);
        c.Activated(S, A, UiButtonEvent.Click, true, 0.16);
        Assert.Null(c.Release(true, null, Shown, 0.17));   // the timer was forgotten too: off the widget, no click
    }

    [Fact]
    public void AKeyboardActivationActsAtOnce()
    {
        // Meitou's Enter / focused-button activation raises Activated outside a mouse press: no release will come
        var c = new UiButtonClick();
        Assert.True(c.Activated(S, A, UiButtonEvent.Click, fromLeftPress: false, 0));
        Assert.False(c.Waiting);
    }

    [Fact]
    public void ASecondReleaseInsideTheDoubleClickTimeClicksWhereverThePointerIs()
    {
        // MyGUI: within INPUT_TIME_DOUBLE_CLICK of the last click the click fires without the point test (:303-309)
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        Assert.Equal((S, A), c.Release(true, (S, A), Shown, 0.05));
        c.Activated(S, A, UiButtonEvent.Click, true, 0.1);
        Assert.Equal((S, A), c.Release(true, null, Shown, 0.2));
        // the timer was not restarted (:303-309 leave it running): 0.30 - 0.05 >= 0.25, so the point test applies again
        c.Activated(S, A, UiButtonEvent.Click, true, 0.25);
        Assert.Null(c.Release(true, null, Shown, 0.30));
    }

    [Fact]
    public void AReleaseOffTheWidgetStillStartsTheTimer()
    {
        // :312-317: mTimerDoubleClick = 0 after the point test, clicked or not
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        Assert.Null(c.Release(true, null, Shown, 0.05));
        c.Hover(S, A, held: false);   // the pointer went back without leaving A's focus: same widget, the timer runs on
        c.Activated(S, A, UiButtonEvent.Click, true, 0.1);
        Assert.Equal((S, A), c.Release(true, null, Shown, 0.15));
    }

    [Fact]
    public void AnotherWidgetUnderThePointerExpiresTheTimer()
    {
        // :208-213: the timer expires when the mouse focus changes; not while a button is held (the pressed widget keeps the focus)
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.Release(true, (S, A), Shown, 0.05);
        c.Hover(S, B, held: true);   // a capture: ignored
        c.Activated(S, A, UiButtonEvent.Click, true, 0.08);
        Assert.Equal((S, A), c.Release(true, null, Shown, 0.1));
        c.Hover(S, B, held: false);
        c.Hover(S, A, held: false);
        c.Activated(S, A, UiButtonEvent.Click, true, 0.12);
        Assert.Null(c.Release(true, null, Shown, 0.14));
    }

    [Fact]
    public void APressOnAnotherButtonReplacesTheFirst()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.Activated(S, B, UiButtonEvent.Click, true, 1);
        Assert.Null(c.Release(true, (S, A), Shown, 1.5));
    }

    [Fact]
    public void APressButtonPressForgetsAWaitingClick()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.Activated(S, B, UiButtonEvent.Press, true, 1);   // a new left press went elsewhere
        Assert.False(c.Waiting);
    }

    [Fact]
    public void TheArenaTableFollowsArenaUiCpp()
    {
        // every PanelButton eventMouseButtonClick (:2541); presets eventMouseButtonPressed (:2881); rows their own pointer (:641-645)
        foreach (var w in new[] { ArenaUINames.Recruit, ArenaUINames.ModeAvB, ArenaUINames.ModeTeams1v1, ArenaUINames.ModeLast, ArenaUINames.DestA,
                     ArenaUINames.DestB, ArenaUINames.AutoBalance, ArenaUINames.BalanceSkill, ArenaUINames.BalanceRating, ArenaUINames.Swap,
                     ArenaUINames.Clear, ArenaUINames.Remove, ArenaUINames.LocArena, ArenaUINames.LocSmall, ArenaUINames.LocBanner,
                     ArenaUINames.Medical, ArenaUINames.Start, ArenaUINames.Stop, ArenaUINames.RosterDrawer, ArenaUINames.SettingsDrawer })
            Assert.Equal(UiButtonEvent.Click, ArenaUINames.ButtonEvent(w));
        for (int i = 0; i < 3; i++) Assert.Equal(UiButtonEvent.Press, ArenaUINames.ButtonEvent(ArenaUINames.Preset(i)));
        Assert.Equal(UiButtonEvent.Pointer, ArenaUINames.ButtonEvent(ArenaUINames.Row(ArenaList.Roster, 0)));
        Assert.Equal(UiButtonEvent.Pointer, ArenaUINames.ButtonEvent(ArenaUINames.Row(ArenaList.Pool, 7)));
    }

    [Fact]
    public void TheResultsAndLeaderboardButtonsAreClicks()
    {
        Assert.Equal(UiButtonEvent.Click, ResultsUINames.ButtonEvent(ResultsUINames.Close));        // ResultsUI.cpp:445
        Assert.Equal(UiButtonEvent.Click, LeaderboardUINames.ButtonEvent(LeaderboardUINames.Close)); // LeaderboardUI.cpp:565
        Assert.Equal(UiButtonEvent.Click, LeaderboardUINames.ButtonEvent(LeaderboardUINames.Reset)); // LeaderboardUI.cpp:567
    }

    [Fact]
    public void ANewLeftPressForgetsAStaleWaitingPress()
    {
        // review 4.4b: a press whose MouseUp Meitou skipped must not click on a later press elsewhere dragged onto the button
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.BeginLeftPress();   // a later left press on the world (no Activated)
        Assert.False(c.Waiting);
        Assert.Null(c.Release(true, (S, A), Shown, 1));
    }

    [Fact]
    public void FrameCheckWithPointerButtons()
    {
        const int Left = 0;
        var b = new PointerButtons();
        var c = new UiButtonClick();

        // the R1 sequence: a physical press, the system says up (marked in pass m), the real release arrives before m + 2: still a click
        c.BeginLeftPress(); c.Activated(S, A, UiButtonEvent.Click, true, 0); b.Press(Left, true);
        b.Reconcile(false, 10);
        Assert.False(c.FrameCheck(b.IsDown(Left), false));
        b.Reconcile(false, 11);
        Assert.False(c.FrameCheck(b.IsDown(Left), false));
        b.Release(Left);
        Assert.Equal((S, A), c.Release(true, (S, A), Shown, 0.1));

        // a press dropped at m + 2 (no release seen) forgets the waiting click
        c.BeginLeftPress(); c.Activated(S, A, UiButtonEvent.Click, true, 1); b.Press(Left, true);
        b.Reconcile(false, 20);
        Assert.False(c.FrameCheck(b.IsDown(Left), false));
        Assert.True(b.Reconcile(false, 22));
        Assert.True(c.FrameCheck(b.IsDown(Left), false));
        Assert.Null(c.Release(true, (S, A), Shown, 1.1));

        // inside the left press (Activated came before the MouseDown postfix records the button): no forget
        c.BeginLeftPress(); c.Activated(S, A, UiButtonEvent.Click, true, 2);
        Assert.False(c.FrameCheck(b.IsDown(Left), leftPressing: true));
        Assert.True(c.Waiting);
        Assert.True(c.FrameCheck(b.IsDown(Left), leftPressing: false));
    }

    [Fact]
    public void TheTimerIsLiveOnlyInsideTheDoubleClickTime()
    {
        var c = new UiButtonClick();
        Assert.False(c.TimerLive(0));
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.Release(true, (S, A), Shown, 1.0);
        Assert.True(c.TimerLive(1.1));
        Assert.False(c.TimerLive(1.25));   // 0.25 s later: the shortcut is over, no hit tests needed
    }

    [Fact]
    public void FrameCheckKeepsTheTimer()
    {
        var c = new UiButtonClick();
        c.Activated(S, A, UiButtonEvent.Click, true, 0);
        c.Release(true, (S, A), Shown, 0.05);
        c.Activated(S, A, UiButtonEvent.Click, true, 0.08);
        Assert.True(c.FrameCheck(leftHeld: false, leftPressing: false));
        Assert.True(c.TimerLive(0.1));
    }
}
