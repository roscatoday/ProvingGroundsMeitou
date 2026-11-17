using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using RN = ProvingGrounds.Core.Glue.ResultsUINames;
using LN = ProvingGrounds.Core.Glue.LeaderboardUINames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The pixel arithmetic of the Results window (ResultsUI.cpp:89-365) and the Leaderboard window (LeaderboardUI.cpp:164-512), task 4.6a,
/// over a fake text measure (10 px a character, one line 20 px high).</summary>
public class ResultsLeaderboardLayoutTests
{
    /// <summary>10 px per character, word-free wrapping by width (ceil), 20 px per line; a button caption 11 px per character.</summary>
    sealed class Fake : IPgTextMeasure
    {
        public (int Width, int Height) Standard(string text, int wrapWidth)
        {
            int widest = 0, lines = 0;
            foreach (var part in text.Split('\n'))
            {
                int w = part.Length * 10;
                if (wrapWidth > 0 && w > wrapWidth) { lines += (w + wrapWidth - 1) / wrapWidth; widest = Math.Max(widest, wrapWidth); }
                else { lines++; widest = Math.Max(widest, w); }
            }
            return (widest, lines * 20);
        }
        public int ButtonWidth(string caption) => caption.Length * 11;
    }

    static readonly Fake M = new();

    static ResultsLayoutInput Results(int cards, bool empty = false, int viewW = 1920, int viewH = 1080) => new()
    {
        ViewWidth = viewW, ViewHeight = viewH, FontHeight = 20, Header = "Team A wins", Context = "Teams 1v1  -  6 fighters", EmptyVisible = empty,
        Slots = [.. Enumerable.Range(0, cards).Select(i => ("MVP", "Fighter " + i + "  -  Team A", "DEALT 1  •  TAKEN 0\nHITS 1"))],
    };

    [Fact]
    public void ThreeCardsSitSideBySideSecondFirstThirdAndBottomAligned()
    {
        var l = ResultsUILayout.Compute(Results(3), M);
        Assert.Equal(10, l.Gap);   // NativeUI::Spacing(20)
        Assert.Equal(20, l.Body);
        // FitWindowToContent: natural cell = max(5 x 20, 96) + 20 gaps = 300; 3 cells + 2 gaps = 920; + 4 gaps + the scroll inset 15 + chrome 6
        Assert.Equal(920 + 40 + 15 + 6, l.Window.W);
        Assert.Equal((1920 - l.Window.W) / 2, l.Window.X);
        Assert.Equal((1080 - l.Window.H) / 2, l.Window.Y);
        var s0 = l.Rects[RN.Slot(0)];
        var s1 = l.Rects[RN.Slot(1)];
        var s2 = l.Rects[RN.Slot(2)];
        Assert.True(s1.X < s0.X && s0.X < s2.X);   // podium order: 2nd, 1st, 3rd (LeaderboardLayout.FitPodium)
        Assert.Equal(s0.Bottom, s1.Bottom);       // bottom-aligned (:345-352)
        Assert.Equal(s0.Bottom, s2.Bottom);
        Assert.True(s0.H > s1.H);                 // the champion's portrait is bigger
        Assert.Equal(100, l.Rects[RN.Portrait(RN.Slot(0))].W);   // max(5 body, 96)
        Assert.Equal(80, l.Rects[RN.Portrait(RN.Slot(1))].W);    // max(4 body, 72)
        Assert.DoesNotContain(RN.RailLeft, l.Hidden);
        Assert.DoesNotContain(RN.Slot(2), l.Hidden);
        Assert.Contains(RN.Empty, l.Hidden);
        Assert.Contains(RN.Measure, l.Hidden);
        // the close button: centred at the bottom of the client, its caption + 4 gaps wide (:280-283)
        var close = l.Rects[RN.Close];
        Assert.Equal("Close results".Length * 11 + 40, close.W);
        Assert.Equal((l.ClientWidth - close.W) / 2, close.X);
        Assert.Equal(l.ClientHeight - 10 - close.H, close.Y);
        // the window collapses around the content (:264-272)
        Assert.Equal(Math.Max(180, l.ContentHeight + 15 + close.H + 30 + 45), l.Window.H);
        Assert.False(l.ScrollBar);
    }

