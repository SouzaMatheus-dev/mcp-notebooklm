using System.Text.Json;

namespace McpNotebookLM.Services;

internal enum DocumentationKind
{
    Text,
    MarkdownFile,
    File,
    Url,
}

internal sealed record DocumentationItem(DocumentationKind Kind, string Title, string Payload);

internal static class DocumentationBatch
{
    public const int DefaultBatch = 8;
    public const int MaxBatch = 20;
    public const int MaxItems = 300;

    public static int NormalizeBatch(int lote) =>
        lote <= 0 ? DefaultBatch : Math.Clamp(lote, 1, MaxBatch);

    public static List<DocumentationItem> ParseTexts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new NotebookLmException(
                "textos não é um JSON válido. Use [{\"titulo\":\"...\",\"conteudo\":\"...\"}].");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new NotebookLmException("textos deve ser um JSON array de {titulo, conteudo}.");
            }

            var list = new List<DocumentationItem>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("titulo", out var titleNode) ||
                    titleNode.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("conteudo", out var contentNode) ||
                    contentNode.ValueKind != JsonValueKind.String)
                {
                    throw new NotebookLmException("Cada item de textos precisa de titulo e conteudo em texto.");
                }

                var title = titleNode.GetString()?.Trim() ?? "";
                var content = contentNode.GetString() ?? "";
                if (title.Length is < 1 or > 200 || string.IsNullOrWhiteSpace(content))
                {
                    throw new NotebookLmException("Cada item de textos precisa de titulo (1–200) e conteudo.");
                }

                list.Add(new DocumentationItem(DocumentationKind.Text, title, content));
            }

            return list;
        }
    }

    public static List<DocumentationItem> ListMarkdownFolder(string folder)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new NotebookLmException("Pasta não encontrada: " + root);
        }

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new NotebookLmException("Nenhum arquivo .md ou .markdown em " + root);
        }

        if (files.Count > MaxItems)
        {
            throw new NotebookLmException($"A pasta tem mais de {MaxItems} arquivos Markdown.");
        }

        var list = new List<DocumentationItem>(files.Count);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file);
            var title = MarkdownTitle(root, file);
            list.Add(new DocumentationItem(DocumentationKind.MarkdownFile, title, file));
        }

        return list;
    }

    public static string ReadMarkdown(DocumentationItem item)
    {
        var content = File.ReadAllText(item.Payload);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new NotebookLmException("Arquivo Markdown vazio: " + item.Title);
        }

        return content;
    }

    public static List<DocumentationItem> Pending(
        IReadOnlyList<DocumentationItem> items,
        IReadOnlyList<SourceInfo> existing,
        bool replace)
    {
        if (replace)
        {
            return items.ToList();
        }

        var titles = existing.Select(source => source.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documentsStarted = items.Any(item =>
            item.Kind != DocumentationKind.Url && titles.Contains(item.Title));
        return items.Where(item =>
            item.Kind == DocumentationKind.Url
                ? !documentsStarted
                : !titles.Contains(item.Title)).ToList();
    }

    public static List<DocumentationItem> Page(IReadOnlyList<DocumentationItem> pending, int inicio, int lote, bool replace)
    {
        var start = replace ? Math.Clamp(inicio, 0, pending.Count) : 0;
        return pending.Skip(start).Take(lote).ToList();
    }

    private static string MarkdownTitle(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var title = Path.ChangeExtension(relative, null)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (title.Length is < 1 or > 200)
        {
            throw new NotebookLmException("O título derivado de " + relative + " precisa ter entre 1 e 200 caracteres.");
        }

        return title;
    }
}
