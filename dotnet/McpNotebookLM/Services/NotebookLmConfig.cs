namespace McpNotebookLM.Services;

public sealed class NotebookLmConfig
{
    public const string DefaultBaseUrl = "https://notebook.google.com";

    public string StoragePath => FirstNonEmpty(
        Environment.GetEnvironmentVariable("NOTEBOOKLM_STORAGE_STATE"),
        Path.Combine(ProfileDirectory, "storage_state.json"))!;

    public string HostPath => Path.Combine(Path.GetDirectoryName(StoragePath) ?? ProfileDirectory, "host.json");

    public string ProfileDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mcp-notebooklm");

    public string? AuthJson => BlankToNull(Environment.GetEnvironmentVariable("NOTEBOOKLM_AUTH_JSON"));

    public string? CookieHeader => BlankToNull(Environment.GetEnvironmentVariable("NOTEBOOKLM_COOKIE"));

    public string AuthUser =>
        FirstNonEmpty(Environment.GetEnvironmentVariable("NOTEBOOKLM_AUTHUSER"), "0")!;

    public string Language =>
        FirstNonEmpty(Environment.GetEnvironmentVariable("NOTEBOOKLM_HL"), "pt-BR")!;

    public int MaxTextChars => GetInt("NOTEBOOKLM_MAX_TEXT_CHARS", 450_000);

    public int MaxFileBytes => GetInt("NOTEBOOKLM_MAX_FILE_BYTES", 50 * 1024 * 1024);

    public string BaseUrl
    {
        get
        {
            var fromEnv = NormalizeBase(Environment.GetEnvironmentVariable("NOTEBOOKLM_BASE_URL"));
            if (fromEnv is not null)
            {
                return fromEnv;
            }

            if (File.Exists(HostPath))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(HostPath));
                    if (doc.RootElement.TryGetProperty("baseUrl", out var value))
                    {
                        var saved = NormalizeBase(value.GetString());
                        if (saved is not null)
                        {
                            return saved;
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                }
            }

            return DefaultBaseUrl;
        }
    }

    public static string? NormalizeBase(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (!IsNotebookHost(uri.Host))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static bool IsNotebookHost(string host) =>
        host.Equals("notebook.google.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("notebooklm.google.com", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static int GetInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
