using System.Text.RegularExpressions;

namespace ProvingGrounds.Tests.Fidelity;

/// <summary>Reads the C string literals of an upstream header (read only), so a test can compare a ported text table with the
/// original byte for byte, including lines no code path reaches.</summary>
internal static class UpstreamLiterals
{
    /// <summary>Every <c>"..."</c> literal on lines <paramref name="firstLine"/>..<paramref name="lastLine"/> (1-based, inclusive) of
    /// plugin/src/<paramref name="header"/>, in order. Fails on an escape: the tables read here have none, so a literal is its text.</summary>
    public static string[] Read(string header, int firstLine, int lastLine)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoPaths.Upstream(RepoPaths.Root()), "plugin", "src", header));
        var result = new List<string>();
        for (int i = firstLine - 1; i < lastLine; ++i)
            foreach (Match m in Regex.Matches(lines[i], "\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                Assert.DoesNotContain("\\", m.Groups[1].Value);
                result.Add(m.Groups[1].Value);
            }
        return result.ToArray();
    }
}
