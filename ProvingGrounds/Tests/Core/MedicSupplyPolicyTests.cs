using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/MedicSupplyPolicy.cs (task 1.5); each expected value is read off the C++.

public class MedicSupplyPolicyTests
{
    sealed class Access : MedicSupplyPolicy.IAccess
    {
        public bool Scratch = true, Main, Backpack, GiveOk = true;
        public readonly List<string> Calls = new();
        public bool IsScratchMedic() { Calls.Add("scratch"); return Scratch; }
        public bool MainInventoryHasUsableKit(MedicSupplyPolicy.Supply s) { Calls.Add("main:" + s); return Main; }
        public bool BackpackHasUsableKit(MedicSupplyPolicy.Supply s) { Calls.Add("back:" + s); return Backpack; }
        public bool GiveReplacement(string id) { Calls.Add("give:" + id); return GiveOk; }
        public void RechargeUsedKit() { Calls.Add("recharge"); }
    }

    [Fact]   // MedicSupplyPolicy.h:7-15
    public void ShouldRestock_and_ids()
    {
        Assert.True(MedicSupplyPolicy.ShouldRestock(true, 1.0f, 0.0f, false));
        Assert.True(MedicSupplyPolicy.ShouldRestock(true, 0.01f, -0.5f, false));
        Assert.False(MedicSupplyPolicy.ShouldRestock(false, 1.0f, 0.0f, false));
        Assert.False(MedicSupplyPolicy.ShouldRestock(true, 0.0f, 0.0f, false));
        Assert.False(MedicSupplyPolicy.ShouldRestock(true, 1.0f, 0.1f, false));
        Assert.False(MedicSupplyPolicy.ShouldRestock(true, 1.0f, 0.0f, true));
        Assert.Equal("18020-gamedata.base", MedicSupplyPolicy.ReplacementId(MedicSupplyPolicy.Supply.RobotRepair));
        Assert.Equal("1359-gamedata.base", MedicSupplyPolicy.ReplacementId(MedicSupplyPolicy.Supply.FirstAid));
    }

    [Fact]   // MedicSupplyPolicy.h:17-29, call order included
    public void RestockAfterUse_paths()
    {
        var a = new Access { Scratch = false };
        Assert.Equal(MedicSupplyPolicy.RestockResult.NoRestock, MedicSupplyPolicy.RestockAfterUse(1.0f, 0.0f, MedicSupplyPolicy.Supply.FirstAid, ref a));
        Assert.Equal(new[] { "scratch" }, a.Calls);

        a = new Access();
        Assert.Equal(MedicSupplyPolicy.RestockResult.NoRestock, MedicSupplyPolicy.RestockAfterUse(1.0f, 0.5f, MedicSupplyPolicy.Supply.FirstAid, ref a));
        Assert.Equal(new[] { "scratch" }, a.Calls);

        a = new Access { Main = true };
        Assert.Equal(MedicSupplyPolicy.RestockResult.NoRestock, MedicSupplyPolicy.RestockAfterUse(1.0f, 0.0f, MedicSupplyPolicy.Supply.RobotRepair, ref a));
        Assert.Equal(new[] { "scratch", "main:RobotRepair" }, a.Calls);

        a = new Access { Backpack = true };
        Assert.Equal(MedicSupplyPolicy.RestockResult.NoRestock, MedicSupplyPolicy.RestockAfterUse(1.0f, 0.0f, MedicSupplyPolicy.Supply.FirstAid, ref a));
        Assert.Equal(new[] { "scratch", "main:FirstAid", "back:FirstAid" }, a.Calls);

        a = new Access();
        Assert.Equal(MedicSupplyPolicy.RestockResult.ReplacementSpawned, MedicSupplyPolicy.RestockAfterUse(1.0f, 0.0f, MedicSupplyPolicy.Supply.RobotRepair, ref a));
        Assert.Equal(new[] { "scratch", "main:RobotRepair", "back:RobotRepair", "give:18020-gamedata.base" }, a.Calls);

        a = new Access { GiveOk = false };
        Assert.Equal(MedicSupplyPolicy.RestockResult.UsedKitRecharged, MedicSupplyPolicy.RestockAfterUse(1.0f, 0.0f, MedicSupplyPolicy.Supply.FirstAid, ref a));
        Assert.Equal(new[] { "scratch", "main:FirstAid", "back:FirstAid", "give:1359-gamedata.base", "recharge" }, a.Calls);
    }

    struct StructAccess : MedicSupplyPolicy.IAccess
    {
        public int Recharges;
        public string? Given;
        public bool IsScratchMedic() => true;
        public bool MainInventoryHasUsableKit(MedicSupplyPolicy.Supply s) => false;
        public bool BackpackHasUsableKit(MedicSupplyPolicy.Supply s) => false;
        public bool GiveReplacement(string id) { Given = id; return false; }
        public void RechargeUsedKit() { ++Recharges; }
    }

    [Fact]   // MedicSupplyPolicy.h:17 `Access& access`: a change made through it is seen by the caller
    public void Struct_access_mutation_is_visible_after_the_call()
    {
        var a = new StructAccess();
        Assert.Equal(MedicSupplyPolicy.RestockResult.UsedKitRecharged,
            MedicSupplyPolicy.RestockAfterUse(2.0f, 0.0f, MedicSupplyPolicy.Supply.FirstAid, ref a));
        Assert.Equal(1, a.Recharges);
        Assert.Equal("1359-gamedata.base", a.Given);
    }
}
