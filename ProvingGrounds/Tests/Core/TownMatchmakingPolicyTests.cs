using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownMatchmakingPolicy;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownMatchmakingPolicy.cs that the golden vectors (Tests/Golden/matchmaking.json) do not show: the
/// constructors' defaults and the by-value copy of <see cref="Match"/>.</summary>
public class TownMatchmakingPolicyTests
{
    [Fact]   // TownMatchmakingPolicy.h:20, :34-38, :46-50
    public void Constructors_give_the_cpp_defaults()
    {
        var f = new Fighter();
        Assert.Equal("", f.Id); Assert.Equal("", f.Role); Assert.Equal(0.0, f.Ability); Assert.Equal(100.0, f.Mmr);
        var m = new Match();
        Assert.False(m.Valid); Assert.Empty(m.A); Assert.Empty(m.B); Assert.Equal(Match.FailureCode.NoFailure, m.Failure);
        Assert.Equal((int)TownDiagnosticData.Model.StatsOnly, m.DiagnosticModel); Assert.Equal(0, m.Relaxation);
        Assert.Equal(3, m.ModelScoreA.Length); Assert.All(m.ModelProbabilityA, p => Assert.Equal(0.0, p));
        var q = new DiagnosticRequest();
        Assert.False(q.Active); Assert.False(q.Underdog); Assert.True(q.BalanceStyleTier); Assert.Equal(.25, q.ReducedInfluence);
        for (int i = 0; i < TownDiagnosticData.CoverageCount; ++i) Assert.True(q.EnabledCoverage[i]);
    }

    [Fact]
    public void Match_clone_is_a_deep_copy()
    {
        var m = new Match { Valid = true, ScoreA = 2 };
        m.A.Add("a"); m.B.Add("b"); m.ModelScoreB[1] = 5;
        Match c = m.Clone();
        c.A.Add("x"); c.B.Clear(); c.ModelScoreB[1] = 9; c.ScoreA = 3;
        Assert.Equal(new[] { "a" }, m.A); Assert.Equal(new[] { "b" }, m.B); Assert.Equal(5.0, m.ModelScoreB[1]); Assert.Equal(2.0, m.ScoreA);
    }

    [Fact]   // TownMatchmakingPolicy.h:471-540: the saved stream moves exactly one step per attempt, whatever happens inside
    public void SelectAmbient_advances_the_saved_seed_once()
    {
        uint seed = 5;
        uint expected = 5; Hashing.Xorshift32(ref expected);
        SelectAmbient(new List<Fighter>(), ref seed, new List<List<string>>(), 1);
        Assert.Equal(expected, seed);
    }

    // ---- guard: Match.Clone / Ranked.Clone copy every field (a new field must be added to Clone and to this count) ----
    internal const System.Reflection.BindingFlags Inst =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    internal static Match DistinctMatch(int seed)
    {
        var m = new Match();
        int k = 0;
        foreach (var f in typeof(Match).GetFields(Inst))
        {
            ++k;
            if (f.FieldType == typeof(bool)) f.SetValue(m, true);
            else if (f.FieldType == typeof(int)) f.SetValue(m, seed * 100 + k);
            else if (f.FieldType == typeof(double)) f.SetValue(m, seed * 100 + k + .5);
            else if (f.FieldType == typeof(Match.FailureCode)) f.SetValue(m, Match.FailureCode.PlayersTooWeak);
            else if (f.FieldType == typeof(List<string>)) ((List<string>)f.GetValue(m)!).Add($"id{seed}_{k}");
            else if (f.FieldType == typeof(double[])) { var a = (double[])f.GetValue(m)!; for (int i = 0; i < a.Length; ++i) a[i] = seed * 100 + k + i / 10.0; }
            else Assert.Fail($"Match field {f.Name} of type {f.FieldType} not handled: update this test");
        }
        return m;
    }

    internal static void AssertMatchCopied(Match source, Match copy, string what)
    {
        Assert.False(ReferenceEquals(source, copy), what + ": same object");
        var fresh = new Match();
        foreach (var f in typeof(Match).GetFields(Inst))
        {
            object? a = f.GetValue(source), b = f.GetValue(copy);
            if (a is List<string> la)
            {
                Assert.False(ReferenceEquals(la, b), $"{what}: {f.Name} shares its list");
                Assert.Equal(la, (List<string>)b!);
            }
            else if (a is double[] da)
            {
                Assert.False(ReferenceEquals(da, b), $"{what}: {f.Name} shares its array");
                Assert.Equal(da, (double[])b!);
            }
            else
            {
                Assert.True(Equals(a, b), $"{what}: {f.Name}");
                Assert.False(Equals(a, f.GetValue(fresh)), $"{what}: {f.Name} was not set to a non-default value");
            }
        }
    }

    [Fact]
    public void Match_Clone_copies_every_field()
    {
        // C++ Match (TownMatchmakingPolicy.h:23-39) has 16 members; the C# class mirrors them one for one.
        Assert.True(typeof(Match).GetFields(Inst).Length == 16, "Match gained or lost a field: update Clone (and this count)");
        Match source = DistinctMatch(3);
        AssertMatchCopied(source, source.Clone(), "Match.Clone");
    }

    [Fact]
    public void Ranked_Clone_copies_every_field()
    {
        // C++ Detail::Ranked (TownMatchmakingPolicy.h:215-229) has 4 members.
        Assert.True(typeof(Detail.Ranked).GetFields(Inst).Length == 4, "Ranked gained or lost a field: update Clone (and this count)");
        var r = new Detail.Ranked { Match = DistinctMatch(5), Fit = 7, Novelty = 2.5, Tie = 99 };
        Detail.Ranked c = r.Clone();
        Assert.Equal(7, c.Fit); Assert.Equal(2.5, c.Novelty); Assert.Equal(99u, c.Tie);
        AssertMatchCopied(r.Match, c.Match, "Ranked.Clone");
    }
}
