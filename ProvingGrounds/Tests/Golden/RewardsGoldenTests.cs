using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaRewards;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaRewards.cs (and PGConfig.ScaleMarks / GearCost / SupplyCost / LicenceCost under it) against
/// Tests/Golden/rewards.json (golden/gen_rewards.cpp). The generator stamps the catalogue once per process at marks multiplier
/// 0.5, 1 and 2.5; each test here stamps a fresh catalogue in its own flow the same way. Everything is compared exactly.</summary>
public class RewardsGoldenTests
{
    static readonly double[] Multipliers = { 0.5, 1.0, 2.5 };

    static List<JsonElement> Cases(string kind, double? m)
    {
        var list = GoldenFile.Load("rewards").EnumerateArray()
            .Where(c => c.GetProperty("k").GetString() == kind &&
                        (m == null || c.GetProperty("m").GetDouble() == m.Value))
            .ToList();
        Assert.NotEmpty(list);
        return list;
    }

    /// <summary>Runs <paramref name="body"/> against a fresh catalogue stamped at <paramref name="m"/> (the generator's child).</summary>
    static void Stamped(double m, Action body)
    {
        using (UseStampedForTest(m))
            body();
    }

    static string? S(JsonElement c, string name)
    {
        JsonElement v = c.GetProperty(name);
        return v.ValueKind == JsonValueKind.Null ? null : v.GetString();
    }

    static int I(JsonElement c, string name) => c.GetProperty(name).GetInt32();
    static bool B(JsonElement c, string name) => c.GetProperty(name).GetInt32() != 0;

    [Fact]
    public void Catalogue_rows_match_at_every_multiplier()
    {
        foreach (double m in Multipliers)
            Stamped(m, () =>
            {
                var rows = Cases("rw", m);
                Assert.Equal(92, rows.Count);   // 17 armour pieces x 4 grades + 8 weapons x 3 grades
                foreach (JsonElement c in rows)
                {
                    Reward r = Get((Piece)I(c, "p"), (Tier)I(c, "t"));
                    string w = $"m={m} rw p={I(c, "p")} t={I(c, "t")}";
                    Assert.True((int)r.Piece == I(c, "rp"), w + " piece");
                    Assert.True((int)r.Tier == I(c, "rt"), w + " tier");
                    Assert.True(r.PieceName == S(c, "pn"), w + " pieceName");
                    Assert.True(r.TierName == S(c, "tn"), w + " tierName");
                    Assert.True(r.StringId == S(c, "sid"), w + " stringId");
                    Assert.True(r.ModelStringId == S(c, "mdl"), w + " model");
                    Assert.True(r.ManufacturerStringId == S(c, "mf"), w + " manufacturer");
                    Assert.True(r.QualityLevel == I(c, "q"), w + " quality");
                    Assert.True(r.Cost == I(c, "cost"), $"{w} cost: expected {I(c, "cost")}, got {r.Cost}");
                }
            });
    }

    [Fact]
    public void General_items_and_licences_match_at_every_multiplier()
    {
        foreach (double m in Multipliers)
            Stamped(m, () =>
            {
                foreach (JsonElement c in Cases("gi", m))
                {
                    GeneralReward g = GetGeneral((GeneralItem)I(c, "i"));
                    string w = $"m={m} gi i={I(c, "i")}";
                    Assert.True((int)g.Item == I(c, "item"), w + " item");
                    Assert.True(g.StableId == S(c, "sid"), w + " stableId");
                    Assert.True(g.Name == S(c, "name"), w + " name");
                    Assert.True(g.StringId == S(c, "str"), w + " stringId");
                    Assert.True(g.BlueprintResearchStringId == S(c, "bp"), w + " blueprint");
                    Assert.True(g.Cost == I(c, "cost"), $"{w} cost: expected {I(c, "cost")}, got {g.Cost}");
                    Assert.True(g.Charges == I(c, "ch"), w + " charges");
                    Assert.True(g.Repeatable == B(c, "rep"), w + " repeatable");
                    Assert.True(g.PrerequisiteId == S(c, "pre"), w + " prerequisite");
                    Assert.True(g.GrantsUnlockId == S(c, "gr"), w + " grants");
                }
                foreach (JsonElement c in Cases("li", m))
                {
                    Licence l = B(c, "w") ? GetWeaponLicence() : GetLicence((ArmourSet)I(c, "set"));
                    string w = $"m={m} li set={I(c, "set")}";
                    Assert.True((int)l.Set == I(c, "ls"), w + " set");
                    Assert.True(l.StableId == S(c, "sid"), w + " stableId");
                    Assert.True(l.Name == S(c, "name"), w + " name");
                    Assert.True(l.Cost == I(c, "cost"), $"{w} cost: expected {I(c, "cost")}, got {l.Cost}");
                    Assert.True(l.PrerequisiteId == S(c, "pre"), w + " prerequisite");
                }
            });
    }

