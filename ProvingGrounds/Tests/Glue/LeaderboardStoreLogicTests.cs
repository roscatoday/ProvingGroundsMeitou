using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake character world for LeaderboardStoreLogic: ints are characters (0 = null), keys by id.</summary>
sealed class StoreFake : ILeaderboardStoreHost<int>
{
    public readonly Dictionary<int, string> Keys = [];          // the identity GetOrCreate gives (absent: none)
    public readonly Dictionary<int, string> FoundKeys = [];     // Find, when it differs from Keys (the duplicate-token reveal)
    public readonly HashSet<int> Created = [];                  // identities made so far (Find answers only these, as the C++)
    public readonly HashSet<int> Invalid = [];
    public readonly HashSet<int> Players = [];
    public readonly HashSet<int> Dead = [];
    public readonly HashSet<int> Roster = [];
    public readonly HashSet<int> MatchPrisonerSet = [];
    public readonly HashSet<int> Chained = [];
    public readonly Dictionary<int, string> Names = [];
    public readonly Dictionary<int, int> Participants = [];     // result-row participant id -> character
    public readonly List<int> Squad = [];
    public readonly List<int> RosterList = [];
    public readonly List<string> Debugs = [], Errors = [];
    public int CreateCalls;

    public bool IsNull(int c) => c == 0;
    public bool Valid(int c) => c != 0 && !Invalid.Contains(c);
    public bool TryIdentity(int c, bool create, out string token)
    {
        token = "";
        if (create)
        {
            ++CreateCalls;
            if (!Keys.TryGetValue(c, out var k)) return false;
            Created.Add(c);
            token = k;
            return true;
        }
        if (!Created.Contains(c)) return false;
        token = FoundKeys.TryGetValue(c, out var f) ? f : Keys[c];
        return true;
    }
    public string Name(int c) => Names.TryGetValue(c, out var n) ? n : "";
    public bool TryOverallSkill(int c, out float skill) { skill = 30f; return true; }
    public bool IsPlayerCharacter(int c) => Players.Contains(c);
    public bool IsChainedMode(int c) => Chained.Contains(c);
    public bool IsDead(int c) => Dead.Contains(c);
    public bool IsRosterPrisoner(int c) => Roster.Contains(c);
    public bool IsMatchPrisoner(int c) => MatchPrisonerSet.Contains(c);
    public int Participant(int participantId) => Participants.TryGetValue(participantId, out var c) ? c : 0;
    public IEnumerable<int> PlayerSquad() => Squad;
    public IEnumerable<int> RosterPrisoners() => RosterList;
    public IEnumerable<int> MatchPrisoners() => MatchPrisonerList;
    public readonly List<int> MatchPrisonerList = [];
    public void Debug(string line) => Debugs.Add(line);
    public void Error(string line) => Errors.Add(line);

    /// <summary>Characters 1 (Ann) and 2 (Bob), participants 0 and 1, keys k1 / k2.</summary>
    public static StoreFake TwoPlayers()
    {
        var f = new StoreFake();
        f.Keys[1] = "k1"; f.Keys[2] = "k2";
        f.Names[1] = "Ann"; f.Names[2] = "Bob";
        f.Participants[0] = 1; f.Participants[1] = 2;
        f.Players.Add(1); f.Players.Add(2);
        f.Squad.AddRange([1, 2]);
        return f;
    }
}

/// <summary>LeaderboardStore (Core/Glue/LeaderboardStoreLogic.cs, task 5.1; the rating half was task 3.3's MatchAwards): ApplyFromSnapshot (rated
/// / unrated / stop / draw / not Ready / missing identity / duplicate token / town owner / prisoner owner), the purchases, Marks spend / refund,
/// the standings, the setters, the reads and FindRatedCharacter.</summary>
public class LeaderboardStoreLogicTests
{
    static ArenaWorldState Ready()
    {
        var w = new ArenaWorldState(_ => { }, _ => null);
        w.Resolve(loadedFromSave: false, tickCount: 1);
        Assert.True(w.Ready);
        return w;
    }

    static (ArenaWorldState W, StoreFake F, LeaderboardStoreLogic<int> S) New(StoreFake? fake = null)
    {
        var w = Ready();
        var f = fake ?? StoreFake.TwoPlayers();
        return (w, f, new LeaderboardStoreLogic<int>(w, f));
    }

