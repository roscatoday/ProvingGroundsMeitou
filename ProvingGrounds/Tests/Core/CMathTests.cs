using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/CMath.cs, with the half cases where C++ floor(x + 0.5) differs from .NET's default banker's rounding.</summary>
public class CMathTests
{
    [Theory]
    [InlineData(0.5f, 1f)] [InlineData(1.5f, 2f)] [InlineData(2.5f, 3f)] [InlineData(-0.5f, 0f)] [InlineData(-1.5f, -1f)]
    [InlineData(-2.5f, -2f)] [InlineData(0f, 0f)] [InlineData(1.4f, 1f)] [InlineData(-1.6f, -2f)]
    public void RoundHalfUpF_float_is_floorf_x_plus_half(float x, float expected) => Assert.Equal(expected, CMath.RoundHalfUpF(x));

    [Theory]
    [InlineData(0.5, 1.0)] [InlineData(1.5, 2.0)] [InlineData(2.5, 3.0)] [InlineData(-0.5, 0.0)] [InlineData(-1.5, -1.0)]
    [InlineData(-2.5, -2.0)] [InlineData(0.0, 0.0)] [InlineData(1.4, 1.0)] [InlineData(-1.6, -2.0)]
    public void RoundHalfUp_double_is_floor_x_plus_half(double x, double expected) => Assert.Equal(expected, CMath.RoundHalfUp(x));

    [Theory]
    [InlineData(0.5f, 1f)] [InlineData(1.5f, 2f)] [InlineData(2.5f, 3f)] [InlineData(-0.5f, -1f)] [InlineData(-1.5f, -2f)]
    [InlineData(-2.5f, -3f)] [InlineData(0f, 0f)] [InlineData(-1.4f, -1f)]
    public void RoundHalfAwayFromZero_float(float x, float expected) => Assert.Equal(expected, CMath.RoundHalfAwayFromZero(x));

    [Theory]
    [InlineData(0.5, 1)] [InlineData(1.5, 2)] [InlineData(2.5, 3)] [InlineData(-0.5, 0)] [InlineData(-1.5, -1)] [InlineData(-2.5, -2)]
    [InlineData(-3.0, -2)] [InlineData(1.4999, 1)] [InlineData(0.0, 0)]
    public void TruncatePlusHalf_truncates_toward_zero(double x, int expected) => Assert.Equal(expected, CMath.TruncatePlusHalf(x));

    [Fact]
    public void Half_cases_differ_from_bankers_rounding()
    {
        // .NET default (ToEven) gives 0, 2, -2 here; the C++ patterns do not.
        Assert.Equal(0f, MathF.Round(0.5f)); Assert.Equal(1f, CMath.RoundHalfUpF(0.5f));
        Assert.Equal(2f, MathF.Round(2.5f)); Assert.Equal(3f, CMath.RoundHalfUpF(2.5f));
        Assert.Equal(-2f, MathF.Round(-1.5f)); Assert.Equal(-1f, CMath.RoundHalfUpF(-1.5f));
        Assert.Equal(-2.0, Math.Round(-2.5)); Assert.Equal(-2, CMath.TruncatePlusHalf(-2.5));
        Assert.Equal(-3f, CMath.RoundHalfAwayFromZero(-2.5f));
    }

    [Fact]
    public void Float_addition_is_rounded_in_float_like_floorf()
    {
        // 0.49999997f + 0.5f rounds to 1.0f in single precision, so floorf gives 1 (in double it would be 0).
        Assert.Equal(1f, CMath.RoundHalfUpF(0.49999997f));
        Assert.Equal(0.0, Math.Floor(0.49999997f + 0.5));
    }

    [Fact]
    public void Int_argument_takes_the_double_overload()
    {
        Assert.IsType<double>(CMath.RoundHalfUp(3));
        Assert.Equal(3.0, CMath.RoundHalfUp(3));
    }

    [Fact]
    public void Nan_stays_nan() => Assert.True(float.IsNaN(CMath.RoundHalfAwayFromZero(float.NaN)));

    // printf("%.17g") as the shipped original's msvcr100 prints it (measured on msvcr100.dll 10.00.40219.325 with the probe's
    // `fmt` command, 2026-11-12): three-digit exponents. The codec's std::setprecision(17) output is msvcp100 -> this.
    [Theory]
    [InlineData(0.1, "0.10000000000000001")]
    [InlineData(1.0 / 3.0, "0.33333333333333331")]
    [InlineData(1e-5, "1.0000000000000001e-005")]
    [InlineData(0.0, "0")]
    [InlineData(-0.0, "-0")]
    [InlineData(2.5, "2.5")]
    [InlineData(24000000.0, "24000000")]
    [InlineData(1e17, "1e+017")]
    [InlineData(12345678901234567.0, "12345678901234568")]
    [InlineData(123456789012345678.0, "1.2345678901234568e+017")]
    public void FormatG_precision_17(double value, string expected) => Assert.Equal(expected, CMath.FormatG(value, 17));

