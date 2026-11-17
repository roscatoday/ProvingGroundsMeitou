using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>ArenaRingGuard.cpp's tick (Core/Glue/RingGuardLogic.cs) over a fake host: the zones, the return on the fighter's own bearing at the
/// return radius with its height kept, the near-band step, the centre case, the off condition, Banner / small / full scales, eliminated and
/// downed fighters left alone, every outside fighter corrected in one pass, the 1,000 ms gate per kind (E3).</summary>
public class RingGuardLogicTests
{
    sealed class Fighter
    {
        public float X, Y, Z;
        public bool Exists = true, Eliminated, Downed, NoMovement;
    }

    sealed class Host : IRingGuardHost<int>
    {
        public bool Active = true, SiteValid = true;
        public float Cx = 1000, Cy = 50, Cz = -2000;
        public readonly List<Fighter> F = [];
        public uint Now = 3_600_000;
        public Func<float, float, bool> Walkable = (_, _) => true;
        public float Ground = 0;   // ground height under any spot, added to the nearY hint (a slope)
        public readonly List<(int Id, float X, float Y, float Z)> Placed = [];
        public readonly List<string> Lines = [];

        public bool SparActive => Active;
        public bool TryGetSiteCentre(out float x, out float y, out float z) { x = Cx; y = Cy; z = Cz; return SiteValid; }
        public int ParticipantCount => F.Count;
        public int Participant(int i) => F[i] is null ? -1 : i;
        public bool IsNone(int c) => c < 0;
        public bool Exists(int c) => F[c].Exists;
        public bool IsEliminated(int c) => F[c].Eliminated;
        public bool TryGetPosition(int c, out float x, out float y, out float z)
        {
            var f = F[c]; x = f.X; y = f.Y; z = f.Z; return !f.NoMovement;
        }
        public bool IsDowned(int c) => F[c].Downed;
        public bool TryPlaceKeepingTask(int c, float x, float nearY, float z)
        {
            if (!Walkable(x, z)) return false;
            float y = nearY + Ground;
            Placed.Add((c, x, y, z)); F[c].X = x; F[c].Y = y; F[c].Z = z;
            return true;
        }
        public uint NowMs => Now;
        public void Debug(string line) => Lines.Add(line);

        /// <summary>A fighter at <paramref name="r"/> from the centre on bearing <paramref name="deg"/> (degrees from +x toward +z).</summary>
        public Fighter At(float r, float deg = 0, float y = 7)
        {
            double a = deg * Math.PI / 180;
            var f = new Fighter { X = Cx + (float)(r * Math.Cos(a)), Y = y, Z = Cz + (float)(r * Math.Sin(a)) };
            F.Add(f);
            return f;
        }

        public float Dist(Fighter f) => MathF.Sqrt((f.X - Cx) * (f.X - Cx) + (f.Z - Cz) * (f.Z - Cz));
    }

    static (Host, RingGuardLogic<int>) Rig(RingSiteKind kind = RingSiteKind.Arena)
    {
        var h = new Host();
        var g = new RingGuardLogic<int>();
        g.SetArena(true, kind);
        return (h, g);
    }

    [Fact]
    public void Constants_AsTheCpp()
    {
        Assert.Equal(0.6f, RingGuardLogic<int>.SmallArenaRingScale);
        Assert.Equal(1000u, RingGuardLogic<int>.CorrectionIntervalMs);
        Assert.Equal(2.5f, RingGuardLogic<int>.NearStepUnits);
        Assert.Equal(145.0, BalanceTuning.Get().RingNearRadius);
        Assert.Equal(147.5, BalanceTuning.Get().RingOutRadius);
        Assert.Equal(142.5, BalanceTuning.Get().RingReturnRadius);
    }

    [Fact]
    public void RingScaleForSite_NoneBannerSmallFull()
    {
        Assert.Equal(0.0f, RingGuardLogic<int>.RingScaleForSite(false, RingSiteKind.Arena));
        Assert.Equal(0.0f, RingGuardLogic<int>.RingScaleForSite(true, RingSiteKind.Banner));
        Assert.Equal(0.6f, RingGuardLogic<int>.RingScaleForSite(true, RingSiteKind.SmallArena));
        Assert.Equal(1.0f, RingGuardLogic<int>.RingScaleForSite(true, RingSiteKind.Arena));
    }

