using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace McpNotebookLM.Services;

internal static class RpcCodec
{
    public const string ListNotebooks = "wXbhsf";
    public const string CreateNotebook = "CCqFvf";
    public const string GetNotebook = "rLM1Ne";
    public const string AddSource = "izAoDd";
    public const string AddSourceFile = "o4cbdc";
    public const string DeleteSource = "tGMBJ";
    public const string RenameSource = "b7Wfje";

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string BuildBody(string rpcId, JsonArray parameters, string csrfToken)
    {
        var paramsJson = parameters.ToJsonString(Compact);
        var inner = new JsonArray { rpcId, paramsJson, null, "generic" };
        var outer = new JsonArray { new JsonArray { inner } };
        var freq = outer.ToJsonString(Compact);
        return $"f.req={Uri.EscapeDataString(freq)}&at={Uri.EscapeDataString(csrfToken)}&";
    }

    public static JsonArray TemplateBlock() =>
        new()
        {
            2,
            null,
            null,
            new JsonArray
            {
                1, null, null, null, null, null, null, null, null, null, new JsonArray { 1 },
            },
        };

    public static JsonArray ListParams() =>
        new()
        {
            null,
            1,
            null,
            TemplateBlock(),
        };

    public static JsonArray CreateParams(string title) =>
        new() { title, null, null, TemplateBlock() };

    public static JsonArray GetParams(string notebookId) =>
        new() { notebookId, null, TemplateBlock(), null, 0 };

    public static JsonArray AddTextParams(string notebookId, string title, string content)
    {
        var spec = new JsonArray
        {
            null,
            new JsonArray { title, content },
            null,
            2,
            null,
            null,
            null,
            null,
            null,
            null,
            1,
        };
        return new JsonArray { new JsonArray { spec }, notebookId, TemplateBlock() };
    }

    public static JsonArray AddUrlParams(string notebookId, string url, bool youtube)
    {
        JsonArray spec = youtube
            ? new() { null, null, null, null, null, null, null, new JsonArray { url }, null, null, 1 }
            : new() { null, null, new JsonArray { url }, null, null, null, null, null, null, null, 1 };
        return new JsonArray { new JsonArray { spec }, notebookId, TemplateBlock() };
    }

    public static JsonArray DeleteSourceParams(string sourceId) =>
        new() { new JsonArray { new JsonArray { sourceId } } };

    public static JsonArray RenameSourceParams(string sourceId, string title) =>
        new() { null, new JsonArray { sourceId }, new JsonArray { new JsonArray { new JsonArray { title } } } };

    public static JsonArray RegisterFileParams(string notebookId, string fileName) =>
        new()
        {
            new JsonArray { new JsonArray { fileName } },
            notebookId,
            TemplateBlock(),
        };

    public static bool IsYouTube(string url) =>
        url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    public static JsonNode? Decode(string raw, string rpcId)
    {
        if (raw.Contains("<html", StringComparison.OrdinalIgnoreCase) &&
            !raw.Contains("wrb.fr", StringComparison.Ordinal))
        {
            throw new NotebookLmException(
                "NotebookLM devolveu a página de login. Rode `mcp-notebooklm login` de novo.");
        }

        var text = raw.TrimStart();
        if (text.StartsWith(")]}'", StringComparison.Ordinal))
        {
            text = text[4..].TrimStart('\r', '\n');
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.All(char.IsDigit))
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(trimmed);
            }
            catch (JsonException)
            {
                continue;
            }

            if (!TryFindFrame(node, rpcId, out var payload, out var errorCode))
            {
                continue;
            }

            if (payload is null && errorCode is int code)
            {
                throw new NotebookLmException($"NotebookLM recusou a chamada {rpcId} (código {code}).");
            }

            return payload;
        }

        throw new NotebookLmException(
            $"A resposta não trouxe resultado para {rpcId}. A API interna do NotebookLM pode ter mudado.");
    }

    public static string? ExtractWiz(string html, string key)
    {
        var escaped = Regex.Escape(key);
        string[] patterns =
        [
            $"\"{escaped}\"\\s*:\\s*\"([^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"",
            $"'{escaped}'\\s*:\\s*'([^'\\\\]*(?:\\\\.[^'\\\\]*)*)'",
            $"&quot;{escaped}&quot;\\s*:\\s*&quot;((?:(?!&quot;).)*)&quot;",
        ];

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern);
            if (match.Success)
            {
                return Regex.Unescape(match.Groups[1].Value);
            }
        }

        return null;
    }

    private static bool TryFindFrame(
        JsonNode? node,
        string rpcId,
        out JsonNode? payload,
        out int? errorCode)
    {
        payload = null;
        errorCode = null;
        if (node is not JsonArray array)
        {
            return false;
        }

        if (array.Count > 2 &&
            StringAt(array, 0) == "wrb.fr" &&
            StringAt(array, 1) == rpcId)
        {
            payload = UnwrapPayload(array[2]);
            errorCode = ReadErrorCode(array.Count > 5 ? array[5] : null);
            return true;
        }

        foreach (var child in array)
        {
            if (TryFindFrame(child, rpcId, out payload, out errorCode))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonNode? UnwrapPayload(JsonNode? payload)
    {
        if (payload is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return payload;
            }
        }

        return payload;
    }

    private static int? ReadErrorCode(JsonNode? node)
    {
        if (node is JsonArray array &&
            array.Count > 0 &&
            array[0] is JsonValue value &&
            value.TryGetValue<int>(out var code))
        {
            return code;
        }

        return null;
    }

    private static string? StringAt(JsonArray array, int index)
    {
        if (index >= array.Count || array[index] is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : null;
    }
}
