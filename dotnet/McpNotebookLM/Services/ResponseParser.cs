using System.Text.Json.Nodes;

namespace McpNotebookLM.Services;

public sealed record NotebookInfo(string Id, string Title, int SourceCount);

public sealed record SourceInfo(string Id, string Title, int? Status = null);

internal static class ResponseParser
{
    public static IReadOnlyList<NotebookInfo> Notebooks(JsonNode? payload)
    {
        var rows = new List<NotebookInfo>();
        CollectNotebooks(payload, rows, 0);
        return rows
            .GroupBy(row => row.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    public static IReadOnlyList<SourceInfo> Sources(JsonNode? payload)
    {
        var rows = new List<SourceInfo>();
        CollectSources(payload, rows, 0);
        return rows
            .Where(row => row.Id.Length > 0)
            .GroupBy(row => row.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    public static NotebookInfo SelectCreated(
        string title,
        IReadOnlySet<string> knownIds,
        IReadOnlyList<NotebookInfo> responseNotebooks,
        string? fallbackId,
        string responseShape)
    {
        var fresh = responseNotebooks.Where(item => !knownIds.Contains(item.Id)).ToList();
        var titled = fresh.FirstOrDefault(item =>
            item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (titled is not null)
        {
            return titled;
        }

        if (fresh.Count > 0)
        {
            return fresh[0];
        }

        if (!string.IsNullOrEmpty(fallbackId) && !knownIds.Contains(fallbackId))
        {
            return new NotebookInfo(fallbackId, title, 0);
        }

        throw new NotebookLmException(
            "A resposta da criação não trouxe um id novo. Um notebook antigo com o mesmo título foi ignorado. Forma: " +
            responseShape);
    }

    public static string? FirstId(JsonNode? payload, string? exclude = null)
    {
        string? found = null;
        WalkIds(payload, exclude, ref found, 0);
        return found;
    }

    public static string Shape(JsonNode? node, int depth = 0)
    {
        if (node is null || depth > 4)
        {
            return "null";
        }

        if (node is JsonValue)
        {
            return "value";
        }

        if (node is JsonArray array)
        {
            return "arr[" + string.Join(",", array.Take(6).Select(item => Shape(item, depth + 1))) + "]";
        }

        if (node is JsonObject obj)
        {
            return "obj{" + string.Join(",", obj.Select(pair => pair.Key)) + "}";
        }

        return "?";
    }

    private static void CollectNotebooks(JsonNode? node, List<NotebookInfo> rows, int depth)
    {
        if (node is not JsonArray array || depth > 6)
        {
            return;
        }

        if (TryNotebook(array, out var info))
        {
            rows.Add(info);
            return;
        }

        foreach (var child in array)
        {
            CollectNotebooks(child, rows, depth + 1);
        }
    }

    private static bool TryNotebook(JsonArray array, out NotebookInfo info)
    {
        info = new NotebookInfo("", "", 0);
        if (array.Count < 3 ||
            array[1] is not null and not JsonArray ||
            StringOf(array[0]) is not string title ||
            StringOf(array[2]) is not string id ||
            !LooksLikeId(id))
        {
            return false;
        }

        var sources = array[1] is JsonArray sourceArray ? sourceArray.Count : 0;
        info = new NotebookInfo(id, title.Replace("thought\n", "", StringComparison.Ordinal).Trim(), sources);
        return true;
    }

    private static void CollectSources(JsonNode? node, List<SourceInfo> rows, int depth)
    {
        if (node is not JsonArray array || depth > 8)
        {
            return;
        }

        var id = ReadId(array.Count > 0 ? array[0] : null);
        var title = array.Count > 1 ? StringOf(array[1]) : null;
        if (LooksLikeId(id) && title is not null)
        {
            rows.Add(new SourceInfo(id, title, ReadStatus(array.Count > 3 ? array[3] : null)));
            return;
        }

        foreach (var child in array)
        {
            CollectSources(child, rows, depth + 1);
        }
    }

    private static void WalkIds(JsonNode? node, string? exclude, ref string? found, int depth)
    {
        if (found is not null || node is null || depth > 8)
        {
            return;
        }

        if (StringOf(node) is string text && LooksLikeId(text) && text != exclude)
        {
            found = text;
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                WalkIds(child, exclude, ref found, depth + 1);
            }
        }
    }

    private static int? ReadStatus(JsonNode? settings)
    {
        if (settings is not JsonArray array || array.Count < 2 || array[1] is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<int>(out var status) ? status : null;
    }

    private static string ReadId(JsonNode? node)
    {
        if (StringOf(node) is string plain)
        {
            return plain;
        }

        if (node is not JsonArray array || array.Count == 0)
        {
            return "";
        }

        if (StringOf(array[0]) is string wrapped && wrapped.Length > 0)
        {
            return wrapped;
        }

        if (array.Count > 2 &&
            array[2] is JsonArray nested &&
            nested.Count > 0 &&
            StringOf(nested[0]) is string drive)
        {
            return drive;
        }

        return "";
    }

    private static bool LooksLikeId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 8 and <= 80 &&
        !value.Contains(' ', StringComparison.Ordinal) &&
        !value.Contains('\n', StringComparison.Ordinal);

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