    [Fact]
    public void Inside_NothingMoved()
    {
        var (h, g) = Rig();
        h.At(0.5f); h.At(100, 30); h.At(144.9f, 200);
        g.Tick(h);
        Assert.Empty(h.Placed);
        Assert.Empty(h.Lines);
    }

    [Fact]
    public void BeyondOut_ReturnedOnOwnBearing_AtReturnRadius_HeightKept()
    {
        var (h, g) = Rig();
        var f = h.At(200, 37, y: 12.5f);
        float bx = f.X - h.Cx, bz = f.Z - h.Cz;
        g.Tick(h);
        Assert.Single(h.Placed);
        Assert.Equal(142.5f, h.Dist(f), 3);
        Assert.Equal(12.5f, f.Y);
        // the same bearing: the new offset is the old one scaled
        Assert.Equal(MathF.Atan2(bz, bx), MathF.Atan2(f.Z - h.Cz, f.X - h.Cx), 4);
        Assert.Equal(1, g.OutCorrections);
        Assert.Equal("Proving Grounds: ring guard returned fighter to the ring (dist=200 out=148 -> 143 scale=1.00)", Assert.Single(h.Lines));
    }

    [Fact]
    public void ExactBoundaries_NearAndOutAreInclusive()
    {
        var (h, g) = Rig();
        var n = h.At(145.0f);    // >= near: near band
        var o = h.At(147.5f, 90);   // >= out: outside
        g.Tick(h);
        Assert.Equal(2, h.Placed.Count);
        Assert.Equal(142.5f, h.Dist(n), 3);   // 145 - 2.5
        Assert.Equal(142.5f, h.Dist(o), 3);
        Assert.Equal(1, g.NearCorrections);
        Assert.Equal(1, g.OutCorrections);
        // just inside near: untouched
        var (h2, g2) = Rig();
        h2.At(144.99f);
        g2.Tick(h2);
        Assert.Empty(h2.Placed);
    }

    [Fact]
    public void NearBand_StepsInward_OnOwnBearing_NeverPastReturn()
    {
        var (h, g) = Rig();
        var f = h.At(146.5f, -120, y: 3);
        g.Tick(h);
        Assert.Equal(144.0f, h.Dist(f), 3);   // 146.5 - 2.5
        Assert.Equal(3f, f.Y);
        Assert.Equal(-120.0, Math.Atan2(f.Z - h.Cz, f.X - h.Cx) * 180 / Math.PI, 2);
        Assert.Equal("Proving Grounds: ring guard steering fighter inward (dist=147 near=145 -> 143 scale=1.00)", Assert.Single(h.Lines));
        // small arena: band 87..88.5, return 85.5, the step scaled to 2.5 x 0.6 = 1.5: 88 -> 86.5
        var (s, gs) = Rig(RingSiteKind.SmallArena);
        var fs = s.At(88f);
        gs.Tick(s);
        Assert.Equal(86.5f, s.Dist(fs), 3);
        // a step that would pass the return point stops there (tuned: near 100, return 99.5, step 2.5 from 100.2)
        using var _ = BalanceTuning.UseForTest(new BalanceTuning.Values { RingNearRadius = 100, RingOutRadius = 105, RingReturnRadius = 99.5 });
        var (t, gt) = Rig();
        var ft = t.At(100.2f);
        gt.Tick(t);
        Assert.Equal(99.5f, t.Dist(ft), 3);
    }

    [Fact]
    public void FighterAtTheCentre_ZeroBearing_GoesAlongPlusX()
    {
        // RingReturnPoint (ArenaRingGuard.h:44-48): length <= 1 -> (targetRadius, 0). Reachable only with radii where near <= 1: tune them.
        var t = new BalanceTuning.Values { RingNearRadius = 0.5, RingOutRadius = 0.6, RingReturnRadius = 0.4 };
        using var _ = BalanceTuning.UseForTest(t);
        var (h, g) = Rig();
        var f = h.At(0.8f, 90);   // outside (>= 0.6), length 0.8 <= 1
        g.Tick(h);
        Assert.Equal(h.Cx + 0.4f, f.X, 4);
        Assert.Equal(h.Cz, f.Z, 4);
    }

