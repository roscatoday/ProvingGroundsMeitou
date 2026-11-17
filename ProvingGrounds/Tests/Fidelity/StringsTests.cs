// Gate G2 (00-overview §6): every player-visible string of the original C++ (fidelity/strings.json, written by
// tools/extract_strings.py) appears in the C# port, or in docs/divergences.md on a line starting `string:` with the reason.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProvingGrounds.Tests.Fidelity;

public class StringsTests
{
    internal sealed record Entry(string Text, string File, int Line, string Kind);

    // The honest extraction keeps 1,676 of the 4,017 string literals (python3 tools/extract_strings.py prints the count);
    // the plan asks for more than 500 as a floor, which also catches a truncated or emptied catalog.
    const int MinimumEntries = 500;

    // A printf conversion as the C++ writes them (%d, %.1f, %+.1f, %02d, %u, %s, %%, ...).
    static readonly Regex PrintfCode = new(@"%[-+ #0]*(\d+|\*)?(\.(\d+|\*))?(hh|h|ll|l|L|z|j|t|I64|I32|I)?[diouxXeEfFgGaAcspn%]");

    internal static List<Entry> Load(string root)
    {
        var path = Path.Combine(root, "fidelity", "strings.json");
        Assert.True(File.Exists(path), $"{path} is missing: run python3 tools/extract_strings.py");
        var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(entries);
        return entries!;
    }

    [Fact]
    public void CatalogLoadsAndIsSizeable()
    {
        var root = RepoPaths.Root();
        var entries = Load(root);
        Assert.True(entries.Count > MinimumEntries, $"only {entries.Count} strings in fidelity/strings.json");
        var source = Path.Combine(RepoPaths.Upstream(root), "plugin", "src");
        foreach (var e in entries)
        {
            Assert.False(string.IsNullOrEmpty(e.Text));
            Assert.False(string.IsNullOrEmpty(e.Kind));
            Assert.True(e.Line > 0);
            Assert.True(File.Exists(Path.Combine(source, e.File)), $"{e.File} is not in upstream/ProvingGrounds/plugin/src");
        }
        var sorted = entries.OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Line).ToList();
        Assert.True(entries.Select(e => (e.File, e.Line)).SequenceEqual(sorted.Select(e => (e.File, e.Line))),
            "fidelity/strings.json is not sorted by file and line");
    }

    /// <summary>The parts of an entry the port must reproduce: the whole text, or for a printf format each piece between
    /// its % codes that is longer than 3 characters.</summary>
    internal static IEnumerable<string> Pieces(string text) =>
        PrintfCode.IsMatch(text) ? PrintfCode.Split(text).Where(p => p.Length > 3) : new[] { text };

    static string CSharpEscaped(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    [Fact]
    public void EveryStringAppearsInThePort()
    {
        var root = RepoPaths.Root();
        var code = string.Join("\n", new[] { "src", "Core" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
        var divergencesPath = Path.Combine(root, "docs", "divergences.md");
        var excused = File.Exists(divergencesPath)
            ? string.Join("\n", File.ReadAllLines(divergencesPath).Where(l => l.StartsWith("string:", StringComparison.Ordinal)))
            : "";
        bool Found(string piece) =>
            code.Contains(piece, StringComparison.Ordinal) ||
            code.Contains(CSharpEscaped(piece), StringComparison.Ordinal) ||
            code.Contains(CSharpEscaped(piece).Replace("{", "{{").Replace("}", "}}"), StringComparison.Ordinal) ||
            excused.Contains(piece, StringComparison.Ordinal) ||
            excused.Contains(CSharpEscaped(piece), StringComparison.Ordinal);
        var missing = Load(root)
            .SelectMany(e => Pieces(e.Text).Where(p => !Found(p)).Select(p => $"{e.File}:{e.Line} [{e.Kind}] {p}"))
            .ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} strings are not in the port:\n" + string.Join("\n", missing.Take(200)));
    }
}
