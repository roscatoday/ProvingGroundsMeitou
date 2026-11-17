using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Core/BalanceTuning.cs by hand (BalanceTuning.cpp:6-24 shipped values; WriteJson text produced by the original
/// BalanceTuning.cpp compiled with g++).</summary>
public class BalanceTuningTests
{
    [Fact]
    public void Shipped_values()
    {
        var v = new BalanceTuning.Values();
        Assert.Equal(2.5, v.AbilityExponent);
        Assert.Equal(1.0, v.RatingInfluence);
        Assert.True(v.ZeroSumRating);
        Assert.Equal(0, v.DownedPercent);
        Assert.Equal(1.0, v.NpcLeadHoursMin);
        Assert.Equal(1.0, v.NpcLeadHoursSpread);
        Assert.Equal(0, v.ShowcasePercent);
        Assert.Equal(1.10, v.AmbientCloseCap);
        Assert.Equal(0.10, v.AmbientCloseSpan);
        Assert.Equal(1.15, v.AmbientRatioCap);
        Assert.Equal(0.05, v.AmbientUnderdogSpan);
        Assert.Equal(6.65, v.PricingExponent);
        Assert.Equal(1.10, v.PricingKnee);
        Assert.Equal(145.0, v.RingNearRadius);
        Assert.Equal(147.5, v.RingOutRadius);
        Assert.Equal(142.5, v.RingReturnRadius);
    }

    [Fact]
    public void Get_is_one_instance_and_use_for_test_restores()
    {
        var shipped = BalanceTuning.Get();
        Assert.Same(shipped, BalanceTuning.Get());
        var mine = new BalanceTuning.Values { ZeroSumRating = false };
        using (BalanceTuning.UseForTest(mine))
            Assert.Same(mine, BalanceTuning.Get());
        Assert.Same(shipped, BalanceTuning.Get());
    }

    // Output of the original BalanceTuning::WriteJson (g++, shipped values).
    const string ShippedJson =
        "\"ability_exponent\":2.5000,\"rating_influence\":1.0000,\"zero_sum_rating\":1,\"downed_percent\":0,\"npc_lead_min\":1.000," +
        "\"npc_lead_spread\":1.000,\"showcase_percent\":0,\"pricing_exponent\":6.6500,\"pricing_knee\":1.1000,\"ambient_close_cap\":1.1000," +
        "\"ambient_close_span\":0.1000,\"ambient_ratio_cap\":1.1500,\"ambient_underdog_span\":0.0500,\"ring_near_radius\":145.0," +
        "\"ring_out_radius\":147.5,\"ring_return_radius\":142.5";

    [Fact]
    public void WriteJson_shipped() => Assert.Equal(ShippedJson, BalanceTuning.WriteJson());

    [Fact]
    public void WriteJson_changed_levers()
    {
        // Output of the original with zeroSumRating false, downedPercent 50, abilityExponent 3.25, pricingKnee -0.00005 (g++).
        var v = new BalanceTuning.Values { ZeroSumRating = false, DownedPercent = 50, AbilityExponent = 3.25, PricingKnee = -0.00005 };
        using (BalanceTuning.UseForTest(v))
            Assert.Equal(
                "\"ability_exponent\":3.2500,\"rating_influence\":1.0000,\"zero_sum_rating\":0,\"downed_percent\":50,\"npc_lead_min\":1.000," +
                "\"npc_lead_spread\":1.000,\"showcase_percent\":0,\"pricing_exponent\":6.6500,\"pricing_knee\":-0.0001,\"ambient_close_cap\":1.1000," +
                "\"ambient_close_span\":0.1000,\"ambient_ratio_cap\":1.1500,\"ambient_underdog_span\":0.0500,\"ring_near_radius\":145.0," +
                "\"ring_out_radius\":147.5,\"ring_return_radius\":142.5",
                BalanceTuning.WriteJson());
    }

    [Fact]
    public void WriteJson_exact_binary_ties()
    {
        // The original's msvcr100 sprintf_s rounds an exact binary tie half away from zero ("%.3f" of 0.0625 -> "0.063", "%.1f" of 0.25
        // -> "0.3", measured on msvcr100.dll; g++/glibc print "0.062" / "0.2"). Not shipped values.
        var v = new BalanceTuning.Values { NpcLeadHoursMin = 0.0625, RingNearRadius = 0.25 };
        using (BalanceTuning.UseForTest(v))
        {
            string s = BalanceTuning.WriteJson();
            Assert.Contains("\"npc_lead_min\":0.063,", s);
            Assert.Contains("\"ring_near_radius\":0.3,", s);
        }
    }

    [Fact]
    public void WriteJson_negative_rounding_to_zero_keeps_the_sign()
    {
        // Output of the original with pricingKnee -0.00001 and ringNearRadius -0.01 (g++/glibc): "-0.0000" and "-0.0".
        var v = new BalanceTuning.Values { PricingKnee = -0.00001, RingNearRadius = -0.01 };
        using (BalanceTuning.UseForTest(v))
            Assert.Equal(
                "\"ability_exponent\":2.5000,\"rating_influence\":1.0000,\"zero_sum_rating\":1,\"downed_percent\":0,\"npc_lead_min\":1.000," +
                "\"npc_lead_spread\":1.000,\"showcase_percent\":0,\"pricing_exponent\":6.6500,\"pricing_knee\":-0.0000,\"ambient_close_cap\":1.1000," +
                "\"ambient_close_span\":0.1000,\"ambient_ratio_cap\":1.1500,\"ambient_underdog_span\":0.0500,\"ring_near_radius\":-0.0," +
                "\"ring_out_radius\":147.5,\"ring_return_radius\":142.5",
                BalanceTuning.WriteJson());
    }
}