    [Fact]
    public void OffWhenOutNotAboveNear_OrReturnNotBelowNear()
    {
        foreach (var t in new[]
        {
            new BalanceTuning.Values { RingNearRadius = 145, RingOutRadius = 145, RingReturnRadius = 142.5 },   // out <= near
            new BalanceTuning.Values { RingNearRadius = 145, RingOutRadius = 140, RingReturnRadius = 130 },
            new BalanceTuning.Values { RingNearRadius = 145, RingOutRadius = 147.5, RingReturnRadius = 145 },   // return >= near
        })
        {
            using var _ = BalanceTuning.UseForTest(t);
            var (h, g) = Rig();
            h.At(300); h.At(146, 10);
            g.Tick(h);
            Assert.Empty(h.Placed);
        }
    }

    [Fact]
    public void Banner_NoSite_InvalidSite_Inactive_DoNothing()
    {
        var (h, g) = Rig(RingSiteKind.Banner);
        h.At(300);
        g.Tick(h);
        Assert.Empty(h.Placed);
        Assert.Equal(0f, g.RingScale);

        var g2 = new RingGuardLogic<int>();   // never set
        g2.Tick(h);
        g2.SetArena(false, RingSiteKind.Arena);
        g2.Tick(h);
        Assert.Empty(h.Placed);

        var (h3, g3) = Rig();
        h3.At(300);
        h3.SiteValid = false;
        g3.Tick(h3);
        h3.SiteValid = true;
        h3.Active = false;
        g3.Tick(h3);
        Assert.Empty(h3.Placed);
        h3.Active = true;
        g3.Tick(h3);
        Assert.Single(h3.Placed);
    }

    [Fact]
    public void SmallAndFullArenaScales()
    {
        var (s, gs) = Rig(RingSiteKind.SmallArena);
        var a = s.At(120);   // full arena: inside; small (out 88.5): outside
        gs.Tick(s);
        Assert.Equal(85.5f, s.Dist(a), 3);   // 142.5 * 0.6
        Assert.Equal("Proving Grounds: ring guard returned fighter to the ring (dist=120 out=89 -> 86 scale=0.60)", Assert.Single(s.Lines));
        var (f, gf) = Rig();
        f.At(120);
        gf.Tick(f);
        Assert.Empty(f.Placed);
    }

    [Fact]
    public void Eliminated_Downed_Invalid_NoMovement_LeftAlone()
    {
        var (h, g) = Rig();
        h.At(300).Eliminated = true;
        h.At(300, 45).Downed = true;
        h.At(146, 90).Downed = true;
        h.At(300, 135).Exists = false;
        h.At(300, 180).NoMovement = true;
        g.Tick(h);
        Assert.Empty(h.Placed);
        Assert.Equal(2, g.DownedSkips);
        h.Now += 33; g.Tick(h);   // the next tick: the same two downed bodies are not counted again within the second
        Assert.Equal(2, g.DownedSkips);
        h.Now += 967; g.Tick(h);  // 1,000 ms later they count once more
        Assert.Equal(4, g.DownedSkips);
        // a downed skip does not stamp the gate: an up fighter right after is corrected at once
        var up = h.At(300, 225);
        g.Tick(h);
        Assert.Single(h.Placed);
        Assert.Equal(142.5f, h.Dist(up), 3);
    }

    [Fact]
    public void NullParticipant_Skipped()
    {
        var (h, g) = Rig();
        h.F.Add(null!);
        var f = h.At(300);
        g.Tick(h);
        Assert.Equal(142.5f, h.Dist(f), 3);
    }

    [Fact]
    public void AllOutsideFighters_CorrectedInOnePass()
    {
        var (h, g) = Rig();
        var a = h.At(300, 0); var b = h.At(160, 120); var c = h.At(500, 240);
        var n1 = h.At(146, 10); var n2 = h.At(147, 50);
        g.Tick(h);
        Assert.Equal(5, h.Placed.Count);
        foreach (var f in new[] { a, b, c }) Assert.Equal(142.5f, h.Dist(f), 3);
        Assert.Equal(143.5f, h.Dist(n1), 3);
        Assert.Equal(144.5f, h.Dist(n2), 3);
        Assert.Equal(3, g.OutCorrections);
        Assert.Equal(2, g.NearCorrections);
    }

