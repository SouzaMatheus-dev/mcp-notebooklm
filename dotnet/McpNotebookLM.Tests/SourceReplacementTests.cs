using McpNotebookLM.Services;

namespace McpNotebookLM.Tests;

public class SourceReplacementTests
{
    [Fact]
    public void RequireSingle_returns_the_only_title_match()
    {
        var sources = new[]
        {
            new SourceInfo("src-aaaaaaaa", "Outro"),
            new SourceInfo("src-bbbbbbbb", "ADR-001"),
        };

        var match = SourceReplacement.RequireSingle(sources, "adr-001");

        Assert.Equal("src-bbbbbbbb", match.Id);
    }

    [Fact]
    public void RequireSingle_rejects_a_missing_title()
    {
        var error = Assert.Throws<NotebookLmException>(() =>
            SourceReplacement.RequireSingle([new SourceInfo("src-aaaaaaaa", "ADR-002")], "ADR-001"));

        Assert.Contains("ADR-001", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindSingle_rejects_two_sources_with_the_same_title()
    {
        var sources = new[]
        {
            new SourceInfo("src-aaaaaaaa", "ADR-001"),
            new SourceInfo("src-bbbbbbbb", "ADR-001"),
        };

        var error = Assert.Throws<NotebookLmException>(() => SourceReplacement.FindSingle(sources, "ADR-001"));

        Assert.Contains("src-aaaaaaaa", error.Message, StringComparison.Ordinal);
        Assert.Contains("src-bbbbbbbb", error.Message, StringComparison.Ordinal);
    }
}
