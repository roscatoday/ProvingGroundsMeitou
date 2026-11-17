using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The rules half of RewardsUI.cpp (task 5.3a): RebuildRows' list and selection (:1230-1311), Show's status line (:1592-1617),
/// UpdateCatalogVisibility (:1313-1387) and UpdateDetails' texts (:1389-1556), the fighter / tab clicks (:463-521), Close / AbandonWorldState
/// (:1618-1641), the failure recovery (AbandonWindow :569-588), the purchase gate and the scroll model.</summary>
public class RewardsUILogicTests
{
    sealed class Host : IRewardsUIHost<int>
    {
        public readonly List<int> Squad = [];
        public readonly HashSet<int> Invalid = [], Dead = [];
        public readonly Dictionary<int, string> Names = [];
        public readonly Dictionary<int, int> Marks = [];
        public readonly HashSet<string> Unlocks = new(StringComparer.Ordinal);
        public readonly Dictionary<(int, ArenaRewards.Piece), ArenaRewards.Offer> Offers = [];
        public readonly List<string> Errors = [];
        public bool Throw;
        public int OfferCalls;
        public IReadOnlyList<int> PlayerSquad() => Throw ? throw new InvalidOperationException("boom") : Squad;
        public bool Valid(int c) => c != 0 && !Invalid.Contains(c);
        public bool IsDead(int c) => Dead.Contains(c);
        public string Name(int c) => Names.TryGetValue(c, out var n) ? n : "";
        public int GetMarks(int c) => Marks.TryGetValue(c, out var m) ? m : 0;
        public bool HasFactionUnlock(string id) => Unlocks.Contains(id);
        public ArenaRewards.Offer ResolveOffer(int c, ArenaRewards.Piece piece)
        {
            OfferCalls++;
            if (Offers.TryGetValue((c, piece), out var o)) return o;
            var first = ArenaRewards.FirstTier(piece);
            return new ArenaRewards.Offer { Kind = ArenaRewards.OfferKind.Claim, Tier = first, Cost = ArenaRewards.Get(piece, first).Cost };
        }
        public void Error(string line) => Errors.Add(line);
        public readonly HashSet<ArenaRewards.Piece> NoIcon = [];
        public int IconCalls;
        public string? RewardIconRecord(ArenaRewards.Piece piece) { IconCalls++; return NoIcon.Contains(piece) ? null : "icon-" + piece; }
        public bool RecordExists(string recordId) => recordId != ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.SplintKit).StringId;
    }

    static Host ThreeFighters()
    {
        var h = new Host();
        h.Squad.AddRange([1, 2, 3, 4]);
        h.Names[1] = "Ann"; h.Names[2] = "Bob"; h.Names[3] = "Cat"; h.Names[4] = "Dan";
        h.Marks[1] = 10; h.Marks[2] = 500; h.Marks[3] = 0;
        h.Dead.Add(4);   // dead: not listed
        return h;
    }

    static RewardsPieceCardView Card(RewardsUiView<int> v, ArenaRewards.Piece p) => v.Cards[(int)p];
    static RewardsGeneralCardView Gen(RewardsUiView<int> v, ArenaRewards.GeneralItem i) => v.General[(int)i];

    [Fact]
    public void ShowListsLiveSquadInOrderAndPrefersTheTrader()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 2);
        var v = l.BuildView();
        Assert.True(v.RequestOpen);
        Assert.Equal(1, v.RequestSerial);
        Assert.Equal([1, 2, 3], v.Rows.Select(r => r.Character));
        Assert.Equal(["Ann", "Bob", "Cat"], v.Rows.Select(r => r.Name));
        Assert.Equal(["10 Marks", "500 Marks", "0 Marks"], v.Rows.Select(r => r.Marks));
        Assert.Equal([false, true, false], v.Rows.Select(r => r.Selected));
        Assert.Equal([false, true, false], v.Rows.Select(r => r.RailVisible));
        Assert.True(v.HasSelected);
        Assert.Equal(2, v.Selected);
        Assert.Equal("Purchases go to Bob.", v.Status);
        Assert.Equal("500 ARENA MARKS", v.Career);
        Assert.Equal("Proving Grounds - Arena Rewards", v.Caption);
        Assert.Equal("NULL'S CRUCIBLE CATALOGUE  |  Victory buys privilege", v.Subtitle);
        Assert.Equal("FIGHTERS", v.FightersHeading);
        Assert.Equal("Close catalogue", v.CloseCaption);
        Assert.True(v.TabsVisible);
    }

    [Fact]
    public void APreferredFighterOutsideTheSquadFallsBackToTheFirstRow()
    {
        var h = ThreeFighters();
        h.Names[9] = "Null";
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 9);
        Assert.Equal(1, l.BuildView().Selected);
        Assert.Equal("Purchases go to Ann.", l.BuildView().Status);
        l.Show(h, true, 4);   // dead: not a row
        Assert.Equal(1, l.Selected);
        h.Invalid.Add(2);
        l.Show(h, true, 2);   // not valid: g_preferred = NULL
        Assert.Equal(1, l.Selected);
    }

    [Fact]
    public void NoEligibleFighter()
    {
        var h = new Host();
        var l = new RewardsUILogic<int>();
        l.Show(h, false, 0);
        var v = l.BuildView();
        Assert.True(v.RequestOpen);
        Assert.False(v.HasSelected);
        Assert.Empty(v.Rows);
        Assert.Equal("No eligible fighters available.", v.Status);
        Assert.Equal("NO ELIGIBLE FIGHTER  |  0 ARENA MARKS", v.Career);
        Assert.All(v.Cards, c =>
        {
            Assert.Equal("CLAIM", c.ClaimCaption);
            Assert.False(c.ClaimEnabled);
            Assert.False(c.HasOffer);
            Assert.Equal("Select a fighter to review requirements.", c.Progress);
            Assert.Equal(RewardsLayout.RarityIndex((int)ArenaRewards.FirstTier(c.Piece), false), c.Rarity);
        });
        Assert.All(v.General, g => Assert.False(g.BuyEnabled));
        Assert.All(v.General, g => Assert.Equal("", g.Details));   // the widget's creation caption: the C++ writes none here
        Assert.All(v.Licences, x => Assert.False(x.BuyEnabled));
        Assert.False(v.WeaponLicence.BuyEnabled);
        Assert.Equal(0, h.OfferCalls);
    }

    [Fact]
    public void GeneralTabTexts()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 1);   // 10 Marks
        var v = l.BuildView();
        Assert.Equal(RewardsCatalogTab.General, v.Tab);
        Assert.True(v.HeadingsVisible);
        var one = ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.ArenaBuildingsI);
        Assert.True(Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).Visible);
        Assert.False(Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsII).Visible);   // until blueprint.arena1
        Assert.Equal(one.Name, Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).Name);
        Assert.Equal($"PHYSICAL BLUEPRINT BOOK  |  {one.Cost} Marks  |  INSUFFICIENT MARKS", Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).Details);
        Assert.Equal("BUY", Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).BuyCaption);
        Assert.False(Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).BuyEnabled);
        var kit = ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.SplintKit);
        Assert.StartsWith("REPEATABLE FIGHT SUPPLY  |  " + kit.Cost + " Marks", Gen(v, ArenaRewards.GeneralItem.SplintKit).Details);
        Assert.All(v.Cards, c => Assert.False(c.Visible));
        Assert.All(v.Licences, x => Assert.False(x.Visible));

        h.Unlocks.Add("blueprint.arena1");
        l.OnFighterClicked(h, 2);   // 500 Marks
        v = l.BuildView();
        Assert.True(Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsII).Visible);
        Assert.Equal($"PHYSICAL BLUEPRINT BOOK  |  {one.Cost} Marks", Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).Details);
        Assert.Equal(one.GrantsUnlockId == "blueprint.arena1" ? "BUY AGAIN" : "BUY", Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).BuyCaption);
        Assert.True(Gen(v, ArenaRewards.GeneralItem.ArenaBuildingsI).BuyEnabled);
        Assert.Equal("Purchases go to Ann.", v.Status);   // a fighter click leaves the status line
    }

    [Fact]
    public void ArmourTabLicencesAndCards()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 1);   // 10 Marks
        Assert.True(l.OnCatalogTabClicked(h, RewardsCatalogTab.Armour));
        Assert.False(l.OnCatalogTabClicked(h, RewardsCatalogTab.Armour));
        var v = l.BuildView();
        Assert.False(v.HeadingsVisible);
        Assert.Equal(["|  PITFIGHTER", "|  RETAINER LEATHER", "|  PLATE ENFORCER", "|  WARLORD"], v.Licences.Select(x => x.Heading));
        var t1 = ArenaRewards.GetLicence(ArenaRewards.ArmourSet.T1);
        Assert.Equal($"FACTION LICENCE  |  {t1.Cost} Marks  |  INSUFFICIENT MARKS  |  Footwraps / Pants / Harness / Jaw Guard", v.Licences[0].Details);
        Assert.Equal("LOCKED  |  Previous armour licence required  |  Plated Boots / Legplates / Chest / Helmet", v.Licences[2].Details);
        Assert.Equal("PREREQUISITE REQUIRED", v.Licences[2].BuyCaption);
        Assert.Equal("UNLOCK LICENCE", v.Licences[0].BuyCaption);
        Assert.All(v.Licences, x => { Assert.True(x.Visible); Assert.True(x.BuyVisible); Assert.False(x.BuyEnabled); });
        Assert.All(v.Cards, c => Assert.False(c.Visible));   // no licence: no armour card

        h.Unlocks.Add(t1.StableId);
        l.OnFighterClicked(h, 2);   // 500 Marks
        v = l.BuildView();
        Assert.Equal("LICENCE ACTIVE  |  Footwraps / Pants / Harness / Jaw Guard", v.Licences[0].Details);
        Assert.True(v.Licences[0].Unlocked);
        Assert.False(v.Licences[0].BuyVisible);
        var t2 = ArenaRewards.GetLicence(ArenaRewards.ArmourSet.T2);
        Assert.Equal($"FACTION LICENCE  |  {t2.Cost} Marks  |  Plated Boots / Legplates / Chest / Helmet", v.Licences[2].Details);
        Assert.True(v.Licences[2].BuyEnabled);
        Assert.True(Card(v, ArenaRewards.Piece.PitfighterHarness).Visible);
        Assert.True(Card(v, ArenaRewards.Piece.RetainerHelmet).Visible);
        Assert.False(Card(v, ArenaRewards.Piece.EnforcerChest).Visible);
        Assert.False(Card(v, ArenaRewards.Piece.Greatsword).Visible);
        Assert.Equal([ArenaRewards.Piece.PlateBoots, ArenaRewards.Piece.PlateSkirt, ArenaRewards.Piece.PlateArmour, ArenaRewards.Piece.Cloak,
            ArenaRewards.Piece.Helmet], RewardsUILogic<int>.ManifestPieces(ArenaRewards.ArmourRow.Warlord));
    }

    [Fact]
    public void CardStatesClaimUpgradeReplaceComplete()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        var p = ArenaRewards.Piece.PitfighterHarness;
        var std = ArenaRewards.Get(p, ArenaRewards.Tier.Standard);
        var high = ArenaRewards.Get(p, ArenaRewards.Tier.HighGrade);
        h.Unlocks.Add(ArenaRewards.GetLicence(ArenaRewards.ArmourSet.T1).StableId);
        l.Show(h, true, 1);   // 10 Marks
        var c = Card(l.BuildView(), p);
        Assert.Equal($"NEXT: {std.TierName}  |  {std.Cost} Marks  |  INSUFFICIENT MARKS", c.Progress);
        Assert.Equal("CLAIM", c.ClaimCaption);
        Assert.False(c.ClaimEnabled);
        Assert.True(c.HasOffer);
        Assert.Equal(0, c.Rarity);

        h.Offers[(2, p)] = new ArenaRewards.Offer { Kind = ArenaRewards.OfferKind.Claim, Tier = ArenaRewards.Tier.HighGrade, Cost = high.Cost };
        l.OnFighterClicked(h, 2);
        c = Card(l.BuildView(), p);
        Assert.Equal($"NEXT: {high.TierName}  |  {high.Cost} Marks", c.Progress);
        Assert.Equal("UPGRADE", c.ClaimCaption);
        Assert.True(c.ClaimEnabled);
        Assert.Equal(1, c.Rarity);
        Assert.Equal(ArenaRewards.Tier.HighGrade, c.Offer.Tier);

        h.Offers[(2, p)] = new ArenaRewards.Offer { Kind = ArenaRewards.OfferKind.Replace, Tier = ArenaRewards.Tier.HighGrade, Cost = 7 };
        l.AfterPurchase(h);
        c = Card(l.BuildView(), p);
        Assert.Equal($"REPLACE: {high.TierName}  |  7 Marks", c.Progress);
        Assert.Equal("REPLACE", c.ClaimCaption);

        h.Offers[(2, p)] = new ArenaRewards.Offer { Kind = ArenaRewards.OfferKind.Complete, Tier = ArenaRewards.Tier.MaxCount, Cost = 0 };
        l.AfterPurchase(h);
        c = Card(l.BuildView(), p);
        Assert.Equal(ArenaRewards.Get(p, ArenaRewards.Tier.Masterwork).TierName + " CLAIMED", c.Progress);
        Assert.Equal("COMPLETE", c.ClaimCaption);
        Assert.False(c.ClaimEnabled);
        Assert.False(c.HasOffer);
        Assert.Equal(3, c.Rarity);
    }

    [Fact]
    public void WeaponsNeedTheirLicence()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 2);
        l.OnCatalogTabClicked(h, RewardsCatalogTab.Weapons);
        var v = l.BuildView();
        var w = ArenaRewards.GetWeaponLicence();
        Assert.Equal("|  WEAPONS LICENCE", v.WeaponLicence.Heading);
        Assert.Equal($"FACTION LICENCE  |  {w.Cost} Marks  |  Unlocks all weapon rewards", v.WeaponLicence.Details);
        Assert.True(v.WeaponLicence.BuyEnabled);
        Assert.True(v.WeaponLicence.Visible);
        var sword = Card(v, ArenaRewards.Piece.Greatsword);
        Assert.True(sword.Visible);
        Assert.Equal("WEAPONS LICENCE REQUIRED", sword.Progress);
        Assert.Equal("LOCKED", sword.ClaimCaption);
        Assert.False(sword.ClaimEnabled);
        Assert.Equal(1, sword.Rarity);   // FirstTier = High Grade
        Assert.All(v.Licences, x => Assert.False(x.Visible));

        h.Unlocks.Add(w.StableId);
        l.AfterPurchase(h);
        v = l.BuildView();
        Assert.Equal("LICENCE ACTIVE  |  All weapon rewards available", v.WeaponLicence.Details);
        Assert.False(v.WeaponLicence.BuyVisible);
        Assert.False(v.WeaponLicence.BuyEnabled);
        Assert.Equal("CLAIM", Card(v, ArenaRewards.Piece.Greatsword).ClaimCaption);   // offer at the first tier
    }

    [Fact]
    public void FighterClicksAndScrollResets()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 1);
        int resets = l.BuildView().DetailResets;
        Assert.True(l.OnFighterClicked(h, 1));   // the same fighter: no reset
        Assert.Equal(resets, l.BuildView().DetailResets);
        Assert.True(l.OnFighterClicked(h, 3));
        Assert.Equal(resets + 1, l.BuildView().DetailResets);
        Assert.False(l.OnFighterClicked(h, 4));   // no row
        Assert.Equal(3, l.Selected);
        h.Invalid.Add(3);
        Assert.True(l.OnFighterClicked(h, 3));   // the row stays; the selection is not valid: the no-fighter texts
        var v = l.BuildView();
        Assert.Equal("NO ELIGIBLE FIGHTER  |  0 ARENA MARKS", v.Career);
        Assert.All(v.Rows, r => Assert.False(r.Selected));
        Assert.Equal(["10 Marks", "500 Marks", "0 Marks"], v.Rows.Select(r => r.Marks));   // left as last written
    }

    [Fact]
    public void RowMarksRefreshOnlyOnAValidSelection()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 2);
        h.Marks[1] = 99;
        h.Marks[2] = 450;
        l.AfterPurchase(h);
        Assert.Equal(["99 Marks", "450 Marks", "0 Marks"], l.BuildView().Rows.Select(r => r.Marks));
        Assert.Equal("450 ARENA MARKS", l.BuildView().Career);
    }

    [Fact]
    public void CloseClosedFromWindowAndAbandon()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 2);
        int serial = l.BuildView().RequestSerial;
        l.Close();
        Assert.False(l.IsVisible);
        Assert.Equal(serial + 1, l.BuildView().RequestSerial);
        l.AfterPurchase(h);   // closed: no refresh
        l.Show(h, true, 2);
        Assert.False(l.OnClosedFromWindow(serial));
        Assert.True(l.OnClosedFromWindow(l.BuildView().RequestSerial));
        Assert.False(l.BuildView().RequestOpen);
        l.Show(h, true, 3);
        l.AbandonWorldState();
        var v = l.BuildView();
        Assert.False(v.RequestOpen);
        Assert.Empty(v.Rows);
        Assert.False(v.HasSelected);
    }

    [Fact]
    public void AShowWhileOpenKeepsTheRequestAndCountsTheShow()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 2);
        var a = l.BuildView();
        l.Show(h, true, 3);
        var b = l.BuildView();
        Assert.Equal(a.RequestSerial, b.RequestSerial);
        Assert.Equal(a.Shows + 1, b.Shows);
        Assert.Equal("Purchases go to Cat.", b.Status);
    }

    [Fact]
    public void TheTabIsKeptAcrossShows()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>(RewardsCatalogTab.Weapons);
        Assert.Equal(RewardsCatalogTab.Weapons, l.Tab);
        l.Show(h, true, 1);
        l.OnCatalogTabClicked(h, RewardsCatalogTab.Armour);
        l.Close();
        l.Show(h, true, 1);
        Assert.Equal(RewardsCatalogTab.Armour, l.BuildView().Tab);
    }

    [Fact]
    public void AFailureAbandonsTheWindow()
    {
        var h = ThreeFighters();
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 2);
        h.Throw = true;
        l.Show(h, true, 2);
        Assert.Equal(["Proving Grounds: native rewards content failed (Kenshi_Button1/Kenshi_PaintedWordWrapEmpty/Kenshi_TextboxPaintedText/ImageBox)"], h.Errors);
        var v = l.BuildView();
        Assert.False(v.RequestOpen);
        Assert.Empty(v.Rows);
        Assert.Equal("Earn Marks in rated arena matches.", v.Status);   // the widgets are made again by the next Show
        h.Throw = false;
        l.Show(h, true, 2);
        Assert.True(l.BuildView().RequestOpen);
        Assert.Equal("Purchases go to Bob.", l.BuildView().Status);
    }

    [Fact]
    public void IconsAreResolvedOnceOnTheSimSideAndPublished()
    {
        var h = ThreeFighters();
        h.NoIcon.Add(ArenaRewards.Piece.Mace);
        var l = new RewardsUILogic<int>();
        l.Show(h, true, 1);
        var v = l.BuildView();
        var sword = Card(v, ArenaRewards.Piece.Greatsword);
        var first = ArenaRewards.Get(ArenaRewards.Piece.Greatsword, ArenaRewards.FirstTier(ArenaRewards.Piece.Greatsword));
        Assert.Equal("icon-Greatsword", sword.Icon.Record);   // BindRewardIcon: the first tier's preview item
        Assert.Equal(first.QualityLevel, sword.Icon.Quality);
        Assert.Equal(first.ModelStringId, sword.Icon.Model);
        Assert.Equal(first.ManufacturerStringId, sword.Icon.Maker);
        Assert.Null(Card(v, ArenaRewards.Piece.Mace).Icon.Record);   // no item data: the icon is hidden
        var aid = ArenaRewards.GetGeneral(ArenaRewards.GeneralItem.AdvancedFirstAid);
        Assert.Equal(aid.StringId, Gen(v, ArenaRewards.GeneralItem.AdvancedFirstAid).Icon.Record);
        Assert.Equal(-1, Gen(v, ArenaRewards.GeneralItem.AdvancedFirstAid).Icon.Quality);   // CreateRewardItem(itemData, -1)
        Assert.Null(Gen(v, ArenaRewards.GeneralItem.SplintKit).Icon.Record);   // its record is missing
        int calls = h.IconCalls;
        Assert.Equal((int)ArenaRewards.Piece.Count, calls);   // each card once
        l.Show(h, true, 2);   // iconLoaded: resolved icons are kept; a missing one is asked again (the C++ retries an unloaded icon)
        Assert.Equal(calls + 1, h.IconCalls);
        Assert.Equal([ArenaRewards.Piece.PitfighterFootwraps, ArenaRewards.Piece.PitfighterPants, ArenaRewards.Piece.PitfighterHarness,
            ArenaRewards.Piece.PitfighterJawGuard], l.BuildView().Licences[0].Manifest);
    }

    [Fact]
    public void LastPurchasesAreKeptPerSender()
    {
        var none = RewardPurchaseResults<string>.Empty;
        Assert.Null(none.Of(RewardPurchaseSender.Window));
        var a = none.With(RewardPurchaseSender.Window, "w1");
        var b = a.With(RewardPurchaseSender.TestVerb, "t1");   // another sender's later purchase does not hide the window's
        Assert.Equal("w1", b.Of(RewardPurchaseSender.Window));
        Assert.Equal("t1", b.Of(RewardPurchaseSender.TestVerb));
        Assert.Null(b.Of(RewardPurchaseSender.TestGate));
        Assert.Null(a.Of(RewardPurchaseSender.TestVerb));   // immutable: published copies never change
    }

    [Fact]
    public void ColoursAndAlphas()
    {
        Assert.Equal((.22f, .52f, .95f), RewardsUiColours.Grade(-1));
        Assert.Equal((.22f, .76f, .36f), RewardsUiColours.Grade(1));
        Assert.Equal((.65f, .34f, .92f), RewardsUiColours.Grade(2));
        Assert.Equal((1f, .49f, .12f), RewardsUiColours.Grade(7));
        Assert.Equal(.12f, RewardsUiColours.FadeAlpha(0));
        Assert.Equal(.12f * 1 / 16f, RewardsUiColours.FadeAlpha(15));
        Assert.Equal(.9f, RewardsUiColours.BorderAlpha(0));
        Assert.Equal(.35f, RewardsUiColours.BorderAlpha(3));
        Assert.Equal((0.58f, 0.25f, 0.12f), RewardsUiColours.Rail);
    }

    [Fact]
    public void DisplayOrders()
    {
        Assert.Equal(17, RewardsUILogic<int>.ArmourDisplayOrder.Count);
        Assert.Equal(17, RewardsUILogic<int>.ArmourDisplayOrder.Distinct().Count());
        Assert.All(RewardsUILogic<int>.ArmourDisplayOrder, p => Assert.False(ArenaRewards.IsWeapon(p)));
        Assert.Equal(ArenaRewards.GeneralItem.AdvancedFirstAid, RewardsUILogic<int>.SupplyDisplayOrder[0]);
        Assert.Equal(6, RewardsUILogic<int>.SupplyDisplayOrder.Count);
    }

    [Fact]
    public void PurchaseGate()
    {
        var g = new RewardsPurchaseGate();
        Assert.Equal("Purchases go to Bob.", g.Status("Purchases go to Bob."));
        long a = g.Send(0, 1);
        Assert.Equal(1, a);
        Assert.True(g.Waiting(0));
        Assert.Equal(0, g.Send(0, 1));   // blocked: no result yet
        g.Observe(1, true, "Bought.", false, 1);
        Assert.False(g.Waiting(1));
        Assert.Equal("Bought.", g.Status("Purchases go to Bob."));
        long b = g.Send(1, 1);
        Assert.Equal(2, b);
        g.Observe(2, true, "", true, 1);   // the offer changed: the line stays
        Assert.Equal("Bought.", g.Status("x"));
        g.Observe(2, true, "", true, 2);   // a new Show: its own line
        Assert.Equal("Purchases go to Cat.", g.Status("Purchases go to Cat."));
        long c = g.Send(2, 2);
        Assert.Equal(3, c);
        Assert.True(g.Waiting(2));   // only its own sender's results count: a later number of another sender is not in this space
        g.Observe(3, true, "Third.", false, 2);
        Assert.False(g.Waiting(3));
        Assert.Equal("Third.", g.Status("Purchases go to Cat."));
        long d = g.Send(3, 2);
        g.Observe(d, true, "Late.", false, 3);   // sent under Show 2, seen under Show 3: not shown
        Assert.Equal("m", g.Status("m"));
    }

    [Fact]
    public void PurchaseGate_ANewWorldEndsTheWaitAndTheLine()
    {
        var g = new RewardsPurchaseGate();
        object w1 = new(), w2 = new();
        g.FollowWorld(w1);
        long a = g.Send(0, 1);
        g.Observe(a, true, "Bought.", false, 1);
        long b = g.Send(a, 1);
        Assert.True(g.Waiting(a));
        g.FollowWorld(w1);   // the same world: nothing changes
        Assert.True(g.Waiting(a));
        g.FollowWorld(w2);   // abandoned with a purchase pending: the next world publishes no result for it
        Assert.False(g.Waiting(0));
        Assert.Equal("m", g.Status("m"));
        Assert.Equal(1, g.Send(0, 1));   // the next world counts from 1 again (its results start empty)
        g.Reset();
        Assert.False(g.Waiting(0));
        Assert.True(b > 0);
    }

    [Fact]
    public void PurchaseGate_SendingBeforeTheLastResultIsReadKeepsTheStatusLine()
    {
        var g = new RewardsPurchaseGate();
        long a = g.Send(0, 1);
        g.Observe(a, true, "First bought.", false, 1);
        Assert.Equal("First bought.", g.Status("m"));
        long b = g.Send(a, 1);
        Assert.Equal(2, b);
        long c = g.Send(b, 1);   // b's result is published but not read yet: the wait ends, the line is not written
        Assert.Equal(3, c);
        Assert.Equal("First bought.", g.Status("m"));
    }

    [Fact]
    public void ScrollModel()
    {
        var s = new RewardsScrollModel();
        s.OnShow(RewardsCatalogTab.General);
        Assert.Equal(0, s.Detail);
        s.OnWheel(RewardsScrollModel.Canvas.Detail, -1, 20, 1000, 300);
        Assert.Equal(60, s.Detail);
        s.OnWheel(RewardsScrollModel.Canvas.Detail, 1, 20, 1000, 300);
        s.OnWheel(RewardsScrollModel.Canvas.Detail, 1, 20, 1000, 300);
        Assert.Equal(0, s.Detail);
        s.OnWheel(RewardsScrollModel.Canvas.Detail, -5, 300, 1000, 300);
        Assert.Equal(700, s.Detail);   // clamped to content - view
        s.OnTabChanged(RewardsCatalogTab.General, RewardsCatalogTab.Armour);
        Assert.Equal(0, s.Detail);
        Assert.Equal(700, s.TabOffset(RewardsCatalogTab.General));
        s.OnWheel(RewardsScrollModel.Canvas.Detail, -1, 10, 500, 100);
        s.AfterDetailLayout(RewardsCatalogTab.Armour, 500, 100);
        Assert.Equal(30, s.TabOffset(RewardsCatalogTab.Armour));
        s.OnTabChanged(RewardsCatalogTab.Armour, RewardsCatalogTab.General);
        Assert.Equal(700, s.Detail);
        s.AfterDetailLayout(RewardsCatalogTab.General, 400, 300);
        Assert.Equal(100, s.Detail);
        s.OnSelectionChanged();
        Assert.Equal(0, s.Detail);
        s.OnShow(RewardsCatalogTab.Armour);
        Assert.Equal(30, s.Detail);
        Assert.Equal(5, RewardsScrollModel.Wheel(5, 0, 20, 100, 10));
        s.Reset();
        Assert.Equal(0, s.TabOffset(RewardsCatalogTab.Armour));
    }
}
