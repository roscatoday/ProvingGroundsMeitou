using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Core/Glue/CombatLogFighter.cs: what the glue reads for one fighter of a CombatBalanceLog line (task 2.7; the profile and the health
/// task 7.2), and LeaderboardStore::GetRating (LeaderboardStore.cpp:425-439).</summary>
public class CombatLogFighterTests
{
    static void SameAsUnavailable(CombatBalanceLog.FighterInfo f, float rating)
    {
        var u = CombatBalanceLog.FighterInfo.Unavailable;
        Assert.Equal((u.Identity, u.Role, u.Valid, u.Name, u.ProfileValid, u.Style, u.Ability, u.HasHealth),
            (f.Identity, f.Role, f.Valid, f.Name, f.ProfileValid, f.Style, f.Ability, f.HasHealth));
        Assert.Equal(rating, f.Rating);
    }

    [Fact]
    public void NoCharacterIsUnavailable() =>
        SameAsUnavailable(CombatLogFighter.From(new CombatLogFighter.Reads { Present = false, Rating = 777 }), MmrRating.DefaultMmr);

    [Fact]
    public void AnInvalidCharacterIsUnavailableAndItsRatingIsNotRead() =>   // Fighter(): valid ? GetRating : 0 (CombatBalanceLog.cpp:153)
        SameAsUnavailable(CombatLogFighter.From(new CombatLogFighter.Reads { Present = true, Valid = false, Identity = "x", Name = "N", Rating = 150 }), MmrRating.DefaultMmr);

    [Fact]
    public void AValidCharacterCarriesIdentityRoleNameAndRating()
    {
        var f = CombatLogFighter.From(new CombatLogFighter.Reads { Present = true, Valid = true, Identity = "0000abcd-1", Role = "55-gamedata.base", Name = "Beep", Rating = 123.5f });
        Assert.Equal(("0000abcd-1", "55-gamedata.base", true, "Beep", 123.5f), (f.Identity, f.Role, f.Valid, f.Name, f.Rating));
        // no profile or health given (a default Reads): what the C++ writes for an unusable profile, no health
        Assert.False(f.ProfileValid);
        Assert.Equal(-1, f.Ability);
        Assert.False(f.HasHealth);
    }

    [Fact]
    public void TheProfileAndHealthAreCopiedAndTheAbilityEvaluated()   // task 7.2: ArenaCombatProfile::Read + Evaluate, RosterStatus::Read
    {
        var p = ArenaCombatProfile.FromStats(true, ArenaCombatProfile.WeaponCategory.Katanas, attack: 40, defence: 30, strength: 20, toughness: 10,
            dexterity: 50, weaponSkill: 60);
        var health = new RosterStatus.Snapshot { LowestLimb = 0.75f, Blood = 0.5f, Recovery = 0.25f };
        var f = CombatLogFighter.From(new CombatLogFighter.Reads { Present = true, Valid = true, Rating = 100, Profile = p, HasHealth = true, Health = health });
        Assert.Equal((true, "Katana", 40.0, 30.0, 20.0, 10.0, 50.0, 60.0), (f.ProfileValid, f.Style, f.Attack, f.Defence, f.Strength, f.Toughness, f.Dexterity, f.WeaponSkill));
        Assert.Equal(ArenaCombatProfile.Evaluate(p), f.Ability);
        Assert.Equal((true, 0.75f, 0.5f, 0.25f), (f.HasHealth, f.LowestLimb, f.Blood, f.Recovery));
        var noHealth = CombatLogFighter.From(new CombatLogFighter.Reads { Present = true, Valid = true, Profile = p, HasHealth = false, Health = health });
        Assert.False(noHealth.HasHealth);
        Assert.Equal(0f, noHealth.LowestLimb);
    }

    [Fact]
    public void ValidCharacterWithNullTextsQuotesAsEmpty()
    {
        var f = CombatLogFighter.From(new CombatLogFighter.Reads { Present = true, Valid = true, Rating = 100 });
        Assert.Equal(("", "", ""), (f.Identity, f.Role, f.Name));
    }

    [Fact]
    public void StatusRowListsEverySubsystemPhase()
    {
        string row = PgStatusRow.Text("Proving Grounds 0.1.0: 3 slow ticks", [("arena persistence", "ready"), ("ArenaUI", "idle")], "none", "idle");
        Assert.Equal("Proving Grounds 0.1.0: 3 slow ticks; subsystems: arena persistence ready, ArenaUI idle; current match: none; town: idle", row);
    }

    [Theory]
    [InlineData("WorldLifecycle.Inputs", "WorldLifecycle")]
    [InlineData("KOWatcher.SlowWorld.Scan", "KOWatcher")]
    [InlineData("NoDot", "NoDot")]   // a step name without a dot is its own subsystem (it used to throw and stop every publish)
    [InlineData("", "")]
    [InlineData(".Leading", "")]
    public void SubsystemIsTheStepNameBeforeItsFirstDot(string step, string subsystem) => Assert.Equal(subsystem, PgStatusRow.Subsystem(step));
}
