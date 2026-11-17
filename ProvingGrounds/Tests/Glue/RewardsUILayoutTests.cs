using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.RewardsUINames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The Rewards window's pixel arithmetic (RewardsUI.cpp:177-1065, task 5.3b) over a fake text measure (10 px a character, 20 px a line;
/// a button caption 11 px a character), at 1920 x 1080 and font height 20 (gap 10, body 20). Expected numbers worked by hand from the C++.</summary>
public class RewardsUILayoutTests
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

    sealed class Host : IRewardsUIHost<int>
    {
        public readonly List<int> Squad = [1, 2, 3];
        public readonly HashSet<string> Unlocks = new(StringComparer.Ordinal);
        public IReadOnlyList<int> PlayerSquad() => Squad;
        public bool Valid(int c) => c != 0;
        public bool IsDead(int c) => c == 2;
        public string Name(int c) => c == 1 ? "Ann" : "Cat";
        public int GetMarks(int c) => c == 1 ? 10 : 500;
        public bool HasFactionUnlock(string id) => Unlocks.Contains(id);
        public ArenaRewards.Offer ResolveOffer(int c, ArenaRewards.Piece piece)
        {
            var first = ArenaRewards.FirstTier(piece);
            return new ArenaRewards.Offer { Kind = ArenaRewards.OfferKind.Claim, Tier = first, Cost = ArenaRewards.Get(piece, first).Cost };
        }
        public void Error(string line) { }
        public string? RewardIconRecord(ArenaRewards.Piece piece) => "icon-" + piece;
        public bool RecordExists(string recordId) => true;
    }

    static readonly Fake M = new();

    static RewardsUiView<int> View(RewardsCatalogTab tab, params string[] unlocks)
    {
        var h = new Host();
        foreach (var u in unlocks) h.Unlocks.Add(u);
        var logic = new RewardsUILogic<int>(tab);
        logic.Show(h, true, 1);
        return logic.BuildView();
    }

    static RewardsLayoutInput Input(int viewW = 1920, int viewH = 1080, string status = "Purchases go to Ann.",
        IReadOnlyList<(int W, int H)?>? pieces = null) => new()
    {
        ViewWidth = viewW, ViewHeight = viewH, FontHeight = 20, Status = status, PieceIcons = pieces ?? [],
    };

    static RewardsWindowLayout Compute(RewardsUiView<int> v, RewardsLayoutInput i) => RewardsUILayout<int>.Compute(v, i, M);

    static string WeaponLicence => ArenaRewards.GetWeaponLicence().StableId;
    static string Licence(ArenaRewards.ArmourSet s) => ArenaRewards.GetLicence(s).StableId;

    [Fact]
    public void TheWindowFillsNinetyPercentWithTheCloseStatusSubtitleAndThreeScrollViews()
    {
        var l = Compute(View(RewardsCatalogTab.General), Input());
        Assert.Equal(10, l.Gap);
        Assert.Equal(20, l.Body);
        Assert.Equal(new LayoutRect(96, 54, 1728, 972), l.Window);   // FitWindowToScreen :945-956
        Assert.Equal(1722, l.ClientWidth);                           // Kenshi_WindowCX chrome 6 x 45
        Assert.Equal(927, l.ClientHeight);
        // LayoutWindow :961-965: "Close catalogue" 15 x 11 + 4 gaps wide, RowHeight 20 + 2 gaps, centred at the bottom
        Assert.Equal(new LayoutRect(758, 877, 205, 40), l.Rects[N.Close]);
        // the status line: one text line + the Kenshi_WordWrap insets 30, above the close button (:967-970, :1020)
        Assert.Equal(new LayoutRect(10, 817, 1702, 50), l.Rects[N.Status]);
        Assert.Equal(new LayoutRect(10, 10, 1702, 50), l.Rects[N.Subtitle]);
        Assert.False(l.Stacked);   // 1702 >= max(720, 50 body)
        Assert.False(l.Compact);
        Assert.DoesNotContain(N.Subtitle, l.Hidden);
        // the fighter column: FighterWidth = min(320, max(220, 357)) = 320, its heading above (:1024-1037)
        Assert.Equal(new LayoutRect(10, 70, 320, 20), l.Rects[N.FightersHeading]);
        Assert.Equal(new LayoutRect(10, 100, 320, 707), l.Rects[N.FighterScroll]);
        // the profile: as tall as its content (career + one row of tabs) + the scroll insets (:1046-1053); the details below it
        Assert.Equal(new LayoutRect(350, 70, 1362, 105), l.Rects[N.ProfileScroll]);
        Assert.Equal(new LayoutRect(350, 185, 1362, 622), l.Rects[N.DetailScroll]);
        Assert.Contains(N.Title, l.Hidden);
        Assert.Contains(N.SelectedHeading, l.Hidden);
        Assert.Contains(N.Selected, l.Hidden);
    }

    [Fact]
    public void TheProfileHoldsTheCareerLineAndThreeTabsInOneRow()
    {
        var l = Compute(View(RewardsCatalogTab.Armour), Input());
        // LayoutProfile :906-943: view 1362 - 15 = 1347, width 1327; career one line; tabs max(7 x 11) + 4 gaps = 117 wide -> 3 columns of 435
        Assert.Equal(new LayoutRect(10, 10, 1327, 20), l.Rects[N.Career]);
        Assert.Equal(new LayoutRect(10, 40, 435, 40), l.Rects[N.GeneralTab]);
        Assert.Equal(new LayoutRect(455, 40, 435, 40), l.Rects[N.ArmourTab]);
        Assert.Equal(new LayoutRect(900, 40, 435, 40), l.Rects[N.WeaponsTab]);
        Assert.Equal(90, l.Profile.Content);
        Assert.False(l.Profile.Bar);
    }

    [Fact]
    public void FighterRowsSkipTheDeadAndKeepTheirSquadIndexInTheirNames()
    {
        var v = View(RewardsCatalogTab.General);
        Assert.Equal([0, 2], v.Rows.Select(r => r.SquadIndex));
        var l = Compute(v, Input());
        // LayoutFighters :590-623: view 320 - 15 = 305, width 285, portrait max(40, 3 body) = 60, text at 60 + 2 gaps
        Assert.Equal(new LayoutRect(10, 10, 285, 80), l.Rects[N.Row(0)]);
        Assert.Equal(new LayoutRect(10, 100, 285, 80), l.Rects[N.Row(2)]);
        Assert.Equal(new LayoutRect(10, 10, 60, 60), l.Rects[N.RowPortrait(0)]);
        Assert.Equal(new LayoutRect(80, 10, 195, 20), l.Rects[N.RowName(0)]);
        Assert.Equal(new LayoutRect(80, 40, 195, 20), l.Rects[N.RowMarks(0)]);
        Assert.Equal(new LayoutRect(0, 0, 5, 80), l.Rects[N.RowRail(0)]);
        Assert.DoesNotContain(N.RowRail(0), l.Hidden);   // the selected fighter's rail
        Assert.Contains(N.RowRail(2), l.Hidden);
        Assert.False(l.Rects.ContainsKey(N.Row(1)));
        Assert.Equal(190, l.Fighter.Content);
        Assert.Equal(new RewardsCanvasInfo(190, 692, false), l.Fighter);
        Assert.Equal((305, 692), l.Canvas[N.FighterScroll]);
    }

    [Fact]
    public void GeneralTabBlueprintRowThenTwoColumnsOfSuppliesAndTheBarNarrowsTheSecondPass()
    {
        var l = Compute(View(RewardsCatalogTab.General), Input());
        // content 690 > view 607: the bar shows and the second pass lays out at 1347 - 20 = 1327, width 1307 (:744-748, :893-894)
        Assert.True(l.Detail.Bar);
        Assert.Equal(690, l.Detail.Content);
        Assert.Equal(new LayoutRect(10, 10, 1307, 20), l.Rects[N.BlueprintsHeading]);
        int b1 = (int)ArenaRewards.GeneralItem.ArenaBuildingsI;
        // LayoutGeneralCard :683-737, not fixed: icon 80 at (gap, top + gap), BUY centred on the 100-high row at the right
        Assert.Equal(new LayoutRect(20, 50, 80, 80), l.Rects[N.GeneralIcon(b1)]);
        Assert.Equal(new LayoutRect(110, 50, 1124, 20), l.Rects[N.GeneralName(b1)]);
        Assert.Equal(new LayoutRect(1234, 70, 33 + 40, 40), l.Rects[N.GeneralBuy(b1)]);
        Assert.Equal(new LayoutRect(10, 150, 1307, 20), l.Rects[N.SuppliesHeading]);
        // six supplies, 2 columns of (1307 - 10) / 2 = 648, cards max(8 body, 120) = 160 high, BUY at the bottom right
        int first = (int)ArenaRewards.GeneralItem.AdvancedFirstAid, second = (int)ArenaRewards.GeneralItem.PitSurgeonRoll;
        Assert.Equal(new LayoutRect(575, 290, 73, 40), l.Rects[N.GeneralBuy(first)]);
        Assert.Equal(new LayoutRect(1233, 290, 73, 40), l.Rects[N.GeneralBuy(second)]);
        int splint = (int)ArenaRewards.GeneralItem.SplintKit;   // the fifth in display order: row 2, column 0
        Assert.Equal(new LayoutRect(20, 190 + 2 * 170, 80, 80), l.Rects[N.GeneralIcon(splint)]);
        Assert.DoesNotContain(N.BlueprintsHeading, l.Hidden);
        Assert.Contains(N.Claim(0), l.Hidden);
        Assert.Contains(N.LicenceHeading(0), l.Hidden);
    }

    [Fact]
    public void WeaponCardsTwoColumnsWithTheTextureFittedIntoTheIconArea()
    {
        var icons = Enumerable.Range(0, (int)ArenaRewards.Piece.Count).Select(_ => ((int, int)?)(512, 64)).ToArray();
        var l = Compute(View(RewardsCatalogTab.Weapons, WeaponLicence), Input(pieces: icons));
        Assert.True(l.Detail.Bar);   // 8 weapons, 4 rows of 140 + the licence lines: 670 > 607
        Assert.Contains(N.WeaponBuy, l.Hidden);   // licence active: no button
        Assert.Equal(new LayoutRect(10, 10, 1307, 20), l.Rects[N.WeaponHeading]);
        Assert.Equal(new LayoutRect(10, 40, 1307, 20), l.Rects[N.WeaponDetails]);
        int g = (int)ArenaRewards.Piece.Greatsword;
        // LayoutRewardCard :625-681 at (10, 70, 648, 140): image 70 high, max(45 % of 648, 3.5 x 70) = 245 wide
        Assert.Equal(new LayoutRect(553, 160, 95, 40), l.Rects[N.Claim(g)]);
        Assert.Equal(new LayoutRect(285, 80, 363, 20), l.Rects[N.Name(g)]);
        Assert.Equal(new LayoutRect(30, 99, 245, 31), l.Rects[N.Icon(g)]);   // 512 x 64 scaled by 245 / 512, centred
        Assert.Equal(new LayoutRect(10, 70, 648, 140), l.Rects[N.Accent(g)]);
        Assert.Equal(new LayoutRect(0, 0, 40, 140), l.Rects[N.Fade(g, 0)]);
        Assert.Equal(new LayoutRect(607, 0, 41, 140), l.Rects[N.Fade(g, 15)]);   // 648 * 15 / 16 = 607
        Assert.Equal(new LayoutRect(0, 0, 5, 140), l.Rects[N.Border(g, 0)]);
        Assert.Equal(new LayoutRect(647, 0, 1, 140), l.Rects[N.Border(g, 3)]);
        Assert.Contains(N.Icon(g), l.IconsShown);
        int katana = (int)ArenaRewards.Piece.Katana;   // the second visible weapon in piece order: column 1
        Assert.Equal(658 + 10, l.Rects[N.Accent(katana)].X);
    }

    [Fact]
    public void ALockedArmourRowShowsItsButtonAndTheManifestIconsUnderIt()
    {
        var icons = Enumerable.Range(0, (int)ArenaRewards.Piece.Count).Select(_ => ((int, int)?)(256, 256)).ToArray();
        var l = Compute(View(RewardsCatalogTab.Armour), Input(pieces: icons));
        // nothing unlocked: each row = heading, details, UNLOCK LICENCE (or PREREQUISITE REQUIRED), its pieces' icons (max(32, 2 body) = 40)
        Assert.Equal(new LayoutRect(10, 10, l.Rects[N.LicenceHeading(0)].W, 20), l.Rects[N.LicenceHeading(0)]);
        Assert.Equal(new LayoutRect(10, 70, "UNLOCK LICENCE".Length * 11 + 40, 40), l.Rects[N.LicenceBuy(0)]);
        Assert.DoesNotContain(N.LicenceBuy(0), l.Hidden);
        int footwraps = (int)ArenaRewards.Piece.PitfighterFootwraps, pants = (int)ArenaRewards.Piece.PitfighterPants;
        Assert.Equal(new LayoutRect(10, 130, 40, 40), l.Rects[N.Icon(footwraps)]);
        Assert.Equal(new LayoutRect(60, 130, 40, 40), l.Rects[N.Icon(pants)]);
        Assert.Contains(N.Icon(footwraps), l.IconsShown);
        Assert.DoesNotContain(N.Icon(footwraps), l.Hidden);
        Assert.Contains(N.Accent(footwraps), l.Hidden);   // the card itself is not shown (its licence is locked)
        Assert.Contains(N.Claim(footwraps), l.Hidden);
        // without a texture the manifest icon stays hidden (:827)
        var none = Compute(View(RewardsCatalogTab.Armour), Input());
        Assert.Contains(N.Icon(footwraps), none.Hidden);
    }

    [Fact]
    public void UnlockedArmourRowsLayFourColumnsOfCardsWithTheBootsIconCentred()
    {
        var v = View(RewardsCatalogTab.Armour, Licence(ArenaRewards.ArmourSet.T1));
        var l = Compute(v, Input());
        int boots = (int)ArenaRewards.Piece.RetainerBoots;
        Assert.True(v.Cards[boots].Visible);
        var accent = l.Rects[N.Accent(boots)];
        var claim = l.Rects[N.Claim(boots)];
        var icon = l.Rects[N.Icon(boots)];
        int columns = RewardsLayout.GridColumns(l.Rects[N.DetailScroll].W - 15 - (l.Detail.Bar ? 20 : 0) - 20, 300, 10, 4);
        Assert.Equal(4, columns);
        Assert.Equal(160, accent.H);   // max(8 body, 120)
        // boots: imageHeight = min(160 - 40 - 30, max(100, 80)) = 90, icon 7/8 of it, centred in the 90 square (:657-669)
        Assert.Equal(new LayoutRect(accent.X + 20 + (90 - 78) / 2, accent.Y + 10 + (90 - 78) / 2, 78, 78), icon);
        Assert.Equal(accent.Right - claim.W - 10, claim.X);
        Assert.Equal(accent.Bottom - 40 - 10, claim.Y);
    }

    [Fact]
    public void ANarrowViewStacksAndATinyOneGoesCompact()
    {
        var stacked = Compute(View(RewardsCatalogTab.General), Input(900, 900));
        Assert.True(stacked.Stacked);
        Assert.False(stacked.Compact);
        var f = stacked.Rects[N.FighterScroll];
        var p = stacked.Rects[N.ProfileScroll];
        Assert.Equal(f.W, p.W);
        Assert.Equal(f.X, p.X);
        Assert.Equal(f.Bottom + 10, p.Y);
        var compact = Compute(View(RewardsCatalogTab.General), Input(800, 300));
        Assert.True(compact.Compact);
        Assert.Contains(N.Subtitle, compact.Hidden);
        Assert.Contains(N.FightersHeading, compact.Hidden);
        Assert.True(compact.Rects[N.FighterScroll].Y >= 10);
    }

    [Fact]
    public void ALongStatusLineTakesAtMostTwoLines()
    {
        string longText = string.Concat(Enumerable.Repeat("Enforcer Plated Boots - High Grade replaced for Shutori. ", 10));
        var l = Compute(View(RewardsCatalogTab.General), Input(status: longText));
        Assert.Equal(2 * 20 + 30, l.Rects[N.Status].H);   // Min(text + insets, 2 body + insets)
        Assert.Equal(877 - 10 - 70, l.Rects[N.Status].Y);
    }

    [Fact]
    public void EveryLaidOutWidgetHasAPositiveSizeAtSeveralViewsAndFonts()
    {
        foreach (var (w, h, font) in new[] { (1920, 1080, 17), (2560, 1440, 20), (3840, 2160, 30), (1280, 720, 17), (1024, 600, 17) })
            foreach (var tab in new[] { RewardsCatalogTab.General, RewardsCatalogTab.Armour, RewardsCatalogTab.Weapons })
            {
                var l = RewardsUILayout<int>.Compute(View(tab, WeaponLicence, Licence(ArenaRewards.ArmourSet.T1)),
                    Input(w, h) with { FontHeight = font }, M);
                foreach (var (name, r) in l.Rects)
                    Assert.True(r.W >= 1 && r.H >= 1, $"{w}x{h} font {font} {tab}: {name} {r}");
            }
    }

    [Fact]
    public void WidgetNamesParseBack()
    {
        Assert.True(N.TryClaim(N.Claim(17), out int piece) && piece == 17);
        Assert.True(N.TryGeneralBuy(N.GeneralBuy(3), out int item) && item == 3);
        Assert.True(N.TryLicenceBuy(N.LicenceBuy(2), out int row) && row == 2);
        Assert.True(N.TryRow(N.Row(5), out int s) && s == 5);
        Assert.False(N.TryRow(N.RowPortrait(5), out _));
        Assert.False(N.TryClaim(N.Name(3), out _));
        Assert.False(N.TryRow("PG_RewardFighter_", out _));
        Assert.Equal("PG_RewardMarks_4_Rail", N.RowRail(4));
        Assert.Equal(UiButtonEvent.Click, N.ButtonEvent(N.Claim(0)));
    }

    [Fact]
    public void EveryWidgetTheCxxBindsTheWheelToScrollsItsCanvas()
    {
        static RewardsScrollModel.Canvas? C(string w) => N.WheelCanvas(w, out var c) ? c : null;
        var P = RewardsScrollModel.Canvas.Profile; var F = RewardsScrollModel.Canvas.Fighter; var D = RewardsScrollModel.Canvas.Detail;
        // :1095-1099 the tabs and the profile labels
        foreach (var w in new[] { N.GeneralTab, N.ArmourTab, N.WeaponsTab, N.SelectedHeading, N.Selected, N.Career }) Assert.Equal(P, C(w));
        // :1246-1274 the rows (portrait / name / Marks unreachable in the port: NeedMouse false)
        foreach (var w in new[] { N.Row(3), N.RowPortrait(3), N.RowName(3), N.RowMarks(3) }) Assert.Equal(F, C(w));
        // :1125-1128, :1157-1170, :1191-1193, :1208-1210
        foreach (var w in new[] { N.Icon(0), N.Name(24), N.Progress(7), N.Claim(12), N.GeneralIcon(0), N.GeneralName(7), N.GeneralDetails(3),
                     N.GeneralBuy(5), N.BlueprintsHeading, N.SuppliesHeading, N.LicenceHeading(0), N.LicenceDetails(3), N.LicenceBuy(2),
                     N.WeaponHeading, N.WeaponDetails, N.WeaponBuy })
            Assert.Equal(D, C(w));
        // not bound: rails, accents and their parts, scroll views, the window, the status line, the close button, other names
        foreach (var w in new[] { N.RowRail(3), N.Accent(2), N.Fade(2, 3), N.Border(2, 0), N.DetailScroll, N.FighterScroll, N.Window, N.Status, N.Subtitle,
                     N.Close, N.FightersHeading, N.Title, "PG_General_", "PG_General_x_Buy", "PG_Reward__Name", "PG_Licence_1_Accent", "PG_RewardMarks_" })
            Assert.Null(C(w));
        Assert.Equal(D, C("PG_GeneralBlueprintsHeading"));   // the heading, not a PG_General_%d part
        Assert.Null(C("PG_GeneralBlueprintsHeading_Icon"));
    }

    [Fact]
    public void TheDetailBarDisappearsInTheSecondPassWhenTheNarrowerViewStillFits()
    {
        // the bar shown after the previous layout: pass 1 at 1327, the canvas fits -> the bar goes, pass 2 at 1347 (:744-748, :893-894)
        var l = Compute(View(RewardsCatalogTab.General), Input(1920, 1440) with { DetailBarBefore = true });   // content 690 in a taller view
        Assert.False(l.Detail.Bar);
        Assert.Equal(1347 - 20, l.Rects[N.BlueprintsHeading].W);   // laid at the wide view (no bar)
        Assert.Equal((1347, l.Detail.View), l.Canvas[N.DetailScroll]);
    }

    [Fact]
    public void FourteenFightersOverflowTheFighterCanvasAndItsRowsNarrowForTheBar()
    {
        var h = new Host();
        h.Squad.Clear();
        h.Squad.AddRange(Enumerable.Range(10, 14));
        var logic = new RewardsUILogic<int>();
        logic.Show(h, true, 10);
        var l = Compute(logic.BuildView(), Input());
        Assert.True(l.Fighter.Bar);
        Assert.Equal(10 + 14 * 90, l.Fighter.Content);   // rows 80 high + one gap each, from gap
        // view 320 - 15 - 20 = 285, width 265
        Assert.Equal(new LayoutRect(10, 10, 265, 80), l.Rects[N.Row(0)]);
        Assert.Equal(new LayoutRect(10, 10 + 13 * 90, 265, 80), l.Rects[N.Row(13)]);
        Assert.Equal((285, 10 + 14 * 90), l.Canvas[N.FighterScroll]);
    }

    [Fact]
    public void TheScrollModelReadsBackTheShownOffset()
    {
        var s = new RewardsScrollModel();
        s.Observe(RewardsScrollModel.Canvas.Detail, 120);
        s.OnTabChanged(RewardsCatalogTab.General, RewardsCatalogTab.Armour);
        Assert.Equal(120, s.TabOffset(RewardsCatalogTab.General));
        s.Observe(RewardsScrollModel.Canvas.Fighter, -5);
        Assert.Equal(0, s.Fighter);
    }
}
