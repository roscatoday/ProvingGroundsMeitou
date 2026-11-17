using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/PrisonerRecruitmentLogic.cs (task 1.5); each expected value is read off the C++.

public class PrisonerRecruitmentLogicTests
{
    [Fact]   // PrisonerRecruitmentLogic.cpp:8-27 at the shipped values: clamp(ceil(avg * 2.5 / 5) * 5, 25, 250)
    public void Shipped_prices()
    {
        using (PGConfig.UseForTest(PGConfig.State.Shipped()))
        {
            Assert.Equal(25, PrisonerRecruitmentLogic.RequiredMarks(0.0f));
            Assert.Equal(25, PrisonerRecruitmentLogic.RequiredMarks(-50.0f));
            Assert.Equal(25, PrisonerRecruitmentLogic.RequiredMarks(10.0f));
            Assert.Equal(30, PrisonerRecruitmentLogic.RequiredMarks(10.5f));
            Assert.Equal(125, PrisonerRecruitmentLogic.RequiredMarks(50.0f));
            Assert.Equal(250, PrisonerRecruitmentLogic.RequiredMarks(100.0f));
            Assert.Equal(250, PrisonerRecruitmentLogic.RequiredMarks(300.0f));
            Assert.True(PrisonerRecruitmentLogic.CanRecruit(50.0f, 125));
            Assert.False(PrisonerRecruitmentLogic.CanRecruit(50.0f, 124));
        }
    }
}
