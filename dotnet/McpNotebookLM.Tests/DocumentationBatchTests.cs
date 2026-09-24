using McpNotebookLM.Services;

namespace McpNotebookLM.Tests;

public class DocumentationBatchTests
{
    [Fact]
    public void ParseTexts_reads_title_and_content()
    {
        var items = DocumentationBatch.ParseTexts("""
            [{"titulo":"Intro","conteudo":"# Manual\n\nlinha | outra"}]
            """);

        Assert.Single(items);
        Assert.Equal("Intro", items[0].Title);
        Assert.Contains("linha | outra", items[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadMarkdownFolder_uses_the_relative_path_as_title()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-notebooklm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "guia"));
        File.WriteAllText(Path.Combine(root, "guia", "01-intro.md"), "# Intro");

        try
        {
            var items = DocumentationBatch.ListMarkdownFolder(root);

            Assert.Single(items);
            Assert.Equal("guia/01-intro", items[0].Title);
            Assert.Equal("# Intro", DocumentationBatch.ReadMarkdown(items[0]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Page_skips_titles_already_in_the_notebook_and_limits_the_batch()
    {
        var items = new List<DocumentationItem>
        {
            new(DocumentationKind.MarkdownFile, "a", "a.md"),
            new(DocumentationKind.MarkdownFile, "b", "b.md"),
            new(DocumentationKind.MarkdownFile, "c", "c.md"),
        };
        var existing = new List<SourceInfo> { new("src-aaaaaaaa", "a") };

        var pending = DocumentationBatch.Pending(items, existing, replace: false);
        var page = DocumentationBatch.Page(pending, inicio: 0, lote: 1, replace: false);

        Assert.Equal(["b", "c"], pending.Select(item => item.Title).ToArray());
        Assert.Equal("b", page[0].Title);
        Assert.Single(page);
    }
}
