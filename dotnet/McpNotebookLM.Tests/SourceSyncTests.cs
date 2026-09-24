using System.Text.Json.Nodes;
using McpNotebookLM.Services;

namespace McpNotebookLM.Tests;

public class SourceSyncTests
{
    [Fact]
    public void Decide_skips_same_hash_replaces_changed_and_adds_unknown()
    {
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADR-001"] = "aaa",
        };

        Assert.Equal(SyncAction.Skip, SourceSync.Decide("ADR-001", "aaa", manifest, titleExists: true));
        Assert.Equal(SyncAction.Replace, SourceSync.Decide("ADR-001", "bbb", manifest, titleExists: true));
        Assert.Equal(SyncAction.Skip, SourceSync.Decide("ADR-002", "ccc", manifest, titleExists: true));
        Assert.Equal(SyncAction.Add, SourceSync.Decide("ADR-003", "ddd", manifest, titleExists: false));
    }

    [Fact]
    public void Sources_read_ready_status()
    {
        var payload = JsonNode.Parse("""
            [[["src-12345678"],"ADR-001",null,[null,2]]]
            """);

        var sources = ResponseParser.Sources(payload);

        Assert.Equal(2, sources[0].Status);
    }
}
