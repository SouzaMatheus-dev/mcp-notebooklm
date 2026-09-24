namespace McpNotebookLM.Services;

internal static class SourceReplacement
{
    public static SourceInfo? FindSingle(IReadOnlyList<SourceInfo> sources, string title)
    {
        var matches = sources
            .Where(source => source.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            throw new NotebookLmException(
                $"Há {matches.Count} fontes com o título \"{title}\". Ids: " +
                string.Join(", ", matches.Select(source => source.Id)));
        }

        return matches[0];
    }

    public static SourceInfo RequireSingle(IReadOnlyList<SourceInfo> sources, string title)
    {
        var match = FindSingle(sources, title);
        if (match is null)
        {
            throw new NotebookLmException($"Não há fonte com o título \"{title}\" neste notebook.");
        }

        return match;
    }
}
