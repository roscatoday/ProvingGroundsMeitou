using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.DebugMenuNames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>DebugMenu.cpp's Layout (:143-230) and the window's real coordinates (:546-548), task 4.6b, over a fake text measure (10 px a character
/// wrapped by width, 20 px a line; buttons 11 px a character).</summary>
public class DebugMenuLayoutTests
{
    sealed class Fake : IDebugMenuTextMeasure
    {
        static int Lines(string text, int width)
        {
            int lines = 0;
            foreach (var part in text.Split('\n'))
            {
                int w = part.Length * 10;
                lines += width > 0 && w > width ? (w + width - 1) / width : 1;
            }
            return lines;
        }
        public int StandardHeight(string text, int width) => Lines(text, width) * 20;
        public int PaintedHeight(string text, int width) => Lines(text, width) * 20;
        public int ButtonWidth(string caption) => caption.Length * 11;
    }

    static readonly Fake M = new();

    static DebugMenuLayoutInput Input(int rows, bool fighters = true, int viewW = 1920, int viewH = 1080) => new()
    {
        ViewWidth = viewW, ViewHeight = viewH, FontHeight = 20, Fighters = fighters,
        Heading = fighters ? DebugMenuTexts.HeadingFighters : DebugMenuTexts.HeadingDiagnostics,
        Rows = [.. Enumerable.Range(0, 8).Select(i => new DebugRowView(i < rows, i < rows ? "Fighter " + i + "    R:150  M:0" : "", false))],
        DiagnosticLabels = DebugMenuTexts.DiagnosticLabels(new ProvingGrounds.Core.TownDiagnosticData.State(), 0),
        DiagnosticSummary = "Diagnostic: off",
    };

    [Fact]
    public void WindowIsTheRealCoordinatesOfTheView()
    {
        var l = DebugMenuLayout.Compute(Input(0), M);
        Assert.Equal(new LayoutRect(230, 97, 1459, 885), l.Window);   // (int)(0.12f*1920), (int)(0.09f*1080), (int)(0.76f*1920), (int)(0.82f*1080)
        Assert.Equal(1459 - 6, l.ClientWidth);
        Assert.Equal(885 - 45, l.ClientHeight);
    }

    [Fact]
    public void FightersPageStacksTabsHeadingPagerScrollAndStatus()
    {
        var l = DebugMenuLayout.Compute(Input(3), M);
        int gap = 10, body = 20, pageH = 40, w = l.ClientWidth - 2 * gap;
        Assert.Equal(gap, l.Gap);
        Assert.Equal(body, l.Body);
        int tab = (w - gap) / 2;
        Assert.Equal(new LayoutRect(gap, gap, tab, pageH), l.Rects[N.FightersTab]);
        Assert.Equal(new LayoutRect(gap + tab + gap, gap, w - tab - gap, pageH), l.Rects[N.DiagnosticsTab]);
        int headingTop = gap + pageH + gap;
        Assert.Equal(new LayoutRect(gap, headingTop, w, body), l.Rects[N.Heading]);
        int pageTop = headingTop + body + gap;
        int pageW = "Next >".Length * 11 + 4 * gap;   // both captions are 6 characters
        Assert.Equal(new LayoutRect(gap, pageTop, pageW, pageH), l.Rects[N.Prev]);
        Assert.Equal(new LayoutRect(l.ClientWidth - gap - pageW, pageTop, pageW, pageH), l.Rects[N.Next]);
        Assert.Equal(new LayoutRect(gap + pageW + gap, pageTop, w - 2 * pageW - 2 * gap, pageH), l.Rects[N.Page]);
        int statusTop = l.ClientHeight - gap - (body * 3 + 2 * gap);
        Assert.Equal(new LayoutRect(gap, statusTop, w, l.ClientHeight - gap - statusTop), l.Rects[N.Status]);
        int scrollTop = pageTop + pageH + gap;
        Assert.Equal(new LayoutRect(gap, scrollTop, w, statusTop - gap - scrollTop), l.Rects[N.Scroll]);
        // side by side: the list on the left, the details on the right; the actions overflow the view: its bar shows (the second pass)
        Assert.True(l.ScrollBar);
        int view = w - 15 - 20;
        int content = view - 2 * gap;
        Assert.False(content < body * 36 + 3 * gap);
        int list = (content - gap) / 2;
        Assert.Equal(new LayoutRect(gap, gap, list, body + 2 * gap), l.Rects[N.Row(0)]);
        Assert.Equal(new LayoutRect(gap, gap + body + 3 * gap, list, body + 2 * gap), l.Rects[N.Row(1)]);
        Assert.Equal(new LayoutRect(gap, gap, list - 2 * gap, body), l.Rects[N.RowText(0)]);
        Assert.Equal(new LayoutRect(gap + list + gap, gap, content - list - gap, body), l.Rects[N.Selected]);
        Assert.Equal(gap + body + gap, l.Rects[N.Rating].Y);
        var firstAction = l.Rects[N.Actions[0]];
        Assert.Equal(gap + 3 * (body + gap), firstAction.Y);
        Assert.Equal(body + 2 * gap, firstAction.H);
        var lastAction = l.Rects[N.Actions[11]];
        int bottom = lastAction.Y + lastAction.H + gap;
        Assert.Equal((Math.Max(1, view), Math.Max(l.Rects[N.Scroll].H - 15, bottom + gap)), l.Canvas[N.Scroll]);
    }

    [Fact]
    public void NarrowViewStacksTheDetailsUnderTheList()
    {
        var l = DebugMenuLayout.Compute(Input(2, viewW: 800, viewH: 900), M);
        int gap = 10, body = 20;
        var row0 = l.Rects[N.Row(0)];
        var row1 = l.Rects[N.Row(1)];
        var sel = l.Rects[N.Selected];
        Assert.Equal(gap, sel.X);
        Assert.Equal(row1.Y + row1.H + gap + gap, sel.Y);   // listBottom + gap
        Assert.Equal(row0.W, sel.W);
        Assert.True(l.ScrollBar);                            // the tall content shows the bar: the second pass used the narrower view
        Assert.Equal(l.Rects[N.Scroll].W - 15 - 20 - 2 * gap, row0.W);
        Assert.True(body > 0);
    }

    [Fact]
    public void LongTextsWrapAndGrowTheirRows()
    {
        var input = Input(1) with { Rows = [new DebugRowView(true, new string('x', 200), false), .. Enumerable.Repeat(new DebugRowView(false, "", false), 7)] };
        var l = DebugMenuLayout.Compute(input, M);
        var text = l.Rects[N.RowText(0)];
        int lines = (2000 + text.W - 1) / text.W;
        Assert.Equal(lines * 20, text.H);
        Assert.Equal(text.H + 20, l.Rects[N.Row(0)].H);
    }

    [Fact]
    public void DiagnosticsPagePutsTheActionsInTwoColumnsWithoutThePager()
    {
        var l = DebugMenuLayout.Compute(Input(0, fighters: false), M);
        int gap = 10, body = 20, pageH = 40;
        int pageTop = gap + pageH + gap + body + gap;
        Assert.Equal(pageTop, l.Rects[N.Scroll].Y);   // no pager row on this tab
        var summary = l.Rects[N.DiagnosticSummary];
        Assert.Equal(new LayoutRect(gap, gap, summary.W, body), summary);
        int actionTop = gap + body + 2 * gap;
        var a0 = l.Rects[N.Diagnostics[0]];
        var a1 = l.Rects[N.Diagnostics[1]];
        var a2 = l.Rects[N.Diagnostics[2]];
        Assert.Equal(actionTop, a0.Y);
        Assert.Equal(a0.Y, a1.Y);
        Assert.Equal(a0.X + a0.W + gap, a1.X);
        Assert.Equal(a0.Y + body * 2 + 2 * gap + gap, a2.Y);
        Assert.Equal(body * 2 + 2 * gap, a0.H);
        var last = l.Rects[N.Diagnostics[18]];   // 19 actions: the last one alone in its row
        Assert.Equal(gap, last.X);
        Assert.Equal(actionTop + 9 * (body * 2 + 3 * gap), last.Y);
    }
}
