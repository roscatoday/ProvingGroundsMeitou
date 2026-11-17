using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The rules half of LeaderboardUI.cpp (task 4.6a): GetStandings (LeaderboardStore.cpp:397-423), RefreshContent's texts (:654-702),
/// RebuildRows' row texts (:585-652), the two-click reset (:70-91) and Close (:718).</summary>
public class LeaderboardUILogicTests
{
    sealed class Host : ILeaderboardHost<int>
    {
        public bool Ready = true, CanReset, ResetWorks = true;
        public int Resets;
        public readonly List<LeaderboardData.Standing> Player = [], Town = [];
        public readonly Dictionary<string, (int Id, string Name)> Live = [];
        public readonly List<string> Lines = [];
        public bool HasActiveSave => Ready;
        public string Key = "meitou";
        public string ActiveSaveKey => Key;
        public bool CanResetUnmatchedSidecar() => CanReset;
        public bool ResetUnmatchedSidecar() { Resets++; if (ResetWorks) CanReset = false; return ResetWorks; }
        public IReadOnlyList<LeaderboardData.Standing> RawStandings(LeaderboardData.Kind kind) => kind == LeaderboardData.Kind.Town ? Town : Player;
        public bool TryRatedCharacter(LeaderboardData.Kind kind, string id, bool withSource, out int character, out string name)
        {
            bool ok = Live.TryGetValue(id, out var c);
            character = c.Id;
            name = c.Name ?? "";
            return ok;
        }
        public void Debug(string line) => Lines.Add(line);
    }

    static LeaderboardData.Standing S(string id, string name, float mmr, int w, int l) =>
        new() { Id = id, Name = name, Mmr = mmr, Wins = w, Losses = l, Matches = w + l };

    [Fact]
    public void StandingsKeepRowsWithMatchesUseLiveNamesAndSortByRatingThenName()
    {
        var h = new Host();
        h.Player.AddRange([S("a", "Ann", 120f, 1, 0), S("b", "Bob", 140f, 2, 0), S("z", "Zed", 0f, 0, 0), S("c", "Cat", 120f, 1, 1), S("d", "Abe", 120f, 0, 1)]);
        h.Live["c"] = (7, "Aaron");
        h.Live["a"] = (8, "");   // an empty live name keeps the stored one
        var s = LeaderboardUILogic<int>.GetStandings(h, LeaderboardData.Kind.Player);
        Assert.Equal(["Bob", "Aaron", "Abe", "Ann"], s.Select(r => r.Name));
        Assert.Equal("Cat", h.Player[3].Name);   // copies: the table keeps its names
    }

    [Fact]
    public void NamesCompareByTheirUtf8Bytes()
    {
        // "é" (C3 A9) sorts after "z" (7A) as bytes; UTF-16 ordinal agrees here, but "Ａ" (EF BC A1) vs "\U0001F600" (F0 9F 98 80) does not:
        // UTF-16 puts the surrogate pair D83D first, the bytes put EF first
        var h = new Host();
        h.Player.AddRange([S("1", "\U0001F600", 100f, 1, 0), S("2", "Ａ", 100f, 1, 0)]);
        var s = LeaderboardUILogic<int>.GetStandings(h, LeaderboardData.Kind.Player);
        Assert.Equal(["Ａ", "\U0001F600"], s.Select(r => r.Name));
    }

    [Fact]
    public void RowTexts()
    {
        var r = LeaderboardUILogic<int>.RowTexts(0, S("x", "Harp", 155.6f, 3, 0), true, 5);
        Assert.Equal(("1", "Harp", "156", "3 - 0", "3"), (r.Rank, r.Name, r.Rating, r.Record, r.Matches));
        Assert.Equal("Rating 156  •  3 - 0  •  3 bouts", r.Details);
        Assert.Equal("H", r.Initial);
        Assert.Equal(1, r.RankColour);
        var one = LeaderboardUILogic<int>.RowTexts(3, S("y", "", 99.4f, 1, 0), false, 0);
        Assert.Equal("Rating 99  •  1 - 0  •  1 bout", one.Details);
        Assert.Equal("?", one.Initial);
        Assert.Equal(0, one.RankColour);
        Assert.Equal("4", one.Rank);
        Assert.Equal("�", LeaderboardUILogic<int>.Placeholder("Émile"));
    }

