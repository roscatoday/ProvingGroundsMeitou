// Golden generators that COPY upstream code verbatim (golden/gen_formation.cpp, task M4.3a) mark each copy with
// "// BEGIN COPY <File>:<first>-<last>" ... "// END COPY" and state the exact number in a "// copies: <n>" line. A copy must still
// equal upstream line for line, or the goldens pin a formula the original no longer has. golden/check_copies.py runs the same check
// (same grammar, same errors) inside gen.sh; this test runs it without g++.
using System.Text.RegularExpressions;

namespace ProvingGrounds.Tests.Fidelity;

public class GoldenCopiesTests
{
    static readonly Regex Begin = new(@"^// BEGIN COPY ([A-Za-z0-9_]+\.(?:cpp|h)):([0-9]+)-([0-9]+)$");
    static readonly Regex Count = new(@"^// copies: ([0-9]+)$");
    const string End = "// END COPY";

    /// <summary>Lines with any trailing \r removed (File.ReadAllLines also splits on \r\n), so a CRLF checkout compares equal.</summary>
    static string[] Lines(string path) => File.ReadAllLines(path);

    /// <summary>The check of golden/check_copies.py for one generator: the copies found and every error.</summary>
    internal static (int Copies, int? Declared, List<string> Errors) Check(string path, string upstreamSrc)
    {
        string name = Path.GetFileName(path);
        string[] lines = Lines(path);
        var errors = new List<string>();
        var cache = new Dictionary<string, string[]>();
        int copies = 0;
        int? declared = null;
        (int Start, string File, int First, int Last)? open = null;
        for (int i = 0; i < lines.Length; ++i)
        {
            string line = lines[i];
            string where = $"{name}:{i + 1}";
            if (line.StartsWith("// copies:", StringComparison.Ordinal))
            {
                Match c = Count.Match(line);
                if (!c.Success) errors.Add($"{where}: malformed count line '{line}' (expected '// copies: <n>')");
                else if (declared != null) errors.Add($"{where}: a second '// copies:' line");
                else declared = int.Parse(c.Groups[1].Value);
            }
            else if (line.StartsWith("// BEGIN COPY", StringComparison.Ordinal))
            {
                Match m = Begin.Match(line);
                if (!m.Success) { errors.Add($"{where}: malformed marker '{line}' (expected '// BEGIN COPY <File>:<first>-<last>')"); continue; }
                if (open != null) errors.Add($"{where}: BEGIN COPY inside the copy opened at line {open.Value.Start + 1}");
                open = (i, m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            }
            else if (line.StartsWith("// END COPY", StringComparison.Ordinal))
            {
                if (line != End) { errors.Add($"{where}: malformed marker '{line}' (expected '// END COPY')"); continue; }
                if (open == null) { errors.Add($"{where}: END COPY without BEGIN COPY"); continue; }
                var (start, file, first, last) = open.Value;
                open = null;
                ++copies;
                string src = Path.Combine(upstreamSrc, file);
                if (!File.Exists(src)) { errors.Add($"{name}:{start + 1}: no upstream file {file}"); continue; }
                if (!cache.TryGetValue(file, out string[]? upstream))
                    cache[file] = upstream = Lines(src);
                if (!(1 <= first && first <= last && last <= upstream.Length))
                {
                    errors.Add($"{name}:{start + 1}: range {file}:{first}-{last} is outside 1..{upstream.Length} (or first > last)");
                    continue;
                }
                string[] copy = lines[(start + 1)..i];
                string[] want = upstream[(first - 1)..last];
                if (copy.Length != want.Length)
                    errors.Add($"{name}:{start + 1}: {file}:{first}-{last} is {want.Length} lines, the copy has {copy.Length}");
                for (int k = 0; k < Math.Min(copy.Length, want.Length); ++k)
                    if (copy[k] != want[k])
                    {
                        errors.Add($"{name}:{start + 2 + k}: differs from {file}:{first + k}\n  copy:     {copy[k]}\n  upstream: {want[k]}");
                        break;
                    }
            }
        }
        if (open != null)
            errors.Add($"{name}:{open.Value.Start + 1}: BEGIN COPY {open.Value.File}:{open.Value.First}-{open.Value.Last} has no END COPY");
        if (declared == null && copies > 0)
            errors.Add($"{name}: {copies} copies but no '// copies: <n>' line");
        else if (declared != null && declared != copies)
            errors.Add($"{name}: '// copies: {declared}' but the file has {copies}");
        return (copies, declared, errors);
    }

    static string UpstreamSrc(string root) => Path.Combine(RepoPaths.Upstream(root), "plugin", "src");

    [Fact]
    public void Verbatim_copies_equal_upstream()
    {
        string root = RepoPaths.Root();
        var errors = new List<string>();
        foreach (string gen in Directory.GetFiles(Path.Combine(root, "golden"), "gen_*.cpp"))
            errors.AddRange(Check(gen, UpstreamSrc(root)).Errors);
        Assert.True(errors.Count == 0, string.Join("\n", errors));

        var formation = Check(Path.Combine(root, "golden", "gen_formation.cpp"), UpstreamSrc(root));
        Assert.Equal(8, formation.Declared);
        Assert.Equal(8, formation.Copies);
    }

    [Fact]
    public void Checker_rejects_drift_and_bad_markers()
    {
        string root = RepoPaths.Root();
        string up = UpstreamSrc(root);
        string line1 = File.ReadAllLines(Path.Combine(up, "ArenaIngress.cpp"))[0];
        string dir = Path.Combine(Path.GetTempPath(), "pg-goldencopies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            List<string> Errors(params string[] text)
            {
                string p = Path.Combine(dir, "gen_case.cpp");
                File.WriteAllText(p, string.Join("\n", text) + "\n");
                return Check(p, up).Errors;
            }

            Assert.Empty(Errors("// copies: 1", "// BEGIN COPY ArenaIngress.cpp:1-1", line1, End));
            // CRLF checkout
            string crlf = Path.Combine(dir, "gen_crlf.cpp");
            File.WriteAllText(crlf, $"// copies: 1\r\n// BEGIN COPY ArenaIngress.cpp:1-1\r\n{line1}\r\n{End}\r\n");
            Assert.Empty(Check(crlf, up).Errors);

            Assert.Contains(Errors("// copies: 1", "// BEGIN COPY ArenaIngress.cpp:1-1", line1 + " ", End), e => e.Contains("differs"));
            Assert.Contains(Errors("// copies: 2", "// BEGIN COPY ArenaIngress.cpp:1-1", line1, End), e => e.Contains("but the file has 1"));
            Assert.Contains(Errors("// BEGIN COPY ArenaIngress.cpp:1-1", line1, End), e => e.Contains("no '// copies"));
            Assert.Contains(Errors("// copies: 0", End), e => e.Contains("END COPY without BEGIN"));
            Assert.Contains(Errors("// copies: 0", "// END COPY ArenaIngress.cpp"), e => e.Contains("malformed marker"));
            Assert.Contains(Errors("// copies: 0", "// BEGIN COPY ArenaIngress.cpp 1-1"), e => e.Contains("malformed marker"));
            Assert.Contains(Errors("// copies: x"), e => e.Contains("malformed count"));
            Assert.Contains(Errors("// copies: 1", "// BEGIN COPY ArenaIngress.cpp:1-1", line1), e => e.Contains("has no END COPY"));
            Assert.Contains(Errors("// copies: 1", "// BEGIN COPY ArenaIngress.cpp:0-0", End), e => e.Contains("outside"));
            Assert.Contains(Errors("// copies: 1", "// BEGIN COPY ArenaIngress.cpp:3-2", End), e => e.Contains("outside"));
            Assert.Contains(Errors("// copies: 1", "// BEGIN COPY ArenaIngress.cpp:99999-99999", End), e => e.Contains("outside"));
            Assert.Contains(Errors("// copies: 1", "// BEGIN COPY Missing.cpp:1-1", "x", End), e => e.Contains("no upstream file"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
