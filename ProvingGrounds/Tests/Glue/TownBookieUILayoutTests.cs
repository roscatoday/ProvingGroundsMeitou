using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.TownBookieUINames;
using R = ProvingGrounds.Core.Glue.TownBookieResultUINames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The bookie windows' pixel arithmetic (TownBookieUI.cpp:50-356, TownBookieResultUI.cpp:53-321, task 8.1b) over a fake text measure (10 px a
/// character, 20 px a line, word-wrapped; a button caption 11 px a character), font height 20 (gap 10, body 20). Expected numbers worked by hand
/// from the C++ (each step's arithmetic in the comments).</summary>
public class TownBookieUILayoutTests
{
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

    static BookieFighterCard<int> Card(int id, string name, string style, bool face = true) =>
        new(id, face ? "k" + id : "", new TownBookie.FighterView { Name = name, Style = style, Record = "Unrated | 0 W / 0 L", Stats = "Attack 10" });

    static TownBookieUiView<int> Betting(string timer = "NEXT BOUT 14:48 | STARTS IN 0h 53m")
    {
        var v = new TownBookie.View
        {
            Header = "BETTING OPEN | 5000 Cats | NPC bout 1v2", Timer = timer,
            Status = "Select a team, then choose your stake. One wager per bout.",
            Slip = "YOUR BET: Choose a team and stake. Place bet confirms your wager.", SelectedSide = -1, Stake = 100,
        };
        v.Odds[0] = "1.59x return"; v.Odds[1] = "2.07x return";
        IReadOnlyList<IReadOnlyList<BookieFighterCard<int>>> cards = [[Card(1, "Soo", "Sabre")], [Card(2, "Masaru", "Katana"), Card(3, "Skinner", "Sabre", face: false)]];
        return new TownBookieUiView<int>(true, 1, 0, v, cards, new bool[6], "k");
    }

    static PgWindowLayout Compute(TownBookieUiView<int> v, int w = 1920, int h = 1080, bool barBefore = false) =>
        TownBookieUILayout<int>.Compute(v, new BookieLayoutInput { ViewWidth = w, ViewHeight = h, FontHeight = 20, RosterBarBefore = barBefore }, M);

    [Fact]   // Layout :275-323: the window, the header and timer, the action row, the slip and status, the stake row, the side buttons, the roster
    public void At_1920_by_1080_the_controls_stack_up_from_the_bottom()
    {
        var l = Compute(Betting());
        // 1920 * 88 / 100 = 1689, 1080 * 90 / 100 = 972, centred: (115, 54); the client 6 x 45 less
        Assert.Equal(new LayoutRect(115, 54, 1689, 972), l.Window);
        Assert.Equal((1683, 927), (l.ClientWidth, l.ClientHeight));
        // width 1663, infoHalf (1663 - 10) / 2 = 826; header bounded: one line in 796 = 20 + 30 = 50 (limit max(40, 103))
        Assert.Equal(new LayoutRect(10, 10, 826, 50), l.Rects[N.Header]);
        Assert.Equal(new LayoutRect(846, 10, 827, 50), l.Rects[N.Timer]);    // gap * 2 + infoHalf; 1663 - 826 - 10
        // row RowHeight(20) = 40; actionMin max(99, 231) + 40 = 271, 1663 >= 552: two columns; bottom 927 - 10 - 40 = 877
        Assert.Equal(new LayoutRect(10, 877, 826, 40), l.Rects[N.Bet]);
        Assert.Equal(new LayoutRect(846, 877, 827, 40), l.Rects[N.Arrange]);
        // slip and status bounded one line: 50; bottom 877 - 60 = 817
        Assert.Equal(new LayoutRect(10, 817, 826, 50), l.Rects[N.Slip]);
        Assert.Equal(new LayoutRect(846, 817, 827, 50), l.Rects[N.Status]);
        // stakeColumn (1663 - 60) / 7 = 229; bottom 817 - 50 = 767; the stake value in column 3; the last button to the client's edge less a gap
        Assert.Equal(new LayoutRect(10, 767, 229, 40), l.Rects[N.StakeAdjust(0)]);
        Assert.Equal(new LayoutRect(488, 767, 229, 40), l.Rects[N.StakeAdjust(2)]);
        Assert.Equal(new LayoutRect(727, 767, 229, 40), l.Rects[N.StakeValue]);
        Assert.Equal(new LayoutRect(966, 767, 229, 40), l.Rects[N.StakeAdjust(3)]);
        Assert.Equal(new LayoutRect(1444, 767, 229, 40), l.Rects[N.StakeAdjust(5)]);   // 1683 - 10 - 1444
        // bottom 767 - 50 = 717: the side buttons; the roster from top 70 to 717 - 10
        Assert.Equal(new LayoutRect(10, 717, 826, 40), l.Rects[N.Side(0)]);
        Assert.Equal(new LayoutRect(846, 717, 827, 40), l.Rects[N.Side(1)]);
        Assert.Equal(new LayoutRect(10, 70, 1663, 637), l.Rects[N.Roster]);
        Assert.Contains(N.TipBackground, l.Hidden);
        Assert.DoesNotContain(N.Timer, l.Hidden);
    }

