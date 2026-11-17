using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/ArenaIngressPolicyTests.cpp (one [Fact] for its asserts, same values).</summary>
public class ArenaIngressPolicyTests
{
    [Fact]   // ArenaIngressPolicyTests.cpp:7-10
    public void Prisoner_release_advances_only_when_all_three_hold()
    {
        Assert.True(ArenaIngressPolicy.ShouldAdvancePrisonerRelease(true, true, true));
        Assert.False(ArenaIngressPolicy.ShouldAdvancePrisonerRelease(false, true, true));
        Assert.False(ArenaIngressPolicy.ShouldAdvancePrisonerRelease(true, false, true));
        Assert.False(ArenaIngressPolicy.ShouldAdvancePrisonerRelease(true, true, false));
    }
}
