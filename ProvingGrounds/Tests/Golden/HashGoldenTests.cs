using System.Text;
using System.Text.Json;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/Hashing.cs against Tests/Golden/hash.json (golden/gen_hash.cpp, which calls the original inline functions).</summary>
public class HashGoldenTests
{
    static byte[] Hex(JsonElement c) => Convert.FromHexString(c.GetProperty("hex").GetString()!);

    static IEnumerable<JsonElement> Cases(string kind)
    {
        var all = GoldenFile.Load("hash").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(all);
        return all;
    }

    [Fact]
    public void Xorshift32_matches_matchmaking_Detail_Next()
    {
        foreach (var c in Cases("next"))
        {
            uint state = c.GetProperty("seed").GetUInt32();
            foreach (var expected in c.GetProperty("out").EnumerateArray())
                Assert.Equal(expected.GetUInt32(), Hashing.Xorshift32(ref state));
            Assert.Equal(c.GetProperty("state").GetUInt32(), state);
        }
    }

    [Fact]
    public void LcgNext_matches_challenge_Draw()
    {
        foreach (var c in Cases("draw"))
        {
            uint seed = c.GetProperty("seed").GetUInt32(), state = seed;
            foreach (var expected in c.GetProperty("out").EnumerateArray())
            {
                uint before = state;
                Assert.Equal(expected.GetUInt32(), Hashing.LcgNext(ref state));
                Assert.Equal(state, Hashing.Lcg(before));
            }
            Assert.Equal(c.GetProperty("state").GetUInt32(), state);
        }
    }

    [Fact]
    public void ChallengeMix_matches_the_inline_scramble()
    {
        foreach (var c in Cases("mix"))
            Assert.Equal(c.GetProperty("out").GetUInt32(), Hashing.ChallengeMix(c.GetProperty("seed").GetUInt32()));
    }

    [Fact]
    public void LimbShopMix_matches()
    {
        foreach (var c in Cases("lmix"))
            Assert.Equal(c.GetProperty("out").GetUInt32(),
                Hashing.LimbShopMix(c.GetProperty("hash").GetUInt32(), c.GetProperty("value").GetUInt32()));
    }

    [Fact]
    public void Fnv1a32_over_utf8_strings_matches_Detail_Hash()
    {
        int nonAscii = 0, empty = 0;
        foreach (var c in Cases("utf8"))
        {
            byte[] bytes = Hex(c);
            string text = new UTF8Encoding(false, true).GetString(bytes);   // throws if a case is not valid UTF-8
            Assert.Equal(bytes, Encoding.UTF8.GetBytes(text));
            uint seed = c.GetProperty("seed").GetUInt32(), expected = c.GetProperty("out").GetUInt32();
            Assert.Equal(expected, Hashing.Fnv1a32(seed, text));
            Assert.Equal(expected, Hashing.Fnv1a32(seed, bytes));
            if (seed == 0) Assert.Equal(expected, Hashing.Fnv1a32(text));
            if (bytes.Any(b => b >= 0x80)) ++nonAscii;
            if (bytes.Length == 0) ++empty;
        }
        Assert.True(nonAscii >= 4, "multibyte UTF-8 cases present");
        Assert.True(empty >= 1, "empty string case present");
    }

    [Fact]
    public void Fnv1a32_over_raw_bytes_matches_Detail_Hash()
    {
        foreach (var c in Cases("bytes"))
        {
            byte[] bytes = Hex(c);
            uint seed = c.GetProperty("seed").GetUInt32();
            Assert.Equal(c.GetProperty("out").GetUInt32(), Hashing.Fnv1a32(seed, bytes));
            if (seed == 0) Assert.Equal(c.GetProperty("out").GetUInt32(), Hashing.Fnv1a32((ReadOnlySpan<byte>)bytes));
        }
    }

    [Fact]
    public void LimbShopSeed_matches_including_the_zero_fix()
    {
        bool zeroFix = false;
        foreach (var c in Cases("lseed"))
        {
            byte[] bytes = Hex(c);
            int matches = c.GetProperty("matches").GetInt32();
            uint mask = c.GetProperty("mask").GetUInt32(), expected = c.GetProperty("out").GetUInt32();
            Assert.Equal(expected, Hashing.LimbShopSeed(bytes, matches, mask));
            Assert.Equal(expected, Hashing.LimbShopSeed(Encoding.ASCII.GetString(bytes), matches, mask));
            zeroFix |= expected == Hashing.ZeroSeedFix;
        }
        Assert.True(zeroFix, "the case whose raw hash is 0 is in the vectors");
    }
}
