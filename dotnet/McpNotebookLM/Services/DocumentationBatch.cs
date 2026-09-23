using System.Text.Json;

namespace McpNotebookLM.Services;

internal static class DocumentationBatch
{
    public const int MaxTexts = 40;
    public const int MaxFiles = 20;
    public const int MaxUrls = 20;

    public static List<(string Title, string Content)> ParseTexts(string? json)
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

            var list = new List<(string Title, string Content)>();
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

                list.Add((title, content));
            }

            return list;
        }
    }

    public static List<(string Title, string Content)> ReadMarkdownFolder(string folder)
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

        if (files.Count > MaxTexts)
        {
            throw new NotebookLmException($"A pasta tem mais de {MaxTexts} arquivos Markdown.");
        }

        var list = new List<(string Title, string Content)>(files.Count);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file);
            var title = Path.ChangeExtension(relative, null)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (title.Length is < 1 or > 200)
            {
                throw new NotebookLmException("O título derivado de " + relative + " precisa ter entre 1 e 200 caracteres.");
            }

            var content = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new NotebookLmException("Arquivo Markdown vazio: " + relative);
            }

            list.Add((title, content));
        }

        return list;
    }
}