    [Fact]   // Layout :325-354 (two columns: usable 1628 >= 58 bodies) and LayoutCard :254-273
    public void Wide_the_teams_stand_in_two_columns_with_VS_between()
    {
        var l = Compute(Betting());
        // view 1663 - 15 = 1648 x 637 - 15 = 622; usable 1628; divider 60; teamWidth (1628 - 60 - 20) / 2 = 774
        Assert.Equal(new LayoutRect(794, 150, 60, 60), l.Rects[N.Versus]);   // gap * 2 + teamWidth, gap + Body * 7
        Assert.DoesNotContain(N.Versus, l.Hidden);
        Assert.Equal(new LayoutRect(10, 10, 774, 20), l.Rects[N.Team(0)]);
        // Team A's one card (solo): inner 754, beside (754 >= 540), portrait min(260, 339) = 260
        Assert.Equal(new LayoutRect(10, 40, 774, 340), l.Rects[N.Card(0, 0)]);   // max(60 + 260, 90) + 20
        Assert.Equal(new LayoutRect(10, 0, 754, 3), l.Rects[N.Accent(0, 0)]);
        Assert.Equal(new LayoutRect(10, 10, 754, 40), l.Rects[N.Name(0, 0)]);    // "Soo\nSabre": two lines
        Assert.Equal(new LayoutRect(10, 60, 260, 260), l.Rects[N.Portrait(0, 0)]);
        Assert.Equal(new LayoutRect(280, 60, 484, 20), l.Rects[N.Record(0, 0)]); // beside the portrait: 754 - 260 - 10
        // Team B at x 10 + 774 + 60 + 20 = 864, two cards a row (774 >= 700): 382 wide; not beside (362 < 540): portrait min(160, 200) = 160, centred
        Assert.Equal(new LayoutRect(864, 10, 774, 20), l.Rects[N.Team(1)]);
        Assert.Equal(new LayoutRect(864, 40, 382, 280), l.Rects[N.Card(1, 0)]);  // max(60 + 160, 230 + 20 + 10) + 20
        Assert.Equal(new LayoutRect(111, 60, 160, 160), l.Rects[N.Portrait(1, 0)]);
        Assert.Equal(new LayoutRect(10, 230, 362, 20), l.Rects[N.Record(1, 0)]);
        Assert.Equal(new LayoutRect(1256, 40, 382, 280), l.Rects[N.Card(1, 1)]);
        // the canvas: max(view 622, content 410); no bar
        Assert.Equal((1648, 622), l.Canvas[N.Roster]);
        Assert.Equal(410, l.ContentHeight);
        Assert.False(l.ScrollBar);
        Assert.Equal(0, TownBookieUILayout<int>.ClampOffset(l, 300));
        // the cards past the counts are hidden; a card without a key shows its placeholder, else its portrait
        Assert.Contains(N.Card(0, 1), l.Hidden);
        Assert.Contains(N.Card(1, 2), l.Hidden);
        Assert.Contains(N.Placeholder(0, 0), l.Hidden);
        Assert.Contains(N.Portrait(1, 1), l.Hidden);
        Assert.DoesNotContain(N.Placeholder(1, 1), l.Hidden);
    }

