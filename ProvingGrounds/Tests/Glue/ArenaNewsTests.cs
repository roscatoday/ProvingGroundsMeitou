using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 10.2 (port only): which town bout results become news for The Wasteland Remembers, and the words (Core/Glue/ArenaNews.cs).</summary>
public class ArenaNewsTests
{
    const string Town = "Scratch";
    const int Skarn = (int)TownChallengePolicy.Encounter.Skarn, Senn = (int)TownChallengePolicy.Encounter.Senn;
    static readonly SparPodium.OutcomeKind AWins = SparPodium.OutcomeKind.TeamAWins, BWins = SparPodium.OutcomeKind.TeamBWins;

    static ArenaNews.Fighter F(string name, MatchTeam team, float rating, bool player = false, int unique = -1, bool rated = true) =>
        new(name, team, rated, rating, player, unique);

    [Fact]
    public void ABoutTheTownDoesNotOwnIsNeverNews() =>
        Assert.Null(ArenaNews.Decide(AWins, false, [F("Ruka", MatchTeam.TeamA, 900, player: true), F("Hagar", MatchTeam.TeamB, 1600)], Town));

    [Theory]
    [InlineData(SparPodium.OutcomeKind.Stopped)]
    [InlineData(SparPodium.OutcomeKind.Draw)]
    [InlineData(SparPodium.OutcomeKind.LastStanding)]
    public void OnlyATeamWinIsNews(SparPodium.OutcomeKind outcome) =>
        Assert.Null(ArenaNews.Decide(outcome, true, [F("Ruka", MatchTeam.TeamA, 900), F("Hagar", MatchTeam.TeamB, 1600)], Town));

    [Fact]
    public void TheTownsUnderdogWinningIsTownNewsOnly()
    {
        var item = ArenaNews.Decide(AWins, true, [F("Ruka", MatchTeam.TeamA, 1000), F("Hagar", MatchTeam.TeamB, 1300)], Town);
        Assert.NotNull(item);
        Assert.Equal(ArenaNews.TownUpsetKind, item.Kind);
        Assert.Equal("Ruka beat the favourite Hagar in the arena at Scratch.", item.Text);
        Assert.Equal(ArenaNews.TownUpsetWeight, item.Weight);
        Assert.False(item.SquadStory);
    }

    [Fact]
    public void TheFavouriteWinningOrAnEvenBoutIsNotNews()
    {
        Assert.Null(ArenaNews.Decide(BWins, true, [F("Ruka", MatchTeam.TeamA, 1000), F("Hagar", MatchTeam.TeamB, 1300)], Town));
        Assert.Null(ArenaNews.Decide(AWins, true, [F("Ruka", MatchTeam.TeamA, 1000), F("Hagar", MatchTeam.TeamB, 1100)], Town));
    }

    [Fact]
    public void TheThresholdIsAOneInFourChance()
    {
        // ExpectedScore(1000, 1000 + d) = 1 / (1 + 10^(d/400)) = 0.25 at d = 400 * log10(3) = 190.85
        Assert.NotNull(ArenaNews.Decide(AWins, true, [F("Ruka", MatchTeam.TeamA, 1000), F("Hagar", MatchTeam.TeamB, 1191)], Town));
        Assert.Null(ArenaNews.Decide(AWins, true, [F("Ruka", MatchTeam.TeamA, 1000), F("Hagar", MatchTeam.TeamB, 1190)], Town));
    }

    [Fact]
    public void UnratedFightersCannotMakeAnUpset()
    {
        Assert.Null(ArenaNews.Decide(AWins, true, [F("Ruka", MatchTeam.TeamA, 0, rated: false), F("Hagar", MatchTeam.TeamB, 1600)], Town));
        Assert.Null(ArenaNews.WinChance([F("Ruka", MatchTeam.TeamA, 1000)], [F("Hagar", MatchTeam.TeamB, float.NaN)]));
    }

    [Fact]
    public void TeamsUseEachSidesMeanAndPluralWords()
    {
        var item = ArenaNews.Decide(BWins, true, [F("Ruka", MatchTeam.TeamA, 1500), F("Hagar", MatchTeam.TeamA, 1300),
            F("Mira", MatchTeam.TeamB, 1000), F("Tolo", MatchTeam.TeamB, 1100), F("Senk", MatchTeam.TeamB, 1050)], Town);
        Assert.NotNull(item);
        Assert.Equal("Mira, Tolo and Senk beat the favourites Ruka and Hagar in the arena at Scratch.", item.Text);
    }

