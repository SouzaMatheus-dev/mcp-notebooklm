using System.Text.Json.Nodes;
using McpNotebookLM.Services;

namespace McpNotebookLM.Tests;

public class RpcCodecTests
{
    [Fact]
    public void BuildBody_encodes_create_notebook_request()
    {
        var body = RpcCodec.BuildBody(RpcCodec.CreateNotebook, RpcCodec.CreateParams("Meu caderno"), "token");

        Assert.StartsWith("f.req=", body);
        Assert.EndsWith("&", body);
        Assert.Contains("CCqFvf", body, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Meu caderno"), body, StringComparison.Ordinal);
        Assert.Contains("at=" + Uri.EscapeDataString("token"), body, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_reads_wrb_payload()
    {
        const string raw = ")]}'\n12\n[[\"wrb.fr\",\"wXbhsf\",\"[[\\\"Titulo\\\",[],\\\"11111111-1111-1111-1111-111111111111\\\"]]\",null,null,null]]";

        var payload = RpcCodec.Decode(raw, RpcCodec.ListNotebooks);

        var notebooks = ResponseParser.Notebooks(payload);
        Assert.Single(notebooks);
        Assert.Equal("11111111-1111-1111-1111-111111111111", notebooks[0].Id);
        Assert.Equal("Titulo", notebooks[0].Title);
    }

    [Fact]
    public void ExtractWiz_reads_csrf_and_session()
    {
        const string html = """{"SNlM0e":"abc123","FdrFJe":"999888"}""";

        Assert.Equal("abc123", RpcCodec.ExtractWiz(html, "SNlM0e"));
        Assert.Equal("999888", RpcCodec.ExtractWiz(html, "FdrFJe"));
    }

    [Fact]
    public void AddTextParams_puts_title_and_content_in_the_source_spec()
    {
        var parameters = RpcCodec.AddTextParams("nb-12345678", "Runbook", "conteudo");
        var spec = parameters[0]![0]!;

        Assert.Equal("nb-12345678", parameters[1]!.GetValue<string>());
        Assert.Equal("Runbook", spec[1]![0]!.GetValue<string>());
        Assert.Equal("conteudo", spec[1]![1]!.GetValue<string>());
        Assert.Equal(2, spec[3]!.GetValue<int>());
    }

    [Fact]
    public void DeleteSourceParams_nests_the_source_id_three_times()
    {
        var parameters = RpcCodec.DeleteSourceParams("src-12345678");

        Assert.Equal("src-12345678", parameters[0]![0]![0]!.GetValue<string>());
    }
}
