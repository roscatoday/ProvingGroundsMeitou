using System.Text;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand values for Core/Hashing.cs, computed independently of the C# (by hand / a few lines of Python) and agreeing with
/// Tests/Golden/hash.json; the golden test covers the 6,000 vectors.</summary>
public class HashingTests
{
    [Fact]
    public void Xorshift32_zero_seed_is_replaced_by_the_golden_ratio_constant()
    {
        uint zero = 0, fixedSeed = Hashing.ZeroSeedFix;
        Assert.Equal(1359758873u, Hashing.Xorshift32(ref zero));     // golden: first "next" draw from seed 0
        Assert.Equal(1359758873u, Hashing.Xorshift32(ref fixedSeed));
        Assert.Equal(1359758873u, zero);                              // the state is the returned value
        Assert.Equal(0x9e3779b9u, Hashing.ZeroSeedFix);
        uint one = 1;
        Assert.Equal(270369u, Hashing.Xorshift32(ref one));           // 1 -> 1^(1<<13) = 8193 -> ^>>17 = 8193 -> ^<<5 = 270369
    }

    [Fact]
    public void Fnv1a32_known_values()
    {
        Assert.Equal(2166136261u, Hashing.Fnv1a32(""));               // offset basis
        Assert.Equal(0xe40c292cu, Hashing.Fnv1a32("a"));             // the published FNV-1a 32 value of "a"
        Assert.Equal(1996896320u, Hashing.Fnv1a32("616-Proving Grounds.mod"));
        Assert.Equal(2166136261u ^ 5u, Hashing.Fnv1a32(5u, ""));     // the seed is xored into the basis
        Assert.Equal(Hashing.Fnv1a32(""), Hashing.Fnv1a32((string?)null));
        Assert.Equal(Hashing.Fnv1a32(7u, ""), Hashing.Fnv1a32(7u, (string?)null));
        Assert.Equal(Hashing.Fnv1a32("Mönch"), Hashing.Fnv1a32(Encoding.UTF8.GetBytes("Mönch")));
        Assert.Equal(Hashing.Fnv1a32(0u, "x"), Hashing.Fnv1a32("x"));
    }

    [Fact]
    public void Lcg_known_values()
    {
        Assert.Equal(1013904223u, Hashing.Lcg(0));
        Assert.Equal(1015568748u, Hashing.Lcg(1));
        Assert.Equal(1012239698u, Hashing.Lcg(0xffffffffu));          // wraps
        uint state = 0;
        Assert.Equal(1013904223u, Hashing.LcgNext(ref state));
        Assert.Equal(1196435762u, Hashing.LcgNext(ref state));        // golden: second "draw" from seed 0
        Assert.Equal(1196435762u, state);
    }

    [Fact]
    public void ChallengeMix_and_LimbShopMix_known_values()
    {
        Assert.Equal(0u, Hashing.ChallengeMix(0));
        Assert.Equal(0u, Hashing.LimbShopMix(0, 0));                  // 0 ^ 0 times the prime stays 0
        Assert.Equal(Hashing.Fnv1a32(new byte[] { 1, 2, 3, 4 }), Hashing.LimbShopMix(Hashing.FnvOffsetBasis, 0x04030201u));
    }

    [Fact]
    public void LimbShopSeed_clamps_matches_masks_limbs_and_never_returns_zero()
    {
        Assert.Equal(Hashing.LimbShopSeed("id", 0, 3), Hashing.LimbShopSeed("id", -9, 3));
        Assert.Equal(Hashing.LimbShopSeed("id", 4, 3), Hashing.LimbShopSeed("id", 4, 3 | 0xfff0u));
        Assert.Equal(Hashing.ZeroSeedFix, Hashing.LimbShopSeed("q@RMu\\ ", 7, 3));   // raw hash 0 (found by brute force)
        Assert.Equal(Hashing.LimbShopSeed("", 1, 1), Hashing.LimbShopSeed((string?)null, 1, 1));
        Assert.Equal(Hashing.LimbShopMix(Hashing.LimbShopMix(Hashing.FnvOffsetBasis, 0), 0), Hashing.LimbShopSeed("", 0, 0));
    }
}