    [Fact]
    public void PlayerBoardTexts()
    {
        var h = new Host();
        h.Player.Add(S("a", "Ann", 120f, 1, 0));
        h.Live["a"] = (9, "Ann");
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Player);
        var v = l.BuildView();
        Assert.Equal("Proving Grounds - Player Leaderboard", v.Caption);
        Assert.Equal("Player arena standings - meitou", v.Subtitle);
        Assert.Equal(LeaderboardUiTexts.EmptyPlayer, v.Empty);
        Assert.False(v.ResetVisible);
        Assert.Single(v.Rows);
        Assert.True(v.Rows[0].HasCharacter);
        Assert.Equal(9, v.Rows[0].Character);
        Assert.True(v.RequestOpen);
        Assert.Equal(1, v.RequestSerial);
        Assert.Contains("Proving Grounds: native leaderboard UI shown", h.Lines);
    }

    [Fact]   // task 7.6 review: a new game never saved has a ready ledger with an empty save key (LeaderboardStore.cpp:194-200 CompleteWorldTransition(true):
             // a default Snapshot, saveKey ""), so the C++ subtitle ends with nothing after the dash (LeaderboardUI.cpp:665-669); "no active save" is the not-ready case
    public void TownBoardInANewGameHasAnEmptySaveKey()
    {
        var h = new Host { Key = "" };
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Town);
        Assert.Equal("Scratch circuit standings - ", l.BuildView().Subtitle);
    }

    [Fact]
    public void TownBoardWithoutASaveIsEmpty()
    {
        var h = new Host { Ready = false };
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Town);
        var v = l.BuildView();
        Assert.Equal("Proving Grounds - Town Leaderboard", v.Caption);
        Assert.Equal("Scratch circuit standings - no active save", v.Subtitle);
        Assert.Equal("No town bouts recorded yet.\nComplete a town arena bout to open the standings.", v.Empty);
        Assert.Empty(v.Rows);
    }

    [Fact]
    public void ResetTakesTwoClicksAndThenLaysOutAgain()
    {
        var h = new Host { CanReset = true };
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Player);
        var v = l.BuildView();
        Assert.True(v.ResetVisible);
        Assert.Equal(LeaderboardUiTexts.ResetSubtitle, v.Subtitle);
        Assert.Equal(LeaderboardUiTexts.ResetEmpty, v.Empty);
        int rev = v.Revision;
        Assert.True(l.OnResetClicked(h));
        Assert.Equal("Confirm reset to zero", l.BuildView().ResetCaption);
        Assert.Equal(rev, l.BuildView().Revision);   // a caption only: no layout pass in the C++
        Assert.Equal(0, h.Resets);
        Assert.True(l.OnResetClicked(h));
        v = l.BuildView();
        Assert.Equal(1, h.Resets);
        Assert.False(v.ResetVisible);
        Assert.Equal(LeaderboardUiTexts.ResetDoneSubtitle, v.Subtitle);
        Assert.Equal(LeaderboardUiTexts.ResetDoneEmpty, v.Empty);
        Assert.Equal(rev + 1, v.Revision);
        Assert.Equal(1, v.Shows);   // a layout pass, not a new show: the scroll offset stays
        Assert.False(l.OnResetClicked(h));   // no longer offered: the click does nothing
    }

    [Fact]
    public void AFailedResetGoesBackToTheFirstClick()
    {
        var h = new Host { CanReset = true, ResetWorks = false };
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Player);
        int rev = l.BuildView().Revision;
        l.OnResetClicked(h);
        l.OnResetClicked(h);
        var v = l.BuildView();
        Assert.Equal("Reset arena progress", v.ResetCaption);
        Assert.Equal(LeaderboardUiTexts.ResetFailed, v.Empty);
        Assert.True(v.ResetVisible);
        Assert.Equal(rev, v.Revision);
        Assert.False(l.ResetConfirm);
    }

    [Fact]
    public void CloseForgetsTheFirstClick()
    {
        var h = new Host { CanReset = true };
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Player);
        l.OnResetClicked(h);
        l.Close();
        var v = l.BuildView();
        Assert.False(v.RequestOpen);
        Assert.Equal(2, v.RequestSerial);
        Assert.Equal("Reset arena progress", v.ResetCaption);
        Assert.False(l.ResetConfirm);
    }

    [Fact]
    public void ClosedFromTheWindowOnlyForItsSerial()
    {
        var h = new Host();
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Player);
        Assert.False(l.OnClosedFromWindow(5));
        Assert.True(l.OnClosedFromWindow(1));
        Assert.False(l.IsVisible);
        Assert.Equal(1, l.BuildView().RequestSerial);
    }

    [Fact]
    public void ShowingAgainWhileOpenIsANewLayoutWithoutANewOpenRequest()
    {
        var h = new Host();
        var l = new LeaderboardUILogic<int>();
        l.Show(h, LeaderboardData.Kind.Player);
        l.Show(h, LeaderboardData.Kind.Town);
        var v = l.BuildView();
        Assert.Equal(1, v.RequestSerial);
        Assert.Equal(2, v.Revision);
    }
}

/// <summary>The memoised FindRatedCharacter (task 4.6a review): the C++ search order, first match kept, the town search lazy and only on request.</summary>
public class RatedCharacterIndexTests
{
    [Fact]
    public void FirstMatchInTheCxxOrderWinsAndTheTownIsReadLazily()
    {
        int townReads = 0;
        var index = new RatedCharacterIndex<int>(
            () => [(1, "a"), (2, ""), (3, "b"), (4, "a")],   // squad / prisoners: "a" twice, the first (1) wins; "" is no key
            () => { townReads++; return [(9, "b"), (10, "t")]; });
        Assert.True(index.TryFind("a", true, out int c) && c == 1);
        Assert.True(index.TryFind("b", true, out c) && c == 3);   // the squad before the town
        Assert.Equal(0, townReads);
        Assert.False(index.TryFind("t", false, out _));           // no building: no town search
        Assert.Equal(0, townReads);
        Assert.True(index.TryFind("t", true, out c) && c == 10);
        Assert.True(index.TryFind("t", true, out _));
        Assert.Equal(1, townReads);
        Assert.False(index.TryFind("", true, out _));
        Assert.False(index.TryFind("zz", true, out _));
    }

    [Fact]
    public void WithoutATownSearchOnlyThePrimaryCandidatesAnswer()
    {
        var index = new RatedCharacterIndex<int>(() => [(5, "x")], null);
        Assert.True(index.TryFind("x", true, out int c) && c == 5);
        Assert.False(index.TryFind("y", true, out _));
    }
}
