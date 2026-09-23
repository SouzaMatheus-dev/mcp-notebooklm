using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using McpNotebookLM.Services;

namespace McpNotebookLM.Tools;

[McpServerToolType]
public sealed class NotebookLmTools(NotebookLmClient client)
{
    [McpServerTool, Description("Verifica a sessão Google corporativa do NotebookLM, sem exibir cookies.")]
    public Task<string> StatusAutenticacao() => Safe(client.StatusAsync);

    [McpServerTool, Description("Lista os notebooks da conta autenticada no NotebookLM.")]
    public Task<string> ListarNotebooks() => Safe(async cancellationToken =>
    {
        var notebooks = await client.ListNotebooksAsync(cancellationToken).ConfigureAwait(false);
        if (notebooks.Count == 0)
        {
            return "Nenhum notebook encontrado.";
        }

        var builder = new StringBuilder();
        foreach (var notebook in notebooks.Take(200))
        {
            builder.Append(notebook.Id)
                .Append(" | ")
                .Append(notebook.Title)
                .Append(" | fontes=")
                .Append(notebook.SourceCount)
                .Append(" | ")
                .Append(Link(notebook.Id))
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    });

    [McpServerTool, Description("Cria um notebook vazio no NotebookLM e devolve o id e o link.")]
    public Task<string> CriarNotebook(string titulo) => Safe(async cancellationToken =>
    {
        var title = RequireTitle(titulo);
        var notebook = await client.CreateNotebookAsync(title, cancellationToken).ConfigureAwait(false);
        return $"{notebook.Id} | {notebook.Title} | {Link(notebook.Id)}";
    });

    [McpServerTool, Description("Lista as fontes já indexadas em um notebook.")]
    public Task<string> ListarFontes(string notebookId) => Safe(async cancellationToken =>
    {
        var sources = await client.ListSourcesAsync(RequireId(notebookId), cancellationToken).ConfigureAwait(false);
        if (sources.Count == 0)
        {
            return "Nenhuma fonte encontrada.";
        }

        return string.Join(
            Environment.NewLine,
            sources.Take(200).Select(source => $"{source.Id} | {source.Title}"));
    });

    [McpServerTool, Description("Cola um documento de texto ou Markdown como fonte do notebook.")]
    public Task<string> AdicionarDocumentoTexto(string notebookId, string titulo, string conteudo) =>
        Safe(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(conteudo))
            {
                return "Informe o conteúdo do documento.";
            }

            var sources = await client.AddTextAsync(
                RequireId(notebookId),
                RequireTitle(titulo),
                conteudo,
                cancellationToken).ConfigureAwait(false);
            return FormatSources(sources);
        });

    [McpServerTool, Description("Adiciona uma URL (página ou YouTube) como fonte do notebook.")]
    public Task<string> AdicionarDocumentoUrl(string notebookId, string url) =>
        Safe(async cancellationToken =>
        {
            var sources = await client.AddUrlAsync(RequireId(notebookId), url.Trim(), cancellationToken)
                .ConfigureAwait(false);
            return FormatSources(sources);
        });

    [McpServerTool, Description("Envia um arquivo local (pdf, txt, md, docx, html, csv, epub) como fonte do notebook.")]
    public Task<string> AdicionarDocumentoArquivo(string notebookId, string caminho) =>
        Safe(async cancellationToken =>
        {
            var source = await client.AddFileAsync(RequireId(notebookId), caminho, cancellationToken)
                .ConfigureAwait(false);
            return $"{source.Id} | {source.Title}";
        });

    [McpServerTool, Description("Cria um notebook e envia textos, arquivos locais e URLs. Separe vários itens com | ou quebra de linha.")]
    public Task<string> PublicarDocumentacao(
        string titulo,
        string texto = "",
        string tituloTexto = "Documentação",
        string arquivos = "",
        string urls = "") => Safe(async cancellationToken =>
    {
        var title = RequireTitle(titulo);
        var notebook = await client.CreateNotebookAsync(title, cancellationToken).ConfigureAwait(false);
        var report = new StringBuilder();
        report.Append("Notebook ").Append(notebook.Id).Append(" | ").Append(Link(notebook.Id)).AppendLine();

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var added = await client.AddTextAsync(
                notebook.Id,
                string.IsNullOrWhiteSpace(tituloTexto) ? "Documentação" : tituloTexto.Trim(),
                texto,
                cancellationToken).ConfigureAwait(false);
            report.AppendLine("Texto: " + FormatSources(added));
        }

        var files = SplitList(arquivos).Take(20).ToList();
        foreach (var file in files)
        {
            var added = await client.AddFileAsync(notebook.Id, file, cancellationToken).ConfigureAwait(false);
            report.Append("Arquivo: ").Append(added.Id).Append(" | ").Append(added.Title).AppendLine();
        }

        foreach (var url in SplitList(urls).Take(20))
        {
            var added = await client.AddUrlAsync(notebook.Id, url, cancellationToken).ConfigureAwait(false);
            report.AppendLine("URL: " + FormatSources(added));
        }

        if (string.IsNullOrWhiteSpace(texto) && files.Count == 0 && SplitList(urls).Count == 0)
        {
            report.AppendLine("Nenhuma fonte enviada. Use texto, arquivos ou urls.");
        }

        return report.ToString().TrimEnd();
    });

    private string Link(string id) => client.BaseUrl + "/notebook/" + id;

    private static async Task<string> Safe(Func<CancellationToken, Task<string>> action)
    {
        try
        {
            return await action(CancellationToken.None).ConfigureAwait(false);
        }
        catch (NotebookLmException ex)
        {
            return "Erro: " + ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return "Erro: " + Sanitize(ex.Message);
        }
    }

    private static string RequireTitle(string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length is < 1 or > 200)
        {
            throw new NotebookLmException("O título deve ter entre 1 e 200 caracteres.");
        }

        return trimmed;
    }

    private static string RequireId(string notebookId)
    {
        var trimmed = notebookId.Trim();
        if (trimmed.Length is < 8 or > 80 || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            throw new NotebookLmException("notebookId inválido.");
        }

        return trimmed;
    }

    private static List<string> SplitList(string? raw) =>
        (raw ?? "")
            .Split(['|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToList();

    private static string FormatSources(IReadOnlyList<SourceInfo> sources) =>
        sources.Count == 0
            ? "fonte registrada sem id na resposta"
            : string.Join("; ", sources.Select(source => $"{source.Id} | {source.Title}"));

    private static string Sanitize(string message)
    {
        var cut = message.IndexOf('?', StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}