    [Fact]
    public void FormatG_precision_6_is_the_default_and_0_means_1()
    {
        Assert.Equal(CMath.FormatG(1234567.0), CMath.FormatG(1234567.0, 6));
        Assert.Equal("1.23457e+006", CMath.FormatG(1234567.0));
        Assert.Equal("1e+006", CMath.FormatG(1234567.0, 0));
        Assert.Equal("2", CMath.FormatG(1.5, 1));
    }

    // printf("%.*f") as msvcr100 prints it (each value measured on the DLL): the 17-digit value rounded half away from zero, so exact
    // ties round up in magnitude (0.5 -> 1, 2.5 -> 3, 1.125 -> 1.13), unlike glibc's half to even.
    [Theory]
    [InlineData(0.5, 0, "1")] [InlineData(1.5, 0, "2")] [InlineData(2.5, 0, "3")] [InlineData(-0.4, 0, "-0")] [InlineData(-0.0, 0, "-0")]
    [InlineData(0.0, 2, "0.00")] [InlineData(0.125, 2, "0.13")] [InlineData(0.375, 2, "0.38")] [InlineData(1.125, 2, "1.13")]
    [InlineData(1.005, 2, "1.00")] [InlineData(2.675, 2, "2.67")] [InlineData(100.5, 0, "101")] [InlineData(101.5, 0, "102")]
    [InlineData(-3.5, 0, "-4")] [InlineData(5.0, 2, "5.00")] [InlineData(0.001, 2, "0.00")] [InlineData(12345.678, 1, "12345.7")]
    public void FormatF_is_printf_fixed(double value, int decimals, string expected) => Assert.Equal(expected, CMath.FormatF(value, decimals));

    [Fact]
    public void FormatF_prints_17_digits_then_zeros()
    {
        Assert.Equal("100000000000000000000", CMath.FormatF(1e20, 0));
        Assert.Equal("9007199254740994", CMath.FormatF(9007199254740994.0, 0));
        Assert.Equal("0.1000000000000000100000000000000000", CMath.FormatF(0.1, 34));
        Assert.Equal("1", CMath.FormatF(double.PositiveInfinity, 0));        // table_report.md: msvcr100 prints "1" (1.#INF cut at 0 decimals)
        Assert.Equal("-1.#J", CMath.FormatF(double.NegativeInfinity, 2));   // "-1.#I" rounded up by the 'N'
        Assert.Equal("-1.#IND00", CMath.FormatF(double.NaN, 6));
    }

    // The golden-test mode: glibc's printf (exact binary value, ties to even, two-digit exponents), the bytes g++ wrote into the goldens.
    [Theory]
    [InlineData(1e-5, "1.0000000000000001e-05")] [InlineData(1e17, "1e+17")] [InlineData(123456789012345678.0, "1.2345678901234568e+17")]
    [InlineData(0.1, "0.10000000000000001")]
    public void Glibc_mode_FormatG_precision_17(double value, string expected)
    {
        using var glibc = CMath.UseGlibcForTest();
        Assert.Equal(expected, CMath.FormatG(value, 17));
    }

    [Theory]
    [InlineData(0.5, 0, "0")] [InlineData(2.5, 0, "2")] [InlineData(0.125, 2, "0.12")] [InlineData(1.125, 2, "1.12")] [InlineData(100.5, 0, "100")]
    [InlineData(-0.4, 0, "-0")] [InlineData(2.675, 2, "2.67")]
    public void Glibc_mode_FormatF(double value, int decimals, string expected)
    {
        using var glibc = CMath.UseGlibcForTest();
        Assert.Equal(expected, CMath.FormatF(value, decimals));
    }

    [Fact]
    public void Glibc_mode_is_scoped_and_restored()
    {
        Assert.Equal("3", CMath.FormatF(2.5, 0));
        using (CMath.UseGlibcForTest())
        {
            Assert.Equal("1e+06", CMath.FormatG(1234567.0, 0));
            Assert.Equal("0.1000000000000000055511151231257827", CMath.FormatF(0.1, 34));
            Assert.Equal("inf", CMath.FormatF(double.PositiveInfinity, 0));
            Assert.Equal("-inf", CMath.FormatF(double.NegativeInfinity, 2));
        }
        Assert.Equal("3", CMath.FormatF(2.5, 0));
        Assert.Equal("1e+006", CMath.FormatG(1234567.0, 0));
    }
}
