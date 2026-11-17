using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.4c: ArenaMedical.cpp:135-155 RestockMedicIfDepleted (ArenaMedicalLogic.RestockMedicIfDepleted over Core MedicSupplyPolicy) with a
/// fake medic: the replacement ids, the fallback recharge and its error line, the C++ lines byte for byte, and every no-restock case.</summary>
public class MedicRestockTests
{
    struct Access : MedicSupplyPolicy.IAccess
    {
        public bool Scratch, MainKit, BackpackKit, GiveOk;
        public string? Given;
        public bool Recharged;
        public bool IsScratchMedic() => Scratch;
        public bool MainInventoryHasUsableKit(MedicSupplyPolicy.Supply supply) => MainKit;
        public bool BackpackHasUsableKit(MedicSupplyPolicy.Supply supply) => BackpackKit;
        public bool GiveReplacement(string itemId) { Given = itemId; return GiveOk; }
        public void RechargeUsedKit() => Recharged = true;
    }

    [Fact]
    public void The_charges_before_are_the_prefixs_for_the_same_kit_and_just_above_zero_for_a_kit_taken_this_tick()
    {
        Assert.Equal(3.5f, ArenaMedicalLogic.KitChargesBefore(true, 3.5f));
        Assert.Equal(float.Epsilon, ArenaMedicalLogic.KitChargesBefore(false, 0f));
        // a kit taken this tick and used up restocks; the same kit already empty before the tick does not
        var a = new Access { Scratch = true, GiveOk = true };
        Assert.NotNull(ArenaMedicalLogic.RestockMedicIfDepleted(true, false, ArenaMedicalLogic.KitChargesBefore(false, 0f), 0f, ref a, "M", out _));
        var b = new Access { Scratch = true, GiveOk = true };
        Assert.Null(ArenaMedicalLogic.RestockMedicIfDepleted(true, false, ArenaMedicalLogic.KitChargesBefore(true, 0f), 0f, ref b, "M", out _));
    }

    [Fact]
    public void A_scratch_medics_spent_first_aid_kit_is_replaced_by_an_advanced_kit()
    {
        var a = new Access { Scratch = true, GiveOk = true };
        string? line = ArenaMedicalLogic.RestockMedicIfDepleted(true, false, 3f, 0f, ref a, "Medic", out bool error);
        Assert.Equal("Proving Grounds: Scratch medic supply restored actor=Medic type=advanced first aid method=replacement item", line);
        Assert.False(error);
        Assert.Equal("1359-gamedata.base", a.Given);
        Assert.False(a.Recharged);
    }

    [Fact]
    public void A_spent_robot_kit_is_replaced_by_a_robotics_repair_kit()
    {
        var a = new Access { Scratch = true, GiveOk = true };
        string? line = ArenaMedicalLogic.RestockMedicIfDepleted(true, true, 1f, -0.5f, ref a, "Medic", out _);
        Assert.Equal("Proving Grounds: Scratch medic supply restored actor=Medic type=skeleton repair method=replacement item", line);
        Assert.Equal("18020-gamedata.base", a.Given);
    }

    [Fact]
    public void A_refused_replacement_recharges_the_spent_kit_and_is_also_an_error()
    {
        var a = new Access { Scratch = true, GiveOk = false };
        string? line = ArenaMedicalLogic.RestockMedicIfDepleted(true, false, 2f, 0f, ref a, "invalid", out bool error);
        Assert.Equal("Proving Grounds: Scratch medic supply restored actor=invalid type=advanced first aid method=recharged spent kit fallback", line);
        Assert.True(error);
        Assert.True(a.Recharged);
    }

    [Theory]
    [InlineData(false, true, false, false, 3f, 0f)]   // no kit equipment
    [InlineData(true, false, false, false, 3f, 0f)]   // not a Scratch medic
    [InlineData(true, true, true, false, 3f, 0f)]     // another usable kit in the main inventory
    [InlineData(true, true, false, true, 3f, 0f)]     // another in the backpack
    [InlineData(true, true, false, false, 3f, .1f)]   // charges left
    [InlineData(true, true, false, false, 0f, 0f)]    // nothing before
    public void No_restock(bool isKit, bool scratch, bool mainKit, bool backpack, float before, float after)
    {
        var a = new Access { Scratch = scratch, MainKit = mainKit, BackpackKit = backpack, GiveOk = true };
        Assert.Null(ArenaMedicalLogic.RestockMedicIfDepleted(isKit, false, before, after, ref a, "Medic", out bool error));
        Assert.False(error);
        Assert.Null(a.Given);
        Assert.False(a.Recharged);
    }
}
