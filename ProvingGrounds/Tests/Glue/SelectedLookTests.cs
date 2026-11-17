using ProvingGrounds.Core.Glue;
using ProvingGrounds.Tests.Fidelity;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.8: MyGUI's <c>Button::setStateSelected</c> (NativeUI.cpp:18-25 builds Kenshi_Button1 buttons; DebugMenu.cpp:290, :354 and
/// ArenaUI.cpp:684-2486 select them) draws the skin's <c>*_checked</c> look. Meitou's painter reads only the <c>Checked</c> property for that
/// look (MeitouClient v0.17.3 src/Meitou.Game/Rendering/Ui/UiPainter.cs:830 skin, :925 caption colour), MyGUI's own reader takes
/// <c>StateSelected</c>: a selected button binds both, through one helper.</summary>
public class SelectedLookTests
{
    [Fact]
    public void SelectedBindsCheckedAndStateSelected()
    {
        var on = NativeUI.SelectedState(true);
        Assert.Equal("true", on["StateSelected"]);
        Assert.Equal("true", on["Checked"]);
        Assert.Equal(2, on.Count);
    }

    [Fact]
    public void NotSelectedBindsBothFalse()
    {
        var off = NativeUI.SelectedState(false);
        Assert.Equal("false", off["StateSelected"]);
        Assert.Equal("false", off["Checked"]);
        Assert.Equal(2, off.Count);
    }

    // the files allowed to hold the literal: the helper itself, and MyGUI's known property names (a key list the layout check reads, never a
    // binding); each must still hold it, so a moved file or a broken path cannot leave the scan passing
    static readonly string[] Allowed =
    [
        Path.Combine("Core", "Glue", "NativeUI.cs"),
        Path.Combine("Core", "Glue", "LayoutBuilder.cs"),
    ];

    [Fact]
    public void NoWindowBindsStateSelectedOnItsOwn()
    {
        // every window goes through NativeUI.SelectedState; a bare "StateSelected" literal anywhere else would bind it without Checked and
        // lose the selected look (or the other way round)
        string root = RepoPaths.Root();
        var scanned = new[] { "src", "Core" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(root, f))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToList();
        Assert.NotEmpty(scanned);
        foreach (string a in Allowed)
        {
            Assert.Contains(a, scanned);
            Assert.Contains("\"StateSelected\"", File.ReadAllText(Path.Combine(root, a)));
        }
        var bare = scanned
            .Where(f => !Allowed.Contains(f) && File.ReadAllText(Path.Combine(root, f)).Contains("\"StateSelected\"", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(bare);
    }
}