    [Fact]   // Layout :325-354 at 1280 x 720: one column (usable < 58 bodies), VS hidden, the content overflows: the second pass with the bar
    public void Narrow_the_teams_stack_and_the_roster_scrolls()
    {
        var l = Compute(Betting(), 1280, 720);
        // client 1120 x 603; width 1100; slip / status wrap to two lines (70, limit 75); bottom 553 - 80 = 473, 423, 373; roster (10, 70, 1100, 293)
        Assert.Equal(new LayoutRect(10, 70, 1100, 293), l.Rects[N.Roster]);
        Assert.Contains(N.Versus, l.Hidden);
        // pass 1: view 1085, content 750 > 278: the bar; pass 2: view 1065, usable 1045: Team A at the top, Team B under it (teamTop 410)
        Assert.True(l.ScrollBar);
        Assert.Equal((1065, 750), l.Canvas[N.Roster]);
        Assert.Equal(new LayoutRect(10, 40, 1045, 340), l.Rects[N.Card(0, 0)]);
        Assert.Equal(new LayoutRect(10, 410, 1045, 20), l.Rects[N.Team(1)]);
        Assert.Equal(new LayoutRect(10, 440, 517, 280), l.Rects[N.Card(1, 0)]);    // (1045 - 10) / 2
        Assert.Equal(new LayoutRect(537, 440, 517, 280), l.Rects[N.Card(1, 1)]);
        Assert.Equal(new LayoutRect(178, 60, 160, 160), l.Rects[N.Portrait(1, 0)]); // 10 + (497 - 160) / 2
        Assert.Equal(472, TownBookieUILayout<int>.ClampOffset(l, 1000));             // 750 - 278
    }

    [Fact]   // Refresh :398 / Layout :286: no timer text: the timer hidden, the header the whole width
    public void Without_a_timer_the_header_takes_the_whole_width()
    {
        var l = Compute(Betting(timer: ""));
        Assert.Contains(N.Timer, l.Hidden);
        Assert.Equal(new LayoutRect(10, 10, 1663, 50), l.Rects[N.Header]);
    }

    [Fact]   // Refresh :412-413, BindCard :358
    public void Headings_and_names_are_the_CPPs()
    {
        var v = new TownBookie.View { SelectedSide = 1 };
        v.Odds[0] = "1.59x return"; v.Odds[1] = "2.07x return";
        Assert.Equal("TEAM A  |  1.59x return", TownBookieUILayout<int>.Heading(v, 0));
        Assert.Equal("TEAM B [SELECTED]  |  2.07x return", TownBookieUILayout<int>.Heading(v, 1));
        Assert.Equal("Soo\nSabre", TownBookieUILayout<int>.NameCaption(new TownBookie.FighterView { Name = "Soo", Style = "Sabre" }));
    }

    [Fact]   // names: the C++'s own
    public void The_names_are_EnsureWindows()
    {
        Assert.Equal("PG_BookieCard1_3Portrait", N.Portrait(1, 3));
        Assert.Equal("PG_BookieStakeAdjust5", N.StakeAdjust(5));
        Assert.True(N.TrySide("PG_BookieSide1", out int s) && s == 1);
        Assert.True(N.TryStakeAdjust("PG_BookieStakeAdjust4", out int a) && a == 4);
        Assert.True(N.TryTipOwner("PG_BookieCard0_2Placeholder", out int side, out int slot) && side == 0 && slot == 2);
        Assert.False(N.TryTipOwner("PG_BookieCard0_2Name", out _, out _));
        Assert.Equal("PG_BookieResult_1_2Border3", R.Border(1, 2, 3));
    }

    // ---------- the results window ----------

    static TownBookieResultUiView<int> Result(int winning = 1, int selected = 1, int tone = 1)
    {
        var v = new TownBookie.ResultView
        {
            Title = "WAGER WON", Context = "Team B claimed the arena. Your pick delivered.",
            Payout = "STAKE  4500 CATS    RETURNED  8974 CATS\nNET WIN  +4474 CATS", WinningSide = winning, SelectedSide = selected, Tone = tone,
        };
        IReadOnlyList<IReadOnlyList<BookieFighterCard<int>>> cards =
            [[Card(1, "Set", "Sabre"), Card(2, "Bark", "Martial arts")], [Card(3, "Sing", "Sabre"), Card(4, "Little", "Katana"), Card(5, "Kat", "Heavy weapon", face: false)]];
        return new TownBookieResultUiView<int>(true, 2, 0, v, cards, "k");
    }

