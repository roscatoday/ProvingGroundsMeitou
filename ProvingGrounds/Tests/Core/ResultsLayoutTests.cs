using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand tests for Core/ResultsLayout.cs: the string overload of the portrait initial and the byte version's edge rules. The C++
/// vectors (4,000+ byte strings) are in Tests/Golden/LayoutsGoldenTests.cs.</summary>
public class ResultsLayoutTests
{
    [Theory]
    [InlineData(null, "?")]
    [InlineData("", "?")]
    [InlineData("Beep", "B")]
    [InlineData("Ébène", "É")]
    [InlineData("日本", "日")]
    [InlineData("🥊", "🥊")]
    [InlineData("\0abc", "?")]
    [InlineData("a\0b", "a")]
    public void String_overload_returns_the_first_character_or_a_question_mark(string? name, string expected)
    {
        Assert.Equal(expected, ResultsLayout.PortraitInitialUtf8(name));
    }

    [Fact]
    public void A_lone_surrogate_cannot_reach_the_original_and_reads_as_the_replacement_character()
    {
        // .NET encodes "\uD800" as EF BF BD (U+FFFD), which is well-formed UTF-8, so the byte rules accept it (docs/divergences.md)
        Assert.Equal("�", ResultsLayout.PortraitInitialUtf8("\uD800"));
    }

    [Fact]
    public void Byte_version_returns_only_the_first_character()
    {
        Assert.Equal("日", ResultsLayout.PortraitInitialUtf8(new byte[] { 0xE6, 0x97, 0xA5, 0xE6, 0x9C, 0xAC }));
        Assert.Equal("􏿿", ResultsLayout.PortraitInitialUtf8(new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }));   // U+10FFFF, the upper bound
    }

    [Fact]
    public void Byte_version_treats_the_end_of_the_span_and_a_zero_byte_as_the_terminator()
    {
        Assert.Equal("?", ResultsLayout.PortraitInitialUtf8(ReadOnlySpan<byte>.Empty));
        Assert.Equal("?", ResultsLayout.PortraitInitialUtf8(new byte[] { 0xE6, 0x97 }));         // cut short: reads past the end, no throw
        Assert.Equal("?", ResultsLayout.PortraitInitialUtf8(new byte[] { 0xE6, 0x97, 0x00, 0xA5 }));   // a NUL inside the sequence
        Assert.Equal("?", ResultsLayout.PortraitInitialUtf8(new byte[] { 0x00, 0x41 }));
        Assert.Equal("A", ResultsLayout.PortraitInitialUtf8(new byte[] { 0x41, 0x00, 0x42 }));
    }

    [Theory]
    [InlineData(new byte[] { 0xC0, 0x80 })]                // overlong two-byte
    [InlineData(new byte[] { 0xC1, 0xBF })]
    [InlineData(new byte[] { 0xE0, 0x9F, 0xBF })]          // overlong three-byte
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]          // UTF-16 surrogate U+D800
    [InlineData(new byte[] { 0xF0, 0x8F, 0xBF, 0xBF })]    // overlong four-byte
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]    // past U+10FFFF
    [InlineData(new byte[] { 0xF5, 0x80, 0x80, 0x80 })]
    [InlineData(new byte[] { 0x80 })]                      // a continuation byte cannot lead
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xC2, 0x41 })]                // the follower is not a continuation byte
    public void Byte_version_rejects_malformed_sequences(byte[] bytes)
    {
        Assert.Equal("?", ResultsLayout.PortraitInitialUtf8(bytes));
    }

    [Fact]
    public void FitCeremony_is_the_three_leaderboard_fits_with_the_portraits_from_the_metrics()
    {
        ResultsLayout.Ceremony c = ResultsLayout.FitCeremony(1000, 8, 16, 3, 300);
        Assert.Equal(LeaderboardLayout.FitCeremonialMetrics(16, 8), c.Metrics);
        Assert.Equal(LeaderboardLayout.FitTitleRails(1000, 8, 300), c.Title);
        Assert.Equal(96, c.Metrics.ChampionPortrait);
        Assert.Equal(72, c.Metrics.OtherPortrait);
        Assert.Equal(350, c.Title.TitleLeft);
        // podium: cell = min((1000 - 16) / 3, 96 + 160) = 256, used = 784, first left = 108, first place (middle slot) uses the champion portrait
        Assert.Equal(108, c.Podium.Slots[0].Left);
        Assert.Equal(256, c.Podium.Slots[0].Width);
        Assert.Equal(96, c.Podium.Slots[1].Portrait);
        Assert.Equal(72, c.Podium.Slots[0].Portrait);
    }
}
