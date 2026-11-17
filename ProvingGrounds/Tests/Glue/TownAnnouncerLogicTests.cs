using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using P = ProvingGrounds.Core.TownAnnouncerPolicy.Program;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.3: TownAnnouncer.cpp's decisions (Core/Glue/TownAnnouncerLogic.cs) over a fake world: the cast (House, Skarn for Senn / Torka
/// opponents, Senn and Torka only for slot 5) and its fallbacks, the spot search (record, town, finished, nearest), the speaker search (record, town,
/// player, fighter, faction, state, pending release, nearest), the shared external slots, the RUN walk re-ordered every 0.5 s, the arrival and the
/// hold facing the arena, the 20 s stall, the cues 3.25 s apart byte for byte, the duration, the release plans (resume, defer, forget) and
/// AbandonWorldState.</summary>
public class TownAnnouncerLogicTests
{
    const long Town = 7, OtherTown = 8;
    static readonly ArenaBuildingRef ArenaRef = new(ArenaBuildingSource.Town, "arena", PgRecords.Arena);
    static readonly Vector3 ArenaPos = new(0, 0, 0);
    static readonly Vector3 SpotPos = new(100, 10, 0);

    sealed class Npc
    {
        public string Record = PgRecords.ScratchAnnouncer;
        public long Town = TownAnnouncerLogicTests.Town;
        public Vector3 Pos;
        public bool Player, Dead, Ko, Combat, Carried, Carrying, Gone, Foreign;
        public bool Hold = true, Passive = false;
        public byte Speed = 1;
        public Vector3? Faced;
    }

    sealed class FakeHost : ITownAnnouncerHost<int>
    {
        public readonly Dictionary<int, Npc> Npcs = [];
        public readonly List<string> Calls = [];
        public readonly List<string> Debugs = [];
        public readonly List<(int Who, string Text)> Said = [];
        public readonly HashSet<int> Reserved = [];
        public readonly List<PlacedBuilding> Spots = [];
        public int ExternalCapacity = 4;
        public bool ArenaGone, SpotGone;
        public uint Now = 1000;
        public double Hours = 10.0;

        public FakeHost() => Spots.Add(Spot(SpotPos, Town, true, "spot"));

        public static PlacedBuilding Spot(Vector3 pos, long town, bool finished, string key, string record = PgRecords.AnnouncerSpot) =>
            new(new ArenaBuilding(ArenaBuildingSource.Town, key, record, null, finished, pos, 0f), town);

