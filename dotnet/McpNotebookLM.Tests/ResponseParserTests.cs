using System.Text.Json.Nodes;
using McpNotebookLM.Services;

namespace McpNotebookLM.Tests;

public class ResponseParserTests
{
    [Fact]
    public void Sources_read_id_envelope_and_title()
    {
        var payload = JsonNode.Parse("""
            [[["src-12345678"],"Runbook.md",[]]]
            """);

        var sources = ResponseParser.Sources(payload);

        Assert.Single(sources);
        Assert.Equal("src-12345678", sources[0].Id);
        Assert.Equal("Runbook.md", sources[0].Title);
    }

    [Fact]
    public void Notebooks_skip_rows_that_are_not_projects()
    {
        var payload = JsonNode.Parse("""
            [[["Caderno",[[["src-12345678"],"doc"]],"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"]]]
            """);

        var notebooks = ResponseParser.Notebooks(payload);

        Assert.Single(notebooks);
        Assert.Equal("Caderno", notebooks[0].Title);
        Assert.Equal(1, notebooks[0].SourceCount);
    }

    [Fact]
    public void SelectCreated_keeps_only_an_id_that_did_not_exist_before()
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "old-id-11111111" };
        var rows = new[]
        {
            new NotebookInfo("old-id-11111111", "Manual", 12),
            new NotebookInfo("new-id-22222222", "Manual", 0),
        };

        var selected = ResponseParser.SelectCreated("Manual", known, rows, "old-id-11111111", "[]");

        Assert.Equal("new-id-22222222", selected.Id);
    }

    [Fact]
    public void SelectCreated_rejects_a_response_that_only_repeats_an_old_notebook()
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "old-id-11111111" };
        var rows = new[] { new NotebookInfo("old-id-11111111", "Manual", 12) };

        var error = Assert.Throws<NotebookLmException>(() =>
            ResponseParser.SelectCreated("Manual", known, rows, "old-id-11111111", "[]"));

        Assert.Contains("id novo", error.Message, StringComparison.Ordinal);
    }
}
