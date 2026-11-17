using System.Xml.Linq;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.4: the layout builder (a MyGUI layout as XML, one call per original <c>createWidget</c>) and the NativeUI helpers
/// (NativeUI.cpp:9-60) as builder calls with the same skins, alignment and text settings.</summary>
public class LayoutBuilderTests
{
    static XElement Root(string xml) => XDocument.Parse(xml).Root!;

    [Fact]
    public void EmptyLayoutIsAMyGuiLayoutDocument()
    {
        var root = Root(new LayoutBuilder().Xml());
        Assert.Equal("MyGUI", root.Name.LocalName);
        Assert.Equal("Layout", (string?)root.Attribute("type"));
        Assert.Equal("3.2.0", (string?)root.Attribute("version"));
        Assert.Empty(root.Elements());
    }

    [Fact]
    public void OpenWritesTypeSkinPixelPositionAndName()
    {
        string xml = new LayoutBuilder().Open("Button", "Kenshi_Button1", 1, 2, 300, 32, "PG_Start").Close().Xml();
        var w = Root(xml).Element("Widget")!;
        Assert.Equal("Button", (string?)w.Attribute("type"));
        Assert.Equal("Kenshi_Button1", (string?)w.Attribute("skin"));
        Assert.Equal("1 2 300 32", (string?)w.Attribute("position"));
        Assert.Equal("PG_Start", (string?)w.Attribute("name"));
        Assert.Null(w.Attribute("align"));   // MyGUI::Align::Default: no attribute
        Assert.Null(w.Attribute("layer"));
        Assert.Null(w.Attribute("position_real"));
    }

    [Fact]
    public void OpenRealWritesFractionsInvariantAndAlignAndLayer()
    {
        // ArenaUI.cpp:2819-2820: createWidgetReal<Window>("Kenshi_WindowCX", .04f, .04f, .92f, .92f, Align::Center, "Window", name)
        string xml = new LayoutBuilder().OpenReal("Window", "Kenshi_WindowCX", .04f, .04f, .92f, .92f, "ProvingGroundsArenaWindow", "Center", "Window")
            .Prop("Caption", "Proving Grounds - Arena").Close().Xml();
        var w = Root(xml).Element("Widget")!;
        Assert.Equal("0.04 0.04 0.92 0.92", (string?)w.Attribute("position_real"));
        Assert.Null(w.Attribute("position"));
        Assert.Equal("Center", (string?)w.Attribute("align"));
        Assert.Equal("Window", (string?)w.Attribute("layer"));
        var p = w.Element("Property")!;
        Assert.Equal("Caption", (string?)p.Attribute("key"));
        Assert.Equal("Proving Grounds - Arena", (string?)p.Attribute("value"));
    }

    [Fact]
    public void ChildrenNestAndTextIsEscaped()
    {
        string xml = new LayoutBuilder()
            .Open("Widget", "PanelEmpty", 0, 0, 10, 10, "outer")
            .Open("TextBox", "Kenshi_TextboxStandardText", 0, 0, 5, 5, "inner").Prop("Caption", "A & <B> \"c\" 'd'").UserString("preset", "0").Close()
            .Close().Xml();
        var inner = Root(xml).Element("Widget")!.Element("Widget")!;
        Assert.Equal("inner", (string?)inner.Attribute("name"));
        Assert.Equal("A & <B> \"c\" 'd'", (string?)inner.Element("Property")!.Attribute("value"));
        var u = inner.Element("UserString")!;
        Assert.Equal("preset", (string?)u.Attribute("key"));
        Assert.Equal("0", (string?)u.Attribute("value"));
    }

    [Fact]
    public void UnbalancedUseThrows()
    {
        Assert.Throws<InvalidOperationException>(() => new LayoutBuilder().Open("Widget", "PanelEmpty", 0, 0, 1, 1, "a").Xml());
        Assert.Throws<InvalidOperationException>(() => new LayoutBuilder().Close());
        Assert.Throws<InvalidOperationException>(() => new LayoutBuilder().Prop("Caption", "x"));
        Assert.Throws<InvalidOperationException>(() => new LayoutBuilder().UserString("k", "v"));
    }