        public bool IsPaused => false;
        public uint NowMs => Now;
        public double GameHours => Hours;
        public void Debug(string line) => Debugs.Add(line);
        public int None => -1;
        public bool IsNull(int c) => c < 0;
        public bool Valid(int c) => Npcs.TryGetValue(c, out var n) && !n.Gone;
        public bool IsPlayer(int c) => Npcs[c].Player;
        public bool IsDead(int c) => Npcs[c].Dead;
        public bool IsUnconscious(int c) => Npcs[c].Ko;
        public bool InCombatMode(int c) => Npcs[c].Combat;
        public bool IsCarried(int c) => Npcs[c].Carried;
        public bool IsCarrying(int c) => Npcs[c].Carrying;
        public string RecordId(int c) => Valid(c) ? Npcs[c].Record : "";
        public bool IsTownFaction(int c) => !Npcs[c].Foreign;
        public Vector3 Position(int c) => Npcs[c].Pos;
        public long CurrentTown(int c) => Npcs[c].Town;
        public IReadOnlyList<int> CharactersInSphere(Vector3 origin, float radius, int max)
        {
            var list = new List<int>();
            foreach (var (c, n) in Npcs)
                if (Vector3.Distance(n.Pos, origin) <= radius && list.Count < max) list.Add(c);
            return list;
        }
        public PlacedBuilding? Resolve(ArenaBuildingRef b)
        {
            if (b == ArenaRef) return ArenaGone ? null : new PlacedBuilding(new ArenaBuilding(ArenaBuildingSource.Town, "arena", PgRecords.Arena, null, true, ArenaPos, 0f), Town);
            if (SpotGone) return null;
            foreach (var s in Spots) if (s.Building.Ref == b) return s;
            return null;
        }
        public IReadOnlyList<PlacedBuilding> SpotsInSphere(Vector3 origin, float radius, int max) =>
            Spots.Where(s => Vector3.Distance(s.Building.Position, origin) <= radius).Take(max).ToList();
        public bool ReserveExternal(int c)
        {
            Calls.Add("reserve " + c);
            if (Reserved.Count >= ExternalCapacity && !Reserved.Contains(c)) return false;
            return Reserved.Add(c);
        }
        public void ReleaseExternal(int c) { Calls.Add("release " + c); Reserved.Remove(c); }
        public (bool Hold, bool Passive) StandingOrders(int c) => (Npcs[c].Hold, Npcs[c].Passive);
        public void SetStandingOrders(int c, bool hold, bool passive) { Calls.Add($"orders {c} {hold} {passive}"); Npcs[c].Hold = hold; Npcs[c].Passive = passive; }
        public byte SpeedOf(int c) => Npcs[c].Speed;
        public void SetSpeed(int c, byte speed) { Calls.Add($"speed {c} {speed}"); Npcs[c].Speed = speed; }
        public void SetRun(int c) { Calls.Add("run " + c); Npcs[c].Speed = 2; }
        public int ThrowOnClearOf = -1;
        public void ClearOrders(int c) { if (c == ThrowOnClearOf) throw new InvalidOperationException("bad clear"); Calls.Add("clear " + c); }
        public void EndWalk(int c) => Calls.Add("endwalk " + c);
        public void Face(int c, Vector3 target) => Npcs[c].Faced = target;
        public void WalkTo(int c, Vector3 target) => Calls.Add($"walk {c} {target.X:0.##} {target.Y:0.##} {target.Z:0.##}");
        public void Replan(int c) => Calls.Add("replan " + c);
        public readonly HashSet<int> PendingHeld = [];
        public bool HoldPending(int c) { Calls.Add("holdpending " + c); return PendingHeld.Add(c) || true; }
        public void ReleasePendingHold(int c) { Calls.Add("unholdpending " + c); PendingHeld.Remove(c); }
        public void Say(int c, string text) => Said.Add((c, text));
        public int Walks(int c) => Calls.Count(x => x.StartsWith($"walk {c} ", StringComparison.Ordinal));
    }

    static FakeHost World(out int house)
    {
        var h = new FakeHost();
        house = 10;
        h.Npcs[house] = new Npc { Record = PgRecords.ScratchAnnouncer, Pos = new(50, 0, 0) };
        h.Npcs[1] = new Npc { Record = PgRecords.ScratchCivilian, Pos = new(5, 0, 0) };   // a fighter of the bout
        h.Npcs[2] = new Npc { Record = PgRecords.ScratchCivilian, Pos = new(6, 0, 0), Player = true };
        return h;
    }

    static readonly int[] Fighters = [2, 1];

    static TownAnnouncerLogic<int> BeginHouse(FakeHost h, string?[]? roles = null, int slot = 0)
    {
        var l = new TownAnnouncerLogic<int>(-1);
        roles ??= [PgRecords.ScratchCivilian];
        l.Begin(h, ArenaRef, true, slot, roles, roles.Length, Fighters, Fighters.Length);
        return l;
    }

    static void Step(FakeHost h, TownAnnouncerLogic<int> l, uint ms) { h.Now += ms; l.TickApproach(h); }

    [Fact]
    public void Npc_bout_keeps_the_legacy_countdown()
    {
        var h = World(out _);
        var l = new TownAnnouncerLogic<int>(-1);
        l.Begin(h, ArenaRef, false, 0, [PgRecords.ScratchCivilian], 1, Fighters, 2);
        Assert.False(l.HasAnnouncement());
        Assert.False(l.WaitingForPosition(h));
        Assert.Empty(h.Reserved);
        Assert.Empty(h.Debugs);
        Assert.Equal("Legacy fighter countdown", l.GetStatus());
        Assert.Equal(0f, l.AnnouncementDuration());
    }

