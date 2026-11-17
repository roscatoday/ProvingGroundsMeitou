using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.5: TownChallengeWallet.cpp's rules over Meitou's long money (Core/Glue/TownChallengeWalletRule.cs).</summary>
public class TownChallengeWalletRuleTests
{
    [Theory]
    [InlineData(0L, 0)]
    [InlineData(20000L, 20000)]
    [InlineData(-5L, -5)]
    [InlineData((long)int.MaxValue + 1, int.MaxValue)]
    [InlineData(long.MaxValue, int.MaxValue)]
    [InlineData((long)int.MinValue - 1, int.MinValue)]
    [InlineData(long.MinValue, int.MinValue)]
    public void BalanceIsTheMoneyClampedToTheIntRange(long money, int balance) => Assert.Equal(balance, TownChallengeWalletRule.Balance(money));

    [Fact]
    public void TakeNeedsAPositiveAmountTheMoneyCovers()
    {
        Assert.True(TownChallengeWalletRule.CanTake(500, 500));
        Assert.False(TownChallengeWalletRule.CanTake(499, 500));
        Assert.False(TownChallengeWalletRule.CanTake(500, 0));
        Assert.False(TownChallengeWalletRule.CanTake(500, -1));
        Assert.True(TownChallengeWalletRule.CanTake(long.MaxValue, int.MaxValue));
        Assert.False(TownChallengeWalletRule.CanTake(long.MinValue, 1));
    }

    [Fact]
    public void CreditRefusesANegativePurseAndAnIntOverflow()
    {
        Assert.True(TownChallengeWalletRule.CanCredit(0, 724));
        Assert.False(TownChallengeWalletRule.CanCredit(-1, 724));
        Assert.False(TownChallengeWalletRule.CanCredit(100, 0));
        Assert.True(TownChallengeWalletRule.CanCredit(int.MaxValue - 724, 724));
        Assert.False(TownChallengeWalletRule.CanCredit(int.MaxValue - 723, 724));
        Assert.False(TownChallengeWalletRule.CanCredit((long)int.MaxValue + 10, 1));   // read as INT_MAX: the C++'s overflow guard refuses
    }
}
