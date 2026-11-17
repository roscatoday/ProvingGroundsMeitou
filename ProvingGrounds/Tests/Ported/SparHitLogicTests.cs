using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/SparHitLogicTests.cpp. SparHitLogic is one three-branch function: these cases cover every
/// branch and both sides of the 0.01 threshold, so it has no golden vector.</summary>
public class SparHitLogicTests
{
    [Fact]   // SparHitLogicTests.cpp:17-19
    public void Force_soft_miss_when_not_dodging() => Assert.True(SparHitLogic.ShouldForceApplySparMiss(true, 10.0f, false));

    [Fact]   // SparHitLogicTests.cpp:20-22
    public void Never_force_successful_dodge() => Assert.False(SparHitLogic.ShouldForceApplySparMiss(true, 10.0f, true));

    [Fact]   // SparHitLogicTests.cpp:23-25
    public void No_force_when_not_a_miss() => Assert.False(SparHitLogic.ShouldForceApplySparMiss(false, 10.0f, false));

    [Fact]   // SparHitLogicTests.cpp:26-28
    public void No_force_when_damage_empty() => Assert.False(SparHitLogic.ShouldForceApplySparMiss(true, 0.0f, false));

    [Fact]   // SparHitLogicTests.cpp:29-31
    public void No_force_at_damage_threshold_boundary() => Assert.False(SparHitLogic.ShouldForceApplySparMiss(true, 0.01f, false));

    [Fact]   // SparHitLogicTests.cpp:32-34
    public void Force_just_above_damage_threshold() => Assert.True(SparHitLogic.ShouldForceApplySparMiss(true, 0.011f, false));
}
