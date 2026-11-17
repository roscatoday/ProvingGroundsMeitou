using ProvingGrounds.Core;
using static ProvingGrounds.Core.PrisonerMatchLogic;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/PrisonerMatchLogicTests.cpp (one [Fact] per Expect group, same values).</summary>
public class PrisonerMatchLogicTests
{
    [Fact]   // PrisonerMatchLogicTests.cpp:19-21
    public void Handler_counts()
    {
        Assert.True(HasEnoughHandlers(0, 0), "0 prisoners ok");
        Assert.True(HasEnoughHandlers(2, 2), "exact handlers ok");
        Assert.False(HasEnoughHandlers(2, 1), "shortage");
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:23-26
    public void Shortage_message()
    {
        Assert.Equal("Need 2 handlers for 2 prisoners (1 available)", FormatHandlerShortage(2, 1));
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:28-37
    public void Prefer_original()
    {
        bool[] free = { true, true };
        float[] dist = { 100.0f, 10.0f };
        bool[] inRange = { true, true };
        bool[] reserved = { false, false };
        Assert.Equal(0, ChooseReturnCage(0, free, dist, inRange, reserved, 2).Index);
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:39-48
    public void Nearest_free_when_original_busy()
    {
        bool[] free = { false, true, true };
        float[] dist = { 1.0f, 50.0f, 20.0f };
        bool[] inRange = { true, true, true };
        bool[] reserved = { false, false, false };
        Assert.Equal(2, ChooseReturnCage(0, free, dist, inRange, reserved, 3).Index);
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:50-59
    public void None_when_out_of_registry_range()
    {
        bool[] free = { true };
        float[] dist = { 5.0f };
        bool[] inRange = { false };
        bool[] reserved = { false };
        Assert.Equal(-1, ChooseReturnCage(0, free, dist, inRange, reserved, 1).Index);
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:61-70
    public void Skip_reserved()
    {
        bool[] free = { true, true };
        float[] dist = { 10.0f, 20.0f };
        bool[] inRange = { true, true };
        bool[] reserved = { true, false };
        Assert.Equal(1, ChooseReturnCage(-1, free, dist, inRange, reserved, 2).Index);
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:72-83
    public void Natural_enemy_suppression()
    {
        Assert.False(ShouldSuppressNaturalEnemy(true, true, false), "spar opponents never suppressed");
        Assert.True(ShouldSuppressNaturalEnemy(false, true, false), "match prisoner vs outsider suppressed");
        Assert.True(ShouldSuppressNaturalEnemy(false, false, true), "outsider vs match prisoner suppressed");
        Assert.False(ShouldSuppressNaturalEnemy(false, false, false), "non-prisoners not suppressed");
    }

    [Fact]   // PrisonerMatchLogicTests.cpp:85-96
    public void Return_actions()
    {
        Assert.Equal(ReturnAction.EscortWalk, ChooseReturnAction(true, false));   // conscious escort
        Assert.Equal(ReturnAction.Carry, ChooseReturnAction(false, false));       // ko carry
        Assert.Equal(ReturnAction.Skip, ChooseReturnAction(false, true));         // dead skip
    }
}
