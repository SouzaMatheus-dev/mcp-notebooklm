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

    [McpServerTool(Name = "atualizar_fonte"), Description(
        "Substitui a fonte de mesmo título em um notebook existente. " +
        "Remove só essa fonte e envia o texto ou arquivo novo com o mesmo nome.")]
    public Task<CallToolResult> AtualizarFonte(
        string notebookId,
        string titulo = "",
        string conteudo = "",
        string caminho = "") => Safe(async cancellationToken =>
    {
        var id = RequireId(notebookId);
        var hasText = !string.IsNullOrWhiteSpace(conteudo);
        var hasFile = !string.IsNullOrWhiteSpace(caminho);
        if (hasText == hasFile)
        {
            throw new NotebookLmException("Informe conteudo ou caminho, um dos dois.");
        }

        SourceInfo source = hasText
            ? await client.ReplaceTextAsync(id, RequireTitle(titulo), conteudo, cancellationToken).ConfigureAwait(false)
            : await client.ReplaceFileAsync(id, caminho, string.IsNullOrWhiteSpace(titulo) ? null : RequireTitle(titulo), cancellationToken)
                .ConfigureAwait(false);
        return $"Notebook {id} | {source.Id} | {source.Title} | {Link(id)}";
    });

    [McpServerTool(Name = "publicar_documentacao"), Description(
        "Publica texto, um JSON de textos, uma pasta de Markdown, arquivos e URLs. " +
        "Com notebookId, acrescenta o que ainda não está no notebook. " +
        "Cada chamada envia no máximo um lote e devolve o notebookId para a próxima. " +
        "Com substituir=true, troca as fontes de mesmo título a partir de inicio.")]
    public Task<CallToolResult> PublicarDocumentacao(
        string titulo = "",
        string notebookId = "",
        string texto = "",
        string tituloTexto = "Documentação",
        string textos = "",
        string pasta = "",
        string arquivos = "",
        string urls = "",
        bool substituir = false,
        int lote = 8,
        int inicio = 0) => Safe(async cancellationToken =>
    {
        var items = new List<DocumentationItem>();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            items.Add(new DocumentationItem(
                DocumentationKind.Text,
                string.IsNullOrWhiteSpace(tituloTexto) ? "Documentação" : RequireTitle(tituloTexto),
                texto));
        }

        items.AddRange(DocumentationBatch.ParseTexts(textos));
        if (!string.IsNullOrWhiteSpace(pasta))
        {
            items.AddRange(DocumentationBatch.ListMarkdownFolder(pasta));
        }

        foreach (var file in SplitList(arquivos))
        {
            items.Add(new DocumentationItem(DocumentationKind.File, Path.GetFileName(file), file));
        }

        var urlItems = SplitList(urls)
            .Select(url => new DocumentationItem(DocumentationKind.Url, url, url))
            .ToList();
        items.InsertRange(0, urlItems);

        if (items.Count == 0)
        {
            throw new NotebookLmException("Informe texto, textos, pasta, arquivos ou urls.");
        }

        if (items.Count > DocumentationBatch.MaxItems)
        {
            throw new NotebookLmException($"No máximo {DocumentationBatch.MaxItems} fontes por publicação.");
        }

        var batchSize = DocumentationBatch.NormalizeBatch(lote);

        if (substituir && string.IsNullOrWhiteSpace(notebookId))
        {
            throw new NotebookLmException("substituir exige notebookId de um notebook que já existe.");
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

        var existing = await client.ListSourcesAsync(id, cancellationToken).ConfigureAwait(false);
        var syncPath = new NotebookLmConfig().SyncPath(id);
        var manifest = SourceSync.Load(syncPath);
        var titles = existing.Select(source => source.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<SyncDecision> work;
        if (substituir)
        {
            work = items
                .Select(item => new SyncDecision(
                    item,
                    "",
                    titles.Contains(item.Title) ? SyncAction.Replace : SyncAction.Add))
                .ToList();
        }
        else
        {
            work = [];
            foreach (var item in items.Where(item => item.Kind != DocumentationKind.Url))
            {
                var hash = SourceSync.Fingerprint(item);
                var action = SourceSync.Decide(item.Title, hash, manifest, titles.Contains(item.Title));
                if (action == SyncAction.Skip && !manifest.ContainsKey(item.Title) && titles.Contains(item.Title))
                {
                    manifest[item.Title] = hash;
                }

                if (action != SyncAction.Skip)
                {
                    work.Add(new SyncDecision(item, hash, action));
                }
            }

            SourceSync.Save(syncPath, manifest);
        }

        if (!string.IsNullOrWhiteSpace(pasta))
        {
            var planned = items
                .Where(item => item.Kind != DocumentationKind.Url)
                .Select(item => item.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var absent = existing
                .Select(source => source.Title)
                .Where(title => !planned.Contains(title))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();
            if (absent.Count > 0)
            {
                report.AppendLine("No notebook e fora da pasta: " + string.Join(", ", absent));
            }
        }

        var start = substituir ? Math.Max(0, inicio) : 0;
        var page = substituir
            ? work.Skip(start).Take(batchSize).ToList()
            : work.Take(batchSize).ToList();
        var pendingCount = substituir ? Math.Max(0, work.Count - start) : work.Count;
        if (page.Count == 0)
        {
            report.AppendLine(substituir
                ? "Nada neste intervalo. Aumente inicio ou a publicação já percorreu a lista."
                : "Nada pendente. As fontes com o mesmo conteúdo já estão no notebook.");
            report.AppendLine("Publicação deste conjunto concluída.");
            return report.ToString().TrimEnd();
        }

        foreach (var decision in page)
        {
            if (decision.Item.Kind != DocumentationKind.Url)
            {
                SourceReplacement.FindSingle(existing, decision.Item.Title);
            }
        }

        var added = 0;
        var sentIds = new List<string>();
        try
        {
            foreach (var decision in page)
            {
                var item = decision.Item;
                var replace = decision.Action == SyncAction.Replace && item.Kind != DocumentationKind.Url;
                switch (item.Kind)
                {
                    case DocumentationKind.Text:
                    case DocumentationKind.MarkdownFile:
                        var content = item.Kind == DocumentationKind.MarkdownFile
                            ? DocumentationBatch.ReadMarkdown(item)
                            : item.Payload;
                        if (replace)
                        {
                            var source = await client.ReplaceTextAsync(id, item.Title, content, cancellationToken)
                                .ConfigureAwait(false);
                            sentIds.Add(source.Id);
                            report.Append("Texto atualizado: ").Append(source.Id).Append(" | ").Append(source.Title).AppendLine();
                        }
                        else
                        {
                            var sources = await client.AddTextAsync(id, item.Title, content, cancellationToken)
                                .ConfigureAwait(false);
                            sentIds.AddRange(sources.Select(source => source.Id));
                            report.AppendLine("Texto: " + FormatSources(sources));
                        }

                        break;
                    case DocumentationKind.File:
                        if (replace)
                        {
                            var source = await client.ReplaceFileAsync(id, item.Payload, null, cancellationToken)
                                .ConfigureAwait(false);
                            sentIds.Add(source.Id);
                            report.Append("Arquivo atualizado: ").Append(source.Id).Append(" | ").Append(source.Title).AppendLine();
                        }
                        else
                        {
                            var source = await client.AddFileAsync(id, item.Payload, cancellationToken)
                                .ConfigureAwait(false);
                            sentIds.Add(source.Id);
                            report.Append("Arquivo: ").Append(source.Id).Append(" | ").Append(source.Title).AppendLine();
                        }

                        break;
                    default:
                        var links = await client.AddUrlAsync(id, item.Payload, cancellationToken).ConfigureAwait(false);
                        sentIds.AddRange(links.Select(source => source.Id));
                        report.AppendLine("URL: " + FormatSources(links));
                        break;
                }

                if (decision.Hash.Length > 0)
                {
                    manifest[item.Title] = decision.Hash;
                    SourceSync.Save(syncPath, manifest);
                }

                added++;
            }

            var indexing = await client.WaitUntilReadyAsync(
                id, sentIds, TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
            if (indexing.Count > 0)
            {
                report.AppendLine("Ainda indexando: " + string.Join(", ", indexing));
            }
        }
        catch (Exception ex) when (ex is NotebookLmException or HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            var detail = ex is NotebookLmException notebookError ? notebookError.Message : Sanitize(ex.Message);
            throw new NotebookLmException(
                $"Falha depois de {added} fonte(s) no notebook {id}. " +
                $"Repita a chamada com notebookId={id}. O que já entrou é pulado. {detail}");
        }

        var sentThrough = start + added;
        var remaining = pendingCount - added;
        report.Append("Lote ").Append(added).Append(" de ").Append(pendingCount).Append(" pendente(s).").AppendLine();
        if (remaining > 0)
        {
            report.Append("Faltam ").Append(remaining).Append(". Repita com notebookId=").Append(id);
            if (substituir)
            {
                report.Append(" substituir=true inicio=").Append(sentThrough);
            }

            report.AppendLine(".");
        }
        else
        {
            report.AppendLine("Publicação deste conjunto concluída.");
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
