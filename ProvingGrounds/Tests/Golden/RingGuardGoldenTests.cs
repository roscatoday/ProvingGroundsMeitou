using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaRingGuard;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaRingGuard.cs against Tests/Golden/ringguard.json (golden/gen_ringguard.cpp).</summary>
public class RingGuardGoldenTests
{
    static IEnumerable<JsonElement> Cases(string kind)
    {
        var all = GoldenFile.Load("ringguard").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(all);
        return all;
    }

    static float F(JsonElement c, string name) => (float)c.GetProperty(name).GetDouble();

    [Fact]
    public void ClassifyRing_matches()
    {
        foreach (var c in Cases("zone"))
            Assert.Equal((RingZone)c.GetProperty("z").GetInt32(), ClassifyRing(F(c, "d"), F(c, "near"), F(c, "out")));
    }

    [Fact]
    public void RingReturnPoint_matches()
    {
        foreach (var c in Cases("ret"))
        {
            RingReturnPoint(F(c, "dx"), F(c, "dz"), F(c, "t"), out float x, out float z);
            GoldenFile.Near(F(c, "x"), x, "x");
            GoldenFile.Near(F(c, "z"), z, "z");
        }
    }

    [Fact]
    public void RingCorrectionDue_matches()
    {
        foreach (var c in Cases("due"))
            Assert.Equal(c.GetProperty("due").GetInt32() != 0,
                RingCorrectionDue(c.GetProperty("now").GetUInt32(), c.GetProperty("last").GetUInt32(), c.GetProperty("iv").GetUInt32()));
    }
}
