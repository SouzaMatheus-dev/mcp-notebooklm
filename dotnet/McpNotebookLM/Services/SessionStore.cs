using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpNotebookLM.Services;

internal sealed record SessionCookie(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly);

internal static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static IReadOnlyList<SessionCookie> Load(NotebookLmConfig config)
    {
        if (config.AuthJson is not null)
        {
            return ParseStorage(config.AuthJson);
        }

        if (config.CookieHeader is not null)
        {
            return ParseCookieHeader(config.CookieHeader);
        }

        if (!File.Exists(config.StoragePath))
        {
            throw new NotebookLmException(
                "Sessão não encontrada. Rode `mcp-notebooklm login` e entre com a conta Google corporativa.");
        }

        return ParseStorage(File.ReadAllText(config.StoragePath));
    }

    public static void Save(NotebookLmConfig config, IReadOnlyList<SessionCookie> cookies, string baseUrl)
    {
        Directory.CreateDirectory(config.ProfileDirectory);
        var payload = new
        {
            cookies = cookies.Select(cookie => new
            {
                cookie.Name,
                cookie.Value,
                cookie.Domain,
                cookie.Path,
                expires = -1,
                httpOnly = cookie.HttpOnly,
                secure = cookie.Secure,
                sameSite = "Lax",
            }),
        };

        File.WriteAllText(config.StoragePath, JsonSerializer.Serialize(payload, JsonOptions));
        File.WriteAllText(
            config.HostPath,
            JsonSerializer.Serialize(new { baseUrl }, JsonOptions));
    }

    public static IReadOnlyList<SessionCookie> ParseStorage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("cookies", out var cookiesElement))
        {
            root = cookiesElement;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new NotebookLmException("Arquivo de sessão inválido: esperado storage_state com a lista cookies.");
        }

        var cookies = new List<SessionCookie>();
        foreach (var item in root.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var value = ReadString(item, "value");
            if (string.IsNullOrEmpty(name) || value is null)
            {
                continue;
            }

            var domain = ReadString(item, "domain") ?? ".google.com";
            var path = ReadString(item, "path") ?? "/";
            var secure = !item.TryGetProperty("secure", out var secureElement) || secureElement.ValueKind != JsonValueKind.False;
            var httpOnly = item.TryGetProperty("httpOnly", out var httpOnlyElement) && httpOnlyElement.ValueKind == JsonValueKind.True;
            cookies.Add(new SessionCookie(name, value, domain, path, secure, httpOnly));
        }

        if (cookies.Count == 0)
        {
            throw new NotebookLmException("A sessão não contém cookies do Google.");
        }

        return cookies;
    }

    public static IReadOnlyList<SessionCookie> ParseCookieHeader(string header)
    {
        var cookies = new List<SessionCookie>();
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            cookies.Add(new SessionCookie(name, value, ".google.com", "/", true, true));
        }

        if (cookies.Count == 0)
        {
            throw new NotebookLmException("NOTEBOOKLM_COOKIE não tem pares nome=valor.");
        }

        return cookies;
    }

    public static string BuildCookieHeader(IReadOnlyList<SessionCookie> cookies, Uri uri)
    {
        var pairs = new List<string>();
        foreach (var cookie in cookies)
        {
            if (cookie.Secure && uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            if (!DomainMatches(cookie.Domain, uri.Host) || !PathMatches(cookie.Path, uri.AbsolutePath))
            {
                continue;
            }

            pairs.Add($"{cookie.Name}={cookie.Value}");
        }

        return string.Join("; ", pairs);
    }

    public static bool HasRequiredCookies(IReadOnlyList<SessionCookie> cookies)
    {
        var names = cookies.Select(cookie => cookie.Name).ToHashSet(StringComparer.Ordinal);
        return names.Contains("SID") && names.Contains("__Secure-1PSIDTS");
    }

    private static bool DomainMatches(string domain, string host)
    {
        var normalized = domain.Trim().TrimStart('.').ToLowerInvariant();
        host = host.ToLowerInvariant();
        return host.Equals(normalized, StringComparison.Ordinal) ||
               host.EndsWith("." + normalized, StringComparison.Ordinal);
    }

    private static bool PathMatches(string path, string requestPath)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return true;
        }

        return requestPath.StartsWith(path, StringComparison.Ordinal);
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}
