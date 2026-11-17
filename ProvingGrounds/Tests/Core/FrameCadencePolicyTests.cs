using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for FrameCadencePolicy.h (25 lines, no upstream test).</summary>
public class FrameCadencePolicyTests
{
    [Fact]
    public void Reset_zeroes()
    {
        float e = 3.5f;
        FrameCadencePolicy.Reset(ref e);
        Assert.Equal(0f, e);
    }

    [Fact]
    public void Non_positive_interval_always_fires_and_keeps_elapsed()
    {
        float e = 1.25f;
        Assert.True(FrameCadencePolicy.Advance(ref e, 0.5f, 0f));
        Assert.True(FrameCadencePolicy.Advance(ref e, 0.5f, -1f));
        Assert.Equal(1.25f, e);
    }

    [Fact]
    public void Accumulates_until_interval_then_keeps_the_fmod_remainder()
    {
        float e = 0f;
        Assert.False(FrameCadencePolicy.Advance(ref e, 0.25f, 1f));
        Assert.Equal(0.25f, e);
        Assert.False(FrameCadencePolicy.Advance(ref e, 0.5f, 1f));
        Assert.True(FrameCadencePolicy.Advance(ref e, 0.5f, 1f));     // 1.25 -> fmod 0.25
        Assert.Equal(0.25f, e);
        Assert.True(FrameCadencePolicy.Advance(ref e, 2.0f, 1f));     // 2.25 -> 0.25 (one fire, remainder only)
        Assert.Equal(0.25f, e);
    }

    [Fact]
    public void Exactly_the_interval_fires_with_zero_remainder()
    {
        float e = 0f;
        Assert.True(FrameCadencePolicy.Advance(ref e, 1f, 1f));
        Assert.Equal(0f, e);
    }

    [Fact]
    public void Non_positive_dt_adds_nothing_but_can_still_fire()
    {
        float e = 0.5f;
        Assert.False(FrameCadencePolicy.Advance(ref e, -3f, 1f));
        Assert.False(FrameCadencePolicy.Advance(ref e, 0f, 1f));
        Assert.Equal(0.5f, e);
        float f = 2.5f;                                               // already past: fires without dt
        Assert.True(FrameCadencePolicy.Advance(ref f, 0f, 1f));
        Assert.Equal(0.5f, f);
    }

    [Fact]
    public void Fmod_keeps_float_remainder_like_fmodf()
    {
        // g++: std::fmod(0.7f, 0.3f) = 0.0999999642 (bits 0x3dccccc8)
        float e = 0f;
        Assert.True(FrameCadencePolicy.Advance(ref e, 0.7f, 0.3f));
        Assert.Equal(0x3dccccc8u, BitConverter.SingleToUInt32Bits(e));
    }
}
