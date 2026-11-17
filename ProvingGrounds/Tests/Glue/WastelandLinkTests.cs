using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 10.2 (port only): the reflection link to The Wasteland Remembers' WastelandApi (Core/Glue/WastelandLink.cs), bound to fake API
/// classes shaped as its documentation says (versions 1 to 3), plus wrong shapes.</summary>
public class WastelandLinkTests
{
    static class Calls
    {
        internal static readonly List<string> Log = [];
        internal static bool Ready = true;
    }

    // ---------- the documented API, version 3 ----------
    static class FakeV3
    {
        public const int Version = 3;
        public static bool Ready => Calls.Ready;
        public static bool Note(string text) { Calls.Log.Add($"Note|{text}"); return true; }
        public static bool Moment(string caption, string place, int weight) { Calls.Log.Add($"Moment|{caption}|{place}|{weight}"); return true; }
        public static bool Report(string kind, string place, string faction, string text, int weight, string source)
        { Calls.Log.Add($"Report|{kind}|{place}|{faction}|{text}|{weight}|{source}"); return true; }
        public static bool LeaveAlone(string name, string faction, string source) { Calls.Log.Add($"LeaveAlone|{name}|{faction}|{source}"); return true; }
        public static double Heat(string town) => 0;   // members we do not use are ignored
    }

    // ---------- version 1 has Report and LeaveAlone too, but its Version says it does not: they must stay unbound ----------
    static class FakeV1
    {
        public const int Version = 1;
        public static bool Ready => true;
        public static bool Note(string text) { Calls.Log.Add($"V1Note|{text}"); return true; }
        public static bool Moment(string caption, string place, int weight) => true;
        public static bool Report(string kind, string place, string faction, string text, int weight, string source) => true;
        public static bool LeaveAlone(string name, string faction, string source) => true;
    }

    static class FakeWrongShapes
    {
        public const int Version = 3;
        public static bool Ready => true;
        public static bool LeaveAlone(string name, string faction) => true;                        // two parameters
        public static void Note(string text) { }                                                    // returns nothing
        public static bool Report(string kind, string place, string faction, string text, double weight, string source) => true;   // double
    }

    static class FakeThrows
    {
        public const int Version = 3;
        public static bool Ready => true;
        public static bool Note(string text) => throw new InvalidOperationException("boom");
        public static bool LeaveAlone(string name, string faction, string source) => true;
    }

    static class FakeNoVersion
    {
        public static bool Ready => true;
        public static bool Note(string text) => true;
    }

    [Fact]
    public void NotInstalledIsNoLink()
    {
        Assert.Null(WastelandLink.Bind(null, null));
        Assert.Null(WastelandLink.Find());   // no LivingWastelandMeitou assembly in the test process
    }

    [Fact]
    public void Version3BindsEverythingAndPassesOurSource()
    {
        Calls.Log.Clear();
        Calls.Ready = true;
        var link = WastelandLink.Bind(typeof(FakeV3), null)!;
        Assert.Equal(3, link.Version);
        Assert.True(link.CanNote && link.CanMoment && link.CanReport && link.CanLeaveAlone);
        Assert.True(link.Ready);
        Assert.True(link.LeaveAlone("Hagar", "Bloodstamp"));
        Assert.True(link.Report("arena-upset", "Scratch", "Bloodstamp", "Ruka beat the favourite Hagar.", 130));
        Assert.True(link.Note("A line."));
        Assert.True(link.Moment("A moment", "Scratch", -5));
        Assert.Equal([
            "LeaveAlone|Hagar|Bloodstamp|ProvingGroundsMeitou",
            "Report|arena-upset|Scratch|Bloodstamp|Ruka beat the favourite Hagar.|100|ProvingGroundsMeitou",   // weight clamped to 100
            "Note|A line.",
            "Moment|A moment|Scratch|0",   // and to 0
        ], Calls.Log);
        Assert.Equal("API version 3, Ready, Note, Moment, Report, LeaveAlone", link.Describe());
    }

    [Fact]
    public void ReadyFollowsTheApi()
    {
        var link = WastelandLink.Bind(typeof(FakeV3), null)!;
        Calls.Ready = false;
        Assert.False(link.Ready);
        Calls.Ready = true;
        Assert.True(link.Ready);
    }

    [Fact]
    public void Version1BindsOnlyItsOwnMembers()
    {
        Calls.Log.Clear();
        var link = WastelandLink.Bind(typeof(FakeV1), null)!;
        Assert.Equal(1, link.Version);
        Assert.True(link.CanNote && link.CanMoment);
        Assert.False(link.CanReport);
        Assert.False(link.CanLeaveAlone);
        Assert.False(link.LeaveAlone("Hagar", ""));
        Assert.False(link.Report("k", "Scratch", "", "t", 1));
        Assert.True(link.Note("x"));
        Assert.Equal(["V1Note|x"], Calls.Log);
    }

    [Fact]
    public void WrongShapesStayUnbound()
    {
        var link = WastelandLink.Bind(typeof(FakeWrongShapes), null)!;
        Assert.False(link.CanLeaveAlone);
        Assert.False(link.CanNote);
        Assert.False(link.CanReport);
        Assert.False(link.CanMoment);   // absent
        Assert.True(link.Ready);
    }

    [Fact]
    public void NoVersionMeansNothing()
    {
        var link = WastelandLink.Bind(typeof(FakeNoVersion), null)!;
        Assert.Equal(0, link.Version);
        Assert.False(link.CanNote);
        Assert.False(link.Ready);
    }

    [Fact]
    public void AThrowSwitchesTheLinkOffWithOneLogLine()
    {
        var lines = new List<string>();
        var link = WastelandLink.Bind(typeof(FakeThrows), lines.Add)!;
        Assert.True(link.CanLeaveAlone);
        Assert.False(link.Note("x"));
        Assert.True(link.Broken);
        Assert.False(link.CanLeaveAlone);
        Assert.False(link.LeaveAlone("Hagar", ""));   // off for the world
        Assert.False(link.Ready);
        Assert.Single(lines);
        Assert.Contains("Note threw InvalidOperationException: boom", lines[0]);
    }
}
