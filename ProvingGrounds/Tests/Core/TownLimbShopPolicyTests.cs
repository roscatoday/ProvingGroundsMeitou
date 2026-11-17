using ProvingGrounds.Core;
using ProvingGrounds.Tests.Fidelity;
using static ProvingGrounds.Core.TownLimbShopPolicy;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand tests for Core/TownLimbShopPolicy.cs beside limbshop.json: the line tables against the upstream literals, the
/// printf facts the C# relies on (only <c>%s</c>, every result under the 192-byte buffer) and the struct default.</summary>
public class TownLimbShopPolicyTests
{
    [Fact]
    public void Line_tables_equal_the_upstream_literals()
    {
        Assert.Equal(UpstreamLiterals.Read("TownLimbShopPolicy.h", 103, 109), Openings.SelectMany(x => x).ToArray());
        Assert.Equal(UpstreamLiterals.Read("TownLimbShopPolicy.h", 120, 126), Replies.SelectMany(x => x).ToArray());
        Assert.Equal(new[] { "shoddy", "standard", "high-grade", "specialist", "masterwork" },
            UpstreamLiterals.Read("TownLimbShopPolicy.h", 47, 47));
        Assert.Equal(5, Openings.Length);
        Assert.Equal(5, Replies.Length);
        Assert.All(Openings.Concat(Replies), r => Assert.Equal(3, r.Length));
    }

    [Fact]
    public void Formats_hold_only_percent_s_and_fit_the_buffer()
    {
        foreach (string f in Openings.Concat(Replies).SelectMany(x => x))
        {
            Assert.True(f.All(ch => ch >= 0x20 && ch < 0x7f), f);
            // at most one conversion, and it is %s (a plain replacement then equals sprintf)
            int at = f.IndexOf('%');
            if (at >= 0)
            {
                Assert.Equal("%s", f.Substring(at, 2));
                Assert.Equal(-1, f.IndexOf('%', at + 1));
            }
        }
        // longest printed line, with the longest order name ("set", "leg", "arm" are all 3), < 192 bytes incl. NUL
        int longest = 0;
        for (int g = 0; g < 5; ++g)
            for (uint v = 0; v < 3; ++v)
                for (int k = 0; k < 3; ++k)
                    longest = Math.Max(longest, Math.Max(TraderLine((Grade)g, v, k).Length, FighterLine((Grade)g, v, k).Length));
        Assert.Equal(64, longest);
        Assert.True(longest + 1 <= 192);
    }

    [Fact]
    public void Approach_default_is_the_cpp_constructor_not_default_struct()
    {
        var p = new ApproachProgress();
        Assert.Equal(0.0f, p.Elapsed);
        Assert.Equal(1.0e30f, p.BestDistance);
        Assert.Equal(0.0f, p.Stalled);
        ApproachProgress d = default;   // NOT the C++ default (documented on the struct)
        Assert.Equal(0.0f, d.BestDistance);
    }

    [Fact]
    public void Price_saturates_and_the_hash_helpers_are_Hashing()
    {
        Assert.Equal(2147483200, Price(Grade.Shoddy, int.MaxValue / 800));
        Assert.Equal(int.MaxValue, Price(Grade.Shoddy, int.MaxValue / 800 + 1));
        Assert.Equal(0, Price(Grade.Masterwork, 0));
        Assert.Equal(Hashing.LimbShopMix(2166136261u, 7u), Mix(2166136261u, 7u));
    }
}
