using System.Text.Json;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The PG-SPAR result line (Core/Glue/SparResultLine.cs, task 9.3e): a non-finite number is written null so the line stays JSON; every
/// finite number is CMath.FormatG's text, as before the change.</summary>
public class SparResultLineTests
{
    static SparPodium.Snapshot Snapshot()
    {
        var s = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins, Mode = MatchMode.TeamAvB, LastStandingId = 0 };
        s.Fighters[0] = new SparPodium.FighterRow
        {
            Id = 0, Team = MatchTeam.TeamA, Name = "Soo", DamageDealt = float.NaN, DamageTaken = 12.5f, DamageMitigated = 3.25f, HitsLanded = 4,
            Blocks = 2, Misses = 1, Dodges = 3, EliminationIndex = -1, RatingBefore = 1200f, RatingAfter = 1210.125f, RatingDelta = float.NaN,
            RatingUpdated = true, MarksBefore = 5, MarksEarned = 9, MarksAfter = 14, MarksUpdated = true,
        };
        s.Fighters[1] = new SparPodium.FighterRow
        {
            Id = 1, Team = MatchTeam.TeamB, Name = "Masaru", DamageDealt = 12.5f, DamageTaken = float.PositiveInfinity, DamageMitigated = 0f,
            EliminationIndex = 0, RatingBefore = 1000f, RatingAfter = 990.5f, RatingDelta = -9.5f,
        };
        s.FighterCount = 2;
        s.Podium[0] = new SparPodium.PodiumEntry { Place = 1, Fighter = s.Fighters[0] };
        s.PodiumCount = 1;
        return s;
    }

    static string Line(float delay = 1.0f) => SparResultLine.Json(Snapshot(), 4321, delay, slot => slot == 0 ? "c:21.0" : "");

    [Fact]
    public void A_non_finite_number_is_written_null_and_the_line_parses()
    {
        using var doc = JsonDocument.Parse(Line());
        var f = doc.RootElement.GetProperty("fighters");
        Assert.Equal(JsonValueKind.Null, f[0].GetProperty("dealt").ValueKind);
        Assert.Equal(JsonValueKind.Null, f[0].GetProperty("ratingDelta").ValueKind);
        Assert.Equal(JsonValueKind.Null, f[1].GetProperty("taken").ValueKind);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(Line(float.NaN)).RootElement.GetProperty("showIn").ValueKind);
    }

    [Fact]
    public void Finite_numbers_keep_CMath_FormatG_text()
    {
        string line = Line();
        foreach (var (key, v) in new (string, float)[]
                 { ("taken", 12.5f), ("mitigated", 3.25f), ("ratingBefore", 1200f), ("ratingAfter", 1210.125f), ("ratingDelta", -9.5f), ("showIn", 1.0f) })
            Assert.Contains("\"" + key + "\":" + CMath.FormatG(v, 9) + ",", line);
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal(4321, root.GetProperty("tick").GetInt64());
        Assert.Equal("team_a_wins", root.GetProperty("outcome").GetString());
        Assert.Equal(1.0, root.GetProperty("showIn").GetDouble());
        var f = root.GetProperty("fighters");
        Assert.Equal(2, f.GetArrayLength());
        Assert.Equal("c:21.0", f[0].GetProperty("id").GetString());
        Assert.Equal("", f[1].GetProperty("id").GetString());
        Assert.Equal(12.5, f[0].GetProperty("taken").GetDouble());
        Assert.Equal(1210.125, f[0].GetProperty("ratingAfter").GetDouble());
        Assert.Equal(12.5, f[1].GetProperty("dealt").GetDouble());
        Assert.Equal(-9.5, f[1].GetProperty("ratingDelta").GetDouble());
        Assert.Equal(0, f[1].GetProperty("mitigated").GetDouble());
        Assert.Equal(14, f[0].GetProperty("marksAfter").GetInt32());
        Assert.Equal("Soo", root.GetProperty("podium")[0].GetProperty("name").GetString());
    }
}
