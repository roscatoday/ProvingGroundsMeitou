using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for RewardsDialoguePolicy.</summary>
public class RewardsDialoguePolicyTests
{
    [Fact]
    public void Dialogue_policy()
    {
        Assert.True(RewardsDialoguePolicy.IsNullId("166-Proving Grounds.mod"));
        Assert.True(RewardsDialoguePolicy.IsNullId("166-Proving Grounds.mod\0tail"));   // strcmp stops at the NUL
        Assert.False(RewardsDialoguePolicy.IsNullId(null));
        Assert.False(RewardsDialoguePolicy.IsNullId("166-Proving Grounds.mo"));
        Assert.False(RewardsDialoguePolicy.IsNullId(""));
        const string N = RewardsDialoguePolicy.NullStringId;
        Assert.Equal(RewardsDialoguePolicy.TradeRoute.PassThrough, RewardsDialoguePolicy.ResolveTrade(N, "x", false));
        Assert.Equal(RewardsDialoguePolicy.TradeRoute.PassThrough, RewardsDialoguePolicy.ResolveTrade(N, N, true));
        Assert.Equal(RewardsDialoguePolicy.TradeRoute.PassThrough, RewardsDialoguePolicy.ResolveTrade("a", null, true));
        Assert.Equal(RewardsDialoguePolicy.TradeRoute.OpenForSecond, RewardsDialoguePolicy.ResolveTrade(N, "a", true));
        Assert.Equal(RewardsDialoguePolicy.TradeRoute.OpenForFirst, RewardsDialoguePolicy.ResolveTrade("a", N, true));
        Assert.Equal(RewardsDialoguePolicy.TradeRoute.OpenForFirst, RewardsDialoguePolicy.ResolveTrade(null, N, true));
        Assert.Equal(RewardsDialoguePolicy.PendingDecision.DiscardPending, RewardsDialoguePolicy.ResolvePending(true, false));
        Assert.Equal(RewardsDialoguePolicy.PendingDecision.DiscardPending, RewardsDialoguePolicy.ResolvePending(false, false));
        Assert.Equal(RewardsDialoguePolicy.PendingDecision.WaitForDialogue, RewardsDialoguePolicy.ResolvePending(true, true));
        Assert.Equal(RewardsDialoguePolicy.PendingDecision.OpenPending, RewardsDialoguePolicy.ResolvePending(false, true));
        Assert.Equal(0, (int)RewardsDialoguePolicy.TradeRoute.PassThrough);
        Assert.Equal(2, (int)RewardsDialoguePolicy.PendingDecision.OpenPending);
    }
}
