using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace McpNotebookLM.Services;

internal enum SyncAction
{
    Add,
    Replace,
    Skip,
}

internal sealed record SyncDecision(DocumentationItem Item, string Hash, SyncAction Action);

internal static class SourceSync
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string HashText(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static string Fingerprint(DocumentationItem item) =>
        item.Kind is DocumentationKind.MarkdownFile or DocumentationKind.File
            ? HashFile(item.Payload)
            : HashText(item.Payload);

    public static SyncAction Decide(string title, string hash, IReadOnlyDictionary<string, string> manifest, bool titleExists)
    {
        if (manifest.TryGetValue(title, out var known))
        {
            return string.Equals(known, hash, StringComparison.Ordinal) ? SyncAction.Skip : SyncAction.Replace;
        }

        return titleExists ? SyncAction.Skip : SyncAction.Add;
    }

    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return map is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Save(string path, IReadOnlyDictionary<string, string> manifest)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Json));
    }
}