    [Fact]
    public void ASingleCardIsCentredAndTheHeaderRailsFrameTheTitle()
    {
        var l = ResultsUILayout.Compute(Results(1), M);
        var card = l.Rects[RN.Slot(0)];
        var canvas = l.Canvas[RN.Scroll];
        int contentW = canvas.W - 20;
        Assert.Equal(10 + (contentW - card.W) / 2, card.X);
        Assert.Contains(RN.Slot(1), l.Hidden);
        Assert.Contains(RN.Slot(2), l.Hidden);
        var header = l.Rects[RN.Header];
        var rails = LeaderboardLayout.FitTitleRails(contentW, 10, "Team A wins".Length * 10 + 40);
        Assert.Equal(10 + rails.TitleLeft, header.X);
        Assert.Equal(rails.TitleWidth, header.W);
        Assert.Equal(rails.TitleWidth, l.HeaderWidth);
        Assert.Equal(rails.LeftWidth, l.Rects[RN.RailLeft].W);
    }

    [Fact]
    public void ADrawShowsTheEmptyTextAndSizesTheWindowFromIt()
    {
        var l = ResultsUILayout.Compute(Results(0, empty: true) with { EmptyWidthBefore = 100 }, M);
        Assert.DoesNotContain(RN.Empty, l.Hidden);
        Assert.Contains(RN.Slot(0), l.Hidden);
        // at the creation width (100) the empty text wraps: 100 px; the context (also wrapped at 100) is 100 too; the window's minimum 320 wins
        Assert.Equal(320, l.Window.W);
        // the next show measures at the widths this one left
        var again = ResultsUILayout.Compute(Results(0, empty: true) with
        {
            EmptyWidthBefore = l.EmptyWidth, ContextWidthBefore = l.ContextWidth, HeaderWidthBefore = l.HeaderWidth, ScrollBarBefore = l.ScrollBar,
        }, M);
        Assert.True(again.Window.W >= 320);
    }

    [Fact]
    public void ANarrowScreenStacksTheCards()
    {
        var l = ResultsUILayout.Compute(Results(3, viewW: 640, viewH: 480), M);
        Assert.Equal(640 * 92 / 100, l.Window.W);
        var s0 = l.Rects[RN.Slot(0)];
        var s1 = l.Rects[RN.Slot(1)];
        Assert.Equal(s0.X, s1.X);
        Assert.True(s1.Y > s0.Y);
        Assert.Equal(480 * 90 / 100, l.Window.H);   // capped: the canvas scrolls
        Assert.True(l.ScrollBar);
    }

