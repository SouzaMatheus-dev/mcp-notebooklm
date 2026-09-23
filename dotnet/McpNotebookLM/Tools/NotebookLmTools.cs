using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpNotebookLM.Services;

namespace McpNotebookLM.Tools;

[McpServerToolType]
public sealed class NotebookLmTools(NotebookLmClient client)
{
    [McpServerTool(Name = "status_autenticacao"), Description("Verifica a sessão Google corporativa do NotebookLM, sem exibir cookies.")]
    public Task<CallToolResult> StatusAutenticacao() => Safe(client.StatusAsync);

    [McpServerTool(Name = "listar_notebooks"), Description("Lista os notebooks da conta autenticada no NotebookLM.")]
    public Task<CallToolResult> ListarNotebooks() => Safe(async cancellationToken =>
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

    [McpServerTool(Name = "criar_notebook"), Description("Cria um notebook vazio no NotebookLM e devolve o id e o link.")]
    public Task<CallToolResult> CriarNotebook(string titulo) => Safe(async cancellationToken =>
    {
        var title = RequireTitle(titulo);
        var notebook = await client.CreateNotebookAsync(title, cancellationToken).ConfigureAwait(false);
        return $"{notebook.Id} | {notebook.Title} | {Link(notebook.Id)}";
    });

    [McpServerTool(Name = "listar_fontes"), Description("Lista as fontes já indexadas em um notebook.")]
    public Task<CallToolResult> ListarFontes(string notebookId) => Safe(async cancellationToken =>
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

    [McpServerTool(Name = "adicionar_documento_texto"), Description("Cola um documento de texto ou Markdown como fonte do notebook.")]
    public Task<CallToolResult> AdicionarDocumentoTexto(string notebookId, string titulo, string conteudo) =>
        Safe(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(conteudo))
            {
                throw new NotebookLmException("Informe o conteúdo do documento.");
            }

            var sources = await client.AddTextAsync(
                RequireId(notebookId),
                RequireTitle(titulo),
                conteudo,
                cancellationToken).ConfigureAwait(false);
            return FormatSources(sources);
        });

    [McpServerTool(Name = "adicionar_documento_url"), Description("Adiciona uma URL (página ou YouTube) como fonte do notebook.")]
    public Task<CallToolResult> AdicionarDocumentoUrl(string notebookId, string url) =>
        Safe(async cancellationToken =>
        {
            var sources = await client.AddUrlAsync(RequireId(notebookId), url.Trim(), cancellationToken)
                .ConfigureAwait(false);
            return FormatSources(sources);
        });

    [McpServerTool(Name = "adicionar_documento_arquivo"), Description("Envia um arquivo local (pdf, txt, md, docx, html, csv, epub) como fonte do notebook.")]
    public Task<CallToolResult> AdicionarDocumentoArquivo(string notebookId, string caminho) =>
        Safe(async cancellationToken =>
        {
            var source = await client.AddFileAsync(RequireId(notebookId), caminho, cancellationToken)
                .ConfigureAwait(false);
            return $"{source.Id} | {source.Title}";
        });

    [McpServerTool(Name = "publicar_documentacao"), Description(
        "Publica texto, um JSON de textos, uma pasta de Markdown, arquivos e URLs. " +
        "Com notebookId, acrescenta fontes no notebook existente. Sem notebookId, cria um notebook com titulo.")]
    public Task<CallToolResult> PublicarDocumentacao(
        string titulo = "",
        string notebookId = "",
        string texto = "",
        string tituloTexto = "Documentação",
        string textos = "",
        string pasta = "",
        string arquivos = "",
        string urls = "") => Safe(async cancellationToken =>
    {
        var texts = new List<(string Title, string Content)>();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            texts.Add((
                string.IsNullOrWhiteSpace(tituloTexto) ? "Documentação" : RequireTitle(tituloTexto),
                texto));
        }

        texts.AddRange(DocumentationBatch.ParseTexts(textos));
        if (!string.IsNullOrWhiteSpace(pasta))
        {
            texts.AddRange(DocumentationBatch.ReadMarkdownFolder(pasta));
        }

        if (texts.Count > DocumentationBatch.MaxTexts)
        {
            throw new NotebookLmException(
                $"No máximo {DocumentationBatch.MaxTexts} textos ou Markdown por chamada.");
        }

        var files = SplitList(arquivos);
        if (files.Count > DocumentationBatch.MaxFiles)
        {
            throw new NotebookLmException($"No máximo {DocumentationBatch.MaxFiles} arquivos por chamada.");
        }

        var links = SplitList(urls);
        if (links.Count > DocumentationBatch.MaxUrls)
        {
            throw new NotebookLmException($"No máximo {DocumentationBatch.MaxUrls} URLs por chamada.");
        }

        if (texts.Count == 0 && files.Count == 0 && links.Count == 0)
        {
            throw new NotebookLmException("Informe texto, textos, pasta, arquivos ou urls.");
        }

        string id;
        var report = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(notebookId))
        {
            id = RequireId(notebookId);
            report.Append("Notebook ").Append(id).Append(" | ").Append(Link(id)).AppendLine();
        }
        else
        {
            var notebook = await client.CreateNotebookAsync(RequireTitle(titulo), cancellationToken)
                .ConfigureAwait(false);
            id = notebook.Id;
            report.Append("Notebook ")
                .Append(id)
                .Append(" | ")
                .Append(notebook.Title)
                .Append(" | ")
                .Append(Link(id))
                .AppendLine();
        }

        var added = 0;
        try
        {
            foreach (var item in texts)
            {
                var sources = await client.AddTextAsync(id, item.Title, item.Content, cancellationToken)
                    .ConfigureAwait(false);
                report.AppendLine("Texto: " + FormatSources(sources));
                added++;
            }

            foreach (var file in files)
            {
                var source = await client.AddFileAsync(id, file, cancellationToken).ConfigureAwait(false);
                report.Append("Arquivo: ").Append(source.Id).Append(" | ").Append(source.Title).AppendLine();
                added++;
            }

            foreach (var url in links)
            {
                var sources = await client.AddUrlAsync(id, url, cancellationToken).ConfigureAwait(false);
                report.AppendLine("URL: " + FormatSources(sources));
                added++;
            }
        }
        catch (Exception ex) when (ex is NotebookLmException or HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            var detail = ex is NotebookLmException notebookError ? notebookError.Message : Sanitize(ex.Message);
            throw new NotebookLmException(
                $"Falha depois de {added} fonte(s) no notebook {id}. " +
                $"Repita a chamada com notebookId={id} para as fontes que faltam. {detail}");
        }

        return report.ToString().TrimEnd();
    });

    private string Link(string id) => client.BaseUrl + "/notebook/" + id;

    private static async Task<CallToolResult> Safe(Func<CancellationToken, Task<string>> action)
    {
        try
        {
            return Ok(await action(CancellationToken.None).ConfigureAwait(false));
        }
        catch (NotebookLmException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return Fail(Sanitize(ex.Message));
        }
    }

    private static CallToolResult Ok(string text) => new()
    {
        IsError = false,
        Content = [new TextContentBlock { Text = text }],
    };

    private static CallToolResult Fail(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    };

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
