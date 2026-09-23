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
        Assert.Contains("linha | outra", items[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadMarkdownFolder_uses_the_relative_path_as_title()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-notebooklm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "guia"));
        File.WriteAllText(Path.Combine(root, "guia", "01-intro.md"), "# Intro");

        try
        {
            var items = DocumentationBatch.ReadMarkdownFolder(root);

            Assert.Single(items);
            Assert.Equal("guia/01-intro", items[0].Title);
            Assert.Equal("# Intro", items[0].Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