    static SparPodium.Snapshot Snap(SparPodium.OutcomeKind outcome)
    {
        var s = new SparPodium.Snapshot { Outcome = outcome, Mode = MatchMode.TeamAvB, FighterCount = 2 };
        s.Fighters[0] = new SparPodium.FighterRow { Id = 0, Team = MatchTeam.TeamA, Name = "Ann", DamageDealt = 50, EliminationIndex = -1 };
        s.Fighters[1] = new SparPodium.FighterRow { Id = 1, Team = MatchTeam.TeamB, Name = "Bob", EliminationIndex = 0 };
        s.PodiumCount = 1;
        s.Podium[0] = new SparPodium.PodiumEntry { Place = 1, Fighter = s.Fighters[0] };
        return s;
    }

    // ---------- ApplyFromSnapshot ----------

    [Theory]
    [InlineData(SparPodium.OutcomeKind.Stopped)]
    [InlineData(SparPodium.OutcomeKind.Draw)]
    public void StopAndDraw_RateNothing_LogNothing_MakeNoIdentity(SparPodium.OutcomeKind outcome)
    {
        var (w, f, s) = New();
        Assert.False(s.ApplyFromSnapshot(Snap(outcome), LeaderboardData.Kind.Player));
        Assert.Empty(f.Debugs);
        Assert.Empty(w.Data.PlayerStandings);
        Assert.Empty(w.Data.Progression);
        Assert.Equal(0, f.CreateCalls);
    }

    [Fact]
    public void LedgerNotReady_LogsTheCppText_MakesNoIdentity()
    {
        var (w, f, s) = New();
        w.BlockPersistence("blocked");
        Assert.False(s.ApplyFromSnapshot(Snap(SparPodium.OutcomeKind.TeamAWins), LeaderboardData.Kind.Player));
        Assert.Equal(["Proving Grounds: rating skip - arena ledger unavailable"], f.Debugs);
        Assert.Equal(0, f.CreateCalls);
        Assert.Empty(w.Data.PlayerStandings);
    }

    [Fact]
    public void TeamAWins_WritesStandings_RowsPodium_AndMarksIntoTheProgression()
    {
        var (w, f, s) = New();
        var snap = Snap(SparPodium.OutcomeKind.TeamAWins);
        Assert.True(s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Player));
        Assert.Equal(["Proving Grounds: ratings updated in active world snapshot"], f.Debugs);

        var expected = new float[16];
        var mask = new bool[16]; mask[0] = mask[1] = true;
        MmrRating.ApplyMatch(Snap(SparPodium.OutcomeKind.TeamAWins), [100f, 100f, .. new float[14]], new int[16], mask, expected, null, null);
        var marks = new int[16];
        ArenaMarks.ComputeAwards(Snap(SparPodium.OutcomeKind.TeamAWins), [30f, 30f, .. new float[14]], marks, 1f);
        var ann = w.Data.PlayerStandings.Single(r => r.Id == "k1");
        var bob = w.Data.PlayerStandings.Single(r => r.Id == "k2");
        Assert.Equal(expected[0], ann.Mmr);
        Assert.Equal(expected[1], bob.Mmr);
        Assert.Equal((1, 0, 1), (ann.Wins, ann.Losses, ann.Matches));
        Assert.Equal((0, 1, 1), (bob.Wins, bob.Losses, bob.Matches));
        Assert.Equal("Ann", ann.Name);

        Assert.True(snap.Fighters[0].RatingUpdated);
        Assert.Equal(100f, snap.Fighters[0].RatingBefore);
        Assert.Equal(ann.Mmr, snap.Fighters[0].RatingAfter);
        Assert.Equal(snap.Fighters[0].RatingAfter - 100f, snap.Fighters[0].RatingDelta);
        Assert.True(snap.Fighters[0].MarksUpdated);
        Assert.True(marks[0] > 0);
        Assert.Equal((0, marks[0], marks[0]), (snap.Fighters[0].MarksBefore, snap.Fighters[0].MarksEarned, snap.Fighters[0].MarksAfter));
        Assert.Equal(snap.Fighters[0].MarksAfter, snap.Podium[0].Fighter.MarksAfter);
        Assert.True(snap.Podium[0].Fighter.MarksUpdated);
        Assert.Equal(snap.Fighters[0].RatingDelta, snap.Podium[0].Fighter.RatingDelta);