    [Fact]
    public void House_cast_is_reserved_and_runs_to_the_spot()
    {
        var h = World(out int house);
        h.Npcs[11] = new Npc { Record = PgRecords.ScratchAnnouncer, Pos = new(900, 0, 0) };   // farther: not chosen
        var l = BeginHouse(h);
        Assert.Equal(P.HouseAnnouncer, l.Program);
        Assert.Equal(1, l.SpeakerCount);
        Assert.Equal(house, l.SpeakerAt(0).Actor);
        Assert.Equal(SpotPos, l.SpeakerAt(0).Target);
        Assert.True(l.SpeakerAt(0).Owned);
        Assert.True(l.SpeakerAt(0).Directed);
        Assert.Equal([house], h.Reserved);
        Assert.Contains($"orders {house} False False", h.Calls);
        Assert.Contains($"run {house}", h.Calls);
        Assert.Equal(1, h.Walks(house));
        Assert.Equal("Announcer walking to the viewing stand", l.GetStatus());
        Assert.Equal("Proving Grounds: announcer cast walking to spot 261", Assert.Single(h.Debugs));
        Assert.True(l.HasAnnouncement());
        Assert.True(l.WaitingForPosition(h));
        // the variant: GetTickCount() ^ (unsigned)(total hours * 60) (TownAnnouncer.cpp:349-351)
        Assert.Equal(TownAnnouncerPolicy.Variant(P.HouseAnnouncer, 1000u ^ 600u), l.Variant);
    }

    [Theory]
    [InlineData(PgRecords.Senn)]
    [InlineData(PgRecords.Torka)]
    public void Skarn_announces_against_Senn_or_Torka(string opponent)
    {
        var h = World(out _);
        h.Npcs[20] = new Npc { Record = PgRecords.Skarn, Pos = new(60, 0, 0) };
        var l = BeginHouse(h, [PgRecords.ScratchCivilian, opponent]);
        Assert.Equal(P.SkarnAnnouncer, l.Program);
        Assert.Equal(20, l.SpeakerAt(0).Actor);
    }

    [Fact]
    public void Skarn_missing_falls_back_to_House_then_to_the_countdown()
    {
        var h = World(out int house);
        var l = BeginHouse(h, [PgRecords.Senn]);
        Assert.Equal(P.HouseAnnouncer, l.Program);
        Assert.Equal(house, l.SpeakerAt(0).Actor);

        h.Npcs[house].Ko = true;
        l.Begin(h, ArenaRef, true, 0, [PgRecords.Senn], 1, Fighters, 2);
        Assert.Equal(P.LegacyCountdown, l.Program);
        Assert.Contains("Proving Grounds: announcer fallback - required cast unavailable", h.Debugs);
        Assert.Empty(h.Reserved);
    }

    [Fact]
    public void Senn_and_Torka_only_for_slot_5_side_by_side_16_apart()
    {
        var h = World(out _);
        h.Npcs[30] = new Npc { Record = PgRecords.Senn, Pos = new(40, 0, 0) };
        h.Npcs[31] = new Npc { Record = PgRecords.Torka, Pos = new(45, 0, 0) };
        var l = BeginHouse(h, [PgRecords.ScratchCivilian], slot: 4);
        Assert.Equal(P.HouseAnnouncer, l.Program);   // no booked slot reaches 5 (kept bug K2)

        l.Begin(h, ArenaRef, true, 5, [PgRecords.ScratchCivilian], 1, Fighters, 2);
        Assert.Equal(P.SennAndTorkaAnnouncers, l.Program);
        Assert.Equal(30, l.SpeakerAt(0).Actor);
        Assert.Equal(31, l.SpeakerAt(1).Actor);
        Vector3 a = l.SpeakerAt(0).Target, b = l.SpeakerAt(1).Target;
        Assert.Equal(16f, Vector3.Distance(a, b), 3);
        Assert.Equal(SpotPos, (a + b) / 2f);
        // side by side: the line between them is square to the way toward the arena (the arena lies along -X from the spot)
        Assert.Equal(0f, Vector3.Dot(b - a, Vector3.Normalize(ArenaPos - SpotPos)), 3);
        Assert.Equal(new Vector3(100, 10, 8), a);   // sideways = (-dz, 0, dx) of (-1, 0, 0), index 0 offset -8
        Assert.Equal(2, h.Reserved.Count);

        l.Begin(h, ArenaRef, true, 5, [PgRecords.Senn], 1, Fighters, 2);
        Assert.Equal(P.HouseAnnouncer, l.Program);   // Senn or Torka in the pit: the house announcer
    }

