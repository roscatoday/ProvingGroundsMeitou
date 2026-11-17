using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownMatchmakingPolicy;
using static ProvingGrounds.Core.TownMatchmakingSearch;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand tests for Core/TownMatchmakingSearch.cs (the queue and generation rule of TownMatchmakingSearch.cpp; the file needs
/// Windows.h, so no golden vectors).</summary>
public class TownMatchmakingSearchTests
{
    static Fighter F(string id, string role = "219-Proving Grounds.mod", double ability = 40) => new Fighter { Id = id, Role = role, Ability = ability };

    static Request R(uint generation, int slot, params string[] npcIds)
    {
        var r = new Request { Generation = generation, Slot = slot };
        r.Players.Add(F("p1", "", 50));
        foreach (string id in npcIds) r.Npcs.Add(F(id));
        return r;
    }

    static Match Valid(params string[] b) { var m = new Match { Valid = true }; m.A.Add("p1"); m.B.AddRange(b); return m; }

    [Fact]
    public void Request_and_result_defaults_are_the_cpp_ones()
    {
        var r = new Request();
        Assert.Equal(0u, r.Generation); Assert.Equal(-1, r.Slot); Assert.Equal(Division.Easy, r.Division); Assert.Equal(0u, r.Seed);
        Assert.Equal(4, r.MaxEnemies); Assert.Equal(6, r.MaxTotal); Assert.Equal(-1, r.Rarity); Assert.False(r.ForceSoloNamed);
        Assert.False(r.Teams1v1); Assert.Equal(PGConfig.ChallengeDifficulty.Normal, r.ChallengeDifficulty); Assert.Equal("", r.RequiredRole);
        var res = new Result();
        Assert.Equal(0u, res.Generation); Assert.Equal(-1, res.Slot); Assert.False(res.Match.Valid);
    }

    [Fact]
    public void Only_the_current_generation_is_queued_and_a_new_generation_clears_both_queues()
    {
        var q = new Queue();
        Assert.True(q.Enqueue(R(0, 0)));          // g_generation starts at 0
        Assert.False(q.Enqueue(R(7, 1)));
        Assert.Single(q.PendingRequests());
        Assert.True(q.ProcessOne(_ => Valid("n1")));
        Assert.Equal(1, q.PendingResultCount);
        Assert.True(q.Enqueue(R(0, 2)));
        q.BeginGeneration(7);
        Assert.Empty(q.PendingRequests());
        Assert.Equal(0, q.PendingResultCount);
        Assert.False(q.Poll(out _));
        Assert.False(q.Enqueue(R(0, 3)));
        Assert.True(q.Enqueue(R(7, 4)));
        Assert.Equal(7u, q.Generation);
    }

    [Fact]
    public void A_result_of_a_stale_generation_is_dropped()
    {
        var q = new Queue();
        q.BeginGeneration(3);
        q.Enqueue(R(3, 0, "n1", "n2"));
        Assert.True(q.TryTake(out Request? running));
        q.BeginGeneration(4);                       // the game thread moves on while the search runs
        q.Enqueue(R(4, 1, "n1", "n2"));
        q.Complete(running!, Valid("n1"));
        Assert.Equal(0, q.PendingResultCount);
        Assert.Equal(new[] { "n1", "n2" }, q.PendingRequests()[0].Npcs.Select(f => f.Id));   // nothing removed by the stale result
        Assert.Empty(q.PendingRequests()[0].History);
    }

    [Fact]
    public void A_valid_result_removes_its_opponents_from_the_other_queued_requests_and_records_them()
    {
        var q = new Queue();
        q.BeginGeneration(1);
        q.Enqueue(R(1, 0, "a", "b", "c", "d"));
        q.Enqueue(R(1, 1, "x", "c", "a", "a", "y"));   // duplicate ids: the reverse loop removes every copy
        q.Enqueue(R(1, 2, "e"));
        Assert.True(q.ProcessOne(_ => Valid("c", "a")));
        IReadOnlyList<Request> left = q.PendingRequests();
        Assert.Equal(new[] { "x", "y" }, left[0].Npcs.Select(f => f.Id));
        Assert.Equal(new[] { "e" }, left[1].Npcs.Select(f => f.Id));
        Assert.Equal(new[] { "a", "c" }, Assert.Single(left[0].History));   // RecordHistory sorts
        Assert.Equal(new[] { "a", "c" }, Assert.Single(left[1].History));
        Assert.True(q.Poll(out Result? result));
        Assert.Equal(1u, result!.Generation); Assert.Equal(0, result.Slot); Assert.Equal(new[] { "c", "a" }, result.Match.B);
    }

    [Fact]
    public void An_invalid_result_is_delivered_but_changes_no_other_request()
    {
        var q = new Queue();
        q.Enqueue(R(0, 0, "a"));
        q.Enqueue(R(0, 1, "a"));
        var invalid = new Match(); invalid.B.Add("a");
        q.ProcessOne(_ => invalid);
        Assert.Equal(new[] { "a" }, q.PendingRequests()[0].Npcs.Select(f => f.Id));
        Assert.Empty(q.PendingRequests()[0].History);
        Assert.True(q.Poll(out Result? result));
        Assert.False(result!.Match.Valid);
    }

    [Fact]
    public void Results_come_out_in_order_and_requests_are_copied_on_enqueue()
    {
        var q = new Queue();
        Request first = R(0, 5, "a");
        q.Enqueue(first);
        first.Npcs.Clear(); first.Slot = 9;           // the queue holds its own copy
        q.Enqueue(R(0, 6, "b"));
        var seen = new List<int>();
        while (q.ProcessOne(r => { seen.Add(r.Npcs.Count); return new Match(); })) { }
        Assert.Equal(new[] { 1, 1 }, seen);
        Assert.True(q.Poll(out Result? a)); Assert.True(q.Poll(out Result? b)); Assert.False(q.Poll(out _));
        Assert.Equal(5, a!.Slot); Assert.Equal(6, b!.Slot);
        Assert.False(q.ProcessOne());
    }

    [Fact]
    public void The_default_search_is_SelectPlayer_with_the_request_fields()
    {
        var r = new Request { Generation = 0, Slot = 2, Division = Division.Medium, Seed = 12345, MaxEnemies = 2, MaxTotal = 5, Rarity = 1,
            Teams1v1 = true, ChallengeDifficulty = PGConfig.ChallengeDifficulty.Hard };
        r.Players.Add(F("p1", "", 52));
        string[] roles = { "219-Proving Grounds.mod", "223-Proving Grounds.mod", "206-Proving Grounds.mod", "213-Proving Grounds.mod", "232-Proving Grounds.mod" };
        for (int i = 0; i < 10; ++i) r.Npcs.Add(F("n" + i, roles[i % 5], 30 + 3 * i));
        r.History.Add(new List<string> { "n3" });
        Match direct = SelectPlayer(r.Players, r.Npcs, r.Division, r.Seed, r.History, r.MaxEnemies, r.MaxTotal, r.RequiredRole, r.Rarity,
            r.ForceSoloNamed, r.Teams1v1, r.ChallengeDifficulty);
        Assert.True(direct.Valid);
        var q = new Queue();
        q.Enqueue(r);
        q.ProcessOne();
        Assert.True(q.Poll(out Result? result));
        Assert.Equal(direct.A, result!.Match.A); Assert.Equal(direct.B, result.Match.B);
        Assert.Equal(direct.ScoreA, result.Match.ScoreA); Assert.Equal(direct.ScoreB, result.Match.ScoreB);
    }

    // ---- guard: Request.Clone / Result.Clone copy every field (a new field must be added to Clone and to this count) ----
    const System.Reflection.BindingFlags Inst = TownMatchmakingPolicyTests.Inst;

    [Fact]
    public void Request_Clone_copies_every_field()
    {
        // C++ Request (TownMatchmakingSearch.h:6-25) has 14 members.
        Assert.True(typeof(Request).GetFields(Inst).Length == 14, "Request gained or lost a field: update Clone (and this count)");
        var r = new Request();
        int k = 0;
        foreach (var f in typeof(Request).GetFields(Inst))
        {
            ++k;
            if (f.FieldType == typeof(uint)) f.SetValue(r, (uint)(1000 + k));
            else if (f.FieldType == typeof(int)) f.SetValue(r, 1000 + k);
            else if (f.FieldType == typeof(bool)) f.SetValue(r, true);
            else if (f.FieldType == typeof(string)) f.SetValue(r, $"s{k}");
            else if (f.FieldType == typeof(Division)) f.SetValue(r, Division.Hard);
            else if (f.FieldType == typeof(PGConfig.ChallengeDifficulty)) f.SetValue(r, PGConfig.ChallengeDifficulty.Easy);
            else if (f.FieldType == typeof(List<Fighter>)) ((List<Fighter>)f.GetValue(r)!).Add(F($"f{k}", $"role{k}", k));
            else if (f.FieldType == typeof(List<List<string>>)) ((List<List<string>>)f.GetValue(r)!).Add(new List<string> { $"bout{k}" });
            else Assert.Fail($"Request field {f.Name} of type {f.FieldType} not handled: update this test");
        }
        Request c = r.Clone();
        var fresh = new Request();
        foreach (var f in typeof(Request).GetFields(Inst))
        {
            object? a = f.GetValue(r), b = f.GetValue(c);
            if (a is List<Fighter> fa)
            {
                Assert.False(ReferenceEquals(fa, b), f.Name + " shares its list");
                Assert.Equal(fa, (List<Fighter>)b!);
            }
            else if (a is List<List<string>> ha)
            {
                var hb = (List<List<string>>)b!;
                Assert.False(ReferenceEquals(ha, hb), f.Name + " shares its list");
                Assert.Equal(ha.Count, hb.Count);
                for (int i = 0; i < ha.Count; ++i) { Assert.False(ReferenceEquals(ha[i], hb[i]), $"{f.Name}[{i}] shares its bout"); Assert.Equal(ha[i], hb[i]); }
            }
            else
            {
                Assert.True(Equals(a, b), f.Name);
                Assert.False(Equals(a, f.GetValue(fresh)), f.Name + " was not set to a non-default value");
            }
        }
    }

    [Fact]
    public void Result_Clone_copies_every_field()
    {
        // C++ Result (TownMatchmakingSearch.h:27-33) has 3 members.
        Assert.True(typeof(Result).GetFields(Inst).Length == 3, "Result gained or lost a field: update Clone (and this count)");
        var r = new Result { Generation = 11, Slot = 4, Match = TownMatchmakingPolicyTests.DistinctMatch(2) };
        Result c = r.Clone();
        Assert.Equal(11u, c.Generation); Assert.Equal(4, c.Slot);
        TownMatchmakingPolicyTests.AssertMatchCopied(r.Match, c.Match, "Result.Clone");
    }
}
