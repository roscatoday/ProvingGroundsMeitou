// Gate G1 (00-overview §6): every original C++ file has a row in docs/port-map.md, and every `ported` row names a C# file
// that exists. The map is written by tools/port_map.py (task 0.4).
using System.Text.RegularExpressions;

namespace ProvingGrounds.Tests.Fidelity;

internal static class RepoPaths
{
    /// <summary>The repository root: the first folder above the test's base directory that holds meitou.json.</summary>
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "meitou.json")))
                return dir.FullName;
        throw new InvalidOperationException($"no meitou.json above {AppContext.BaseDirectory}");
    }

    public static string Upstream(string root) => Path.Combine(root, "upstream", "ProvingGrounds");
}

public class PortMapTests
{
    internal sealed record Row(string[] Files, string Kind, string CSharp, string Status);

    const string Header = "| C++ file | lines | kind | C# file | status |";
    static readonly Regex StatusForm = new(@"^(todo|ported|partial: .+|dropped: .+)$");

    internal static List<Row> ReadRows(string root)
    {
        var path = Path.Combine(root, "docs", "port-map.md");
        Assert.True(File.Exists(path), $"{path} is missing: run python3 tools/port_map.py");
        var rows = new List<Row>();
        var inTable = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Trim() == Header) { inTable = true; continue; }
            if (!inTable) continue;
            if (!line.StartsWith('|')) break;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 5 || cells[0].All(c => c is '-' or ':' or ' ')) continue;
            rows.Add(new Row(cells[0].Split(',').Select(f => f.Trim()).ToArray(), cells[2], cells[3], cells[4]));
        }
        return rows;
    }

    static IEnumerable<string> UpstreamFiles(string root, string folder, string prefix) =>
        Directory.EnumerateFiles(Path.Combine(RepoPaths.Upstream(root), folder), "*", SearchOption.AllDirectories)
            .Select(f => prefix + Path.GetRelativePath(Path.Combine(RepoPaths.Upstream(root), folder), f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void EveryUpstreamSourceFileHasARow()
    {
        var root = RepoPaths.Root();
        var mapped = ReadRows(root).SelectMany(r => r.Files).ToHashSet(StringComparer.Ordinal);
        var source = UpstreamFiles(root, Path.Combine("plugin", "src"), "").ToList();
        Assert.Equal(164, source.Count);
        var missing = source.Where(f => !mapped.Contains(f)).ToList();
        Assert.True(missing.Count == 0, "no port-map row for: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryUpstreamTestFileHasARow()
    {
        var root = RepoPaths.Root();
        var mapped = ReadRows(root).SelectMany(r => r.Files).ToHashSet(StringComparer.Ordinal);
        var missing = UpstreamFiles(root, "tests", "tests/").Where(f => !mapped.Contains(f)).ToList();
        Assert.True(missing.Count == 0, "no port-map row for: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryRowNamesARealFileAndAValidStatus()
    {
        var root = RepoPaths.Root();
        var rows = ReadRows(root);
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Matches(StatusForm, row.Status);
            foreach (var file in row.Files)
            {
                var path = file.StartsWith("tests/", StringComparison.Ordinal)
                    ? Path.Combine(RepoPaths.Upstream(root), file)
                    : Path.Combine(RepoPaths.Upstream(root), "plugin", "src", file);
                Assert.True(File.Exists(path), $"port-map row names {file}, which is not in upstream/");
            }
        }
    }

    [Fact]
    public void PortedRowsNameAnExistingCSharpFile()
    {
        var root = RepoPaths.Root();
        var broken = ReadRows(root)
            .Where(r => r.Status == "ported" && (r.CSharp == "-" || !File.Exists(Path.Combine(root, r.CSharp))))
            .Select(r => $"{string.Join(", ", r.Files)} -> {r.CSharp}")
            .ToList();
        Assert.True(broken.Count == 0, "ported rows without their C# file: " + string.Join("; ", broken));
    }
}
