using System.Text.Json;
using Xunit.Sdk;

namespace ProvingGrounds.Tests.Golden;

public class GoldenFileTests
{
    static readonly string[] CaseFields = ["mode", "outcome", "last", "f"];
    static readonly string[] FighterFields = ["team", "dealt", "taken", "mit", "hits", "blocks", "misses", "dodges", "elim",
        "before", "matches", "mask", "after", "win", "loss"];

    [Fact]
    public void Mmr_vectors_are_copied_to_the_output_and_complete()
    {
        JsonElement cases = GoldenFile.Load("mmr");
        Assert.Equal(JsonValueKind.Array, cases.ValueKind);
        Assert.Equal(2000, cases.GetArrayLength());
        int index = 0;
        foreach (JsonElement c in cases.EnumerateArray())
        {
            foreach (string field in CaseFields) Assert.True(c.TryGetProperty(field, out _), $"case {index} lacks '{field}'");
            JsonElement fighters = c.GetProperty("f");
            Assert.InRange(fighters.GetArrayLength(), 2, 8);
            foreach (JsonElement fighter in fighters.EnumerateArray())
                foreach (string field in FighterFields)
                    Assert.True(fighter.TryGetProperty(field, out _), $"case {index} fighter lacks '{field}'");
            ++index;
        }
    }

    [Fact]
    public void Near_uses_an_absolute_tolerance_of_1e4_up_to_1_and_a_relative_one_above()
    {
        GoldenFile.Near(0f, 0.00009f, "inside, absolute");
        GoldenFile.Near(100f, 100.009f, "inside, relative (tolerance 0.01)");
        GoldenFile.Near(-250f, -250.02f, "inside, negative expected (tolerance 0.025)");
        var below = Assert.ThrowsAny<XunitException>(() => GoldenFile.Near(0f, 0.0002f, "after[3]"));
        Assert.Contains("after[3]", below.Message);
        Assert.Contains("expected 0", below.Message);
        Assert.ThrowsAny<XunitException>(() => GoldenFile.Near(100f, 100.02f, "outside, relative"));
        Assert.ThrowsAny<XunitException>(() => GoldenFile.Near(1f, float.NaN, "NaN never passes"));
    }
}
