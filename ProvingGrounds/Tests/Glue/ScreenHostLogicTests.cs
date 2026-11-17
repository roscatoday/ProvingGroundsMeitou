using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.4: the pure decisions of the window framework (src/Ui/ScreenHost.cs, src/Ui/Portraits.cs): Register once per screen host and
/// Replace only when the shape key changes (spike S6), open/close requests the simulation publishes, the event -> click mapping (the
/// Ctrl modifier stands in for the right-click Meitou's screens never raise) and the portrait cells after the squad bar (spike S7, CAP 7.4).</summary>
public class ScreenHostLogicTests
{
    // ---------- shape keys and the per-frame decisions (ScreenFrame over a fake screen host) ----------

    sealed class FakeHost : IScreenSurface
    {
        public readonly HashSet<string> Registered = [], Opened = [];
        public readonly List<string> Calls = [];
        public bool FailBuild;
        public bool IsRegistered(string id) => Registered.Contains(id);
        public bool IsOpen(string id) => Opened.Contains(id);
        public bool TryRegister(string id)
        {
            Calls.Add("register " + id);
            if (FailBuild) return false;
            if (!Registered.Add(id)) throw new ArgumentException("registered twice");   // as MyGuiScreenHost.cs:188-189
            return true;
        }
        public bool TryReplace(string id) { Calls.Add("replace " + id); return !FailBuild; }
        public void Open(string id) { Calls.Add("open " + id); Opened.Add(id); }
        public void Close(string id) { Calls.Add("close " + id); Opened.Remove(id); }
        public void ReportClosed(string id, int serial) => Calls.Add("closed " + id + " " + serial);
        /// <summary>A close the framework did not make (Escape, the window's close button).</summary>
        public void UserClose(string id) => Opened.Remove(id);
        /// <summary>LoadUi: a new, empty host.</summary>
        public void NewHost() { Registered.Clear(); Opened.Clear(); }
        public List<string> Take() { var c = Calls.ToList(); Calls.Clear(); return c; }
    }

    static ScreenState S(string key, int serial = 0, bool open = false, bool model = true) => new("pg.test", key, model, serial, open);

