using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaRewards;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for what the golden vectors cannot reach: the weapons' unused Standard slots (Get never returns them), the
/// PGConfig override maps (the stub has none: no pg_config.json ships), stamping twice, and per-flow isolation.</summary>
public class ArenaRewardsTests
{
    [Fact]
    public void Weapon_standard_slots_are_the_empty_rows_of_the_table()
    {
        // C++: ArenaRewards.cpp:128, 164, 170, 248, 254, 260, 266, 272 `{ PieceX, TierStandard, "", "", "", 0, 0, 0, 0 }`
        Catalogue c = Catalogue.Shipped();
        foreach (Piece p in new[] { Piece.Greatsword, Piece.Katana, Piece.Polearm, Piece.WarlordLongsword, Piece.WarlordKatana,
                     Piece.Mace, Piece.Sabre, Piece.Hacker })
        {
            Reward r = c.Rewards[(int)p, (int)Tier.Standard];
            Assert.Equal(p, r.Piece);
            Assert.Equal(Tier.Standard, r.Tier);
            Assert.Equal("", r.PieceName);
            Assert.Equal("", r.TierName);
            Assert.Equal("", r.StringId);
            Assert.Null(r.ModelStringId);
            Assert.Null(r.ManufacturerStringId);
            Assert.Equal(0, r.QualityLevel);
            Assert.Equal(0, r.Cost);
        }
    }

    [Fact]
    public void Ids_are_picked_by_id_not_by_display_name()
    {
        Catalogue c = Catalogue.Shipped();
        Reward polearm = c.Rewards[(int)Piece.Polearm, (int)Tier.HighGrade];
        Reward hacker = c.Rewards[(int)Piece.Hacker, (int)Tier.HighGrade];
        Assert.Equal("Pit Cleaver", polearm.TierName);
        Assert.Equal("Pit Cleaver", hacker.TierName);
        Assert.Equal("86-Proving Grounds.mod", polearm.ModelStringId);
        Assert.Equal("146-Proving Grounds.mod", hacker.ModelStringId);
        Assert.Equal("139-Proving Grounds.mod", hacker.StringId);
        Assert.Equal("Warlord Hacker", hacker.PieceName);
        Assert.Equal("Warlord's Judgement", c.Rewards[(int)Piece.Greatsword, (int)Tier.Masterwork].TierName);
        // charges as the code says (the changelog says 1000/100/500)
        Assert.Equal(1200, c.GeneralRewards[(int)GeneralItem.PitSurgeonRoll].Charges);
        Assert.Equal(125, c.GeneralRewards[(int)GeneralItem.BoneSetterBraces].Charges);
        Assert.Equal(600, c.GeneralRewards[(int)GeneralItem.IronmenderCase].Charges);
    }

    [Fact]
    public void Overrides_replace_a_price_once_and_are_then_scaled()
    {
        // C++: PGConfig.cpp:891-929: the key is "gear.<piece>.<tierName>" / "supplies.<id>" / "licences.<id>"; a hit is erased,
        // counted, and passed through ScaleMarks.
        PGConfig.State state = PGConfig.State.Shipped();
        state.Multiplier = 2.5;
        state.Gear["gear.weapon.mace.high"] = 10;
        state.Gear["gear.armour.warlord.cloak.standard"] = 3;
        state.Gear["gear.weapon.mace.standard"] = 99;   // weapons have no Standard slot: never asked for, stays in the map
        state.Supplies["supplies.supply.splint"] = 7;
        state.Licences["licences.weapon.all"] = 20;
        state.Licences["licences.armour.warlord"] = 1;
        using (UseStampedForTest(state))
        {
            Assert.Equal(25, Get(Piece.Mace, Tier.HighGrade).Cost);          // 10 * 2.5
            Assert.Equal(250, Get(Piece.Mace, Tier.Specialist).Cost);        // shipped 100 * 2.5
            Assert.Equal(8, Get(Piece.Cloak, Tier.Standard).Cost);           // 3 * 2.5 = 7.5 -> 8
            Assert.Equal(18, GetGeneral(GeneralItem.SplintKit).Cost);        // 7 * 2.5 = 17.5 -> 18
            Assert.Equal(50, GetWeaponLicence().Cost);
            Assert.Equal(3, GetLicence(ArmourSet.T3).Cost);                 // 1 * 2.5 = 2.5 -> 3
            Assert.Equal(125, GetLicence(ArmourSet.T1).Cost);
            Assert.Equal(2, state.AppliedGear);
            Assert.Equal(1, state.AppliedSupplies);
            Assert.Equal(2, state.AppliedLicences);
            Assert.Single(state.Gear);
            Assert.True(state.Gear.ContainsKey("gear.weapon.mace.standard"));
            Assert.Empty(state.Supplies);
            Assert.Empty(state.Licences);
        }
    }