    [Fact]
    public void Gate_PerKind_1000ms()
    {
        var (h, g) = Rig();
        var o = h.At(300);
        g.Tick(h);                        // out at t0
        Assert.Single(h.Placed);
        h.Now += 400;
        o.X = h.Cx + 300;                 // pushed out again
        var n = h.At(146, 90);            // near band: its own gate never stamped, due
        g.Tick(h);
        Assert.Equal(2, h.Placed.Count);  // only the near one
        Assert.Equal(h.Cx + 300, o.X);
        h.Now += 599;                     // 999 ms after the out correction
        g.Tick(h);
        Assert.Equal(2, h.Placed.Count);
        h.Now += 1;                       // 1000 ms: out due again
        g.Tick(h);
        Assert.Equal(3, h.Placed.Count);
        Assert.Equal(142.5f, h.Dist(o), 3);
        n.X = h.Cx; n.Z = h.Cz + 146.5f;  // near again, 600 ms after its correction
        g.Tick(h);
        Assert.Equal(3, h.Placed.Count);
        h.Now += 400;                     // 1000 ms after the near one
        g.Tick(h);
        Assert.Equal(4, h.Placed.Count);
    }

    [Fact]
    public void Gate_WrapSafe_AndSetArenaResetsIt()
    {
        var (h, g) = Rig();
        h.Now = uint.MaxValue - 200;
        var o = h.At(300);
        g.Tick(h);
        o.X = h.Cx + 300;
        h.Now = 799;                      // 1000 ms later across the wrap
        g.Tick(h);
        Assert.Equal(2, h.Placed.Count);
        o.X = h.Cx + 300;
        h.Now += 10;
        g.SetArena(true, RingSiteKind.Arena);   // a new bout: the stamps start again
        g.Tick(h);
        Assert.Equal(3, h.Placed.Count);
    }

    [Fact]
    public void TestScaleOverride()
    {
        var (h, g) = Rig();
        g.OverrideScaleForTest(0.1f);
        var f = h.At(20);
        g.Tick(h);
        Assert.Equal(14.25f, h.Dist(f), 3);
        var none = new RingGuardLogic<int>();
        none.OverrideScaleForTest(0.5f);   // no site: stays off
        Assert.Equal(0f, none.RingScale);
    }

    [Fact]
    public void GroundHeight_FromTheHost_NearOwnFloor()
    {
        var (h, g) = Rig();
        h.Ground = -1.5f;
        var f = h.At(200, 0, y: 12f);
        g.Tick(h);
        Assert.Equal(10.5f, f.Y);   // the host's ground under the spot, asked near the fighter's own height
    }

    [Fact]
    public void UnwalkableSpot_Left_NotStamped_CountedOncePerWindow()
    {
        var (h, g) = Rig();
        var blocked = h.At(300, 0);
        h.Walkable = (x, _) => x < h.Cx + 100;   // the return point on +x (142.5) is a wall
        g.Tick(h);
        Assert.Empty(h.Placed);
        Assert.Equal(1, g.UnwalkableSkips);
        Assert.Equal(0, g.OutCorrections);
        Assert.Equal(0u, g.OutAt);               // not stamped: another fighter is corrected at once
        var other = h.At(300, 180);
        h.Now += 33;
        g.Tick(h);
        Assert.Single(h.Placed);
        Assert.Equal(142.5f, h.Dist(other), 3);
        Assert.Equal(1, g.UnwalkableSkips);      // the blocked one again within the second: not counted twice
        Assert.Equal(300f, h.Dist(blocked), 3);
        // the near band too
        var (n, gn) = Rig();
        n.Walkable = (_, _) => false;
        n.At(146);
        gn.Tick(n);
        Assert.Empty(n.Placed);
        Assert.Equal(1, gn.UnwalkableSkips);
        Assert.Equal(0u, gn.NearAt);
    }

    [Fact]
    public void SetArena_ResetsCounts()
    {
        var (h, g) = Rig();
        h.At(300);
        g.Tick(h);
        Assert.Equal(1, g.OutCorrections);
        g.SetArena(true, RingSiteKind.Arena);
        Assert.Equal(0, g.OutCorrections);
        Assert.Equal(0u, g.OutAt);
    }
}
