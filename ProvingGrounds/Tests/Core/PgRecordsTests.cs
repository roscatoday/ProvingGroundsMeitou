using System.Reflection;
using System.Text.RegularExpressions;
using ProvingGrounds.Core;
using ProvingGrounds.Tests.Fidelity;

namespace ProvingGrounds.Tests.Core;

/// <summary>The id list in Core/PgRecords.cs equals the ids the original's code names, so it cannot drift (task 1.1).</summary>
public class PgRecordsTests
{
    // Same pattern as the task's grep: grep -ohE '"[0-9]+-(Proving Grounds\.mod|gamedata\.base|rebirth\.mod|Newwworld\.mod)"'
    static readonly Regex Id = new("\"([0-9]+-(?:Proving Grounds\\.mod|gamedata\\.base|rebirth\\.mod|Newwworld\\.mod))\"");

    static SortedSet<string> FromSources()
    {
        string src = Path.Combine(RepoPaths.Upstream(RepoPaths.Root()), "plugin", "src");
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src))   // the grep's `src/*`: top-level files only
            foreach (Match m in Id.Matches(File.ReadAllText(file)))
                ids.Add(m.Groups[1].Value);
        return ids;
    }

    static List<string> Declared() =>
        typeof(PgRecords).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).ToList();

    [Fact]
    public void Constants_equal_the_ids_in_the_cpp()
    {
        var found = FromSources();
        var declared = Declared();
        Assert.Equal(declared.Count, declared.Distinct().Count());
        var declaredSet = new SortedSet<string>(declared, StringComparer.Ordinal);
        Assert.Equal("", string.Join("\n", found.Except(declaredSet)));   // in the C++, missing here
        Assert.Equal("", string.Join("\n", declaredSet.Except(found)));   // here, not in the C++
        Assert.Equal(146, found.Count);
        Assert.Equal(139, found.Count(i => i.EndsWith("-Proving Grounds.mod", StringComparison.Ordinal)));
    }

    [Fact]
    public void Required_are_all_declared_ids()
    {
        var declared = Declared();
        Assert.All(PgRecords.Required, r => Assert.Contains(r, declared));
        Assert.DoesNotContain(PgRecords.OldArena, PgRecords.Required);
    }
}
