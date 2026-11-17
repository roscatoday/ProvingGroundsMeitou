using System.Text.Json;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Core/Glue/PgConsoleAnswer.cs: every <c>pg</c> verb answers one JSON line (task 2.7), <c>ok</c> true or false.</summary>
public class PgConsoleAnswerTests
{
    static JsonElement Parse(string line) => JsonDocument.Parse(line).RootElement;

    [Fact]
    public void OkLineHasVerbOkAndTheFieldsInOrder()
    {
        string line = PgConsoleAnswer.Ok("status", ("status", PgConsoleAnswer.Text("Proving Grounds 0.1.0: 5 slow ticks; \"q\" \\ \u0001")), ("queued", "12"));
        Assert.Equal("{\"verb\":\"status\",\"ok\":true,\"status\":\"Proving Grounds 0.1.0: 5 slow ticks; \\\"q\\\" \\\\ \\u0001\",\"queued\":12}", line);
        var d = Parse(line);
        Assert.True(d.GetProperty("ok").GetBoolean());
        Assert.Equal("Proving Grounds 0.1.0: 5 slow ticks; \"q\" \\ \u0001", d.GetProperty("status").GetString());
    }

    [Fact]
    public void FailLineCarriesTheError()
    {
        string line = PgConsoleAnswer.Fail("probe", "usage pg probe c:<slot>.<generation>");
        Assert.Equal("{\"verb\":\"probe\",\"ok\":false,\"error\":\"usage pg probe c:<slot>.<generation>\"}", line);
        Assert.False(Parse(line).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void OkIsAddedToAnObjectThatStartsWithItsVerb()
    {
        Assert.Equal("{\"verb\":\"probe-config\",\"ok\":true,\"loaded\":true}",
            PgConsoleAnswer.WithOk("probe-config", "{\"verb\":\"probe-config\",\"loaded\":true}"));
        // anything else is wrapped, never altered
        Assert.Equal("{\"verb\":\"probe-config\",\"ok\":true,\"value\":{\"loaded\":true}}",
            PgConsoleAnswer.WithOk("probe-config", "{\"loaded\":true}"));
    }

    [Fact]
    public void TextListIsAJsonArray()
    {
        string line = PgConsoleAnswer.Ok("help", ("commands", PgConsoleAnswer.TextList(["pg help: this list", "pg census: \"x\""])));
        var d = Parse(line);
        Assert.Equal(2, d.GetProperty("commands").GetArrayLength());
        Assert.Equal("pg census: \"x\"", d.GetProperty("commands")[1].GetString());
    }
}
