using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaFormation;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaFormation.cs (task M4.3a): the edges formation.json cannot show (empty lists, the plan's table read at
// yaw 0, constants), each expected value read off ArenaIngress.cpp.

public class ArenaFormationTests
{
    static readonly Vec3 Origin = new(0.0f, 0.0f, 0.0f);
    static readonly Quat NoTurn = Quat.FromYaw(0.0f);

    static void Near(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 0.001f);
        Assert.Equal(expected.Y, actual.Y, 0.001f);
        Assert.Equal(expected.Z, actual.Z, 0.001f);
    }

    [Fact]   // ArenaIngress.cpp:286, 368 (targets.resize(0))
    public void No_fighters_give_no_targets()
    {
        foreach (MatchMode mode in Enum.GetValues<MatchMode>())
        {
            Assert.Empty(AssignArenaTargets(mode, [], 0, Origin, NoTurn));
            Assert.Empty(AssignArenaTargets(mode, null, 0, Origin, NoTurn));
            Assert.Empty(AssignBannerTargets(mode, null, 0, Origin));
            foreach (LocationMode loc in Enum.GetValues<LocationMode>())
                Assert.Empty(AssignSiteTargets(loc, false, mode, null, 0, Origin, NoTurn));
        }
    }

    [Fact]   // ArenaIngress.cpp:1329-1331: Small x0.5; Arena x0.70 unless the town arena is busy (x1.0)
    public void Staging_scale_by_location()
    {
        Assert.Equal(0.5f, StagingScale(LocationMode.SmallArena, false));
        Assert.Equal(0.5f, StagingScale(LocationMode.SmallArena, true));
        Assert.Equal(0.70f, StagingScale(LocationMode.Arena, false));
        Assert.Equal(1.0f, StagingScale(LocationMode.Arena, true));
    }

    [Fact]   // the plan's PA §2.5 table at scale 1, yaw 0: AvB x = -(85 + 35 column), rows 46 apart, centred per column
    public void Team_AvB_columns_and_rows()
    {
        var teams = Enumerable.Repeat(MatchTeam.TeamA, 7).Concat(Enumerable.Repeat(MatchTeam.TeamB, 2)).ToArray();
        Vec3[] t = AssignArenaTargets(MatchMode.TeamAvB, teams, teams.Length, Origin, NoTurn);
        // A: first column of 5 rows (centred: -92 .. 92), second column of 2 rows (-23, 23)
        Near(new(-85.0f, 0.0f, -92.0f), t[0]);
        Near(new(-85.0f, 0.0f, 92.0f), t[4]);
        Near(new(-120.0f, 0.0f, -23.0f), t[5]);
        Near(new(-120.0f, 0.0f, 23.0f), t[6]);
        Near(new(85.0f, 0.0f, -23.0f), t[7]);
        Near(new(85.0f, 0.0f, 23.0f), t[8]);
    }

    [Fact]   // ArenaIngress.cpp:290-316: first fighters on the pit markers, the rest on the benches (cycling)
    public void Teams1v1_pit_markers_and_benches()
    {
        MatchTeam[] teams = [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.None];
        Vec3[] t = AssignArenaTargets(MatchMode.Teams1v1, teams, teams.Length, Origin, NoTurn);
        Near(new(-80.0f, 0.0f, 0.0f), t[0]);
        Near(new(80.0f, 0.0f, 0.0f), t[1]);
        Near(new(-120.0f, 0.0f, 40.0f), t[2]);
        Near(new(-120.0f, 0.0f, 0.0f), t[3]);
        Near(new(-120.0f, 0.0f, -40.0f), t[4]);
        Near(new(-120.0f, 0.0f, 40.0f), t[5]);   // (aSlot - 1) % 3 cycles
        Near(new(120.0f, 0.0f, 40.0f), t[6]);    // a team-less fighter counts as B
    }

    [Fact]   // ArenaIngress.cpp:1743-1749: the walk-in pit marker is not scaled by the 0.70 the player arena stages at
    public void Walk_in_is_not_staged_at_070()
    {
        Near(new(-80.0f, 0.0f, 0.0f), WalkInTarget(LocationMode.Arena, MatchTeam.TeamA, Origin, NoTurn));
        Near(new(80.0f, 0.0f, 0.0f), WalkInTarget(LocationMode.Arena, MatchTeam.TeamB, Origin, NoTurn));
        Near(new(-40.0f, 0.0f, 0.0f), WalkInTarget(LocationMode.SmallArena, MatchTeam.TeamA, Origin, NoTurn));
        Near(new(20.0f, 0.0f, 0.0f), WalkInTarget(LocationMode.Banner, MatchTeam.TeamB, Origin, Quat.FromYaw(1.0f)));   // world offset
        // the initial formation of the same fighter at the same arena is staged at 0.70
        Near(new(-56.0f, 0.0f, 0.0f), AssignSiteTargets(LocationMode.Arena, false, MatchMode.Teams1v1, [MatchTeam.TeamA], 1, Origin, NoTurn)[0]);
    }

    [Fact]   // Ogre: a turn of +90 degrees about +Y takes local +X to world -Z and local +Z to world +X
    public void Local_to_world_turns_with_the_building()
    {
        Quat q = Quat.FromYaw(MathF.PI / 2.0f);
        Vec3 pos = new(1000.0f, 5.0f, -2000.0f);
        Near(new(1000.0f, 5.0f, -2080.0f), LocalToWorld(pos, q, new(80.0f, 0.0f, 0.0f)));
        Near(new(1080.0f, 5.0f, -2000.0f), LocalToWorld(pos, q, new(0.0f, 0.0f, 80.0f)));
    }

    [Fact]   // ArenaIngress.cpp:242-253: the sunflower stays between the radii; count <= 0 counts as 1
    public void Scramble_stays_in_the_ring()
    {
        for (int n = 1; n <= 40; ++n)
            for (int i = 0; i < n; ++i)
            {
                Vec3 v = ScrambleOffset(i, n, ArenaLastStandingInnerRadius, ArenaLastStandingOuterRadius);
                float r = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
                Assert.InRange(r, ArenaLastStandingInnerRadius - 0.01f, ArenaLastStandingOuterRadius + 0.01f);
                Assert.Equal(0.0f, v.Y);
            }
        Vec3 a = ScrambleOffset(0, 0, 10.0f, 20.0f), b = ScrambleOffset(0, 1, 10.0f, 20.0f), c = ScrambleOffset(0, -5, 10.0f, 20.0f);
        Assert.Equal(b, a);
        Assert.Equal(b, c);
    }

    [Fact]   // ArenaIngress.cpp:588-594
    public void Escort_grid_rows_of_three()
    {
        Vec3 b = new(10.0f, 2.0f, 20.0f);
        Near(new(-8.0f, 2.0f, 20.0f), EscortMoveTarget(b, 0));
        Near(new(10.0f, 2.0f, 20.0f), EscortMoveTarget(b, 1));
        Near(new(28.0f, 2.0f, 20.0f), EscortMoveTarget(b, 2));
        Near(new(-8.0f, 2.0f, 38.0f), EscortMoveTarget(b, 3));
    }

    [Fact]   // ArenaIngress.cpp:552, 570-573: no site = the origin; banner fallback -Z (world); arena fallback local (-140, 0, 0)
    public void Escort_gather_fallbacks()
    {
        Near(Origin, EscortGatherBase(LocationMode.Arena, null, NoTurn, new(5.0f, 0.0f, 5.0f)));
        Vec3 site = new(100.0f, 7.0f, 100.0f);
        Near(new(100.0f, 7.0f, -20.0f), EscortGatherBase(LocationMode.Banner, site, Quat.FromYaw(2.0f), null));
        Near(new(-40.0f, 7.0f, 100.0f), EscortGatherBase(LocationMode.Arena, site, NoTurn, null));
        // a Registry within 1 unit on the ground (height ignored) does not set the direction
        Near(new(-40.0f, 7.0f, 100.0f), EscortGatherBase(LocationMode.SmallArena, site, NoTurn, new(100.5f, 900.0f, 100.5f)));
        Near(new(220.0f, 7.0f, 100.0f), EscortGatherBase(LocationMode.Arena, site, NoTurn, new(5000.0f, -3.0f, 100.0f)));
    }

    [Fact]   // ArenaIngress.cpp:629-658
    public void Face_point_edges()
    {
        Vec3[] marks = [new(-10.0f, 0.0f, 0.0f), new(10.0f, 0.0f, 0.0f), new(30.0f, 0.0f, 0.0f)];
        MatchTeam[] teams = [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB];
        Assert.Equal(new Vec3(10.0f, 4.0f, 0.0f), OpposingFacePoint(MatchMode.TeamAvB, teams, marks, 0, new(-10.0f, 4.0f, 0.0f)));
        Assert.Equal(new Vec3(-10.0f, 4.0f, 0.0f), OpposingFacePoint(MatchMode.TeamAvB, teams, marks, 2, new(30.0f, 4.0f, 0.0f)));
        Assert.Equal(new Vec3(10.0f, 4.0f, 0.0f), OpposingFacePoint(MatchMode.LastStanding, teams, marks, 2, new(30.0f, 4.0f, 0.0f)));
        Assert.Null(OpposingFacePoint(MatchMode.TeamAvB, [MatchTeam.TeamA, MatchTeam.TeamA], marks[..2], 0, Origin));   // no opponent
        Assert.Null(OpposingFacePoint(MatchMode.TeamAvB, teams, marks, 0, new(9.5f, 0.0f, 0.5f)));                      // within 1 unit
        Assert.Null(OpposingFacePoint(MatchMode.TeamAvB, teams[..2], marks, 0, Origin));                                // size mismatch
        Assert.Null(OpposingFacePoint(MatchMode.TeamAvB, teams, marks, 3, Origin));
        Assert.True(AtMark(new(30.0f, 99.0f, 0.0f), new(0.0f, 0.0f, 0.0f), 30.0f));
        Assert.False(AtMark(new(30.01f, 0.0f, 0.0f), new(0.0f, 0.0f, 0.0f), 30.0f));
    }

    [Fact]   // the C++ dereferences teams unless the mode is Last Standing or there are no fighters
    public void Missing_teams_throw_up_front()
    {
        foreach (MatchMode mode in new[] { MatchMode.TeamAvB, MatchMode.Teams1v1 })
        {
            var e = Assert.Throws<ArgumentNullException>(() => AssignArenaTargets(mode, null, 2, Origin, NoTurn));
            Assert.Equal("teams", e.ParamName);
            Assert.Equal("teams", Assert.Throws<ArgumentNullException>(() => AssignBannerTargets(mode, null, 1, Origin)).ParamName);
            foreach (LocationMode loc in Enum.GetValues<LocationMode>())
                Assert.Equal("teams", Assert.Throws<ArgumentNullException>(
                    () => AssignSiteTargets(loc, true, mode, null, 3, Origin, NoTurn)).ParamName);
        }
        Assert.Equal(4, AssignArenaTargets(MatchMode.LastStanding, null, 4, Origin, NoTurn).Length);
        Assert.Equal(4, AssignBannerTargets(MatchMode.LastStanding, null, 4, Origin).Length);
    }

    [Fact]   // the span forms use one target per team entry and agree with the array + count forms
    public void Span_forms_match_array_forms()
    {
        MatchTeam[] teams = [MatchTeam.TeamB, MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.None, MatchTeam.TeamB, MatchTeam.TeamA];
        Quat q = Quat.FromYaw(-0.7f);
        Vec3 pos = new(123.0f, 4.0f, -56.0f);
        foreach (MatchMode mode in Enum.GetValues<MatchMode>())
        {
            Assert.Equal(AssignArenaTargets(mode, teams, teams.Length, pos, q, 0.7f), AssignArenaTargets(mode, teams.AsSpan(), pos, q, 0.7f));
            Assert.Equal(AssignBannerTargets(mode, teams, teams.Length, pos), AssignBannerTargets(mode, teams.AsSpan(), pos));
            foreach (LocationMode loc in Enum.GetValues<LocationMode>())
                Assert.Equal(AssignSiteTargets(loc, false, mode, teams, teams.Length, pos, q), AssignSiteTargets(loc, false, mode, teams.AsSpan(), pos, q));
            Assert.Equal(3, AssignArenaTargets(mode, teams, 3, pos, q).Length);   // the array form may cover a prefix
        }
    }

    [Fact]
    public void Vec3_equality()
    {
        Vec3 a = new(1.0f, 2.0f, 3.0f);
        Assert.True(a == new Vec3(1.0f, 2.0f, 3.0f));
        Assert.True(a != new Vec3(1.0f, 2.0f, 3.5f));
        Assert.True(a.Equals((object)new Vec3(1.0f, 2.0f, 3.0f)));
        Assert.False(a.Equals("a"));
        Assert.Equal(a.GetHashCode(), new Vec3(1.0f, 2.0f, 3.0f).GetHashCode());
        Assert.Equal(new Vec3(float.NaN, 0.0f, 0.0f), new Vec3(float.NaN, 0.0f, 0.0f));
    }

    [Fact]   // ArenaIngress.cpp:47-110
    public void Constants_and_tables()
    {
        Assert.Equal(6, ArenaMarkers.Length);
        Assert.Equal(ArenaMarkerCount, ArenaMarkers.Length);
        Assert.Equal(ArenaBenchCount, ArenaBenchA.Length);
        Assert.Equal(ArenaBenchCount, ArenaBenchB.Length);
        Assert.Equal(new Vec3(60.0f, 0.0f, -30.0f), ArenaMarkers[5]);
        Assert.Equal(new Vec3(-120.0f, 0.0f, -40.0f), ArenaBenchA[2]);
        Assert.Equal(new Vec3(-3.0f, 10.0f, 3.0f), RegistryUseNodeLocal);
        Assert.Equal(FormationArrivalRadius, NpcFormationProgressDistance);
        Assert.Equal(5, TeamRowsPerColumn);
    }
}
