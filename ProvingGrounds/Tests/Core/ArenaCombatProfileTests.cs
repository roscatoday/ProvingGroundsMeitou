using ProvingGrounds.Core;
using P = ProvingGrounds.Core.ArenaCombatProfile;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/ArenaCombatProfile.cs, worked from ArenaCombatProfile.cpp and the inventory formula (TW §2.3).</summary>
public class ArenaCombatProfileTests
{
    static P.Profile Weapon(double a, double d, double s, double t, double x, double w, string style = "Katana")
    {
        P.Profile p = P.FromStats(true, P.WeaponCategory.Katanas, a, d, s, t, x, w);
        p.Style = style;
        return p;
    }

    [Fact]
    public void Weapon_formula_is_10_plus_quarter_attack_defence_and_tenths()
    {
        // 10 + .25*40 + .25*30 + .15*20 + .15*10 + .10*50 + .10*60 = 10 + 10 + 7.5 + 3 + 1.5 + 5 + 6 = 43
        Assert.Equal(43.0, P.Evaluate(Weapon(40, 30, 20, 10, 50, 60)), 12);
        // every stat 0: just the base 10
        Assert.Equal(10.0, P.Evaluate(Weapon(0, 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void Martial_formula_is_ten_plus_thirty_percent_attack_and_dodge_and_matches_matchmaking_ability()
    {
        P.Profile m = P.FromStats(false, P.WeaponCategory.Katanas, 40, 30, 20, 10, 50, 99);
        // 10 + .30*40 + .30*30 + .15*20 + .15*10 + .10*50 = 10 + 12 + 9 + 3 + 1.5 + 5 = 40.5; the weapon skill is not read
        Assert.Equal(40.5, P.Evaluate(m), 12);
        Assert.Equal(0.0, m.WeaponSkill);
        double[] v = { 0, 1, 2.5, 37.25, 99.9, -10, -50 };
        foreach (double a in v) foreach (double d in v) foreach (double s in v)
        {
            P.Profile p = P.FromStats(false, P.WeaponCategory.Unarmed, a, d, s, d, a, 0);
            Assert.Equal(TownMatchmakingPolicy.Ability(a, d, s, d, a), P.Evaluate(p));
        }
    }

    [Fact]
    public void Evaluate_rejects_invalid_non_finite_and_non_positive()
    {
        Assert.Equal(-1, P.Evaluate(new P.Profile()));                           // the default profile is not valid
        Assert.Equal(-1, P.Evaluate(Weapon(-100, -100, 0, 0, 0, 0)));            // 10 - 25 - 25 < 0
        Assert.Equal(-1, P.Evaluate(Weapon(-20, -20, 0, 0, 0, 0)));              // exactly 0 is not > 0
        P.Profile p = Weapon(1, 1, 1, 1, 1, 1);
        p.WeaponSkill = double.NaN;
        Assert.Equal(-1, P.Evaluate(p));                                         // a weapon profile needs a finite skill
        p.Martial = true;
        Assert.Equal(10 + .30 + .30 + .15 + .15 + .10, P.Evaluate(p));            // a martial one ignores it
        // the coefficients sum to 1, so finite stats keep a finite score (the Finite(score) test is defensive)
        Assert.Equal(10 + 1e308, P.Evaluate(Weapon(1e308, 1e308, 1e308, 1e308, 1e308, 1e308)), 1e293);
    }

    [Fact]
    public void FromStats_copies_Read_decisions()
    {
        Assert.True(P.IsMartial(false, P.WeaponCategory.Katanas));
        Assert.True(P.IsMartial(true, P.WeaponCategory.Unarmed));
        Assert.False(P.IsMartial(true, P.WeaponCategory.Other));
        P.Profile none = P.FromStats(false, P.WeaponCategory.Polearms, 5, 6, 7, 8, 9, 10);
        Assert.True(none.Martial); Assert.Equal("Martial arts", none.Style); Assert.True(none.Valid);
        P.Profile pole = P.FromStats(true, P.WeaponCategory.Polearms, 5, 6, 7, 8, 9, 10);
        Assert.False(pole.Martial); Assert.Equal("Polearm", pole.Style); Assert.Equal(10, pole.WeaponSkill);
        Assert.Equal("Weapon", P.FromStats(true, P.WeaponCategory.Other, 1, 1, 1, 1, 1, 1).Style);
        Assert.False(P.FromStats(true, P.WeaponCategory.Bow, 1, 1, 1, 1, 1, double.PositiveInfinity).Valid);
        Assert.True(P.FromStats(false, P.WeaponCategory.Bow, 1, 1, 1, 1, 1, double.NaN).Valid);   // martial: skill forced to 0
        Assert.False(P.FromStats(false, P.WeaponCategory.Bow, double.NaN, 1, 1, 1, 1, 1).Valid);
    }

    [Fact]
    public void StatText_lines_and_labels()
    {
        Assert.Equal("Combat stats unavailable", P.StatText(new P.Profile()));
        Assert.Equal("Attack 40\nDefence 30\nStrength 20\nToughness 10\nDexterity 50\nKatana skill 60", P.StatText(Weapon(40, 30, 20, 10, 50, 60)));
        // %.0f (msvcp100 -> msvcr100): an exact tie rounds half away from zero (2.5 -> 3, 0.5 -> 1), a negative fraction keeps its sign
        Assert.Equal("Attack 3\nDefence 4\nStrength -0\nToughness 1\nDexterity 1\nWeapon skill 2",
            P.StatText(Weapon(2.5, 3.5, -0.4, 0.5, 1.4999, 1.5, "Weapon")));
        Assert.Equal("Weapon skill 7", P.StatText(Weapon(1, 1, 1, 1, 1, 7, "")).Split('\n')[^1]);
        Assert.Equal("Weapon skill 7", P.StatText(Weapon(1, 1, 1, 1, 1, 7) with { Style = null }).Split('\n')[^1]);
        Assert.Equal("Martial arts 12\nDodge 3\nStrength 1\nToughness 1\nDexterity 1",
            P.StatText(P.FromStats(false, P.WeaponCategory.Other, 12, 3, 1, 1, 1, 0)));
    }

    [Fact]
    public void Finite_includes_the_largest_doubles()   // (mutation pass: `>= -MaxValue` vs `>`)
    {
        Assert.True(P.Finite(double.MaxValue));
        Assert.True(P.Finite(-double.MaxValue));
        Assert.False(P.Finite(double.NegativeInfinity));
        P.Profile p = Weapon(1, -double.MaxValue, 1, 1, 1, 1);
        Assert.True(p.Valid);
        Assert.StartsWith("Attack 1\nDefence -17976931348623157", P.StatText(p));
        Assert.Equal(-1, P.Evaluate(p));   // a finite but negative score
    }
}