    [Fact]
    public void Get_NextTier_CanClaim_match_at_every_multiplier()
    {
        foreach (double m in Multipliers)
            Stamped(m, () =>
            {
                foreach (JsonElement c in Cases("gt", m))
                {
                    Reward r = Get((Piece)I(c, "p"), (Tier)I(c, "t"));
                    string w = $"m={m} gt p={I(c, "p")} t={I(c, "t")}";
                    Assert.True((int)r.Piece == I(c, "rp") && (int)r.Tier == I(c, "rt") && r.Cost == I(c, "cost"), w);
                }
                foreach (JsonElement c in Cases("nt", m))
                {
                    var p = (Piece)I(c, "p");
                    int h = I(c, "h");
                    string w = $"m={m} nt p={(int)p} h={h}";
                    Assert.True((int)NextTier(h, p) == I(c, "next"), w + " next");
                    Assert.True(IsComplete(h, p) == B(c, "done"), w + " done");
                    Assert.True((int)HighestClaimedTier(h, p) == I(c, "hi"), w + " highest");
                }
                foreach (JsonElement c in Cases("cc", m))
                {
                    bool ok = CanClaim(I(c, "h"), (Piece)I(c, "p"), (Tier)I(c, "t"), I(c, "marks"));
                    Assert.True(ok == B(c, "ok"), $"m={m} cc {c}");
                }
            });
    }

    [Fact]
    public void ResolveOffer_matches_at_every_multiplier()
    {
        foreach (double m in Multipliers)
            Stamped(m, () =>
            {
                foreach (JsonElement c in Cases("ro", m))
                {
                    Offer o = ResolveOffer(I(c, "h"), (Piece)I(c, "p"), B(c, "ex"), B(c, "any"));
                    string w = $"m={m} ro {c}";
                    Assert.True((int)o.Kind == I(c, "kind"), w + " kind");
                    Assert.True((int)o.Tier == I(c, "tier"), w + " tier");
                    Assert.True(o.Cost == I(c, "cost"), $"{w} cost: got {o.Cost}");
                }
            });
    }

    [Fact]
    public void Licence_chain_and_ScaleMarks_match_at_every_multiplier()
    {
        foreach (double m in Multipliers)
            Stamped(m, () =>
            {
                foreach (JsonElement c in Cases("cl", m))
                {
                    string w = $"m={m} cl {c}";
                    Assert.True(CanPurchaseLicence((ArmourSet)I(c, "set"), B(c, "un"), B(c, "pre"), I(c, "marks")) == B(c, "ok"), w);
                    Assert.True(CanPurchaseWeaponLicence(B(c, "un"), I(c, "marks")) == B(c, "wok"), w + " weapon");
                }
                foreach (JsonElement c in Cases("sm", m))
                    Assert.True(PGConfig.ScaleMarks(I(c, "v")) == I(c, "r"), $"m={m} ScaleMarks({I(c, "v")}): expected {I(c, "r")}, got {PGConfig.ScaleMarks(I(c, "v"))}");
            });
    }

    [Fact]
    public void Multiplier_independent_functions_match()
    {
        foreach (JsonElement c in Cases("pf", null))
        {
            var p = (Piece)I(c, "p");
            string w = $"pf p={(int)p}";
            Assert.True(TiersFor(p) == I(c, "tiers"), w + " tiers");
            Assert.True((int)FirstTier(p) == I(c, "first"), w + " first");
            Assert.True(IsWeapon(p) == B(c, "wpn"), w + " weapon");
            Assert.True((int)SetFor(p) == I(c, "set"), w + " set");
            Assert.True((int)RowFor(p) == I(c, "row"), w + " row");
            Assert.True(StableId(p) == S(c, "sid"), w + " stableId");
        }
        foreach (JsonElement c in Cases("sr", null))
            Assert.True((int)SetForRow((ArmourRow)I(c, "r")) == I(c, "set"), $"sr {c}");
        foreach (JsonElement c in Cases("vt", null))
            Assert.True(IsValidTier((Piece)I(c, "p"), (Tier)I(c, "t")) == B(c, "ok"), $"vt {c}");
        foreach (JsonElement c in Cases("gv", null))
            Assert.True(IsGeneralVisible((GeneralItem)I(c, "i"), B(c, "a1")) == B(c, "ok"), $"gv {c}");
        foreach (JsonElement c in Cases("rc", null))
            Assert.True(ReplaceCost(I(c, "v")) == I(c, "r"), $"rc {c}");
    }
}