    [Fact]   // Layout :190-277 with Show's display sides (:298-299): the victor (Team B, 3 cards) left, Team A right
    public void The_result_window_fits_its_content_and_puts_the_victor_first()
    {
        var r = TownBookieResultUILayout<int>.Compute(Result(), new BookieResultLayoutInput { FontHeight = 20 }, M);
        var l = r.Layout;
        // body max(20, 20); cardColumns 2, rows 2; natural column 2 * 240 + 30 = 510; content 2 * 510 + 30 = 1050 (title 90 + 100, payout 100 + 80)
        // outer min(1728, max(520, 1056)) = 1056; client 1050; inner 1020; column 505; card 237; portrait min(180, max(100, 217)) = 180
        // cardHeight 260; teams 40 + 20 + 520 + 10 = 590; desired 40 + 40 + 590 + 100 + 40 + 100 = 910; outer min(972, 955) = 955
        Assert.Equal(new LayoutRect(432, 62, 1056, 955), l.Window);
        Assert.Equal((1050, 910), (l.ClientWidth, l.ClientHeight));
        Assert.Equal(new LayoutRect(40, 10, 970, 20), l.Rects[R.Title]);
        // rails (1050 - 90 - 120) / 2 = 420 wide at 10 + 20 / 2
        Assert.Equal(new LayoutRect(10, 20, 420, 3), l.Rects[R.RailLeft]);
        Assert.Equal(new LayoutRect(620, 20, 420, 3), l.Rects[R.RailRight]);
        Assert.Equal(new LayoutRect(10, 40, 1030, 20), l.Rects[R.Context]);
        // columns (1030 - 10) / 2 = 510 at y 80; the heading, its rail; cards (510 - 30) / 2 = 240, portrait 180
        Assert.Equal(new LayoutRect(10, 10, 490, 20), l.Rects[R.Heading(0)]);
        Assert.Equal(new LayoutRect(10, 35, 490, 3), l.Rects[R.Rail(0)]);
        Assert.Equal(new LayoutRect(10, 50, 240, 250), l.Rects[R.Card(0, 0)]);
        Assert.Equal(new LayoutRect(30, 10, 180, 180), l.Rects[R.Portrait(0, 0)]);
        Assert.Equal(new LayoutRect(10, 200, 220, 40), l.Rects[R.Name(0, 0)]);
        Assert.Equal(new LayoutRect(30, 187, 180, 3), l.Rects[R.Border(0, 0, 2)]);
        Assert.Equal(new LayoutRect(207, 10, 3, 180), l.Rects[R.Border(0, 0, 3)]);
        Assert.Equal(new LayoutRect(260, 50, 240, 250), l.Rects[R.Card(0, 1)]);
        Assert.Equal(new LayoutRect(10, 320, 240, 250), l.Rects[R.Card(0, 2)]);
        Assert.Equal(new LayoutRect(10, 80, 510, 580), l.Rects[R.Column(0)]);
        Assert.Equal(new LayoutRect(530, 80, 510, 310), l.Rects[R.Column(1)]);
        // payout at 670 (two lines), the Close button 240 wide centred at 730
        Assert.Equal(new LayoutRect(10, 670, 1030, 40), l.Rects[R.Payout]);
        Assert.Equal(new LayoutRect(405, 730, 240, 40), l.Rects[R.Close]);
        Assert.Equal((970, 1030), (r.TitleWidth, r.PayoutWidth));
        Assert.Contains(R.Card(1, 2), l.Hidden);
        Assert.Contains(R.Portrait(0, 2), l.Hidden);           // Kat has no key: the "?" placeholder
        Assert.DoesNotContain(R.Placeholder(0, 2), l.Hidden);
        Assert.Contains(R.Measure, l.Hidden);
    }

    [Fact]   // Show :298-309, Layout :249-250, OutcomeColour :58-62
    public void Headings_sides_and_colours_follow_the_winner()
    {
        var won = Result().View;
        Assert.Equal("VICTOR  -  TEAM B  [YOUR PICK]", TownBookieResultUILayout<int>.Heading(won, 0));
        Assert.Equal("DEFEATED  -  TEAM A", TownBookieResultUILayout<int>.Heading(won, 1));
        var draw = Result(winning: -1, selected: 0, tone: 0).View;
        Assert.Equal("TEAM A  [YOUR PICK]", TownBookieResultUILayout<int>.Heading(draw, 0));
        Assert.Equal("TEAM B", TownBookieResultUILayout<int>.Heading(draw, 1));
        Assert.Equal((0, 1), (TownBookieResultUILayout<int>.DisplaySide(-1, 0), TownBookieResultUILayout<int>.DisplaySide(-1, 1)));
        Assert.Equal(TownBookieResultUILayout<int>.Neutral, TownBookieResultUILayout<int>.ColumnColour(-1, 0));
        Assert.Equal(TownBookieResultUILayout<int>.Loser, TownBookieResultUILayout<int>.ColumnColour(0, 1));
        Assert.Equal((.90f, .27f, .20f), TownBookieResultUILayout<int>.OutcomeColour(2));
        Assert.Equal(TownBookieResultUILayout<int>.Gold, TownBookieResultUILayout<int>.OutcomeColour(0));
    }

