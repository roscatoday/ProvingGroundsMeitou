using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>MarksHUD.cpp's cadence, decision, caption and placement (task 4.6b).</summary>
public class MarksHUDLogicTests
{
    [Fact]
    public void ClockRefreshesAtMostEvery250MsAndNeverAfterAFailure()
    {
        var c = new MarksHudClock();
        Assert.True(c.Due(1000));      // first call (lastRefresh 0)
        Assert.False(c.Due(1100));
        Assert.False(c.Due(1249));
        Assert.True(c.Due(1250));      // now - last == 250 is not < 250
        Assert.False(c.Due(1499));
        Assert.True(c.Due(1500));
        c.Fail();
        Assert.True(c.Failed);
        Assert.False(c.Due(9000));
        c.Reset();                      // AbandonWorldState
        Assert.False(c.Failed);
        Assert.True(c.Due(9001));
    }

    [Fact]
    public void ClockUsesUnsignedDifferenceAcrossTheWrap()
    {
        var c = new MarksHudClock();
        Assert.True(c.Due(uint.MaxValue - 100));
        Assert.False(c.Due(uint.MaxValue));
        Assert.True(c.Due(149));   // 250 ms later across the wrap
    }

    [Fact]
    public void ClockTreatsAZeroTimeAsNeverRefreshed()
    {
        var c = new MarksHudClock();
        Assert.True(c.Due(0));
        Assert.True(c.Due(10));    // C++: lastRefresh 0 reads as "never"
        Assert.False(c.Due(20));
    }

    [Fact]
    public void DecisionShowsMarksOnlyWithASaveAndAPlayerInScratch()
    {
        int asked = 0;
        int Marks() { asked++; return 37; }
        Assert.Equal(new MarksHudDecision(false, "Marks: 5"), MarksHudLogic.Decide(false, true, Marks, "Marks: 5"));
        Assert.Equal(new MarksHudDecision(false, ""), MarksHudLogic.Decide(true, false, Marks, ""));
        Assert.Equal(0, asked);
        Assert.Equal(new MarksHudDecision(true, "Marks: 37"), MarksHudLogic.Decide(true, true, Marks, ""));
        Assert.Equal(1, asked);
        Assert.Equal("Marks: -3", MarksHudTexts.Marks(-3));
    }

    [Fact]
    public void InScratchNeedsEveryCondition()
    {
        Assert.True(MarksHudLogic.InScratch(true, true, true, true));
        Assert.False(MarksHudLogic.InScratch(false, true, true, true));
        Assert.False(MarksHudLogic.InScratch(true, false, true, true));
        Assert.False(MarksHudLogic.InScratch(true, true, false, true));
        Assert.False(MarksHudLogic.InScratch(true, true, true, false));
    }

    [Fact]
    public void PanelSpansTheMoneyWidgetsAndSitsOneRowAndOneGapAbove()
    {
        // font 26: gap 13; row height = max(text 26, font 26) + 26 = 52
        var money = new LayoutRect(1700, 800, 150, 30);
        var label = new LayoutRect(1660, 802, 60, 30);
        var (panel, text) = MarksHudLogic.Place(money, label, 1920, 100, 26, 26);
        Assert.Equal(new LayoutRect(1660, 800 - 52 - 13, 190, 52), panel);   // left 1660, right 1850
        Assert.Equal(new LayoutRect(13, 13, 190 - 26, 52 - 26), text);
    }

    [Fact]
    public void PanelIsAtLeastTheCaptionWideKeptOnScreenAndNotAboveTheTop()
    {
        var money = new LayoutRect(1880, 20, 30, 20);
        var (panel, _) = MarksHudLogic.Place(money, null, 1920, 200, 17, 17);   // gap 8, width max(30, 216) = 216, height 17 + 16 = 33
        Assert.Equal(216, panel.W);
        Assert.Equal(1920 - 216, panel.X);
        Assert.Equal(0, panel.Y);
        Assert.Equal(33, panel.H);
        var (wide, inner) = MarksHudLogic.Place(new LayoutRect(0, 500, 10, 10), null, 100, 500, 17, 17);
        Assert.Equal(100, wide.W);     // never wider than the screen
        Assert.Equal(0, wide.X);
        Assert.Equal(new LayoutRect(8, 8, 84, 17), inner);
    }
}
