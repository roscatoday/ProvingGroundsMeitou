using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.TownArenaUINames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The town registry window's pixel arithmetic (TownArenaUI.cpp:105-783, task 7.6) over a fake text measure (10 px a character, 20 px a
/// line, the painted text the same; a button caption 11 px a character), font height 20 (gap 10, body 20) unless named. Expected numbers worked by
/// hand from the C++ (each step's arithmetic in the comments).</summary>
public class TownArenaUILayoutTests
{
    sealed class Fake : IRewardsTextMeasure
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
        public (int Width, int Height) Painted(string text, int wrapWidth) => Standard(text, wrapWidth);
        public int ButtonWidth(string caption) => caption.Length * 11;
    }

    static readonly Fake M = new();

    static TownArenaPortraitView<int> Member(string name, bool portrait = true) =>
        new(7, "k", portrait, !portrait, "Portrait unavailable", 1f, true, name, true, "ATK 10 | DEF 20 | STR 30\nTOU 40 | DEX 50 | WPN 60", "tip");

    static TownArenaCardView<int> Card(int slot, bool visible = true, int rarity = 0, params TownArenaPortraitView<int>[] members) =>
        new(slot, visible, "Common | Easy", "", false, true, "Buy-in: 250 Cats", true, true, rarity, false,
            Math.Max(1, Math.Min(4, members.Length)), members, new TownChallengeBuyInPolicy.Quote { BuyIn = 250, Payout = 300 });

    static TownArenaUiView<int> View(int division = 0, int visibleCards = 1, int members = 1) => new()
    {
        Open = true, Feedback = "Challenge reserved: x", Squad = "Selected team: 1 / 3", Direct = "Direct 1v1: Bob", NextRefresh = "Next Refresh: 06:00",
        RefreshButton = "Buy Refresh (500 Cats)", BookingButton = "Book direct 1v1", Progress = "Challenge wins: 0", Division = division,
        SkarnKey = "", Rows = [new TownArenaRowView<int>("a", 1, "Beep\nSelected", true, true, .5f, RosterStatusPolicy.Band.Orange)],
        Cards = [.. Enumerable.Range(0, 5).Select(s => Card(s, s < visibleCards, 0, [.. Enumerable.Range(0, members).Select(m => Member("Bandit"))]))],
    };

    static TownWindowLayout Compute(TownArenaUiView<int> v, int w = 1920, int h = 1080, int font = 20) =>
        TownArenaUILayout<int>.Compute(v, new TownLayoutInput { ViewWidth = w, ViewHeight = h, FontHeight = font }, M);

    [Fact]   // LayoutTownPanel :676-692: the window, the booking button, the status line, the two columns side by side (1740 >= 42 bodies)
    public void At_1920_by_1080_the_window_has_the_booking_button_status_and_two_columns()
    {
        var l = Compute(View());
        Assert.Equal(10, l.Gap);
        Assert.Equal(20, l.Body);
        // 1920 / 25 = 76, 1080 / 25 = 43, 1920 * 23 / 25 = 1766, 1080 * 23 / 25 = 993; the client 6 x 45 less
        Assert.Equal(new LayoutRect(76, 43, 1766, 993), l.Window);
        Assert.Equal(1760, l.ClientWidth);
        Assert.Equal(948, l.ClientHeight);
        // width 1740; buttonHeight RowHeight(20) = 40; buttonTop 948 - 10 - 40 = 898
        Assert.Equal(new LayoutRect(10, 898, 1740, 40), l.Rects[N.Book]);
        // status: bounded, one line in 1710: 20 + 30 = 50 (limit max(20, 948 / 9 = 105)); top 898 - 10 - 50 = 838
        Assert.Equal(new LayoutRect(10, 838, 1740, 50), l.Rects[N.Status]);
        Assert.False(l.Stacked);
        // available 838 - 20 = 818; sideWidth min(1740 * 2 / 5 = 696, max(20 * 14 = 280, 1740 / 5 = 348)) = 348
        Assert.Equal(new LayoutRect(10, 10, 348, 818), l.Rects[N.Sidebar]);
        Assert.Equal(new LayoutRect(368, 10, 1382, 818), l.Rects[N.Challenges]);   // 1740 - 348 - 10
        Assert.Equal(new LayoutRect(0, 0, 100, 24), l.Rects[N.Metrics]);
        Assert.Contains(N.Metrics, l.Hidden);
        Assert.Contains(N.TipBackground, l.Hidden);
    }

    [Fact]   // LayoutSidebar :658-674 and LayoutRoster :646-657 / LayoutRow :404-420 / RefreshStatusBar :270-279
    public void The_sidebar_holds_the_team_line_the_hint_and_the_roster_rows()
    {
        var l = Compute(View());
        // sidebar view 348 - 15 = 333 x 818 - 15 = 803; width 313; y 10
        Assert.Equal(new LayoutRect(10, 10, 313, 20), l.Rects[N.Team]);         // "Selected team: 1 / 3" one line; y 40
        Assert.Equal(new LayoutRect(10, 40, 313, 40), l.Rects[N.RosterHint]);   // 51 characters = 510 px in 313: two lines; y 90
        Assert.Equal(new LayoutRect(10, 90, 313, 703), l.Rects[N.Roster]);      // listH max(160, 803 - 90 - 10)
        Assert.Equal((333, 803), l.Canvas[N.Sidebar]);                          // 313 + 2 gaps, max(90 + 703 + 10, 803)
        // roster view 313 - 15 = 298 x 703 - 15 = 688; textX 48 + 20 = 68; the text (two painted lines) in 298 - 68 - 10 = 220
        Assert.Equal(new LayoutRect(68, 10, 220, 40), l.Rects[N.RowText(0)]);
        Assert.Equal(new LayoutRect(68, 60, 220, 6), l.Rects[N.RowStatusBack(0)]);   // gap + 40 + gap
        Assert.Equal(new LayoutRect(0, 0, 110, 6), l.Rects[N.RowStatusFill(0)]);     // (int)(220 * .5 + .5)
        Assert.Equal(new LayoutRect(0, 0, 298, 76), l.Rects[N.Row(0)]);              // max(48, 40 + 10 + 6) + 2 gaps
        Assert.Equal(new LayoutRect(10, 10, 48, 48), l.Rects[N.RowPortrait(0)]);
        // edge min(max(2, 10 / 3 = 3), max(1, 149)) = 3
        Assert.Equal(new LayoutRect(0, 0, 298, 3), l.Rects[N.RowBorder(0, 0)]);
        Assert.Equal(new LayoutRect(0, 73, 298, 3), l.Rects[N.RowBorder(0, 1)]);
        Assert.Equal(new LayoutRect(0, 0, 3, 76), l.Rects[N.RowBorder(0, 2)]);
        Assert.Equal(new LayoutRect(295, 0, 3, 76), l.Rects[N.RowBorder(0, 3)]);
        Assert.DoesNotContain(N.RowBorder(0, 0), l.Hidden);   // selected: the borders show (SelectionVisuals :261)
        Assert.Equal((298, 688), l.Canvas[N.Roster]);          // max(76 + 10, 688)
        Assert.Contains(N.EmptyRoster, l.Hidden);
    }

    [Fact]   // LayoutTownPanel :694-737: the division buttons, the direct line, the timer, the refresh button, the Skarn panel (LayoutSkarn :635-645)
    public void The_challenges_header_has_the_divisions_direct_line_timer_refresh_and_skarn_panel()
    {
        var l = Compute(View());
        // challenges view 1382 - 15 = 1367 x 803; rowWidth 1347; skarnWidth min(max(160, 96), 1347 / 3 = 449) = 160; headerWidth 1347 - 160 - 10 = 1177
        // Buttons: natural max(4, 6, 4) * 11 + 40 = 106; columns min(3, 1187 / 116 = 10) = 3; cell (1177 - 20) / 3 = 385
        Assert.Equal(new LayoutRect(10, 10, 385, 40), l.Rects[N.Division(0)]);
        Assert.Equal(new LayoutRect(405, 10, 385, 40), l.Rects[N.Division(1)]);
        Assert.Equal(new LayoutRect(800, 10, 385, 40), l.Rects[N.Division(2)]);
        // y 60; actionWidth = timerWidth = min(392, max(260, 235)) = 260; directWidth 1177 - 520 - 20 = 637 (>= 320)
        Assert.Equal(new LayoutRect(10, 60, 637, 50), l.Rects[N.Direct]);
        Assert.Equal(new LayoutRect(657, 60, 260, 50), l.Rects[N.NextRefresh]);
        Assert.Equal(new LayoutRect(927, 60, 260, 40), l.Rects[N.BuyRefresh]);
        // y 60 + max(40, 50) + 10 = 120; skarnHeight max(120, 120 - 10 - 10) = 120; LayoutSkarn(1197, 10, 160, 120)
        Assert.Equal(new LayoutRect(1197, 10, 160, 120), l.Rects[N.SkarnPanel]);
        // labelHeight 30; portraitSize max(1, min(140, 120 - 30 - 20 = 70)) = 70; x (160 - 70) / 2 = 45
        Assert.Equal(new LayoutRect(45, 10, 70, 70), l.Rects[N.SkarnPortrait]);
        Assert.Equal(new LayoutRect(10, 10, 140, 70), l.Rects[N.SkarnPlaceholder]);
        Assert.Equal(new LayoutRect(10, 80, 140, 30), l.Rects[N.SkarnState]);
        Assert.Contains(N.SkarnPortrait, l.Hidden);           // no Skarn key: the placeholder shows (RefreshSkarn :348-349)
        Assert.DoesNotContain(N.SkarnPlaceholder, l.Hidden);
    }

    [Fact]   // LayoutTownPanel :738-778 + LayoutCardContent :578-623 + LayoutAccent :476-493: one card, the spare height given to its row
    public void One_card_fills_its_grid_cell_and_takes_the_spare_height()
    {
        var l = Compute(View());
        // y max(120, 10 + 120 + 10) = 140; minimum max(260, 14 * 11 + 50 + pad 15) = 260; Fit(1347, 260, 10): 1357 / 270 = 5 columns, (1347 - 40) / 5 = 261
        Assert.Equal(5, l.Grid.Columns);
        Assert.Equal(261, l.Grid.Width);
        Assert.False(l.SingleRow);
        // content: pad 15, inner 236; heading (15, 10, 236, 20), y 40; one member: cell 236, portraitSize min(192, 236)
        Assert.Equal(new LayoutRect(15, 10, 236, 20), l.Rects[N.Heading(0)]);
        Assert.Equal(new LayoutRect(37, 40, 192, 192), l.Rects[N.Portrait(0, 0)]);     // 15 + (236 - 192) / 2
        Assert.Equal(new LayoutRect(15, 126, 236, 20), l.Rects[N.Placeholder(0, 0)]);  // hidden: imageHeight stays 192; y 40 + (192 - 20) / 2
        // detailY 40 + 192 + 5 = 237; the name 20 high, + 10 / 3 = 3: 260; the stats two lines of 24 characters in 236: 4 lines, 80
        Assert.Equal(new LayoutRect(15, 237, 236, 20), l.Rects[N.Name(0, 0)]);
        Assert.Equal(new LayoutRect(15, 260, 236, 80), l.Rects[N.Stats(0, 0)]);
        // y 40 + 192 + 5 + 103 + 10 = 350; the text hidden; the terms (15, 350, 236, 20), y 380; with the book: max(20 * 28 = 560, 380 + 40 + 10) = 560
        Assert.Equal(new LayoutRect(15, 350, 236, 20), l.Rects[N.Terms(0)]);
        // progression top 140 + 560 + 10 = 710, its height 50; spare 803 - (710 + 50 + 10) = 33: the row 593
        Assert.Equal(new LayoutRect(10, 140, 261, 593), l.Rects[N.Card(0)]);
        Assert.Equal(new LayoutRect(15, 543, 236, 40), l.Rects[N.CardBook(0)]);   // (pad, 593 - 40 - 10, 261 - 15 - 10, 40)
        Assert.Equal(new LayoutRect(0, 0, 261, 593), l.Rects[N.Accent(0)]);
        Assert.Equal(new LayoutRect(0, 0, 16, 593), l.Rects[N.Fade(0, 0)]);       // 261 * 1 / 16 = 16
        Assert.Equal(new LayoutRect(244, 0, 17, 593), l.Rects[N.Fade(0, 15)]);    // 261 * 15 / 16 = 244 to 261
        Assert.Equal(new LayoutRect(0, 0, 5, 593), l.Rects[N.Border(0, 0)]);      // rail max(3, 5)
        Assert.Equal(new LayoutRect(5, 0, 256, 1), l.Rects[N.Border(0, 1)]);
        Assert.Equal(new LayoutRect(5, 592, 256, 1), l.Rects[N.Border(0, 2)]);
        Assert.Equal(new LayoutRect(260, 0, 1, 593), l.Rects[N.Border(0, 3)]);
        Assert.Equal(new LayoutRect(10, 743, 1347, 50), l.Rects[N.Progress]);   // y 140 + 593 + 10
        Assert.Equal((1367, 803), l.Canvas[N.Challenges]);                      // rowWidth + 2 gaps, max(803, 803)
        Assert.Contains(N.Card(1), l.Hidden);
        Assert.Contains(N.Text(0), l.Hidden);
        Assert.Contains(N.Placeholder(0, 0), l.Hidden);
        Assert.False(l.Challenges.VBar || l.Challenges.HBar);
    }

    [Fact]   // LayoutCardContent :585-610: two members share a row, each in half the inner width; portraits 112 at most
    public void Two_opponents_share_one_portrait_row()
    {
        var l = Compute(View(members: 2));
        // inner 236; columns 2; cell (236 - 10) / 2 = 113; portraitSize min(112, 113) = 112; rowWidth 236; x 15 and 15 + 123
        Assert.Equal(new LayoutRect(15, 40, 112, 112), l.Rects[N.Portrait(0, 0)]);     // 15 + (113 - 112) / 2 = 15
        Assert.Equal(new LayoutRect(138, 40, 112, 112), l.Rects[N.Portrait(0, 1)]);
        Assert.Equal(new LayoutRect(15, 157, 113, 20), l.Rects[N.Name(0, 0)]);         // 40 + 112 + 5
        Assert.Equal(new LayoutRect(138, 157, 113, 20), l.Rects[N.Name(0, 1)]);
        // the stats: 24 characters = 240 px in 113: 3 lines each, 6 lines, 120 high at 157 + 20 + 3 = 180
        Assert.Equal(new LayoutRect(15, 180, 113, 120), l.Rects[N.Stats(0, 0)]);
    }

    [Fact]   // LayoutTownPanel :683-687: a narrow view stacks the sidebar above the challenges
    public void A_narrow_view_stacks_the_columns()
    {
        var l = Compute(View(), 800, 600);
        // window (32, 24, 736, 552), client 730 x 507; width 710 < 840: stacked; buttonTop 507 - 10 - 40 = 457
        Assert.True(l.Stacked);
        // status 50 (limit max(20, 507 / 9 = 56)); top 397; available 377; sideHeight (377 - 10) * 2 / 5 = 146
        Assert.Equal(new LayoutRect(10, 397, 710, 50), l.Rects[N.Status]);
        Assert.Equal(new LayoutRect(10, 10, 710, 146), l.Rects[N.Sidebar]);
        Assert.Equal(new LayoutRect(10, 166, 710, 221), l.Rects[N.Challenges]);   // available - 146 - 10
    }

    [Fact]   // LayoutTownPanel :742-748, :776-777: Hard shows the cards in one row; wider than the view, the canvas scrolls sideways
    public void Hard_puts_every_card_in_one_row_and_the_challenges_view_scrolls_sideways()
    {
        var l = Compute(View(division: 2, visibleCards: 5), font: 30);
        // font 30: gap 15, body 30; width 1730; sideWidth min(692, max(420, 346)) = 420; challenges 1730 - 420 - 15 = 1295 wide
        // pass 1 (no bars): view 1280, rowWidth 1250, minimum max(390, 154 + 75 + 22) = 390; cards at least 840 high: both bars show
        // pass 2: view 1295 - 15 - 20 = 1260, rowWidth 1230; Fit(1230, 390, 15) = 3 columns of (1230 - 30) / 3 = 400; SingleRow 5 columns of 400
        Assert.True(l.SingleRow);
        Assert.Equal(5, l.Grid.Columns);
        Assert.Equal(400, l.Grid.Width);
        Assert.Equal(2090, l.Canvas[N.Challenges].W);   // 5 * 400 + 4 * 15 + 2 gaps
        Assert.True(l.Challenges.VBar);
        Assert.True(l.Challenges.HBar);
        Assert.Equal(1260, l.Challenges.ViewW);
        Assert.Equal(new LayoutRect(15 + 4 * 415, l.Rects[N.Card(0)].Y, 400, l.Rects[N.Card(0)].H), l.Rects[N.Card(4)]);
        Assert.Equal(830, l.Challenges.ClampX(5000));   // 2090 - 1260
        Assert.Equal(0, l.Challenges.ClampX(-3));
    }

    [Fact]   // Tick :937-943 + RefreshSelectionPrompt :562-577: no team: one prompt card the full row wide, no accent, no spare height
    public void The_selection_prompt_is_one_full_width_card_without_accent()
    {
        var v = View();
        v.SelectionPrompt = true;
        v.Cards = [new TownArenaCardView<int>(0, true, "Select a recovered fighter", "Choose 1-3 recovered squad fighters to reveal the current challenge cards.",
            true, false, "", false, false, -1, false, 0, [], new TownChallengeBuyInPolicy.Quote()), .. Enumerable.Range(1, 4).Select(s => Card(s, false))];
        var l = Compute(v);
        Assert.Equal(1, l.Grid.Columns);
        Assert.Equal(1347, l.Grid.Width);
        // heading (15, 10, 1322, 20); y 40; the text (73 characters = 730 px) one line: y 70; no book: 70 + 10 = 80, no spare (:756)
        Assert.Equal(new LayoutRect(10, 140, 1347, 80), l.Rects[N.Card(0)]);
        Assert.Contains(N.Accent(0), l.Hidden);
        Assert.Contains(N.CardBook(0), l.Hidden);
        Assert.Contains(N.Terms(0), l.Hidden);
        for (int m = 0; m < 4; ++m) Assert.Contains(N.Portrait(0, m), l.Hidden);
    }

    [Fact]   // LayoutRoster :652: no squad fighter: the empty label
    public void An_empty_roster_shows_its_label()
    {
        var v = View();
        v.Rows = [];
        var l = Compute(v);
        Assert.DoesNotContain(N.EmptyRoster, l.Hidden);
        // width 298: "No squad fighters available." (28 characters) in 278: two lines, 40; canvas max(40 + 20, 688)
        Assert.Equal(new LayoutRect(10, 10, 278, 40), l.Rects[N.EmptyRoster]);
        Assert.Equal((298, 688), l.Canvas[N.Roster]);
    }

    [Fact]   // PortraitTip :207-218 (a fighter tip) and PlaceTip :171-183
    public void A_fighter_tip_fits_its_widest_line_and_flips_at_the_edge()
    {
        // maxWidth 1740; inset 30; the widest line 60; width min(1740, 60 + 30 + 20) = 110; four lines in 80: 80 + 30
        Assert.Equal((110, 110), TownArenaUILayout<int>.FighterTip(1760, 948, 20, "Bandit\nStyle\n\nATK 10", M));
        // x 1700 + 20 = 1720, past 1760 - 10: flipped to 1700 - 110 - 10; y 100 + 30
        Assert.Equal((1580, 130), TownArenaUILayout<int>.PlaceTip(1760, 948, 20, 1700, 100, 110, 110));
        Assert.Equal((10, 10), TownArenaUILayout<int>.PlaceTip(1760, 948, 20, -100, -100, 110, 110));   // at least a gap
    }

    [Fact]   // PortraitTip :193-206 (the Skarn tip)
    public void The_skarn_tip_stacks_its_seven_lines()
    {
        string[] lines = ["Skarn locked", "Incomplete - Ressa Vane", "Incomplete - Bum-Per", "Incomplete - Senn", "Incomplete - Torka",
            "Incomplete - Veyr the Gilded", "Defeat every unique fighter to unlock Skarn."];
        var (w, h, r) = TownArenaUILayout<int>.SkarnTip(1760, 948, 20, lines, M);
        // width max(440, 180), the longest line 440 + 30 = 470; each line in 450 (text region 420): 50, the last (440 px) two lines: 70
        Assert.Equal(470, w);
        Assert.Equal(new LayoutRect(10, 10, 450, 50), r[0]);
        Assert.Equal(new LayoutRect(10, 70, 450, 50), r[1]);   // + gap after the first
        Assert.Equal(new LayoutRect(10, 125, 450, 50), r[2]);  // + gap / 2
        Assert.Equal(new LayoutRect(10, 290, 450, 50), r[5]);
        Assert.Equal(new LayoutRect(10, 350, 450, 70), r[6]);  // + gap after the sixth
        Assert.Equal(430, h);                                   // 350 + 70 + 5, + gap / 2
    }

    [Fact]
    public void The_names_parse_back()
    {
        Assert.True(N.TryRow(N.Row(12), out int row) && row == 12);
        Assert.False(N.TryRow(N.RowPortrait(1), out _));
        Assert.True(N.TryDivision(N.Division(2), out int d) && d == 2);
        Assert.False(N.TryDivision("PG_TownDivision3", out _));
        Assert.True(N.TryCardBook(N.CardBook(4), out int s) && s == 4);
        Assert.True(N.TryOfferPart(N.Stats(3, 2), out int o) && o == 3);
        Assert.False(N.TryOfferPart(N.CardBook(3), out _));
        Assert.True(N.TryTipOwner(N.Placeholder(1, 3), out int ts, out int tm) && ts == 1 && tm == 3);
        Assert.True(N.IsSkarnTipOwner(N.SkarnPlaceholder));
        Assert.False(N.IsSkarnTipOwner(N.SkarnState));
    }
}