    [Fact]   // Layout :206-207: the title and payout measured at the widths of the previous layout (100 at creation)
    public void A_long_payout_measured_at_its_old_width_widens_the_window()
    {
        var cards = Result().Cards;
        var wide = new TownBookieResultUiView<int>(true, 2, 0, new TownBookie.ResultView
        {
            Title = "WAGER WON", Context = "", Payout = new string('x', 120), WinningSide = 0, SelectedSide = 0, Tone = 1,
        }, cards, "k2");
        // at creation (100): the 1200 px payout wraps at 100: 100 + 80 < 1050, the columns decide: 1056
        var first = TownBookieResultUILayout<int>.Compute(wide, new BookieResultLayoutInput { FontHeight = 20 }, M);
        Assert.Equal(1056, first.Layout.Window.W);
        Assert.Equal(1030, first.PayoutWidth);   // the payout left 1030 wide: the next layout measures it there
        // measured at 1030: 1030 + 80 + 6 = 1116
        var second = TownBookieResultUILayout<int>.Compute(wide, new BookieResultLayoutInput { FontHeight = 20, PayoutWidthBefore = first.PayoutWidth }, M);
        Assert.Equal(1116, second.Layout.Window.W);
        // measured at 1200: 1200 + 80 + 6 = 1286
        var w = TownBookieResultUILayout<int>.Compute(wide, new BookieResultLayoutInput { FontHeight = 20, PayoutWidthBefore = 1200 }, M);
        Assert.Equal(1286, w.Layout.Window.W);
    }

    [Fact]   // Layout :190-277 at 1600 x 900, font 21: a 3v2 result needs two card rows; the window is capped at 90% of the view (no scroll), so the
             // Close button lies below the client (kept as the C++; the town-bookie scenario's oracle for closing by the title bar)
    public void A_two_row_result_at_1600_by_900_puts_Close_below_the_client()
    {
        var r = TownBookieResultUILayout<int>.Compute(Result(), new BookieResultLayoutInput { ViewWidth = 1600, ViewHeight = 900, FontHeight = 21 }, M);
        var l = r.Layout;
        // body max(21, 20) = 21, gap 10; content 2 * (2 * 252 + 30) + 30 = 1098; outer 1104; card (529 - 30) / 2 = 249; portrait min(189, 229) = 189
        // cardHeight 189 + 40 + 42 = 271; teams 42 + 20 + 542 + 10 = 614; desired 42 + 42 + 614 + 105 + 41 + 100 = 944; outer min(810, 989) = 810
        Assert.Equal(new LayoutRect(248, 45, 1104, 810), l.Window);
        Assert.Equal((1098, 765), (l.ClientWidth, l.ClientHeight));
        // victor column: three cards in two rows, the third at 51 + 271 + 10 = 332, 259 high (name two lines: 209 + 40 + 10)
        Assert.Equal(new LayoutRect(10, 332, 252, 259), l.Rects[R.Card(0, 2)]);
        Assert.Equal(new LayoutRect(10, 82, 534, 601), l.Rects[R.Column(0)]);
        // payout at 82 + 591 + 10 + 10 = 693 (two lines: 40); Close at 693 + 40 + 20 = 753, max(252, 115) = 252 wide: 753 + 41 = 794 > 765
        Assert.Equal(new LayoutRect(10, 693, 1078, 40), l.Rects[R.Payout]);
        var close = l.Rects[R.Close];
        Assert.Equal(new LayoutRect(423, 753, 252, 41), close);
        Assert.True(close.Y + close.H > l.ClientHeight);
    }

    [Fact]   // Layout :325-354: the roster's bar from the previous layout narrows the first pass's view; the canvas fits, the bar goes, the second
             // pass lays out at the full width: the same rectangles as without it
    public void A_bar_left_from_before_settles_in_the_second_pass()
    {
        var with = Compute(Betting(), barBefore: true);
        var without = Compute(Betting());
        Assert.False(with.ScrollBar);
        Assert.Equal((1648, 622), with.Canvas[N.Roster]);
        Assert.Equal(new LayoutRect(864, 40, 382, 280), with.Rects[N.Card(1, 0)]);   // pass 1 had teamWidth (1608 - 60 - 20) / 2 = 764
        foreach (var (name, rect) in without.Rects) Assert.Equal(rect, with.Rects[name]);
    }
}
