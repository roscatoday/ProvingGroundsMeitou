using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaIngressPolicy.cs (task 1.5); each expected value is read off the C++.

public class ArenaIngressPolicyHandTests
{
    [Fact]   // ArenaIngressPolicy.h:15-62
    public void Ui_and_countdown_rules()
    {
        for (int b = 0; b < 8; ++b)
        {
            bool expected = b != 0;
            Assert.Equal(expected, ArenaIngressPolicy.ShouldOpenInteractUiImmediately((b & 1) != 0, (b & 2) != 0, (b & 4) != 0));
        }
        Assert.True(ArenaIngressPolicy.ShouldBindInteractSource(false));
        Assert.False(ArenaIngressPolicy.ShouldBindInteractSource(true));

        for (int b = 0; b < 64; ++b)
        {
            var o = new ArenaIngressPolicy.IngressObservation
            {
                AllPrisonersReleased = (b & 1) != 0, AllFightersValid = (b & 2) != 0, AllAtArrivalRadius = (b & 4) != 0,
                AllAtSettleRadius = (b & 8) != 0, AllAtFormationRadius = (b & 16) != 0, AllSettled = (b & 32) != 0,
            };
            Assert.Equal((b & 7) == 7, ArenaIngressPolicy.CanOpenApproachUi(o));
            Assert.Equal((b & 19) == 19, ArenaIngressPolicy.CanBeginCountdown(o));
        }
        for (int b = 0; b < 4; ++b)
        {
            var r = new ArenaIngressPolicy.FightReadiness { AllPrisonersReleased = (b & 1) != 0, AllFightersAtFormation = (b & 2) != 0 };
            Assert.Equal(b == 3, ArenaIngressPolicy.CanBeginCountdown(r));
        }
    }
}