    [Fact]
    public void GearCost_rejects_null_id_and_bad_tier()
    {
        PGConfig.State state = PGConfig.State.Shipped();
        state.Gear["gear.x.standard"] = 1;
        using (PGConfig.UseForTest(state))
        {
            Assert.Equal(40, PGConfig.GearCost(null, 0, 40));
            Assert.Equal(40, PGConfig.GearCost("x", -1, 40));
            Assert.Equal(40, PGConfig.GearCost("x", 4, 40));
            Assert.Equal(1, PGConfig.GearCost("x", 0, 40));
            Assert.Equal(40, PGConfig.GearCost("x", 0, 40));   // erased after the first use
            Assert.Equal(5, PGConfig.SupplyCost(null, 5));
            Assert.Equal(5, PGConfig.LicenceCost(null, 5));
        }
    }

    [Fact]
    public void Stamping_twice_scales_twice_like_the_cpp()
    {
        using (UseStampedForTest(2.5))
        {
            ApplyPriceOverrides();   // the second stamp
            Assert.Equal(625, Get(Piece.Greatsword, Tier.HighGrade).Cost);   // 100 -> 250 -> 625
        }
    }

    [Fact]
    public async Task A_test_catalogue_is_seen_only_in_its_flow()
    {
        int shipped = Get(Piece.Helmet, Tier.Masterwork).Cost;
        Assert.Equal(200, shipped);
        using (UseStampedForTest(0.5))
        {
            Assert.Equal(100, Get(Piece.Helmet, Tier.Masterwork).Cost);
            int other = await Task.Run(() => Get(Piece.Helmet, Tier.Masterwork).Cost);   // a flow started here inherits the override
            Assert.Equal(100, other);
        }
        Assert.Equal(200, Get(Piece.Helmet, Tier.Masterwork).Cost);
    }

    [Fact]
    public void ReplaceCost_half_up()
    {
        Assert.Equal(8, ReplaceCost(15));
        Assert.Equal(5, ReplaceCost(10));
        Assert.Equal(0, ReplaceCost(0));
        Assert.Equal(0, ReplaceCost(-3));
        Assert.Equal(1073741823, ReplaceCost(2147483646));   // 2147483647 / 2
        // INT_MAX + 1 is signed overflow in the C++ (MSVC wraps to INT_MIN, then / 2); the port wraps the same way
        Assert.Equal(-1073741824, ReplaceCost(int.MaxValue));
    }

    [Fact]
    public void Global_catalogue_holds_shipped_prices()
    {
        Assert.False(HasTestOverride);
        Catalogue shipped = Catalogue.Shipped();
        for (int p = 0; p < (int)Piece.Count; ++p)
            for (int t = 0; t < (int)Tier.MaxCount; ++t)
                if (IsValidTier((Piece)p, (Tier)t))
                    Assert.Equal(shipped.Rewards[p, t].Cost, Get((Piece)p, (Tier)t).Cost);
        for (int i = 0; i < (int)GeneralItem.Count; ++i)
            Assert.Equal(shipped.GeneralRewards[i].Cost, GetGeneral((GeneralItem)i).Cost);
        for (int set = 0; set < 3; ++set)
            Assert.Equal(shipped.Licences[set].Cost, GetLicence((ArmourSet)set).Cost);
        Assert.Equal(50, GetWeaponLicence().Cost);
    }

    [Fact]
    public void Stamping_under_a_config_override_without_a_test_catalogue_throws()
    {
        using (PGConfig.UseMarksMultiplierForTest(2.5))
            Assert.Throws<InvalidOperationException>(() => ApplyPriceOverrides());
        Assert.Equal(100, Get(Piece.Greatsword, Tier.HighGrade).Cost);
    }

    [Fact]
    public void UseStampedForTest_restores_both_globals()
    {
        using (UseStampedForTest(0.5))
        {
            Assert.True(HasTestOverride);
            Assert.True(PGConfig.HasTestOverride);
        }
        Assert.False(HasTestOverride);
        Assert.False(PGConfig.HasTestOverride);
    }
}
