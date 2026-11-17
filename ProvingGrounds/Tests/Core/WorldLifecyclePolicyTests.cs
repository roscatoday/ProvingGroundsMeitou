using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/WorldLifecyclePolicy.cs (task 1.5); each expected value is read off the C++.

public class WorldLifecyclePolicyTests
{
    [Fact]   // WorldLifecyclePolicy.h:14-20
    public void Any_activity_postpones()
    {
        for (int b = 0; b < 32; ++b)
        {
            var a = new WorldLifecyclePolicy.ArenaActivity
            {
                IngressPending = (b & 1) != 0, WalkInPending = (b & 2) != 0, MatchActive = (b & 4) != 0,
                PrisonerActivity = (b & 8) != 0, TownActivity = (b & 16) != 0,
            };
            Assert.Equal(b != 0, WorldLifecyclePolicy.ShouldPostponeAutosave(a));
        }
    }
}