    [Fact]
    public void TheSquadBeatingTheFavouriteIsItsStory()
    {
        var item = ArenaNews.Decide(AWins, true, [F("Beep", MatchTeam.TeamA, 1000, player: true), F("Hagar", MatchTeam.TeamB, 1400)], Town);
        Assert.NotNull(item);
        Assert.Equal(ArenaNews.SquadUpsetKind, item.Kind);
        Assert.Equal("Beep beat the favourite Hagar in the arena at Scratch.", item.Text);
        Assert.Equal(ArenaNews.SquadUpsetWeight, item.Weight);
        Assert.True(item.SquadStory);
    }

    [Fact]
    public void AnOrdinarySquadWinOrAnySquadLossIsNotNews()
    {
        Assert.Null(ArenaNews.Decide(AWins, true, [F("Beep", MatchTeam.TeamA, 1200, player: true), F("Hagar", MatchTeam.TeamB, 1100)], Town));
        Assert.Null(ArenaNews.Decide(BWins, true, [F("Beep", MatchTeam.TeamA, 1600, player: true), F("Hagar", MatchTeam.TeamB, 900)], Town));
    }

    [Fact]
    public void BeatingAUniqueFighterIsNewsWhateverTheRatings()
    {
        var item = ArenaNews.Decide(AWins, true, [F("Beep", MatchTeam.TeamA, 1500, player: true), F("Senn", MatchTeam.TeamB, 1000, unique: Senn)], Town);
        Assert.NotNull(item);
        Assert.Equal(ArenaNews.UniqueKind, item.Kind);
        Assert.Equal("Beep defeated Senn, one of the arena's unique fighters, at Scratch.", item.Text);
        Assert.Equal(ArenaNews.UniqueWeight, item.Weight);
        Assert.True(item.SquadStory);
    }

    [Fact]
    public void SkarnComesFirst()
    {
        var item = ArenaNews.Decide(AWins, true, [F("Beep", MatchTeam.TeamA, 1500, player: true), F("Boop", MatchTeam.TeamA, 1500, player: true),
            F("Senn", MatchTeam.TeamB, 1000, unique: Senn), F("Skarn", MatchTeam.TeamB, 1000, unique: Skarn)], Town);
        Assert.NotNull(item);
        Assert.Equal(ArenaNews.SkarnKind, item.Kind);
        Assert.Equal("Beep and Boop defeated Skarn, the arena's last challenge, at Scratch.", item.Text);
        Assert.Equal(ArenaNews.SkarnWeight, item.Weight);
    }

    [Fact]
    public void ANamelessFighterAndAnotherTownNameRead()
    {
        var item = ArenaNews.Decide(AWins, true, [F("", MatchTeam.TeamA, 1000), F("Hagar", MatchTeam.TeamB, 1400)], "  Scratch Ruins ");
        Assert.NotNull(item);
        Assert.Equal("a fighter beat the favourite Hagar in the arena at Scratch Ruins.", item.Text);
        Assert.StartsWith("a fighter", ArenaNews.Decide(AWins, true, [F(" ", MatchTeam.TeamA, 1000), F("H", MatchTeam.TeamB, 1400)], "")!.Text);
        Assert.EndsWith("at Scratch.", ArenaNews.Decide(AWins, true, [F("R", MatchTeam.TeamA, 1000), F("H", MatchTeam.TeamB, 1400)], "")!.Text);
    }

    [Fact]
    public void NameLists()
    {
        static ArenaNews.Fighter N(string n) => F(n, MatchTeam.TeamA, 1000);
        Assert.Equal("A", ArenaNews.Names([N("A")]));
        Assert.Equal("A and B", ArenaNews.Names([N("A"), N("B")]));
        Assert.Equal("A, B and C", ArenaNews.Names([N("A"), N("B"), N("C")]));
        Assert.Equal("A, B and 3 others", ArenaNews.Names([N("A"), N("B"), N("C"), N("D"), N("E")]));
    }

    [Theory]
    [InlineData("169-Proving Grounds.mod", (int)TownChallengePolicy.Encounter.Senn)]
    [InlineData("168-Proving Grounds.mod", (int)TownChallengePolicy.Encounter.Torka)]
    [InlineData("167-Proving Grounds.mod", (int)TownChallengePolicy.Encounter.Skarn)]
    [InlineData("616-Proving Grounds.mod", (int)TownChallengePolicy.Encounter.RessaVane)]
    [InlineData("619-Proving Grounds.mod", (int)TownChallengePolicy.Encounter.BumPer)]
    [InlineData("623-Proving Grounds.mod", (int)TownChallengePolicy.Encounter.Veyr)]
    [InlineData("96-Proving Grounds.mod", -1)]   // the common Sabre challenge's fighter: not a unique one
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void UniqueFighters(string? record, int encounter) => Assert.Equal(encounter, ArenaNews.UniqueOf(record));
}