    [Fact]
    public void Begin_releases_the_previous_cast()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        l.Begin(h, ArenaRef, false, 0, [PgRecords.ScratchCivilian], 1, Fighters, 2);
        Assert.False(l.HasAnnouncement());
        Assert.Empty(h.Reserved);
        Assert.Contains($"release {house}", h.Calls);
    }

    [Fact]
    public void No_spot_or_wrong_spots_give_the_countdown()
    {
        var h = World(out _);
        h.Spots.Clear();
        h.Spots.Add(FakeHost.Spot(SpotPos, OtherTown, true, "other town"));
        h.Spots.Add(FakeHost.Spot(SpotPos, Town, false, "unfinished"));
        h.Spots.Add(FakeHost.Spot(SpotPos, Town, true, "not a spot", PgRecords.Arena));
        h.Spots.Add(FakeHost.Spot(new Vector3(3000, 0, 0), Town, true, "too far"));
        var l = BeginHouse(h);
        Assert.Equal(P.LegacyCountdown, l.Program);
        Assert.Equal("Proving Grounds: announcer fallback - spot 261 not found near arena", Assert.Single(h.Debugs));
        Assert.Empty(h.Reserved);
        Assert.DoesNotContain(h.Calls, c => c.StartsWith("reserve", StringComparison.Ordinal));
    }

    [Fact]
    public void Nearest_finished_spot_of_the_arena_town_is_chosen()
    {
        var h = World(out _);
        h.Spots.Clear();
        h.Spots.Add(FakeHost.Spot(new Vector3(500, 0, 0), Town, true, "far"));
        h.Spots.Add(FakeHost.Spot(new Vector3(200, 0, 0), Town, true, "near"));
        var l = BeginHouse(h);
        Assert.Equal("near", l.Spot!.Value.Key);
        Assert.Equal(new Vector3(200, 0, 0), l.SpeakerAt(0).Target);
    }

    [Theory]
    [InlineData("player")]
    [InlineData("fighter")]
    [InlineData("town")]
    [InlineData("dead")]
    [InlineData("ko")]
    [InlineData("combat")]
    [InlineData("carried")]
    [InlineData("carrying")]
    [InlineData("faction")]
    [InlineData("gone")]
    public void Speaker_must_be_free(string why)
    {
        var h = World(out int house);
        var n = h.Npcs[house];
        switch (why)
        {
            case "player": n.Player = true; break;
            case "fighter": h.Npcs.Remove(1); h.Npcs[1] = n; h.Npcs.Remove(house); break;
            case "town": n.Town = OtherTown; break;
            case "dead": n.Dead = true; break;
            case "ko": n.Ko = true; break;
            case "combat": n.Combat = true; break;
            case "carried": n.Carried = true; break;
            case "carrying": n.Carrying = true; break;
            case "faction": n.Foreign = true; break;
            case "gone": n.Gone = true; break;
        }
        var l = BeginHouse(h);
        Assert.Equal(P.LegacyCountdown, l.Program);
    }

    [Fact]
    public void Full_external_slots_give_the_countdown()
    {
        var h = World(out int house);
        h.ExternalCapacity = 0;
        var l = BeginHouse(h);
        Assert.False(l.HasAnnouncement());
        Assert.Contains("Proving Grounds: announcer fallback - speaker AI ownership could not be reserved", h.Debugs);
        Assert.Equal("Legacy fighter countdown", l.GetStatus());
        Assert.Empty(h.Reserved);
        Assert.Equal(0, h.Walks(house));
    }

    [Fact]
    public void Refused_second_reservation_releases_the_first()
    {
        var h = World(out _);
        h.Npcs[30] = new Npc { Record = PgRecords.Senn, Pos = new(40, 0, 0) };
        h.Npcs[31] = new Npc { Record = PgRecords.Torka, Pos = new(45, 0, 0) };
        h.ExternalCapacity = 1;
        var l = BeginHouse(h, [PgRecords.ScratchCivilian], slot: 5);
        Assert.False(l.HasAnnouncement());
        Assert.Empty(h.Reserved);
        Assert.Contains("release 30", h.Calls);
        Assert.DoesNotContain("release 31", h.Calls);
    }

    [Fact]
    public void Walk_is_reordered_every_half_second_until_arrival_then_held_facing_the_arena()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        Step(h, l, 100);            // elapsed 0.1 >= nextMove 0: re-ordered at once (the C++'s first tick)
        Assert.Equal(2, h.Walks(house));
        Step(h, l, 100);            // 0.2 < 0.6
        Assert.Equal(2, h.Walks(house));
        h.Npcs[house].Pos = new(70, 0, 0);
        Step(h, l, 450);            // 0.65 >= 0.6
        Assert.Equal(3, h.Walks(house));
        Assert.True(l.WaitingForPosition(h));
        Assert.Null(h.Npcs[house].Faced);

        h.Npcs[house].Pos = SpotPos + new Vector3(0, 0, 14.9f);   // within 15
        h.Calls.Clear();
        Step(h, l, 100);
        Assert.False(l.WaitingForPosition(h));
        Assert.Equal("Announcer ready at the viewing stand", l.GetStatus());
        Assert.Contains($"endwalk {house}", h.Calls);
        Assert.Contains($"orders {house} True True", h.Calls);
        Assert.Equal(ArenaPos, h.Npcs[house].Faced);
        Assert.Equal(0, h.Walks(house));
    }

    [Fact]
    public void No_progress_for_20_seconds_falls_back()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        for (int i = 0; i < 199; i++) Step(h, l, 100);
        Assert.True(l.HasAnnouncement());
        Step(h, l, 100);
        Assert.False(l.HasAnnouncement());
        Assert.Contains("Proving Grounds: announcer fallback - viewing stand approach made no progress for 20 seconds", h.Debugs);
        Assert.Empty(h.Reserved);
        Assert.Contains($"replan {house}", h.Calls);
        Assert.Equal("Legacy fighter countdown", l.GetStatus());
    }

    [Fact]
    public void Two_units_of_progress_reset_the_stall()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        for (int i = 0; i < 150; i++) Step(h, l, 100);
        h.Npcs[house].Pos += new Vector3(3, 0, 0);   // 2 units nearer the target (50.99 -> 48.08)
        for (int i = 0; i < 150; i++) Step(h, l, 100);
        Assert.True(l.HasAnnouncement());
    }

    [Theory]
    [InlineData("ko")]
    [InlineData("gone")]
    [InlineData("arena")]
    [InlineData("spot")]
    public void Approach_falls_back_when_a_speaker_or_a_building_is_lost(string what)
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        if (what == "ko") h.Npcs[house].Ko = true;
        else if (what == "gone") h.Npcs[house].Gone = true;
        else if (what == "arena") h.ArenaGone = true;
        else h.SpotGone = true;
        Step(h, l, 100);
        Assert.False(l.HasAnnouncement());
        string reason = what is "ko" or "gone" ? "speaker became unavailable" : "arena or spot became invalid";
        Assert.Contains("Proving Grounds: announcer fallback - " + reason, h.Debugs);
        Assert.Empty(h.Reserved);
    }

    [Fact]
    public void Cues_are_spoken_3_25_seconds_apart_byte_for_byte_and_end_with_FIGHT()
    {
        for (uint seed = 0; seed < 8; seed++)
        {
            var h = World(out int house);
            h.Now = seed;
            h.Hours = 0;
            var l = BeginHouse(h);
            Assert.Equal((int)seed, l.Variant);
            h.Npcs[house].Pos = SpotPos;
            l.TickApproach(h);
            Assert.False(l.WaitingForPosition(h));
            l.BeginAnnouncement(h);
            Assert.True(l.AnnouncementStarted);
            int count = TownAnnouncerPolicy.CueCount(P.HouseAnnouncer, l.Variant);
            Assert.Equal(TownAnnouncerPolicy.HouseScripts[seed][0], Assert.Single(h.Said).Text);
            Assert.Equal("The fight is being announced", l.GetStatus());
            for (int cue = 1; cue < count; cue++)
            {
                l.TickAnnouncement(h, cue * 3.25f - 0.01f);
                Assert.Equal(cue, h.Said.Count);
                l.TickAnnouncement(h, cue * 3.25f);
                Assert.Equal(cue + 1, h.Said.Count);
            }
            Assert.Equal("FIGHT!", h.Said[^1].Text);
            Assert.Equal("Fight!", l.GetStatus());
            for (int i = 0; i < count - 1; i++) Assert.Equal(TownAnnouncerPolicy.HouseScripts[seed][i], h.Said[i].Text);
            Assert.All(h.Said, s => Assert.Equal(house, s.Who));
            Assert.Equal((count - 1) * 3.25f + 0.75f, l.AnnouncementDuration());
            l.TickAnnouncement(h, 100f);
            Assert.Equal(count, h.Said.Count);
            Assert.Equal(ArenaPos, h.Npcs[house].Faced);
        }
    }

    [Fact]
    public void Senn_and_Torka_alternate()
    {
        var h = World(out _);
        h.Npcs[30] = new Npc { Record = PgRecords.Senn, Pos = new(40, 0, 0) };
        h.Npcs[31] = new Npc { Record = PgRecords.Torka, Pos = new(45, 0, 0) };
        h.Now = 1; h.Hours = 0;
        var l = BeginHouse(h, [PgRecords.ScratchCivilian], slot: 5);
        l.BeginAnnouncement(h);
        l.TickAnnouncement(h, 20f);
        Assert.Equal([30, 31, 30, 31, 30], h.Said.Select(s => s.Who));
        Assert.Equal([TownAnnouncerPolicy.SennScripts[1][0], TownAnnouncerPolicy.TorkaScripts[1][0], TownAnnouncerPolicy.SennScripts[1][1],
            TownAnnouncerPolicy.TorkaScripts[1][1], "FIGHT!"], h.Said.Select(s => s.Text));
    }

    [Fact]
    public void Speaker_lost_during_the_announcement_falls_back()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        l.BeginAnnouncement(h);
        h.Npcs[house].Combat = true;
        l.TickAnnouncement(h, 3.25f);
        Assert.False(l.HasAnnouncement());
        Assert.Contains("Proving Grounds: announcer fallback - speaker lost during announcement", h.Debugs);
        Assert.Single(h.Said);
        Assert.Empty(h.Reserved);
        Assert.Equal(1, l.PendingReleaseCount);   // unsafe to resume: deferred
    }

    [Fact]
    public void Release_resumes_a_safe_speaker_with_its_orders_and_speed()
    {
        var h = World(out int house);
        h.Npcs[house].Hold = true; h.Npcs[house].Passive = false; h.Npcs[house].Speed = 1;
        var l = BeginHouse(h);
        h.Calls.Clear();
        l.Release(h);
        Assert.Equal([$"clear {house}", $"orders {house} True False", $"speed {house} 1", $"release {house}", $"replan {house}"], h.Calls);
        Assert.False(l.HasAnnouncement());
        Assert.Equal(0, l.PendingReleaseCount);
        Assert.Equal("Legacy fighter countdown", l.GetStatus());
    }

    [Fact]
    public void Release_defers_an_unsafe_speaker_until_it_is_safe_or_dead()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        h.Npcs[house].Carried = true;
        h.Calls.Clear();
        l.Release(h);
        Assert.Equal([$"holdpending {house}", $"release {house}"], h.Calls);   // held by the pending hold before the external hold goes back
        Assert.Equal([house], h.PendingHeld);
        Assert.Equal(1, l.PendingReleaseCount);
        Assert.False(l.PendingReleaseAt(0).Owned);

        // a pending release is not chosen again
        var again = l;
        again.Begin(h, ArenaRef, true, 0, [PgRecords.ScratchCivilian], 1, Fighters, 2);   // Release first: still unsafe, kept pending
        h.Npcs[house].Carried = false;
        again.Begin(h, ArenaRef, true, 0, [PgRecords.ScratchCivilian], 1, Fighters, 2);
        Assert.False(again.HasAnnouncement());

        h.Calls.Clear();
        l.TickReleases(h);
        Assert.Equal([$"clear {house}", $"orders {house} True False", $"speed {house} 1", $"replan {house}", $"unholdpending {house}"], h.Calls);
        Assert.Equal(0, l.PendingReleaseCount);
        Assert.Empty(h.PendingHeld);

        var m = BeginHouse(h);
        h.Npcs[house].Ko = true;
        m.Release(h);
        Assert.Equal(1, m.PendingReleaseCount);
        h.Npcs[house].Dead = true;
        h.Calls.Clear();
        m.TickReleases(h);
        Assert.Equal([$"unholdpending {house}"], h.Calls);   // forgotten: only the pending hold given back
        Assert.Equal(0, m.PendingReleaseCount);
    }

    [Fact]
    public void Abandon_clears_everything_without_touching_actors()
    {
        var h = World(out int house);
        h.Npcs[11] = new Npc { Record = PgRecords.ScratchAnnouncer, Pos = new(60, 0, 0) };
        var l = BeginHouse(h);
        h.Npcs[house].Ko = true;
        l.Release(h);                      // house deferred
        l.Begin(h, ArenaRef, true, 0, [PgRecords.ScratchCivilian], 1, Fighters, 2);   // the other announcer, owned
        Assert.Equal(1, l.PendingReleaseCount);
        Assert.Equal(11, l.SpeakerAt(0).Actor);
        h.Calls.Clear();
        l.AbandonWorldState();
        Assert.False(l.HasAnnouncement());
        Assert.Equal(0, l.SpeakerCount);
        Assert.Equal(0, l.PendingReleaseCount);
        Assert.Null(l.Arena);
        Assert.Empty(h.Calls);
        Assert.Equal("Legacy fighter countdown", l.GetStatus());
    }

    [Fact]
    public void After_a_throw_directed_speakers_get_their_orders_and_speed_back()
    {
        var h = World(out int house);
        h.Npcs[house].Hold = false; h.Npcs[house].Passive = true; h.Npcs[house].Speed = 1;
        var l = BeginHouse(h);
        h.Npcs[house].Pos = SpotPos;
        l.TickApproach(h);                         // held at the stand: Hold + Passive, RUN
        Assert.True(h.Npcs[house].Hold);
        Assert.Equal((byte)2, h.Npcs[house].Speed);
        h.Calls.Clear();
        Assert.Equal(0, l.RestoreSpeakersAfterFailure(h));
        Assert.Equal([$"clear {house}", $"orders {house} False True", $"speed {house} 1"], h.Calls);
        Assert.False(h.Npcs[house].Hold);
        Assert.True(h.Npcs[house].Passive);
        Assert.Equal((byte)1, h.Npcs[house].Speed);
        Assert.Equal([house], h.Reserved);         // the glue gives the reservation back after it
    }

    [Fact]
    public void A_throwing_restore_is_counted_and_the_others_still_restored()
    {
        var h = World(out _);
        h.Npcs[30] = new Npc { Record = PgRecords.Senn, Pos = new(40, 0, 0), Speed = 1 };
        h.Npcs[31] = new Npc { Record = PgRecords.Torka, Pos = new(45, 0, 0), Speed = 1 };
        var l = BeginHouse(h, [PgRecords.ScratchCivilian], slot: 5);
        h.ThrowOnClearOf = 30;
        Assert.Equal(1, l.RestoreSpeakersAfterFailure(h));
        Assert.Equal((byte)1, h.Npcs[31].Speed);
        Assert.Equal((byte)2, h.Npcs[30].Speed);
    }

    [Fact]
    public void A_pending_release_that_throws_is_dropped_not_retried()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        h.Npcs[house].Carried = true;
        l.Release(h);
        h.Npcs[house].Carried = false;
        h.ThrowOnClearOf = house;
        Assert.Throws<InvalidOperationException>(() => l.TickReleases(h));
        Assert.Equal(0, l.PendingReleaseCount);   // dropped: the next tick does not throw again
        Assert.Empty(h.PendingHeld);              // and its pending hold given back
        l.TickReleases(h);
    }

    [Fact]
    public void After_a_throw_an_unsafe_speaker_is_deferred_not_restored()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        h.Npcs[house].Combat = true;
        h.Calls.Clear();
        Assert.Equal(0, l.RestoreSpeakersAfterFailure(h));
        Assert.Equal([$"holdpending {house}"], h.Calls);
        Assert.True(l.IsPendingRelease(house));
        Assert.False(l.PendingReleaseAt(0).Owned);
        Assert.Equal(0, l.RestoreSpeakersAfterFailure(h));   // already pending: not deferred twice
        Assert.Equal(1, l.PendingReleaseCount);
    }

    [Fact]
    public void Keeps_names_only_owned_speakers()
    {
        var h = World(out int house);
        var l = BeginHouse(h);
        Assert.True(l.Keeps(house));
        Assert.False(l.Keeps(1));
        Assert.False(l.Keeps(-1));
        l.ForgetAll();
        Assert.False(l.Keeps(house));
        Assert.False(l.HasAnnouncement());
    }
}
