using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaFormation;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaFormation.cs (+ Core/Vec3.cs) against Tests/Golden/formation.json (golden/gen_formation.cpp: verbatim copies of
/// ArenaIngress.cpp's formation maths, Ogre 1.9's quaternion maths in golden/stubs/ogre_formation_stub.h).</summary>
public class ArenaFormationGoldenTests
{
    static List<JsonElement> Cases(string kind)
    {
        var all = GoldenFile.Load("formation").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(all);
        return all;
    }

    static float F(JsonElement c, string name) => (float)c.GetProperty(name).GetDouble();
    static int I(JsonElement c, string name) => c.GetProperty(name).GetInt32();

    static Vec3 V(JsonElement e) =>
        new((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble());

    static Vec3 V(JsonElement c, string name) => V(c.GetProperty(name));

    static List<Vec3> Vs(JsonElement c, string name) => c.GetProperty(name).EnumerateArray().Select(V).ToList();

    static MatchTeam[] Teams(JsonElement c) =>
        c.GetProperty("teams").EnumerateArray().Select(t => (MatchTeam)t.GetInt32()).ToArray();

    static void Near(Vec3 expected, Vec3 actual, string what)
    {
        GoldenFile.Near(expected.X, actual.X, what + ".x");
        GoldenFile.Near(expected.Y, actual.Y, what + ".y");
        GoldenFile.Near(expected.Z, actual.Z, what + ".z");
    }

    /// <summary>A world-space point compared as its OFFSET from <paramref name="origin"/> (the site's position), so a layout error of
    /// a fraction of a unit is caught at any position: GoldenFile.Near is relative (1e-4 * |value|), which at x = 90,000 would let a
    /// target be 9 units off. Offsets are taken in double, so the subtraction is exact; the tolerance is GoldenFile.Near's on the
    /// offset plus one float step (ulp) of the world value, the rounding of <c>pos + offset</c> that both sides do in float (a 1-ulp
    /// difference in a sin/cos already moves the rounded sum one step at x = 90,000).</summary>
    static void NearAt(Vec3 origin, Vec3 expected, Vec3 actual, string what)
    {
        NearAt(origin.X, expected.X, actual.X, what + ".x");
        NearAt(origin.Y, expected.Y, actual.Y, what + ".y");
        NearAt(origin.Z, expected.Z, actual.Z, what + ".z");
    }

    static void NearAt(float origin, float expected, float actual, string what)
    {
        double e = (double)expected - origin, a = (double)actual - origin;
        double tolerance = 1e-4 * Math.Max(1.0, Math.Abs(e)) + Math.Max(Ulp(expected), Ulp(origin));
        if (!(Math.Abs(e - a) <= tolerance))
            Assert.Fail(System.FormattableString.Invariant(
                $"{what}: offset expected {e:R}, actual {a:R} (world {expected:R} vs {actual:R}, tolerance {tolerance:R})"));
    }

    static double Ulp(float v) => (double)MathF.BitIncrement(MathF.Abs(v)) - MathF.Abs(v);

    static void NearAt(Vec3 origin, List<Vec3> expected, Vec3[] actual, string what)
    {
        Assert.Equal(expected.Count, actual.Length);
        for (int i = 0; i < expected.Count; ++i)
            NearAt(origin, expected[i], actual[i], $"{what}[{i}]");
    }

    [Fact]
    public void Quaternion_from_yaw_matches()
    {
        foreach (var c in Cases("quat"))
        {
            Quat q = Quat.FromYaw(F(c, "yaw"));
            var e = c.GetProperty("q");
            GoldenFile.Near((float)e[0].GetDouble(), q.W, "w");
            GoldenFile.Near((float)e[1].GetDouble(), q.X, "x");
            GoldenFile.Near((float)e[2].GetDouble(), q.Y, "y");
            GoldenFile.Near((float)e[3].GetDouble(), q.Z, "z");
        }
    }

    [Fact]
    public void Quaternion_times_vector_matches()
    {
        foreach (var c in Cases("rot"))
        {
            var e = c.GetProperty("q");
            var q = new Quat((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble(), (float)e[3].GetDouble());
            Near(V(c, "out"), q * V(c, "v"), "rot");
        }
    }

    [Fact]
    public void HorizontalDistSq_matches()
    {
        foreach (var c in Cases("hd"))
            GoldenFile.Near(F(c, "d"), HorizontalDistSq(V(c, "a"), V(c, "b")), "d");
    }

    [Fact]
    public void LocalToWorld_matches()
    {
        foreach (var c in Cases("l2w"))
            NearAt(V(c, "pos"), V(c, "out"), LocalToWorld(V(c, "pos"), Quat.FromYaw(F(c, "yaw")), V(c, "local")), "l2w");
    }

    [Fact]
    public void ScrambleOffset_matches()
    {
        foreach (var c in Cases("scramble"))
            Near(V(c, "v"), ScrambleOffset(I(c, "slot"), I(c, "n"), F(c, "in"), F(c, "out")), $"scramble slot {I(c, "slot")} n {I(c, "n")}");
    }

    [Fact]
    public void TeamFormationOffset_matches()
    {
        foreach (var c in Cases("team"))
            Near(V(c, "v"), TeamFormationOffset((MatchTeam)I(c, "team"), I(c, "slot"), I(c, "n"), F(c, "side"), F(c, "col"), F(c, "row")),
                $"team {I(c, "team")} slot {I(c, "slot")} n {I(c, "n")}");
    }

    [Fact]
    public void AssignArenaTargets_matches()
    {
        foreach (var c in Cases("arena"))
        {
            var teams = Teams(c);
            Vec3 pos = V(c, "pos");
            var expected = Vs(c, "t");
            NearAt(pos, expected, AssignArenaTargets((MatchMode)I(c, "mode"), teams, teams.Length, pos, Quat.FromYaw(F(c, "yaw")), F(c, "scale")),
                "arena");
            NearAt(pos, expected, AssignArenaTargets((MatchMode)I(c, "mode"), (ReadOnlySpan<MatchTeam>)teams, pos, Quat.FromYaw(F(c, "yaw")),
                F(c, "scale")), "arena span");
        }
    }

    [Fact]
    public void AssignBannerTargets_matches()
    {
        foreach (var c in Cases("banner"))
        {
            var teams = Teams(c);
            Vec3 pos = V(c, "pos");
            NearAt(pos, Vs(c, "t"), AssignBannerTargets((MatchMode)I(c, "mode"), I(c, "null") != 0 ? null : teams, teams.Length, pos), "banner");
            if (I(c, "null") == 0)
                NearAt(pos, Vs(c, "t"), AssignBannerTargets((MatchMode)I(c, "mode"), (ReadOnlySpan<MatchTeam>)teams, pos), "banner span");
        }
    }

    [Fact]
    public void Begin_site_assignment_matches()
    {
        foreach (var c in Cases("site"))
        {
            var teams = Teams(c);
            Vec3 pos = V(c, "pos");
            string what = $"site loc {I(c, "loc")} busy {I(c, "busy")}";
            NearAt(pos, Vs(c, "t"), AssignSiteTargets((LocationMode)I(c, "loc"), I(c, "busy") != 0, (MatchMode)I(c, "mode"), teams, teams.Length,
                pos, Quat.FromYaw(F(c, "yaw"))), what);
            NearAt(pos, Vs(c, "t"), AssignSiteTargets((LocationMode)I(c, "loc"), I(c, "busy") != 0, (MatchMode)I(c, "mode"),
                (ReadOnlySpan<MatchTeam>)teams, pos, Quat.FromYaw(F(c, "yaw"))), what + " span");
        }
    }

    [Fact]
    public void WalkInTarget_matches()
    {
        foreach (var c in Cases("walkin"))
            NearAt(V(c, "pos"), V(c, "out"), WalkInTarget((LocationMode)I(c, "loc"), (MatchTeam)I(c, "team"), V(c, "pos"), Quat.FromYaw(F(c, "yaw"))),
                $"walkin loc {I(c, "loc")} team {I(c, "team")}");
    }

    [Fact]
    public void Escort_gather_and_moves_match()
    {
        foreach (var c in Cases("escort"))
        {
            Vec3? site = I(c, "site") != 0 ? V(c, "pos") : null;
            Vec3? registry = I(c, "reg") == 1 ? V(c, "rpos") : null;   // 0 none, 2 invalid: neither is used
            Vec3 b = EscortGatherBase((LocationMode)I(c, "loc"), site, Quat.FromYaw(F(c, "yaw")), registry);
            Vec3 origin = site ?? new Vec3(0.0f, 0.0f, 0.0f);
            NearAt(origin, V(c, "base"), b, "base");

            var states = c.GetProperty("esc").EnumerateArray().Select(e => e.GetInt32()).ToList();
            var expectedWho = c.GetProperty("who").EnumerateArray().Select(e => e.GetInt32()).ToList();
            // IssueEscortMoves skips null (0) and invalid (2) escorts but keeps their place in the grid
            Assert.Equal(Enumerable.Range(0, states.Count).Where(i => states[i] == 1).ToList(), expectedWho);
            var to = Vs(c, "to");
            for (int j = 0; j < expectedWho.Count; ++j)
                NearAt(origin, to[j], EscortMoveTarget(b, expectedWho[j]), $"escort {expectedWho[j]}");
        }
    }

    [Fact]
    public void RegistryApproachPoint_matches()
    {
        foreach (var c in Cases("regnode"))
        {
            Vec3? marker = I(c, "from") == 2 ? V(c, "marker") : null;   // the marker is only asked for a valid character
            NearAt(V(c, "pos"), V(c, "out"), RegistryApproachPoint(V(c, "pos"), Quat.FromYaw(F(c, "yaw")), marker), $"regnode from {I(c, "from")}");
        }
    }

    [Fact]
    public void FaceOpposingFormation_matches()
    {
        foreach (var c in Cases("face"))
        {
            Vec3 p = V(c, "p");
            Vec3? face = OpposingFacePoint((MatchMode)I(c, "mode"), Teams(c), Vs(c, "t").ToArray(), I(c, "i"), p);
            if (I(c, "looked") == 0)
                Assert.Null(face);
            else
            {
                Assert.NotNull(face);
                NearAt(p, V(c, "face"), face!.Value, "face");   // the fighter stands at or near its mark: offsets from it
            }
        }
    }
}
