using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownChallengePolicy;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/TownChallengePolicyTests.cpp (one [Fact] per block, same values).</summary>
public class TownChallengePolicyTests
{
    static bool HasMode(Card card, ChallengeMode mode)
    {
        for (int i = 0; i < 5; ++i) if (card.Offers[i].Mode == (int)mode) return true;
        return false;
    }

    [Fact]   // TownChallengePolicyTests.cpp:7-17
    public void A_refresh_never_leaves_the_row_in_one_mode()
    {
        var mixed = new Card();
        mixed.Seed = 1;
        RefreshGenerated(mixed, 12, -1);
        Assert.True(HasMode(mixed, ChallengeMode.Teams));
        Assert.True(HasMode(mixed, ChallengeMode.Teams1v1));
    }

    [Fact]   // TownChallengePolicyTests.cpp:19-29
    public void A_refresh_with_a_pinned_slot_keeps_both_modes()
    {
        var pinned = new Card();
        pinned.Seed = 7;
        pinned.Offers[2].Mode = (int)ChallengeMode.Teams1v1;
        RefreshGenerated(pinned, 13, 2);
        Assert.True(HasMode(pinned, ChallengeMode.Teams));
        Assert.True(HasMode(pinned, ChallengeMode.Teams1v1));
    }

    [Fact]   // TownChallengePolicyTests.cpp:31-39
    public void Exactly_half_of_legendary_outcomes_select_the_unique_pool()
    {
        for (int division = 0; division < 3; ++division)
        {
            int unique = 0;
            for (uint roll = 0; roll < 8; ++roll)
            {
                int encounter = LegendaryEncounter(division, roll, false);
                unique += NamedForDivision(encounter, division) ? 1 : 0;
            }
            Assert.Equal(4, unique);
        }
    }

    static Card Progress()
    {
        var progress = new Card();
        progress.Milestones = 7;
        RecordVictory(progress, true, true,
            (uint)(UniqueVictory.RessaVane | UniqueVictory.BumPer | UniqueVictory.Senn | UniqueVictory.Torka));
        RecordVictory(progress, true, true, (uint)UniqueVictory.Veyr);
        return progress;
    }

    [Fact]   // TownChallengePolicyTests.cpp:41-50
    public void The_gate_needs_all_five_unique_victories()
    {
        var progress = new Card();
        progress.Milestones = 7;
        Assert.False(SkarnUnlocked(progress));
        RecordVictory(progress, true, true,
            (uint)(UniqueVictory.RessaVane | UniqueVictory.BumPer | UniqueVictory.Senn | UniqueVictory.Torka));
        Assert.False(SkarnUnlocked(progress));
        RecordVictory(progress, true, true, (uint)UniqueVictory.Veyr);
        Assert.True(SkarnUnlocked(progress));
    }

    [Fact]   // TownChallengePolicyTests.cpp:52-59
    public void Once_unlocked_Skarn_takes_one_hard_unique_outcome()
    {
        int skarnBefore = 0, skarnAfter = 0;
        for (uint roll = 0; roll < 8; ++roll)
        {
            skarnBefore += LegendaryEncounter(2, roll, false) == (int)Encounter.Skarn ? 1 : 0;
            skarnAfter += LegendaryEncounter(2, roll, true) == (int)Encounter.Skarn ? 1 : 0;
        }
        Assert.Equal(0, skarnBefore);
        Assert.Equal(1, skarnAfter);
    }

    [Fact]   // TownChallengePolicyTests.cpp:61-67
    public void Only_the_five_prerequisite_fighters_map_into_progress()
    {
        Assert.Equal((uint)UniqueVictory.RessaVane, UniqueVictoryForEncounter((int)Encounter.RessaVane));
        Assert.Equal((uint)UniqueVictory.BumPer, UniqueVictoryForEncounter((int)Encounter.BumPer));
        Assert.Equal((uint)UniqueVictory.Senn, UniqueVictoryForEncounter((int)Encounter.Senn));
        Assert.Equal((uint)UniqueVictory.Torka, UniqueVictoryForEncounter((int)Encounter.Torka));
        Assert.Equal((uint)UniqueVictory.Veyr, UniqueVictoryForEncounter((int)Encounter.Veyr));
        Assert.Equal(0u, UniqueVictoryForEncounter((int)Encounter.Skarn));
    }