    [Fact]
    public void RegistersOnceThenReplacesOnlyWhenTheShapeKeyChanges()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("rows 3")]);
        Assert.Equal(["register pg.test"], h.Take());
        f.Run(h, w, [S("rows 3")]);
        Assert.Empty(h.Take());
        f.Run(h, w, [S("rows 5")]);
        Assert.Equal(["replace pg.test"], h.Take());
        Assert.Equal("rows 5", f.Current("pg.test"));
        Assert.True(f.Bindable("pg.test", "rows 5"));
    }

    [Fact]
    public void ANewHostRegistersAgainNeverTwiceOnOneHost()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("rows 3")]);
        h.NewHost(); f.NewHost();
        f.Run(h, w, [S("rows 3")]);
        f.Run(h, w, [S("rows 3")]);
        Assert.Equal(["register pg.test", "register pg.test"], h.Take());
    }

    [Fact]
    public void AFailedBuildIsNotCommittedAndIsRetriedOnlyWhenTheKeyChanges()
    {
        var f = new ScreenFrame(); var h = new FakeHost { FailBuild = true }; object w = new();
        f.Run(h, w, [S("rows 3")]);
        f.Run(h, w, [S("rows 3")]);   // no spinning on a broken build
        Assert.Equal(["register pg.test"], h.Take());
        Assert.Null(f.Current("pg.test"));
        h.FailBuild = false;
        f.Run(h, w, [S("rows 4")]);   // a new key: tried again
        Assert.Equal(["register pg.test"], h.Take());
        Assert.Equal("rows 4", f.Current("pg.test"));
    }

    [Fact]   // task 6.4b review: the C++ creates a window again at its next show; a new open request retries the failed key, the same one does not
    public void AFailedBuildIsRetriedOnANewOpenRequestNotOnTheSameOne()
    {
        var f = new ScreenFrame(); var h = new FakeHost { FailBuild = true }; object w = new();
        f.Run(h, w, [S("rows 1", 1, true)]);
        Assert.Equal(["register pg.test", "closed pg.test 1"], h.Take());
        f.Run(h, w, [S("rows 1", 1, true)]);   // the same open request: not retried
        f.Run(h, w, [S("rows 1", 1, false)]);  // the sim half heard it closed (the serial kept): not retried
        Assert.Empty(h.Take());
        f.Run(h, w, [S("rows 1", 2, true)]);   // a new Show, same shape, still failing: tried once more
        f.Run(h, w, [S("rows 1", 2, true)]);
        Assert.Equal(["register pg.test", "closed pg.test 2"], h.Take());
        h.FailBuild = false;
        f.Run(h, w, [S("rows 1", 3, true)]);   // the next Show: registered and opened
        Assert.Equal(["register pg.test", "open pg.test"], h.Take());
        Assert.Equal("rows 1", f.Current("pg.test"));
    }

    [Fact]   // task 6.4b review: a failed register while the sim half wants the window open is reported closed, once
    public void AFailedRegisterWhileAskedOpenIsReportedClosedOnce()
    {
        var f = new ScreenFrame(); var h = new FakeHost { FailBuild = true }; object w = new();
        f.Run(h, w, [S("rows 1", 1, true)]);
        Assert.Equal(["register pg.test", "closed pg.test 1"], h.Take());
        f.Run(h, w, [S("rows 1", 1, true)]);   // not acknowledged yet: nothing again
        f.Run(h, w, [S("rows 1", 1, false)]);  // acknowledged (the sim half closed it under the same serial)
        Assert.Empty(h.Take());
        f.Run(h, w, [S("rows 1", 2, true)]);   // the next show: retried, and reported again when it fails again
        Assert.Equal(["register pg.test", "closed pg.test 2"], h.Take());
        var g = new ScreenFrame(); var h2 = new FakeHost { FailBuild = true };
        g.Run(h2, w, [S("rows 1", 0, false)]);  // not asked open: nothing to report
        Assert.Equal(["register pg.test"], h2.Take());
    }

    [Fact]   // task 6.4b review: a failed Replace closes and reports; it is retried on the next open serial and not before
    public void AFailedReplaceIsRetriedOnTheNextOpenSerialAndNotBefore()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("rows 1", 1, true)]);
        h.Take();
        h.FailBuild = true;
        f.Run(h, w, [S("rows 2", 1, true)]);
        Assert.Equal(["replace pg.test", "close pg.test", "closed pg.test 1"], h.Take());
        f.Run(h, w, [S("rows 2", 1, true)]);
        f.Run(h, w, [S("rows 2", 1, false)]);
        Assert.Empty(h.Take());
        h.FailBuild = false;
        f.Run(h, w, [S("rows 2", 1, false)]);  // the build would work now, but no new open request: not before
        Assert.Empty(h.Take());
        f.Run(h, w, [S("rows 2", 2, true)]);
        Assert.Equal(["replace pg.test", "open pg.test"], h.Take());
    }

    [Fact]   // task 6.4b re-review: a new world restarts the request serials, so an old world's failed (key, serial) is forgotten
    public void ANewWorldForgetsAFailedKey()
    {
        var f = new ScreenFrame(); var h = new FakeHost { FailBuild = true };
        object w1 = new(), w2 = new();
        f.Run(h, w1, [S("rows 1", 1, true)]);
        Assert.Equal(["register pg.test", "closed pg.test 1"], h.Take());
        f.Run(h, w2, [S("rows 1", 1, true)]);   // a new world's first open request, the same serial and key: tried and reported
        Assert.Equal(["register pg.test", "closed pg.test 1"], h.Take());
        h.FailBuild = false;
        f.Run(h, new object(), [S("rows 1", 1, true)]);
        Assert.Equal(["register pg.test", "open pg.test"], h.Take());
    }

    [Fact]   // task 6.4b review: a click is judged against the model the window was last bound (drawn) with, not the newest one published
    public void AClickIsJudgedAgainstTheModelLastBound()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        object a = "model A", b = "model B";
        Assert.Same(b, f.ClickModel("pg.test", b));   // nothing bound yet: the newest
        f.Run(h, w, [S("rows 1", 1, true)]);
        f.Bound("pg.test", a);                        // drawn with A
        object? received = null;
        void Handler(object? m) => received = m;
        Handler(f.ClickModel("pg.test", b));          // B published since, the click arrives before the next bind
        Assert.Same(a, received);
        f.Bound("pg.test", b);
        Assert.Same(b, f.ClickModel("pg.test", b));
        f.Run(h, new object(), [S("rows 1", 0, false)]);   // a new world forgets it
        Assert.Same(a, f.ClickModel("pg.test", a));
        f.Bound("pg.test", b);
        f.NewHost();                                   // so does a new screen host
        Assert.Same(a, f.ClickModel("pg.test", a));
    }

    [Fact]   // a close request (a new serial, not open) does not retry a failed key
    public void AFailedBuildIsNotRetriedOnACloseRequest()
    {
        var f = new ScreenFrame(); var h = new FakeHost { FailBuild = true }; object w = new();
        f.Run(h, w, [S("rows 1", 1, true)]);
        f.Run(h, w, [S("rows 1", 2, false)]);
        Assert.Equal(["register pg.test", "closed pg.test 1"], h.Take());
    }

    [Fact]
    public void AFailedReplaceClosesTheWindowAndKeepsTheOldKeyUnbindable()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("rows 3", 1, true)]);
        h.Take();
        h.FailBuild = true;
        f.Run(h, w, [S("rows 5", 1, true)]);
        Assert.Equal(["replace pg.test", "close pg.test", "closed pg.test 1"], h.Take());   // the sim half hears it closed
        Assert.Equal("rows 3", f.Current("pg.test"));
        Assert.False(f.Bindable("pg.test", "rows 5"));   // the old layout is never bound to the new model
        f.Run(h, w, [S("rows 5", 1, true)]);
        Assert.Empty(h.Take());   // not retried, not reported
        h.FailBuild = false;
        f.Run(h, w, [S("rows 6", 2, true)]);
        Assert.Equal(["replace pg.test", "open pg.test"], h.Take());
    }

    [Fact]
    public void ACloseTheFrameworkDidNotMakeIsReportedOnce()
    {
        // MyGuiScreenHost.Escape (:613), CloseAll (:286) and the close button close the window directly: the sim half must hear of it.
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("k", 1, true)]);
        h.Take();
        h.UserClose("pg.test");
        f.Run(h, w, [S("k", 1, true)]);
        Assert.Equal(["closed pg.test 1"], h.Take());
        f.Run(h, w, [S("k", 1, false)]);   // the sim half says closed now, serial unchanged: nothing more
        Assert.Empty(h.Take());
        f.Run(h, w, [S("k", 2, true)]);   // the next open request works with one press
        Assert.Equal(["open pg.test"], h.Take());
    }

    [Fact]
    public void AUiReloadBeforeTheSimAcknowledgesAWindowSideCloseDoesNotReopen()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("k", 1, true)]);
        h.UserClose("pg.test");
        f.Run(h, w, [S("k", 1, true)]);
        h.Take();
        h.NewHost(); f.NewHost();   // LoadUi before the sim handled the Closed click: the model still says open, serial 1
        f.Run(h, w, [S("k", 1, true)]);
        Assert.Equal(["register pg.test"], h.Take());
        f.Run(h, w, [S("k", 1, false)]);   // acknowledged
        f.Run(h, w, [S("k", 2, true)]);
        Assert.Equal(["open pg.test"], h.Take());
    }

    [Fact]
    public void ANewerRequestAfterAReportedCloseStillOpens()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("k", 1, true)]);
        h.UserClose("pg.test");
        f.Run(h, w, [S("k", 1, true)]);
        h.Take();
        f.Run(h, w, [S("k", 2, true)]);   // a new open before the close was acknowledged: the new serial wins
        Assert.Equal(["open pg.test"], h.Take());
    }

    [Fact]
    public void PortraitRectCacheEmptiesWhenThePortraitSizeChanges()
    {
        var cache = new PortraitRectCache<string>();
        Assert.False(cache.TryGet("face", 5, 128, out _));
        cache.Set("face", 5, "1280 0 128 128");
        Assert.True(cache.TryGet("face", 5, 128, out var r) && r == "1280 0 128 128");
        Assert.False(cache.TryGet("other", 5, 128, out _));   // per widget
        Assert.False(cache.TryGet("face", 5, 256, out _));    // the enhanced look: emptied
        Assert.Equal(0, cache.Count);
        cache.Set("face", 5, "1280 0 256 256");
        Assert.True(cache.TryGet("face", 5, 256, out r) && r == "1280 0 256 256");
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("face", 5, 256, out _));
    }

    [Fact]
    public void ARequestedCloseIsNotReportedBack()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("k", 1, true)]);
        f.Run(h, w, [S("k", 2, false)]);
        f.Run(h, w, [S("k", 2, false)]);
        Assert.Equal(["register pg.test", "open pg.test", "close pg.test"], h.Take());
    }

    [Fact]
    public void AUiReloadOpensAgainAWindowThatWasOpen()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object w = new();
        f.Run(h, w, [S("k", 1, true)]);
        h.Take();
        h.NewHost(); f.NewHost();   // LoadUi again: the new host has nothing registered or open
        f.Run(h, w, [S("k", 1, true)]);
        Assert.Equal(["register pg.test", "open pg.test"], h.Take());
        h.NewHost(); f.NewHost();
        f.Run(h, w, [S("k", 2, false)]);   // closed before the reload: stays closed
        Assert.Equal(["register pg.test"], h.Take());
    }

    [Fact]
    public void ANewWorldClosesOurWindowsWithoutReportingToTheNewWorld()
    {
        var f = new ScreenFrame(); var h = new FakeHost(); object a = new(), b = new();
        f.Run(h, a, [S("k", 1, true)]);
        h.Take();
        f.Run(h, b, [S("k", 0, false)]);
        Assert.Equal(["close pg.test"], h.Take());
        f.Run(h, b, [S("k", 1, true)]);   // the new world's first request (serial 1) acts
        Assert.Equal(["open pg.test"], h.Take());
    }

    [Fact]
    public void NoModelNoRequest()
    {
        var f = new ScreenFrame(); var h = new FakeHost();
        f.Run(h, null, [S("k", 3, true, model: false)]);
        Assert.Equal(["register pg.test"], h.Take());
    }

    [Fact]
    public void ScreenShapesCommitsAndMatches()
    {
        var shapes = new ScreenShapes();
        Assert.Null(shapes.Current("a"));
        Assert.False(shapes.Matches("a", "1"));
        shapes.Commit("a", "1");
        Assert.True(shapes.Matches("a", "1"));
        Assert.False(shapes.Matches("b", "1"));
        shapes.Reset();
        Assert.Null(shapes.Current("a"));
    }

    // ---------- open / close requests ----------

    [Fact]
    public void ARequestActsOnceEachNewSerial()
    {
        var r = new ScreenRequests();
        object world = new();
        Assert.Equal(ScreenRequests.Act.None, r.Observe("s", world, 0, false));
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", world, 1, true));
        Assert.Equal(ScreenRequests.Act.None, r.Observe("s", world, 1, true));
        Assert.Equal(ScreenRequests.Act.Close, r.Observe("s", world, 2, false));
        Assert.Equal(ScreenRequests.Act.None, r.Observe("s", world, 2, false));
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", world, 5, true));   // two requests between frames: the newest wins
    }

    [Fact]
    public void ANewWorldStartsFromSerialZero()
    {
        // Each world's system starts its serials at 0: the first request of a new world (serial 1) acts even if the old world reached 1 too.
        var r = new ScreenRequests();
        object a = new(), b = new();
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", a, 1, true));
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", b, 1, true));
        Assert.Equal(ScreenRequests.Act.None, r.Observe("s", b, 1, true));
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("t", b, 1, true));   // screens are tracked apart
    }

    [Fact]
    public void ResetAppliesTheLatestRequestAgain()
    {
        var r = new ScreenRequests();
        object w = new();
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", w, 4, true));
        Assert.Equal(ScreenRequests.Act.None, r.Observe("s", w, 4, true));
        r.Reset();
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", w, 4, true));
    }

    [Fact]
    public void ANewWorldWithNoRequestDoesNothing()
    {
        var r = new ScreenRequests();
        Assert.Equal(ScreenRequests.Act.Open, r.Observe("s", 1, 3, true));
        Assert.Equal(ScreenRequests.Act.None, r.Observe("s", 2, 0, false));
    }

    // ---------- events -> clicks ----------

    static readonly HashSet<string> Ours = new(StringComparer.Ordinal) { "pg.test" };

    [Fact]
    public void AButtonPressOfOurScreenIsAPress()
    {
        var c = UiClickPolicy.Map(Ours, "pg.test", "PgTestButton", "pg:PgTestButton", "", 0, ctrl: false)!.Value;
        Assert.Equal(new UiClickData("pg.test", "PgTestButton", UiEventKind.Press, "", 0, false), c);
    }

    [Fact]
    public void CtrlIsCarriedOnAPress()
    {
        Assert.True(UiClickPolicy.Map(Ours, "pg.test", "PG_Preset1", "pg:PG_Preset1", "", 0, ctrl: true)!.Value.Ctrl);
    }

    [Fact]
    public void ListAndEditEventsKeepTheirKindTextAndRow()
    {
        // MyGuiScreenHost raises (screen, list, "select"/"accept", argument, line) and (screen, edit, "text", text) (MyGuiScreenHost.cs:420, 427, 645).
        Assert.Equal(new UiClickData("pg.test", "List", UiEventKind.Select, "row7", 3, false), UiClickPolicy.Map(Ours, "pg.test", "List", "select", "row7", 3f, false));
        Assert.Equal(new UiClickData("pg.test", "List", UiEventKind.Accept, "row7", 3, false), UiClickPolicy.Map(Ours, "pg.test", "List", "accept", "row7", 3f, false));
        Assert.Equal(new UiClickData("pg.test", "Edit", UiEventKind.Text, "abc", 0, false), UiClickPolicy.Map(Ours, "pg.test", "Edit", "text", "abc", 0, true));
    }

    [Fact]
    public void OtherScreensAndChromeAreIgnored()
    {
        Assert.Null(UiClickPolicy.Map(Ours, "inventory", "ArrangeButton", "", "", 0, false));
        Assert.Null(UiClickPolicy.Map(Ours, "pg.test", "x", "mygui:close", "", 0, false));
        Assert.Null(UiClickPolicy.Map(Ours, "pg.test", "", "pg:", "", 0, false));
        Assert.Null(UiClickPolicy.Map(Ours, "pg.test", "Bar", "scroll", "", 4, false));   // scroll bars (MyGuiScreenHost.Widgets.cs:387)
        Assert.Null(UiClickPolicy.Map(Ours, "pg.test", "Bar", "step", "end", 1, false));
        Assert.Null(UiClickPolicy.Map(Ours, "pg.test", "Unbound", "", "", 0, false));   // a button with no action of ours
    }

    [Fact]
    public void TheButtonActionNamesItsWidget() => Assert.Equal("pg:PG_Start", UiClickPolicy.ActionFor("PG_Start"));

    // ---------- portrait cells (CAP 7.4: cells after the squad bar, before the conversation's two) ----------

    static Dictionary<string, int> Run(PortraitCells<string> cells, string[] wanted, int firstFree, int end, params int[] taken)
    {
        cells.Update(wanted, firstFree, end, taken.Select((c, i) => ("trade" + i, c)).ToList());
        return cells.Cells.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    [Fact]
    public void CellsStartAfterTheSquadBarInRequestOrder()
    {
        var cells = new PortraitCells<string>();
        Assert.Equal(new Dictionary<string, int> { ["a"] = 6, ["b"] = 7, ["c"] = 8 }, Run(cells, ["a", "b", "c"], 6, 32));
        Assert.Equal(new[] { ("a", 6), ("b", 7), ("c", 8) }, cells.Pairs());
    }

    [Fact]
    public void AKeptCharacterKeepsItsCellWhenOthersComeAndGo()
    {
        var cells = new PortraitCells<string>();
        Run(cells, ["a", "b", "c"], 6, 32);
        Assert.Equal(new Dictionary<string, int> { ["c"] = 8, ["d"] = 6 }, Run(cells, ["c", "d"], 6, 32));
        Assert.Equal(new Dictionary<string, int> { ["c"] = 8, ["d"] = 6, ["e"] = 7 }, Run(cells, ["c", "d", "e"], 6, 32));
    }

    [Fact]
    public void ClosingReleasesEveryCell()
    {
        var cells = new PortraitCells<string>();
        Run(cells, ["a"], 6, 32);
        Assert.Empty(Run(cells, [], 6, 32));
        Assert.False(cells.TryCell("a", out _));
    }

    [Fact]
    public void AFullSquadLeavesNoCellAndTheNameStaysAlone()
    {
        // 30 squad members fill cells 0..29; with a conversation open the end is 30 (DialogueNpcCell): nothing left.
        var cells = new PortraitCells<string>();
        Assert.Empty(Run(cells, ["a", "b"], 30, 30));
        // Without a conversation cells 30 and 31 are free (PortraitCells = 32, TradeWindows.cs:600).
        Assert.Equal(new Dictionary<string, int> { ["a"] = 30, ["b"] = 31 }, Run(cells, ["a", "b", "c"], 30, 32));
        Assert.False(cells.TryCell("c", out _));
    }

    [Fact]
    public void AConversationTakesBackItsCells()
    {
        var cells = new PortraitCells<string>();
        Run(cells, ["a", "b"], 30, 32);
        Assert.Empty(Run(cells, ["a", "b"], 30, 30));
    }

    [Fact]
    public void AGrowingSquadPushesCellsUp()
    {
        var cells = new PortraitCells<string>();
        Run(cells, ["a", "b"], 6, 32);
        Assert.Equal(new Dictionary<string, int> { ["b"] = 7, ["a"] = 8 }, Run(cells, ["a", "b"], 7, 32));
    }

    [Fact]
    public void CellsOtherWindowsTakeAreSkipped()
    {
        // The character trading window's rows take cells after the squad bar too (TradeWindows.cs:594-610): ours go around them.
        var cells = new PortraitCells<string>();
        Assert.Equal(new Dictionary<string, int> { ["a"] = 8, ["b"] = 10 }, Run(cells, ["a", "b"], 6, 32, 6, 7, 9));
    }

    [Fact]
    public void ACharacterTheTradeWindowAlreadyShowsUsesItsCellAndIsNotAppendedAgain()
    {
        // TradeWindows.cs:507 makes a dictionary of the list by character: a character listed twice would throw. Ours is shown in the trade
        // window's cell, gets none of our cells, and is not ours to give back.
        var cells = new PortraitCells<string>();
        cells.Update(["x", "a"], 6, 32, [("x", 6), ("t", 7)]);
        Assert.Equal(new[] { ("a", 8) }, cells.Pairs());
        Assert.Equal(new Dictionary<string, int> { ["a"] = 8 }, cells.Cells.ToDictionary(kv => kv.Key, kv => kv.Value));
        Assert.True(cells.TryCell("x", out int x) && x == 6);
        Assert.True(cells.TryCell("a", out int a) && a == 8);
        // the trade window closes: x now takes one of our cells
        cells.Update(["x", "a"], 6, 32);
        Assert.Equal(new[] { ("a", 8), ("x", 6) }.OrderBy(p => p.Item1), cells.Pairs().OrderBy(p => p.Key));
        // we hold x, then the trade window lists it: our cell for x is dropped, its cell is shown, x is not appended
        cells.Update(["x", "a"], 6, 32, [("x", 9)]);
        Assert.DoesNotContain(cells.Pairs(), p => p.Key == "x");
        Assert.True(cells.TryCell("x", out x) && x == 9);
    }

    [Fact]
    public void TwoWindowsShowingOneCharacterAppendItOnce()
    {
        var cells = new PortraitCells<string>();
        cells.Update(["a", "b", "a"], 6, 32);
        Assert.Equal(new[] { ("a", 6), ("b", 7) }, cells.Pairs());
        cells.Update(["a", "a"], 6, 32, [("a", 10)]);
        Assert.Empty(cells.Pairs());
        Assert.True(cells.TryCell("a", out int c) && c == 10);
    }

    [Fact]
    public void UpdateSaysWhetherAnythingChangedAndKeepsThePairsList()
    {
        var cells = new PortraitCells<string>();
        Assert.True(cells.Update(["a"], 6, 32));
        var first = cells.Pairs();
        Assert.False(cells.Update(["a"], 6, 32));
        Assert.Same(first, cells.Pairs());
        Assert.True(cells.Update(["a", "b"], 6, 32));
        Assert.True(cells.Update([], 6, 32));
        Assert.Empty(cells.Pairs());
        Assert.False(cells.Update([], 6, 32));
    }

    [Fact]
    public void ARepeatedCharacterGetsOneCell()
    {
        var cells = new PortraitCells<string>();
        Assert.Equal(new Dictionary<string, int> { ["a"] = 6, ["b"] = 7 }, Run(cells, ["a", "b", "a"], 6, 32));
    }

    [Fact]
    public void NoCellBeyondTheAtlas()
    {
        var cells = new PortraitCells<string>();
        Assert.Empty(Run(cells, ["a"], 40, 32));
        Assert.Empty(Run(cells, ["a"], -5, 0));
        Assert.Equal(new Dictionary<string, int> { ["a"] = 0 }, Run(cells, ["a"], -5, 32));   // a negative start counts from cell 0
    }

    // ---------- task 4.8: the portrait list's reuse test (src/Ui/Portraits.cs) ----------

    [Fact]
    public void PortraitOutputReuse_OnlyWhenTheInputAndOurPairsAreTheSame_NeverIndexesPastTheLastOutput()
    {
        var input = new List<(string, int)> { ("t", 3) };
        var last = new List<(string, int)> { ("t", 3), ("a", 9) };
        Assert.True(PortraitOutput.SameAsLast(input, input, last, [("a", 9)]));
        // a new character asked for (more of our pairs than the last output holds): not the same, and no index past its end (live: an
        // IndexOutOfRangeException each frame turned the portraits off, arena-full 2026-11-14)
        Assert.False(PortraitOutput.SameAsLast(input, input, last, [("a", 9), ("b", 10)]));
        Assert.False(PortraitOutput.SameAsLast(input, input, last, []));
        Assert.False(PortraitOutput.SameAsLast(input, input, last, [("a", 8)]));
        Assert.False(PortraitOutput.SameAsLast([("t", 4)], input, last, [("a", 9)]));
        Assert.False(PortraitOutput.SameAsLast(input, input, null, [("a", 9)]));
    }
}