    static LeaderboardLayoutInput Board(int rows, bool reset = false, int viewW = 1920, int viewH = 1080) => new()
    {
        ViewWidth = viewW, ViewHeight = viewH, FontHeight = 20, Subtitle = "Player arena standings - meitou", Empty = LeaderboardUiTexts.EmptyPlayer,
        ResetVisible = reset,
        Rows = [.. Enumerable.Range(0, rows).Select(i => new LeaderboardLayoutRow((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Fighter " + i, "120", "1 - 0", "1", "Rating 120  •  1 - 0  •  1 bout"))],
    };

    [Fact]
    public void TheWindowIs88By82PercentOfTheScreen()
    {
        var l = LeaderboardUILayout.Compute(Board(0), M);
        Assert.Equal(new LayoutRect(115, 97, 1689, 885), l.Window);   // 1920 x 88 / 100 = 1689, 1080 x 82 / 100 = 885
        Assert.Equal(1683, l.ClientWidth);
        Assert.Equal(840, l.ClientHeight);
    }

    [Fact]
    public void AnEmptyBoardShowsTheEmptyTextAndNoScroll()
    {
        var l = LeaderboardUILayout.Compute(Board(0), M);
        Assert.Contains(LN.Scroll, l.Hidden);
        Assert.DoesNotContain(LN.Empty, l.Hidden);
        Assert.Contains(LN.Reset, l.Hidden);
        var close = l.Rects[LN.Close];
        Assert.Equal(Math.Max(5 * 11 + 40, 20 * 6), close.W);
        Assert.Equal(Math.Max(10, (l.ClientWidth - close.W) / 2), close.X);
        var sub = l.Rects[LN.Subtitle];
        Assert.Equal(new LayoutRect(10, 10, l.ClientWidth - 20, 20 + 30), sub);   // one line + the Kenshi_WordWrap insets (10 + 20 each way)
        var empty = l.Rects[LN.Empty];
        Assert.Equal(20, empty.X);
        Assert.Equal(l.Rects[LN.Scroll].Y, empty.Y);
    }

    [Fact]
    public void ThreeRowsAreThePodiumOnlyAndNoHeadings()
    {
        var l = LeaderboardUILayout.Compute(Board(3), M);
        Assert.DoesNotContain(LN.Scroll, l.Hidden);
        Assert.Contains(LN.Empty, l.Hidden);
        Assert.DoesNotContain(LN.Champions, l.Hidden);
        for (int c = 0; c < 5; c++) Assert.Contains(LN.Header(c), l.Hidden);
        Assert.Contains(LN.Underline, l.Hidden);
        var r0 = l.Rects[LN.Row(0)];
        var r1 = l.Rects[LN.Row(1)];
        Assert.True(r1.X < r0.X);
        Assert.Equal(r0.Bottom, r1.Bottom);
        Assert.Equal(1, l.FeaturedRank[LN.Row(0)]);
        Assert.Equal(2, l.FeaturedRank[LN.Row(1)]);
        Assert.Contains(LN.Cell(LN.Row(0), 2), l.Hidden);   // a featured row shows only Rank and Fighter, and its details
        Assert.Contains(LN.Separator(LN.Row(0)), l.Hidden);
    }

    [Fact]
    public void RowsAfterThePodiumGetColumnsAndSeparators()
    {
        var l = LeaderboardUILayout.Compute(Board(6), M);
        for (int c = 0; c < 5; c++) Assert.DoesNotContain(LN.Header(c), l.Hidden);
        Assert.DoesNotContain(LN.Underline, l.Hidden);
        string r3 = LN.Row(3), r4 = LN.Row(4);
        Assert.DoesNotContain(LN.Separator(r3), l.Hidden);
        Assert.Contains(LN.Details(r3), l.Hidden);
        Assert.Contains(LN.CardBorder(r3, 0), l.Hidden);
        Assert.Equal(10, l.Rects[r3].X);
        Assert.True(l.Rects[r4].Y > l.Rects[r3].Y);
        // the rating column sits right of the wide Fighter column (FitColumns gives the spare width to Fighter)
        var rank = l.Rects[LN.Header(0)];
        var fighter = l.Rects[LN.Header(1)];
        var rating = l.Rects[LN.Header(2)];
        Assert.Equal(10, rank.X);
        Assert.True(fighter.W > rating.W * 5);
        Assert.Equal(fighter.Right + 10, rating.X);
        // a row's height: max(name, row portrait 40) + 2 row gaps (5)
        Assert.Equal(40 + 10, l.Rects[r3].H);
    }

    [Fact]
    public void TheResetButtonSharesTheBottomRowWithClose()
    {
        var l = LeaderboardUILayout.Compute(Board(0, reset: true), M);
        Assert.DoesNotContain(LN.Reset, l.Hidden);
        var reset = l.Rects[LN.Reset];
        var close = l.Rects[LN.Close];
        Assert.Equal(reset.W, close.W);
        Assert.Equal(reset.Right + 10, close.X);
        Assert.Equal(reset.Y, close.Y);
        Assert.Equal(Math.Max("Reset arena progress".Length * 11 + 40, 20 * 11), reset.W);
    }

    [Fact]
    public void ANarrowWindowStacksTheColumns()
    {
        var l = LeaderboardUILayout.Compute(Board(5, viewW: 420, viewH: 600), M);
        string r3 = LN.Row(3);
        Assert.DoesNotContain(LN.Details(r3), l.Hidden);
        Assert.Contains(LN.Cell(r3, 2), l.Hidden);
        Assert.DoesNotContain(LN.Header(1), l.Hidden);
        Assert.Contains(LN.Header(0), l.Hidden);
    }
}
