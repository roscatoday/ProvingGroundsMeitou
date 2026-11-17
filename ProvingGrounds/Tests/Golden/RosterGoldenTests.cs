using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.RosterStatusPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/RosterStatusPolicy.cs against Tests/Golden/roster.json (golden/gen_roster.cpp).</summary>
public class RosterGoldenTests
{
    static IEnumerable<JsonElement> Cases(string kind)
    {
        var all = GoldenFile.Load("roster").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(all);
        return all;
    }

    static float F(JsonElement c, string name) => (float)c.GetProperty(name).GetDouble();

    [Fact]
    public void Recovery_and_band_match()
    {
        foreach (var c in Cases("rec"))
        {
            float limb = F(c, "limb"), blood = F(c, "blood");
            GoldenFile.Near(F(c, "cl"), Clamp(limb), "clamp limb");
            GoldenFile.Near(F(c, "cb"), Clamp(blood), "clamp blood");
            float rec = Recovery(limb, blood);
            GoldenFile.Near(F(c, "rec"), rec, "recovery");
            Assert.Equal((Band)c.GetProperty("band").GetInt32(), BandFor(rec));
            Assert.Equal((Band)c.GetProperty("rawBand").GetInt32(), BandFor(limb));
        }
    }

    [Fact]
    public void LowestLimb_fold_matches()
    {
        foreach (var c in Cases("fold"))
        {
            float lowest = F(c, "start");
            foreach (var l in c.GetProperty("limbs").EnumerateArray())
                lowest = LowestLimb(lowest, (float)l.GetDouble());
            GoldenFile.Near(F(c, "lowest"), lowest, "lowest");
            float rec = Recovery(lowest, F(c, "blood"));
            GoldenFile.Near(F(c, "rec"), rec, "recovery");
            Assert.Equal((Band)c.GetProperty("band").GetInt32(), BandFor(rec));
        }
    }

    [Fact]
    public void Band_thresholds_match()
    {
        foreach (var c in Cases("band"))
            Assert.True((Band)c.GetProperty("band").GetInt32() == BandFor(F(c, "v")), $"band at {F(c, "v"):R}");
    }

    [Fact]
    public void Reason_strings_match()
    {
        foreach (var c in Cases("str"))
        {
            string reason = c.GetProperty("reason").GetString()!;
            bool ready = c.GetProperty("ready").GetInt32() != 0;
            Assert.Equal(c.GetProperty("isRec").GetInt32() != 0, IsRecoveryReason(reason));
            Assert.Equal(c.GetProperty("inline").GetString(), InlineStatus(ready, reason));
        }
    }
}