    [Fact]
    public void FractionsUseTheInvariantShortForm()
    {
        var w = Root(new LayoutBuilder().OpenReal("Window", "S", 0.08f, 0.09f, 0.84f, 0.82f, "R").Close().Xml()).Element("Widget")!;
        Assert.Equal("0.08 0.09 0.84 0.82", (string?)w.Attribute("position_real"));
        w = Root(new LayoutBuilder().OpenReal("Window", "S", 0f, 1f, 0.5f, 0.123456f, "R").Close().Xml()).Element("Widget")!;
        Assert.Equal("0 1 0.5 0.12346", (string?)w.Attribute("position_real"));
    }

    // ---------- NativeUI (NativeUI.cpp:9-60) ----------

    static XElement Only(Func<LayoutBuilder, LayoutBuilder> build) => Root(build(new LayoutBuilder()).Close().Xml()).Element("Widget")!;

    static Dictionary<string, string> Props(XElement w) =>
        w.Elements("Property").ToDictionary(p => (string)p.Attribute("key")!, p => (string)p.Attribute("value")!);

    [Fact]
    public void LabelIsAStandardTextBoxWithItsCaption()
    {
        // NativeUI.cpp:9-16: createWidget<TextBox>("Kenshi_TextboxStandardText", bounds, Align::Default, name); setCaption(caption)
        var w = Only(b => NativeUI.Label(b, 0, 0, 100, 24, "PG_Metrics", "Arena"));
        Assert.Equal("TextBox", (string?)w.Attribute("type"));
        Assert.Equal("Kenshi_TextboxStandardText", (string?)w.Attribute("skin"));
        Assert.Equal("0 0 100 24", (string?)w.Attribute("position"));
        Assert.Equal("PG_Metrics", (string?)w.Attribute("name"));
        Assert.Null(w.Attribute("align"));
        Assert.Equal(new Dictionary<string, string> { ["Caption"] = "Arena" }, Props(w));
    }

    [Fact]
    public void ButtonIsKenshiButton1WithItsCaption()
    {
        // NativeUI.cpp:18-25
        var w = Only(b => NativeUI.Button(b, 0, 0, 200, 32, "PG_Start", "Start Fight"));
        Assert.Equal("Button", (string?)w.Attribute("type"));
        Assert.Equal("Kenshi_Button1", (string?)w.Attribute("skin"));
        Assert.Equal(new Dictionary<string, string> { ["Caption"] = "Start Fight" }, Props(w));
    }

    [Fact]
    public void ScrollShowsOnlyTheVerticalBarAndPutsTheCanvasTopLeft()
    {
        // NativeUI.cpp:27-38: "Kenshi_ScrollView"; setVisibleHScroll(false); setVisibleVScroll(true); setCanvasAlign(Left | Top)
        var w = Only(b => NativeUI.Scroll(b, 0, 0, 300, 300, "PG_Column0"));
        Assert.Equal("ScrollView", (string?)w.Attribute("type"));
        Assert.Equal("Kenshi_ScrollView", (string?)w.Attribute("skin"));
        Assert.Equal(new Dictionary<string, string> { ["VisibleHScroll"] = "false", ["VisibleVScroll"] = "true", ["CanvasAlign"] = "Left Top" }, Props(w));
    }

    [Fact]
    public void WrappedLabelIsTheWordWrapEmptyEditBox()
    {
        // NativeUI.cpp:40-49: createWidget<EditBox>("Kenshi_WordWrapEmpty", ...); setCaption(caption). The template sets WordWrap, Static and
        // MultiLine itself (kenshi_templates.xml), so the helper adds nothing else.
        var w = Only(b => NativeUI.WrappedLabel(b, 0, 0, 200, 24, "PG_Hero", "Arena Setup"));
        Assert.Equal("EditBox", (string?)w.Attribute("type"));
        Assert.Equal("Kenshi_WordWrapEmpty", (string?)w.Attribute("skin"));
        Assert.Equal(new Dictionary<string, string> { ["Caption"] = "Arena Setup" }, Props(w));
    }

