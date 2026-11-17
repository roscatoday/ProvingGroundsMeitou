using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A frame-loop step that throws is logged once per step per world, not every tick, and its failures are counted.</summary>
public class StepFailureLogTests
{
    [Fact]
    public void LogsEachStepOnceAndCountsEveryFailure()
    {
        var log = new StepFailureLog(3);
        Assert.True(log.Record(1));
        Assert.False(log.Record(1));
        Assert.False(log.Record(1));
        Assert.True(log.Record(0));
        Assert.False(log.Record(0));
        Assert.True(log.Record(2));
        Assert.Equal(3, log.FailedSteps);
        Assert.Equal(2, log.Count(0));
        Assert.Equal(3, log.Count(1));
        Assert.Equal(1, log.Count(2));
    }

    [Fact]
    public void AFreshLogPerWorldStartsClean()
    {
        var first = new StepFailureLog(2);
        first.Record(0);
        var second = new StepFailureLog(2);
        Assert.Equal(0, second.FailedSteps);
        Assert.Equal(0, second.Count(0));
        Assert.True(second.Record(0));
        Assert.Equal(1, second.FailedSteps);
    }
}
