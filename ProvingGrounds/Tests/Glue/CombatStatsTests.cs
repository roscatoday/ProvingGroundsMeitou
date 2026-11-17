using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Spike S12's stand-in for <c>CharStats::getOverallSkillLevel_0_100</c> (docs/spikes.md S12): the mean of seven trained levels,
/// the best weapon skill one of them, clamped to [0, 100], a float (not rounded).</summary>
public class CombatStatsTests
{
    static CombatStats Stats(float attack = 0, float defence = 0, float dodge = 0, float strength = 0, float toughness = 0, float dexterity = 0,
        float katanas = 0, float sabres = 0, float hackers = 0, float heavy = 0, float blunt = 0, float polearms = 0, float martial = 0) => new()
    {
        Valid = true, MeleeAttack = attack, MeleeDefence = defence, Dodge = dodge, Strength = strength, Toughness = toughness,
        Dexterity = dexterity, Katanas = katanas, Sabres = sabres, Hackers = hackers, HeavyWeapons = heavy, Blunt = blunt,
        Polearms = polearms, MartialArts = martial,
    };

    [Fact]
    public void MeanOfSevenWithTheBestWeaponSkill()
    {
        var s = Stats(attack: 10, defence: 20, dodge: 30, strength: 40, toughness: 50, dexterity: 60, katanas: 5, sabres: 70, hackers: 1);
        Assert.Equal((10f + 20f + 30f + 40f + 50f + 60f + 70f) / 7f, OverallSkill.Of(s));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void EachWeaponSkillCanBeTheBest(int which)
    {
        float[] w = new float[7];
        w[which] = 77;
        var s = Stats(katanas: w[0], sabres: w[1], hackers: w[2], heavy: w[3], blunt: w[4], polearms: w[5], martial: w[6]);
        Assert.Equal(77f / 7f, OverallSkill.Of(s));
    }

    [Fact]
    public void NotRounded()
    {
        var s = Stats(attack: 1);
        Assert.Equal(1f / 7f, OverallSkill.Of(s));
        Assert.NotEqual(MathF.Round(1f / 7f), OverallSkill.Of(s));
    }

    [Fact]
    public void FloatArithmeticLeftToRight()
    {
        var s = Stats(attack: 33.3f, defence: 41.7f, dodge: 12.9f, strength: 55.1f, toughness: 61.03f, dexterity: 47.77f, blunt: 28.4f);
        float sum = 33.3f + 41.7f + 12.9f + 55.1f + 61.03f + 47.77f + 28.4f;
        Assert.Equal(sum / 7f, OverallSkill.Of(s));
    }

    [Fact]
    public void ClampedToZeroAndHundred()
    {
        Assert.Equal(100f, OverallSkill.Of(Stats(attack: 101, defence: 101, dodge: 101, strength: 101, toughness: 101, dexterity: 101, katanas: 101)));
        Assert.Equal(0f, OverallSkill.Of(Stats(attack: -50)));
        Assert.Equal(100f, OverallSkill.Of(Stats(attack: 700, defence: 0)));
    }

    [Fact]
    public void InvalidStatsAreNaN()
    {
        Assert.True(float.IsNaN(OverallSkill.Of(default)));
        Assert.True(float.IsNaN(OverallSkill.Of(CombatStats.None)));
        Assert.False(CombatStats.None.Valid);
    }

    [Fact]
    public void NaNStatGivesNaN()
    {
        Assert.True(float.IsNaN(OverallSkill.Of(Stats(attack: float.NaN, defence: 50))));
        Assert.True(float.IsNaN(OverallSkill.Of(Stats(toughness: float.NaN))));
        // every weapon skill NaN: the best stays NaN
        Assert.True(float.IsNaN(OverallSkill.Of(Stats(katanas: float.NaN, sabres: float.NaN, hackers: float.NaN, heavy: float.NaN,
            blunt: float.NaN, polearms: float.NaN, martial: float.NaN))));
    }

    [Fact]
    public void NaNWeaponSkillIsNeverTheBest()
    {
        // Katanas (first) NaN: the next skills still compete; a later NaN never replaces a number
        Assert.Equal(40f / 7f, OverallSkill.Of(Stats(katanas: float.NaN, sabres: 40)));
        Assert.Equal(40f / 7f, OverallSkill.Of(Stats(katanas: 40, blunt: float.NaN)));
    }

    [Fact]
    public void AllZeroIsZero() => Assert.Equal(0f, OverallSkill.Of(Stats()));
}