        // the Marks the rows report are in the ledger: one progression row per fighter, in order, named from the result row
        Assert.Equal(["k1", "k2"], w.Data.Progression.Select(p => p.Id));
        Assert.Equal(marks[0], w.Data.Progression[0].Marks);
        Assert.Equal(marks[1], w.Data.Progression[1].Marks);
        Assert.Equal(snap.Fighters[1].MarksAfter, w.Data.Progression[1].Marks);
        Assert.Equal(marks[0], s.GetMarks(1));
        Assert.Equal(1, s.GetMatchCount(1));
    }

    [Fact]
    public void ExistingRows_AreTheBefore_AndMarksAccumulate()
    {
        var (w, f, s) = New();
        w.Data.PlayerStandings.Add(new LeaderboardData.Standing { Id = "k1", Name = "Ann", Mmr = 400f, Matches = 25 });
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k1", Name = "Old", Marks = 40 });
        var snap = Snap(SparPodium.OutcomeKind.TeamBWins);
        Assert.True(s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Player));
        Assert.Equal(400f, snap.Fighters[0].RatingBefore);
        Assert.Equal(26, w.Data.PlayerStandings.Single(r => r.Id == "k1").Matches);
        Assert.Equal(40, snap.Fighters[0].MarksBefore);
        Assert.Equal(40 + snap.Fighters[0].MarksEarned, w.Data.Progression[0].Marks);
        Assert.Equal("Ann", w.Data.Progression[0].Name);   // the result row's name (AddMarks after EnsureProgressionForCharacter)
    }

    [Fact]
    public void TwoVsTwo_DeltasAreMmrRatings_AndZeroSumWithTheLeverOn()
    {
        using var tuning = BalanceTuning.UseForTest(new BalanceTuning.Values { ZeroSumRating = true });
        var f = new StoreFake();
        for (int i = 0; i < 4; i++) { f.Keys[i + 1] = "k" + i; f.Participants[i] = i + 1; f.Players.Add(i + 1); }
        var (w, _, s) = New(f);
        var snap = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins, Mode = MatchMode.TeamAvB, FighterCount = 4 };
        for (int i = 0; i < 4; i++)
            snap.Fighters[i] = new SparPodium.FighterRow { Id = i, Team = i < 2 ? MatchTeam.TeamA : MatchTeam.TeamB, Name = "F" + i, DamageDealt = 10 * i, EliminationIndex = -1 };
        w.Data.PlayerStandings.Add(new LeaderboardData.Standing { Id = "k2", Name = "F2", Mmr = 180f, Matches = 30 });
        var copy = new SparPodium.Snapshot { Outcome = snap.Outcome, Mode = snap.Mode, FighterCount = 4 };
        Array.Copy(snap.Fighters, copy.Fighters, 4);
        Assert.True(s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Player));

        var expected = new float[16];
        var mask = new bool[16]; for (int i = 0; i < 4; i++) mask[i] = true;
        MmrRating.ApplyMatch(copy, [100f, 100f, 180f, 100f, .. new float[12]], [0, 0, 30, 0, .. new int[12]], mask, expected, null, null);
        float sum = 0f;
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(expected[i], snap.Fighters[i].RatingAfter);
            Assert.Equal(expected[i], w.Data.PlayerStandings.Single(r => r.Id == "k" + i).Mmr);
            sum += snap.Fighters[i].RatingDelta;
        }
        Assert.True(MathF.Abs(sum) < 1e-3f, $"sum {sum}");
    }

    [Fact]
    public void MissingIdentity_LoggedOnce_NotRated()
    {
        var f = StoreFake.TwoPlayers();
        f.Keys.Remove(2);   // Bob has no identity
        var (w, _, s) = New(f);
        var snap = Snap(SparPodium.OutcomeKind.TeamAWins);
        Assert.True(s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Player));
        Assert.Equal(["Proving Grounds: unavailable or conflicting fighter identity during match award"], f.Errors);
        Assert.False(snap.Fighters[1].RatingUpdated);
        Assert.False(snap.Fighters[1].MarksUpdated);
        Assert.Single(w.Data.PlayerStandings);
        Assert.Single(w.Data.Progression);
        s.ApplyFromSnapshot(Snap(SparPodium.OutcomeKind.TeamAWins), LeaderboardData.Kind.Player);
        Assert.Single(f.Errors);   // once per character
        w.ActivateSnapshot(new ArenaPersistence.Snapshot());   // C++ :180 clears the log
        s.ApplyFromSnapshot(Snap(SparPodium.OutcomeKind.TeamAWins), LeaderboardData.Kind.Player);
        Assert.Equal(2, f.Errors.Count);
    }

    [Fact]
    public void DuplicateTokenRevealed_UnratesTheEarlierRow()
    {
        var f = StoreFake.TwoPlayers();
        f.FoundKeys[1] = "other";   // the revalidation finds another token for Ann
        var (w, _, s) = New(f);
        Assert.True(s.ApplyFromSnapshot(Snap(SparPodium.OutcomeKind.TeamAWins), LeaderboardData.Kind.Player));
        Assert.Equal(["k2"], w.Data.PlayerStandings.Select(r => r.Id));
        Assert.Equal(["k2"], w.Data.Progression.Select(r => r.Id));
    }

    [Fact]
    public void NoValidParticipant_RatesNothing_LogsNothing()
    {
        var f = StoreFake.TwoPlayers();
        f.Invalid.Add(1); f.Invalid.Add(2);
        var (w, _, s) = New(f);
        Assert.False(s.ApplyFromSnapshot(Snap(SparPodium.OutcomeKind.TeamAWins), LeaderboardData.Kind.Player));
        Assert.Empty(f.Errors);
        Assert.Empty(f.Debugs);
        Assert.Empty(w.Data.PlayerStandings);
    }

    [Fact]
    public void TownOwner_TownBoard_NpcGetsNoMarks_AndLosesItsProgression_PlayerKeepsMarks()
    {
        var f = StoreFake.TwoPlayers();
        f.Players.Remove(2);   // Bob is a town NPC
        var (w, _, s) = New(f);
        f.Created.Add(2);
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k2", Name = "Bob", Marks = 9 });
        var snap = Snap(SparPodium.OutcomeKind.TeamBWins);
        Assert.True(s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Town, 1f, 0));
        Assert.Equal(["k1", "k2"], w.Data.TownStandings.Select(r => r.Id).OrderBy(x => x));
        Assert.Empty(w.Data.PlayerStandings);
        Assert.True(snap.Fighters[1].RatingUpdated);
        Assert.False(snap.Fighters[1].MarksUpdated);
        Assert.Equal(0, snap.Fighters[1].MarksAfter);
        Assert.Equal(["k1"], w.Data.Progression.Select(r => r.Id));   // the NPC's row removed, the player's written
        Assert.True(snap.Fighters[0].MarksUpdated);
        Assert.Equal(1, s.GetMatchCount(1, LeaderboardData.Kind.Town));
        Assert.Equal(0, s.GetMatchCount(1, LeaderboardData.Kind.Player));
    }

    [Fact]
    public void PrisonerOwner_RosterPrisonerGetsMarks_WithTheCppDebugLine_EvenOnTheTownBoard()
    {
        var f = StoreFake.TwoPlayers();
        f.Players.Remove(2); f.Roster.Add(2);   // Bob is a roster prisoner (M6)
        var (w, _, s) = New(f);
        var snap = Snap(SparPodium.OutcomeKind.TeamBWins);
        Assert.True(s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Town));
        Assert.True(snap.Fighters[1].MarksUpdated);
        var bob = w.Data.Progression.Single(r => r.Id == "k2");
        Assert.Equal(snap.Fighters[1].MarksAfter, bob.Marks);
        Assert.Contains("Proving Grounds: prisoner Marks awarded name=Bob key=k2 earned=" + snap.Fighters[1].MarksEarned + " total=" + bob.Marks, f.Debugs);
        Assert.Equal("Proving Grounds: ratings updated in active world snapshot", f.Debugs[^1]);
    }

    [Fact]
    public void TeamBonusAndMultiplier_ReachTheLedger()
    {
        var (w, _, s) = New();
        var snap = Snap(SparPodium.OutcomeKind.TeamAWins);
        var marks = new int[16];
        ArenaMarks.ComputeAwards(Snap(SparPodium.OutcomeKind.TeamAWins), [30f, 30f, .. new float[14]], marks, 2f);
        var mm = new bool[16]; mm[0] = mm[1] = true;
        ArenaMarks.ApplyWinningTeamBonus(Snap(SparPodium.OutcomeKind.TeamAWins), mm, marks, 5);
        s.ApplyFromSnapshot(snap, LeaderboardData.Kind.Player, 2f, 5);
        Assert.Equal(marks[0], w.Data.Progression[0].Marks);
        Assert.Equal(marks[1], w.Data.Progression[1].Marks);
    }

    // ---------- the purchases ----------

    [Fact]
    public void RewardTierPurchase_BeginThenRollback_IsExact()
    {
        var (w, _, s) = New();
        Assert.True(s.SetMarks(1, 100));
        Assert.Equal(-1, s.GetRewardTier(1, "armour.chest"));
        Assert.False(s.BeginRewardTierPurchase(1, 200, "armour.chest", -1, 0));   // not enough Marks
        Assert.False(s.BeginRewardTierPurchase(1, 30, "armour.chest", 0, 1));     // wrong previous tier
        Assert.Equal(100, s.GetMarks(1));
        Assert.True(s.BeginRewardTierPurchase(1, 30, "armour.chest", -1, 0));
        Assert.Equal(70, s.GetMarks(1));
        Assert.Equal(0, s.GetRewardTier(1, "armour.chest"));
        s.RollbackRewardTierPurchase(1, 30, "armour.chest", -1);
        Assert.Equal(100, s.GetMarks(1));
        Assert.Equal(-1, s.GetRewardTier(1, "armour.chest"));
        s.RollbackRewardTierPurchase(1, 30, "armour.chest", -1);   // nothing left to roll back
        Assert.Equal(100, s.GetMarks(1));
        Assert.False(s.BeginRewardTierPurchase(1, 30, null, -1, 0));
        Assert.False(s.BeginRewardTierPurchase(0, 30, "armour.chest", -1, 0));    // no character
    }

    [Fact]
    public void FactionUnlockPurchase_BeginThenRollback_IsExact()
    {
        var (w, _, s) = New();
        s.SetMarks(1, 50);
        Assert.False(s.HasFactionUnlock("licence.weapons"));
        Assert.True(s.BeginFactionUnlockPurchase(1, 20, "licence.weapons"));
        Assert.True(s.HasFactionUnlock("licence.weapons"));
        Assert.Equal(30, s.GetMarks(1));
        Assert.False(s.BeginFactionUnlockPurchase(2, 0, "licence.weapons"));   // already owned faction-wide
        s.RollbackFactionUnlockPurchase(1, 20, "licence.weapons");
        Assert.False(s.HasFactionUnlock("licence.weapons"));
        Assert.Equal(50, s.GetMarks(1));
        Assert.True(s.AddFactionUnlock("x"));
        Assert.False(s.AddFactionUnlock("x"));
        s.RemoveFactionUnlock("x");
        Assert.False(s.HasFactionUnlock("x"));
        Assert.False(s.AddFactionUnlock(""));
        w.BlockPersistence("b");
        Assert.False(s.AddFactionUnlock("y"));
        Assert.False(s.HasFactionUnlock("y"));
    }

    [Fact]
    public void SpendAndRefundMarks()
    {
        var (_, _, s) = New();
        s.SetMarks(1, 10);
        Assert.False(s.SpendMarks(1, 11));
        Assert.False(s.SpendMarks(1, -1));
        Assert.True(s.SpendMarks(1, 10));
        Assert.Equal(0, s.GetMarks(1));
        s.RefundMarks(1, 4);
        Assert.Equal(4, s.GetMarks(1));
        var f = StoreFake.TwoPlayers(); f.Keys.Remove(2);
        var (_, _, s2) = New(f);
        Assert.False(s2.SpendMarks(2, 0));   // no identity
        s2.RefundMarks(2, 5);
        Assert.Equal(0, s2.GetMarks(2));
    }

    [Fact]
    public void ReserveProgression_ThroughTheTransactionToken()
    {
        var (w, _, s) = New();
        s.SetMarks(1, 25);
        var acc = new Accounting("k1");
        Assert.True(s.ReserveProgression(1, new ArenaLedger.Purchase(10), acc));
        Assert.Equal(15, s.GetMarks(1));
        Assert.False(s.CaptureSnapshot(new ArenaPersistence.Snapshot()));   // a reserved purchase blocks the save capture
        Assert.True(acc.Rollback());
        Assert.Equal(25, s.GetMarks(1));
        Assert.True(s.CompleteProgressionTransaction());
        w.BlockPersistence("b");
        Assert.False(s.CompleteProgressionTransaction());
    }

    sealed class Accounting(string id) : RewardTransaction.SelfBoundReservedAccounting
    {
        public override bool Matches(string token) => token == id;
    }

    // ---------- the setters and the reads ----------

    [Fact]
    public void SetRating_ClampsAndRefuses()
    {
        var (w, f, s) = New();
        Assert.True(s.SetRating(1, 12000));
        Assert.Equal(9999f, s.GetRating(1));
        Assert.True(s.SetRating(1, -5));
        Assert.Equal(0f, s.GetRating(1));
        Assert.True(s.SetMarks(1, 10001));
        Assert.Equal(9999, s.GetMarks(1));
        f.Invalid.Add(2);
        int calls = f.CreateCalls;
        Assert.False(s.SetRating(2, 150));
        Assert.Equal(calls, f.CreateCalls);   // the invalid one made no identity
        f.Invalid.Clear(); f.Keys.Remove(2);
        Assert.False(s.SetMarks(2, 5));
        Assert.Equal(["Proving Grounds: unavailable or conflicting fighter identity during Marks update"], f.Errors);
        w.BlockPersistence("b");
        int before = f.CreateCalls;
        Assert.False(s.SetRating(1, 150));
        Assert.Equal(before, f.CreateCalls);
    }

    [Fact]
    public void Reads_DefaultsAndTheOnceLog()
    {
        var f = StoreFake.TwoPlayers();
        var (w, _, s) = New(f);
        Assert.Equal(MmrRating.DefaultMmr, s.GetRating(0));
        Assert.Equal(0, s.GetMarks(0));
        Assert.Empty(f.Errors);   // no character: nothing logged
        Assert.Equal(MmrRating.DefaultMmr, s.GetRating(1));   // no identity made yet (Find): logged
        Assert.Equal(0, s.GetMarks(1));                        // the same character: not again
        Assert.Equal(["Proving Grounds: unavailable or conflicting fighter identity during rating lookup"], f.Errors);
        f.Created.Add(1);
        Assert.Equal(MmrRating.DefaultMmr, s.GetRating(1));   // identity, no row
        w.Data.PlayerStandings.Add(new LeaderboardData.Standing { Id = "k1", Mmr = 321.25f, Matches = 3 });
        Assert.Equal(321.25f, s.GetRating(1));
        Assert.Equal(3, s.GetMatchCount(1));
        Assert.Equal(-1, s.GetRewardTier(1, ""));
    }

    [Fact]
    public void Standings_FilterOrderAndLiveNames()
    {
        var f = StoreFake.TwoPlayers();
        var (w, _, s) = New(f);
        f.Created.Add(1);
        f.Names[1] = "Annie";
        var rows = w.Data.PlayerStandings;
        rows.Add(new LeaderboardData.Standing { Id = "kz", Name = "Zed", Mmr = 200f, Matches = 1 });
        rows.Add(new LeaderboardData.Standing { Id = "k1", Name = "Ann", Mmr = 150f, Matches = 2 });
        rows.Add(new LeaderboardData.Standing { Id = "ka", Name = "Abe", Mmr = 200f, Matches = 1 });
        rows.Add(new LeaderboardData.Standing { Id = "k0", Name = "None", Mmr = 900f, Matches = 0 });   // no match: not listed
        var st = s.GetStandings(LeaderboardData.Kind.Player);
        Assert.Equal(["Abe", "Zed", "Annie"], st.Select(r => r.Name));
        Assert.Equal("Ann", rows[1].Name);   // copies: the table keeps its name
    }

    [Fact]
    public void FindRatedCharacter_SquadThenPrisonersThenTheTownSearch()
    {
        var f = StoreFake.TwoPlayers();
        f.Keys[7] = "k7"; f.Keys[8] = "k8";
        f.Created.UnionWith([1, 2, 7, 8]);
        f.RosterList.Add(7);
        var (_, _, s) = New(f);
        Assert.True(s.FindRatedCharacter(LeaderboardData.Kind.Player, "k2", null, out int c) && c == 2);
        Assert.True(s.FindRatedCharacter(LeaderboardData.Kind.Player, "k7", null, out c) && c == 7);
        Assert.False(s.FindRatedCharacter(LeaderboardData.Kind.Player, "k8", () => [8], out _));   // the sphere: town board only
        Assert.True(s.FindRatedCharacter(LeaderboardData.Kind.Town, "k8", () => [8], out c) && c == 8);
        f.Dead.Add(8);
        Assert.False(s.FindRatedCharacter(LeaderboardData.Kind.Town, "k8", () => [8], out _));   // the dead are skipped
        Assert.False(s.FindRatedCharacter(LeaderboardData.Kind.Town, "", () => [8], out _));
    }

    [Fact]
    public void TownAccessors_HeldWhileReady_ScratchOtherwise()
    {
        var (w, _, s) = New();
        s.BookieCredit = 12;
        Assert.Equal(12, w.Ledger.State().BookieCredit);
        Assert.Same(w.Ledger.State().Challenges, s.GetTownChallenges());
        w.Data.TownStandings.Add(new LeaderboardData.Standing { Id = "t" });
        Assert.True(s.ResetTownStandings());
        Assert.Empty(w.Data.TownStandings);
        s.GetChallengeAccount().Credit = 33;
        var capture = new ArenaPersistence.Snapshot();
        Assert.True(s.CaptureSnapshot(capture));
        Assert.Equal(33, capture.ChallengeCredit);   // SaveCredit(g_challengeAccount)
        w.BlockPersistence("b");
        Assert.Equal(0, s.BookieCredit);
        s.BookieCredit = 5;
        Assert.Equal(0, s.BookieCredit);
        Assert.NotSame(w.Ledger.State().Challenges, s.GetTownChallenges());
        s.GetChallengeAccount().Credit = 7;
        Assert.Equal(0, s.GetChallengeAccount().Credit);
        Assert.False(s.ResetTownStandings());
        Assert.Equal("", s.GetDiagnosticError());
        Assert.False(s.HasActiveSave());
        Assert.Equal("b", s.GetPersistenceError());
    }

    [Fact]
    public void ProgressionSurvivesTheSave_CaptureEncodeDecodeActivate()
    {
        var (w, _, s) = New();
        string chest = ArenaRewards.StableId((ArenaRewards.Piece)0), blade = ArenaRewards.StableId((ArenaRewards.Piece)1);
        string licence = ArenaRewards.GetWeaponLicence().StableId;
        s.SetMarks(1, 80);
        Assert.True(s.BeginRewardTierPurchase(1, 10, chest, -1, 0));
        Assert.True(s.BeginRewardTierPurchase(1, 10, chest, 0, 1));
        Assert.True(s.BeginRewardTierPurchase(1, 5, blade, -1, 0));
        Assert.True(s.BeginFactionUnlockPurchase(1, 15, licence));
        s.GetChallengeAccount().Credit = 21;
        Assert.True(w.TryWrite("gen-x", ["k1"], out byte[] payload, out string error), error);

        var loaded = new ArenaWorldState(_ => { }, _ => null);
        Assert.Equal(ArenaSaveData.ReadKind.Loaded, loaded.Read(payload).Kind);
        loaded.Resolve(loadedFromSave: true, tickCount: 1);
        var f2 = StoreFake.TwoPlayers(); f2.Created.Add(1);
        var s2 = new LeaderboardStoreLogic<int>(loaded, f2);
        Assert.Equal(40, s2.GetMarks(1));
        Assert.Equal(1, s2.GetRewardTier(1, chest));
        Assert.Equal(0, s2.GetRewardTier(1, blade));
        Assert.True(s2.HasFactionUnlock(licence));
        Assert.Equal(21, s2.GetChallengeAccount().Credit);
        var row = loaded.Data.Progression.Single();
        Assert.Equal(("k1", "Ann"), (row.Id, row.Name));
    }

    // ---------- RemoveTownNpcProgression (LeaderboardStore.cpp:117-134) ----------

    public enum Refusal { Invalid, Player, RosterPrisoner, MatchPrisoner, Chained }

    [Theory]
    [InlineData(Refusal.Invalid)]
    [InlineData(Refusal.Player)]
    [InlineData(Refusal.RosterPrisoner)]
    [InlineData(Refusal.MatchPrisoner)]
    [InlineData(Refusal.Chained)]
    public void RemoveTownNpcProgression_RefusesAndKeepsTheRow(Refusal why)
    {
        var f = StoreFake.TwoPlayers();
        f.Players.Remove(2);
        f.Created.Add(2);
        switch (why)
        {
            case Refusal.Invalid: f.Invalid.Add(2); break;
            case Refusal.Player: f.Players.Add(2); break;
            case Refusal.RosterPrisoner: f.Roster.Add(2); break;
            case Refusal.MatchPrisoner: f.MatchPrisonerSet.Add(2); break;
            case Refusal.Chained: f.Chained.Add(2); break;
        }
        var (w, _, s) = New(f);
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k2", Marks = 9 });
        Assert.False(s.RemoveTownNpcProgression(2));
        Assert.Equal(["k2"], w.Data.Progression.Select(r => r.Id));
    }

    [Fact]
    public void RemoveTownNpcProgression_NpcWithoutIdentity_TrueAndNothingRemoved()
    {
        var f = StoreFake.TwoPlayers();
        f.Players.Remove(2);   // valid NPC, no identity made (Find answers none)
        var (w, _, s) = New(f);
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k2", Marks = 9 });
        Assert.True(s.RemoveTownNpcProgression(2));
        Assert.Equal(["k2"], w.Data.Progression.Select(r => r.Id));
        Assert.Equal(0, f.CreateCalls);   // Find, never create
    }

    [Fact]
    public void RemoveTownNpcProgression_NpcWithKey_RemovesEveryRowWithThatKeyOnly()
    {
        var f = StoreFake.TwoPlayers();
        f.Players.Remove(2);
        f.Created.Add(2);
        var (w, _, s) = New(f);
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k2", Marks = 1 });
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k1", Marks = 2 });
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "k2", Marks = 3 });
        w.Data.Progression.Add(new LeaderboardData.Progression { Id = "kx", Marks = 4 });
        Assert.True(s.RemoveTownNpcProgression(2));
        Assert.Equal(["k1", "kx"], w.Data.Progression.Select(r => r.Id));
    }

    // ---------- blocked ledger, lifecycle ----------

    [Fact]
    public void BlockPersistence_ClearsTheIdentityLog()
    {
        var f = StoreFake.TwoPlayers();
        var (w, _, s) = New(f);
        s.GetMarks(1);
        s.GetMarks(1);
        Assert.Single(f.Errors);
        s.BlockPersistence("blocked");
        s.GetMarks(1);
        Assert.Equal(2, f.Errors.Count);
        Assert.Equal("blocked", s.GetPersistenceError());
    }

    [Fact]
    public void Reads_WhileBlocked_LogOnceAndReturnDefaults()
    {
        var f = StoreFake.TwoPlayers();
        var (w, _, s) = New(f);
        s.SetMarks(1, 40);
        s.SetRating(1, 300);
        Assert.True(s.BeginRewardTierPurchase(1, 0, ArenaRewards.StableId((ArenaRewards.Piece)0), -1, 0));
        w.BlockPersistence("blocked");
        int created = f.CreateCalls;
        Assert.Equal(MmrRating.DefaultMmr, s.GetRating(1));
        Assert.Equal(0, s.GetMarks(1));
        Assert.Equal(-1, s.GetRewardTier(1, ArenaRewards.StableId((ArenaRewards.Piece)0)));
        Assert.Equal(0, s.GetMatchCount(1));
        Assert.Equal(["Proving Grounds: unavailable or conflicting fighter identity during rating lookup"], f.Errors);
        Assert.Equal(created, f.CreateCalls);   // no identity made while blocked
    }

    [Fact]
    public void Standings_WhileNotReady_AreEmpty()
    {
        var (w, _, s) = New();
        w.Data.PlayerStandings.Add(new LeaderboardData.Standing { Id = "k1", Name = "Ann", Matches = 2 });
        w.BlockPersistence("blocked");   // the C++ Block empties the held snapshot
        Assert.Empty(s.GetStandings(LeaderboardData.Kind.Player));
        Assert.False(s.HasActiveSave());
    }

    [Fact]
    public void CompleteWorldTransition_NewGameActivatesAFreshSeededSnapshot_ElseBlocks()
    {
        var (w, f, s) = New();
        w.Data.PlayerStandings.Add(new LeaderboardData.Standing { Id = "k1" });
        s.CompleteWorldTransition(newGame: true, tickCount: 1234);
        Assert.True(s.HasActiveSave());
        Assert.Equal(1235u, w.Ledger.State().Challenges.Seed);
        Assert.Empty(w.Data.PlayerStandings);
        Assert.Equal("", s.GetPersistenceError());
        s.CompleteWorldTransition(newGame: false, tickCount: 1);
        Assert.False(s.HasActiveSave());
        Assert.Equal("Waiting for native arena snapshot ownership metadata.", s.GetPersistenceError());
        s.Unload();
        Assert.Equal("Arena world state is unloaded.", s.GetPersistenceError());
        Assert.False(s.CommitSaveSnapshot("a", "b"));
        Assert.Equal(["Proving Grounds: legacy arena sidecar publication is disabled."], f.Errors);
    }
}