    [Fact]
    public void HelpersLeaveTheWidgetOpenForChildren()
    {
        // PanelButton (ArenaUI.cpp:2537-2545) creates an EditBox inside the button NativeUI::Button returned.
        string xml = NativeUI.Button(new LayoutBuilder(), 0, 0, 200, 32, "PG_Start", "")
            .Open("EditBox", "Kenshi_PaintedWordWrapEmpty", 0, 0, 200, 32, "").Close()
            .Close().Xml();
        Assert.NotNull(Root(xml).Element("Widget")!.Element("Widget"));
    }

    [Theory]
    [InlineData(17, 8)]
    [InlineData(20, 10)]
    [InlineData(30, 15)]
    [InlineData(9, 4)]
    [InlineData(8, 4)]
    [InlineData(10, 5)]
    [InlineData(0, 4)]
    public void SpacingIsHalfTheFontHeightButAtLeastFour(int fontHeight, int spacing)
    {
        // NativeUI.cpp:51-55: height / 2 > 4 ? height / 2 : 4 (integer division)
        Assert.Equal(spacing, NativeUI.Spacing(fontHeight));
    }

    [Theory]
    [InlineData(17, 17, 0, 33)]    // one line at 1080 lines: 17 + 2 * 8
    [InlineData(12, 17, 0, 33)]    // text smaller than the font: the font height
    [InlineData(40, 17, 0, 56)]    // two lines
    [InlineData(17, 17, 64, 80)]   // an image taller than the text
    [InlineData(17, 20, 0, 40)]
    public void RowHeightIsTheTallestOfTextFontAndImagePlusTwoSpacings(int textHeight, int fontHeight, int imageHeight, int row)
    {
        // NativeUI.cpp:57-62
        Assert.Equal(row, NativeUI.RowHeight(textHeight, fontHeight, imageHeight));
    }

    [Fact]
    public void DefaultFontHeightIsKenshisStandardTextAt1080Lines() => Assert.Equal(17, NativeUI.DefaultFontHeight);

    // ---------- property keys (PgCheck ui flags unknown ones) ----------

    [Fact]
    public void TheHelpersUseOnlyKnownPropertyKeys()
    {
        var b = new LayoutBuilder().OpenReal("Window", "Kenshi_WindowCX", .1f, .1f, .5f, .5f, "R", "Center", "Window").Prop("Caption", "x");
        NativeUI.Label(b, 0, 0, 1, 1, "a", "a").Close();
        NativeUI.Button(b, 0, 0, 1, 1, "b", "b").Close();
        NativeUI.Scroll(b, 0, 0, 1, 1, "c").Close();
        NativeUI.WrappedLabel(b, 0, 0, 1, 1, "d", "d").Close();
        var keys = Root(b.Close().Xml()).Descendants("Property").Select(p => (string)p.Attribute("key")!).Distinct().ToList();
        Assert.Empty(MyGuiPropertyKeys.Unknown(keys));
    }

    [Fact]
    public void UnknownKeysAreReported()
    {
        Assert.Equal(new[] { "Captoin", "FontHeight" }, MyGuiPropertyKeys.Unknown(["Caption", "Captoin", "TextAlign", "FontHeight", "ImageRect"]));
    }

    // task 4.6a (found live): a caption's line breaks survive the XML (an attribute value's raw newline is normalised to a space by the parser)
    [Fact]
    public void LineBreaksInACaptionSurviveTheXml()
    {
        var xml = new LayoutBuilder().Open("EditBox", "Kenshi_WordWrapEmpty", 0, 0, 10, 10, "L").Prop("Caption", "a\nb\tc").Close().Xml();
        var caption = System.Xml.Linq.XDocument.Parse(xml).Descendants("Property").Single().Attribute("value")!.Value;
        Assert.Equal("a\nb\tc", caption);
    }
}
