using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Core/TestJson.cs (task 8.8): a TEST-ONLY verb's number is msvcr's text when finite and JSON null when not (the `pg town care`
/// line wrote "aid":-1.#IND for a body the furnace had removed).</summary>
public class TestJsonTests
{
    [Fact]
    public void Finite_values_match_CMath()
    {
        Assert.Equal(CMath.FormatG(1.25f, 9), TestJson.G(1.25f));
        Assert.Equal(CMath.FormatG(-0.1, 9), TestJson.G(-0.1));
        Assert.Equal(CMath.FormatG(85.4013977, 6), TestJson.G(85.4013977, 6));
        Assert.Equal(CMath.FormatF(123.456, 0), TestJson.F(123.456, 0));
    }

    [Fact]
    public void NaN_and_infinity_are_null()
    {
        Assert.Contains('#', CMath.FormatG(double.NaN, 9));   // msvcr's text (1.#IND / 1.#INF family): not JSON
        foreach (double v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, (double)float.NaN })
        {
            Assert.Equal("null", TestJson.G(v));
            Assert.Equal("null", TestJson.F(v, 2));
        }
    }
}