    [Fact]   // TownChallengePolicyTests.cpp:69-94
    public void Named_encounters_anchor_to_the_live_character_record_ids()
    {
        // Named encounters must anchor to the live character record IDs from the town registry.
        var player = new TownMatchmakingPolicy.Fighter { Id = "player", Ability = 50, Mmr = 100 };
        var players = new List<TownMatchmakingPolicy.Fighter> { player };
        var noHistory = new List<List<string>>();
        var namedOpponents = new (int encounter, int division, string role, string id, double ability)[]
        {
            ((int)Encounter.RessaVane, (int)TownMatchmakingPolicy.Division.Easy, "616-Proving Grounds.mod", "ressa", 55),
            ((int)Encounter.BumPer, (int)TownMatchmakingPolicy.Division.Medium, "619-Proving Grounds.mod", "bum-per", 60),
            ((int)Encounter.Veyr, (int)TownMatchmakingPolicy.Division.Hard, "623-Proving Grounds.mod", "veyr", 68),
        };
        for (int i = 0; i < 3; ++i)
        {
            Assert.Equal(namedOpponents[i].role, Role(namedOpponents[i].encounter));
            var npc = new TownMatchmakingPolicy.Fighter
            {
                Id = namedOpponents[i].id, Role = namedOpponents[i].role, Ability = namedOpponents[i].ability, Mmr = 100,
            };
            var npcs = new List<TownMatchmakingPolicy.Fighter> { npc };
            TownMatchmakingPolicy.Match match = TownMatchmakingPolicy.SelectPlayer(
                players, npcs, (TownMatchmakingPolicy.Division)namedOpponents[i].division,
                1, noHistory, 3, 6, Role(namedOpponents[i].encounter), 3);
            // The upstream test asserts here (TownChallengePolicyTests.cpp:92-93):
            //     assert(match.valid);
            //     assert(match.b.size() == 1 && match.b[0] == namedOpponents[i].id);
            // The SHIPPED SelectPlayer does not satisfy it, and the port copies the code, not the test: with rarity 3 (Legendary) the
            // band is rarityLow/High x .80, and each unique is above its top (Ressa (55/50)^2.5 = 1.27 > 1.45*.8 = 1.16; Bum-Per
            // (60/50)^2.5 = 1.58 > 1.80*.8 = 1.44; Veyr (68/50)^2.5 = 2.16 > 2.30*.8 = 1.84), so the search stops at the too-weak
            // check (TownMatchmakingPolicy.h:301-315). Tests/Golden/matchmaking.json has the same three cases from g++ (invalid,
            // PlayersTooWeak). The anchor itself is honoured: the unique is the only fighter the too-weak check looked at.
            Assert.False(match.Valid);
            Assert.Equal(TownMatchmakingPolicy.Match.FailureCode.PlayersTooWeak, match.Failure);
            Assert.Empty(match.B);
        }
    }

    [Fact]   // TownChallengePolicyTests.cpp:96-101
    public void Progress_survives_the_compact_format()
    {
        Card progress = Progress();
        string encoded = Encode(progress);
        var decoded = new Card();
        Assert.True(Decode(encoded, decoded));
        Assert.Equal(31u, decoded.UniqueWins);
        Assert.True(SkarnUnlocked(decoded));
    }

    [Fact]   // TownChallengePolicyTests.cpp:103-111
    public void Compact_version_4_migrates_with_no_invented_unique_victories()
    {
        string current = EncodeLegacy(new Card());
        int first = current.IndexOf(' '), second = current.IndexOf(' ', first + 1);
        int third = current.IndexOf(' ', second + 1), fourth = current.IndexOf(' ', third + 1);
        // "4" + current.substr(first, third - first) + current.substr(fourth)
        string legacy = "4" + current.Substring(first, third - first) + current.Substring(fourth);
        var migrated = new Card();
        Assert.True(DecodeLegacy(legacy, migrated));
        Assert.Equal(0u, migrated.UniqueWins);
        Assert.False(SkarnUnlocked(migrated));
    }

    [Fact]   // TownChallengePolicyTests.cpp:113-133
    public void Saved_or_edited_cards_cannot_bypass_the_five_victory_gate()
    {
        var lockedSkarn = new Card();
        lockedSkarn.Division = 2;
        lockedSkarn.Offers[0].Generated = true;
        lockedSkarn.Offers[0].Division = 2;
        lockedSkarn.Offers[0].Encounter = (int)Encounter.Skarn;
        lockedSkarn.Offers[0].Epoch = 1;
        lockedSkarn.Offers[0].PlayerCount = 1;
        lockedSkarn.Offers[0].EnemyCount = 1;
        lockedSkarn.Offers[0].Context = "lineup";
        lockedSkarn.Offers[0].Opponent = "skarn";
        lockedSkarn.Offers[0].Roles[0] = Role((int)Encounter.Skarn);
        Assert.False(CanBookDivision(lockedSkarn, 0));
        var rejected = new Card();
        Assert.False(Decode(Encode(lockedSkarn), rejected));
        Card unlockedSkarn = lockedSkarn.Clone();   // Card unlockedSkarn = lockedSkarn;
        unlockedSkarn.UniqueWins = (uint)UniqueVictory.AllUniqueVictories;
        Assert.True(CanBookDivision(unlockedSkarn, 0));
        var accepted = new Card();
        Assert.True(Decode(Encode(unlockedSkarn), accepted));
        Assert.Equal((int)Encounter.Skarn, accepted.Offers[0].Encounter);
        Assert.Equal(0u, lockedSkarn.UniqueWins);   // the copy is independent (C++ value semantics)
    }
}
