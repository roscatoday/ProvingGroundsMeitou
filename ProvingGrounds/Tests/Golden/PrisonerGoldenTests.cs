using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.PrisonerMatchLogic;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/PrisonerMatchLogic.cs, PrisonerRecruitmentLogic.cs and PrisonerRecruitmentPresentation.cs against
/// Tests/Golden/prisoner.json (golden/gen_prisoner.cpp).</summary>
public class PrisonerGoldenTests
{
    static List<JsonElement> All() => GoldenFile.Load("prisoner").EnumerateArray().ToList();

    static IEnumerable<JsonElement> Cases(string kind)
    {
        var all = All().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(all);
        return all;
    }

    static bool B(JsonElement e) => e.GetInt32() != 0;

    [Fact]
    public void RequiredMarks_and_CanRecruit_match()
    {
        double mult = double.NaN;
        int n = 0, i = 0;
        foreach (var c in All())
        {
            string? k = c.GetProperty("k").GetString();
            if (k == "mult") { mult = c.GetProperty("mult").GetDouble(); continue; }
            if (k != "price") continue;
            var rec = c.GetProperty("rec");
            var values = new PGConfig.Recruitment
            {
                Minimum = rec[0].GetInt32(), Maximum = rec[1].GetInt32(), Step = rec[2].GetInt32(), CombatScale = rec[3].GetDouble(),
            };
            float stat = (float)c.GetProperty("stat").GetDouble();
            int cur = c.GetProperty("cur").GetInt32();
            using (PGConfig.UseMarksMultiplierForTest(mult))
            using (PGConfig.UseRecruitmentForTest(values))
            {
                Assert.True(c.GetProperty("marks").GetInt32() == PrisonerRecruitmentLogic.RequiredMarks(stat), $"case {i}: marks");
                Assert.True(B(c.GetProperty("can")) == PrisonerRecruitmentLogic.CanRecruit(stat, cur), $"case {i}: can");
            }
            n++; i++;
        }
        Assert.True(n > 1000, $"{n} price cases");
    }

    [Fact]
    public void ChooseCageLockAction_matches_every_combination()
    {
        int n = 0;
        foreach (var c in Cases("lock"))
        {
            int b = c.GetProperty("bits").GetInt32();
            var o = new CageLockObservation
            {
                DestinationValid = (b & 1) != 0, CorrectOccupant = (b & 2) != 0, HasLock = (b & 4) != 0, Broken = (b & 8) != 0,
                VerifierAvailable = (b & 16) != 0, Locked = (b & 32) != 0, HandlerAvailable = (b & 64) != 0,
                OrderIssued = (b & 128) != 0, OrderRejected = (b & 256) != 0, TimedOut = (b & 512) != 0,
            };
            Assert.Equal((CageLockAction)c.GetProperty("act").GetInt32(), ChooseCageLockAction(o));
            n++;
        }
        Assert.Equal(1024, n);
    }

    [Fact]
    public void Handlers_match()
    {
        foreach (var c in Cases("hand"))
        {
            int p = c.GetProperty("p").GetInt32(), a = c.GetProperty("a").GetInt32();
            Assert.Equal(B(c.GetProperty("ok")), HasEnoughHandlers(p, a));
            Assert.Equal(c.GetProperty("msg").GetString(), FormatHandlerShortage(p, a));
        }
    }

    [Fact]
    public void ChooseReturnCage_matches()
    {
        int i = 0;
        foreach (var c in Cases("cage"))
        {
            // the C++ arrays are 6 long; count says how many are read (the original index guard is `< count`)
            var free = new bool[6]; var inRange = new bool[6]; var reserved = new bool[6]; var dist = new float[6];
            int j = 0;
            foreach (var e in c.GetProperty("c").EnumerateArray())
            {
                free[j] = B(e[0]); inRange[j] = B(e[1]); reserved[j] = B(e[2]); dist[j] = (float)e[3].GetDouble();
                j++;
            }
            Assert.Equal(c.GetProperty("n").GetInt32(), j);
            var pick = ChooseReturnCage(c.GetProperty("orig").GetInt32(), free, dist, inRange, reserved, j);
            Assert.True(c.GetProperty("pick").GetInt32() == pick.Index, $"case {i}: pick");
            i++;
        }
    }

    [Fact]
    public void Suppression_and_return_action_match()
    {
        foreach (var c in Cases("supp"))
        {
            int b = c.GetProperty("bits").GetInt32();
            Assert.Equal(B(c.GetProperty("r")), ShouldSuppressNaturalEnemy((b & 1) != 0, (b & 2) != 0, (b & 4) != 0));
        }
        foreach (var c in Cases("ret"))
        {
            int b = c.GetProperty("bits").GetInt32();
            Assert.Equal((ReturnAction)c.GetProperty("r").GetInt32(), ChooseReturnAction((b & 1) != 0, (b & 2) != 0));
        }
    }

    [Fact]
    public void Presentation_matches()
    {
        foreach (var c in Cases("pres"))
        {
            int b = c.GetProperty("bits").GetInt32();
            var input = new PrisonerRecruitmentPresentation.Input
            {
                HasSelection = (b & 1) != 0, LivingRosterPrisoner = (b & 2) != 0, Busy = (b & 4) != 0, Unconscious = (b & 8) != 0,
                PlayerReady = (b & 16) != 0, CurrentMarks = c.GetProperty("cur").GetInt32(), RequiredMarks = c.GetProperty("req").GetInt32(),
            };
            var st = PrisonerRecruitmentPresentation.Evaluate(input);
            Assert.Equal(B(c.GetProperty("on")), st.Enabled);
            Assert.Equal(c.GetProperty("cap").GetString(), st.ActionCaption);
            Assert.Equal(c.GetProperty("exp").GetString(), st.Explanation);
        }
    }
}
