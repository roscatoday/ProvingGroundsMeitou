// Visible to every test file in the project, so a golden test needs no extra using (the plan's M1 samples call GoldenFile.* bare).
global using ProvingGrounds.Tests.Golden;
using System.Globalization;
using System.Text.Json;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Reads and compares the vectors made by the original C++ (task 0.5, `bash golden/gen.sh`). Gate G3: ints, enums, strings and
/// chosen ids exactly; floats within 1e-4 (relative above 1).</summary>
public static class GoldenFile
{
    /// <summary>Parses `Golden/&lt;name&gt;.json` from the test output folder (the csproj copies Tests/Golden/*.json there).</summary>
    public static JsonElement Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Golden", name + ".json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();   // detached from the pooled buffer, so it outlives the document
    }

    /// <summary>Asserts |expected - actual| &lt;= 1e-4 * max(1, |expected|); NaN never passes. The message names <paramref name="what"/>.</summary>
    public static void Near(float expected, float actual, string what)
    {
        double e = expected, a = actual;
        double tolerance = 1e-4 * Math.Max(1.0, Math.Abs(e));
        if (!(Math.Abs(e - a) <= tolerance))
            Assert.Fail(string.Format(CultureInfo.InvariantCulture,
                "{0}: expected {1:R}, actual {2:R} (tolerance {3:R})", what, expected, actual, tolerance));
    }
}
