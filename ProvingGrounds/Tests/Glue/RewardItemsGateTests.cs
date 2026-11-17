using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The rewards part's two gates (task 5.2a review, Core/Glue/RewardItemsGate.cs, used by src/Engine/Items.cs): a refresh that threw stops
/// a NEW purchase but never the moves that finish or roll back one already under way, so a previous grade taken out can always go back.</summary>
public class RewardItemsGateTests
{
    [Fact]
    public void AllPresentAllowsBoth()
    {
        Assert.True(RewardItemsGate.MayStart(membersMissing: false, refreshFailed: false));
        Assert.True(RewardItemsGate.MayMove(membersMissing: false));
    }

    [Fact]
    public void AFailedRefreshStopsANewPurchaseButNotTheRollback()
    {
        Assert.False(RewardItemsGate.MayStart(membersMissing: false, refreshFailed: true));
        Assert.True(RewardItemsGate.MayMove(membersMissing: false));   // Give(new) / Give(previous, restore) / TakeOut still run
    }

    [Fact]
    public void AMissingMemberStopsEverything()
    {
        Assert.False(RewardItemsGate.MayStart(membersMissing: true, refreshFailed: false));
        Assert.False(RewardItemsGate.MayMove(membersMissing: true));   // no Inventory.Add to call at all
    }
}
