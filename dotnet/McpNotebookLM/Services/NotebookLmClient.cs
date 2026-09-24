using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpNotebookLM.Services;

public sealed class NotebookLmClient : IDisposable
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".markdown", ".csv", ".html", ".htm", ".docx", ".epub",
    };

    private readonly NotebookLmConfig _config;
    private readonly HttpClient _http;
    private IReadOnlyList<SessionCookie>? _cookies;
    private string? _csrf;
    private string? _sessionId;

    public NotebookLmClient(NotebookLmConfig config)
    {
        _config = config;
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(_config.Language);
    }

    public string BaseUrl => _config.BaseUrl;

    public async Task<string> StatusAsync(CancellationToken cancellationToken)
    {
        var cookies = SessionStore.Load(_config);
        var required = SessionStore.HasRequiredCookies(cookies);
        await EnsureSessionAsync(cancellationToken, force: true).ConfigureAwait(false);
        return
            $"Autenticado em {_config.BaseUrl}. Cookies carregados: {cookies.Count}. " +
            $"Cookies de sessão do NotebookLM: {(required ? "presentes" : "incompletos (SID ou __Secure-1PSIDTS ausente)")}. " +
            "Token CSRF obtido. Valores de cookie não são exibidos.";
    }

    public async Task<IReadOnlyList<NotebookInfo>> ListNotebooksAsync(CancellationToken cancellationToken)
    {
        var payload = await CallAsync(RpcCodec.ListNotebooks, RpcCodec.ListParams(), "/", cancellationToken)
            .ConfigureAwait(false);
        return ResponseParser.Notebooks(payload);
    }

    public async Task<NotebookInfo> CreateNotebookAsync(string title, CancellationToken cancellationToken)
    {
        var known = (await ListNotebooksAsync(cancellationToken).ConfigureAwait(false))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var payload = await CallAsync(
            RpcCodec.CreateNotebook,
            RpcCodec.CreateParams(title),
            "/",
            cancellationToken).ConfigureAwait(false);
        return ResponseParser.SelectCreated(
            title,
            known,
            ResponseParser.Notebooks(payload),
            ResponseParser.FirstId(payload),
            ResponseParser.Shape(payload));
    }

    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(string notebookId, CancellationToken cancellationToken)
    {
        var payload = await CallAsync(
            RpcCodec.GetNotebook,
            RpcCodec.GetParams(notebookId),
            NotebookPath(notebookId),
            cancellationToken).ConfigureAwait(false);
        return ResponseParser.Sources(payload);
    }

    public async Task<IReadOnlyList<string>> WaitUntilReadyAsync(
        string notebookId,
        IReadOnlyCollection<string> sourceIds,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pending = sourceIds.Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        var deadline = DateTime.UtcNow + timeout;
        while (pending.Count > 0)
        {
            var sources = await ListSourcesAsync(notebookId, cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                if (!pending.Contains(source.Id))
                {
                    continue;
                }

                if (source.Status == 3)
                {
                    throw new NotebookLmException(
                        $"A fonte {source.Id} ({source.Title}) falhou na indexação.");
                }

                if (source.Status is null or 2)
                {
                    pending.Remove(source.Id);
                }
            }

            if (pending.Count == 0 || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }

        return pending.ToList();
    }

    public async Task<IReadOnlyList<SourceInfo>> AddTextAsync(
        string notebookId,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        if (content.Length > _config.MaxTextChars)
        {
            throw new NotebookLmException(
                $"Texto acima do limite de {_config.MaxTextChars} caracteres.");
        }

        var payload = await CallAsync(
            RpcCodec.AddSource,
            RpcCodec.AddTextParams(notebookId, title, content),
            NotebookPath(notebookId),
            cancellationToken).ConfigureAwait(false);
        var sources = ResponseParser.Sources(payload);
        return sources.Count > 0 ? sources : [new SourceInfo(ResponseParser.FirstId(payload) ?? "", title)];
    }

    public async Task<IReadOnlyList<SourceInfo>> AddUrlAsync(
        string notebookId,
        string url,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new NotebookLmException("Informe uma URL http(s) absoluta.");
        }

        var payload = await CallAsync(
            RpcCodec.AddSource,
            RpcCodec.AddUrlParams(notebookId, url, RpcCodec.IsYouTube(url)),
            NotebookPath(notebookId),
            cancellationToken).ConfigureAwait(false);
        var sources = ResponseParser.Sources(payload);
        return sources.Count > 0 ? sources : [new SourceInfo(ResponseParser.FirstId(payload) ?? "", url)];
    }

    public async Task<SourceInfo> AddFileAsync(
        string notebookId,
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new NotebookLmException($"Arquivo não encontrado: {fullPath}");
        }

        var extension = Path.GetExtension(fullPath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new NotebookLmException(
                $"Extensão {extension} não aceita. Use: {string.Join(", ", AllowedExtensions.Order())}.");
        }

        var info = new FileInfo(fullPath);
        if (info.Length > _config.MaxFileBytes)
        {
            throw new NotebookLmException(
                $"Arquivo acima do limite de {_config.MaxFileBytes} bytes.");
        }

        var fileName = Path.GetFileName(fullPath);
        var registered = await CallAsync(
            RpcCodec.AddSourceFile,
            RpcCodec.RegisterFileParams(notebookId, fileName),
            NotebookPath(notebookId),
            cancellationToken).ConfigureAwait(false);
        var sourceId = ResponseParser.Sources(registered).FirstOrDefault()?.Id
            ?? ResponseParser.FirstId(registered, notebookId);
        if (string.IsNullOrEmpty(sourceId))
        {
            throw new NotebookLmException(
                "O NotebookLM não devolveu o id da fonte. Forma: " + ResponseParser.Shape(registered));
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await UploadBytesAsync(notebookId, sourceId, fileName, bytes, ContentType(extension), cancellationToken)
            .ConfigureAwait(false);
        return new SourceInfo(sourceId, fileName);
    }

    public async Task DeleteSourceAsync(string notebookId, string sourceId, CancellationToken cancellationToken)
    {
        await CallAsync(
            RpcCodec.DeleteSource,
            RpcCodec.DeleteSourceParams(sourceId),
            NotebookPath(notebookId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameSourceAsync(
        string notebookId,
        string sourceId,
        string title,
        CancellationToken cancellationToken)
    {
        await CallAsync(
            RpcCodec.RenameSource,
            RpcCodec.RenameSourceParams(sourceId, title),
            NotebookPath(notebookId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SourceInfo> ReplaceTextAsync(
        string notebookId,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        var existing = SourceReplacement.RequireSingle(
            await ListSourcesAsync(notebookId, cancellationToken).ConfigureAwait(false),
            title);
        await DeleteSourceAsync(notebookId, existing.Id, cancellationToken).ConfigureAwait(false);
        try
        {
            var added = await AddTextAsync(notebookId, title, content, cancellationToken).ConfigureAwait(false);
            var created = added.FirstOrDefault();
            return new SourceInfo(created?.Id ?? "", title);
        }
        catch (Exception ex) when (ex is NotebookLmException or HttpRequestException or TaskCanceledException or IOException)
        {
            throw new NotebookLmException(
                $"A fonte {existing.Id} (\"{title}\") foi removida, mas o conteúdo novo não entrou no notebook {notebookId}. " +
                "Envie de novo com adicionar_documento_texto. " + ex.Message);
        }
    }

    public async Task<SourceInfo> ReplaceFileAsync(
        string notebookId,
        string path,
        string? title,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.GetFullPath(path)))
        {
            throw new NotebookLmException($"Arquivo não encontrado: {Path.GetFullPath(path)}");
        }

        var fileName = Path.GetFileName(path);
        var targetTitle = string.IsNullOrWhiteSpace(title) ? fileName : title.Trim();
        var existing = SourceReplacement.RequireSingle(
            await ListSourcesAsync(notebookId, cancellationToken).ConfigureAwait(false),
            targetTitle);
        await DeleteSourceAsync(notebookId, existing.Id, cancellationToken).ConfigureAwait(false);
        SourceInfo created;
        try
        {
            created = await AddFileAsync(notebookId, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NotebookLmException or HttpRequestException or TaskCanceledException or IOException)
        {
            throw new NotebookLmException(
                $"A fonte {existing.Id} (\"{targetTitle}\") foi removida, mas o arquivo novo não entrou no notebook {notebookId}. " +
                "Envie de novo com adicionar_documento_arquivo. " + ex.Message);
        }

        if (!created.Title.Equals(targetTitle, StringComparison.Ordinal))
        {
            await RenameSourceAsync(notebookId, created.Id, targetTitle, cancellationToken).ConfigureAwait(false);
            return new SourceInfo(created.Id, targetTitle);
        }

        return created;
    }

    public void Dispose() => _http.Dispose();

    private async Task UploadBytesAsync(
        string notebookId,
        string sourceId,
        string fileName,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        await EnsureSessionAsync(cancellationToken, force: false).ConfigureAwait(false);
        var origin = _config.BaseUrl;
        var startUri = new Uri($"{origin}/upload/_/");
        using var start = new HttpRequestMessage(HttpMethod.Post, startUri);
        ApplyBrowserHeaders(start, startUri);
        start.Headers.TryAddWithoutValidation("x-goog-authuser", _config.AuthUser);
        start.Headers.TryAddWithoutValidation("x-goog-upload-command", "start");
        start.Headers.TryAddWithoutValidation("x-goog-upload-header-content-length", bytes.Length.ToString());
        start.Headers.TryAddWithoutValidation("x-goog-upload-header-content-type", contentType);
        start.Headers.TryAddWithoutValidation("x-goog-upload-protocol", "resumable");
        start.Content = new StringContent(
            $"PROJECT_ID={Uri.EscapeDataString(notebookId)}&SOURCE_NAME={Uri.EscapeDataString(fileName)}&SOURCE_ID={Uri.EscapeDataString(sourceId)}",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        using var startResponse = await _http.SendAsync(start, cancellationToken).ConfigureAwait(false);
        if (!startResponse.IsSuccessStatusCode)
        {
            throw new NotebookLmException(
                $"Falha ao abrir o upload ({(int)startResponse.StatusCode}).");
        }

        if (!startResponse.Headers.TryGetValues("x-goog-upload-url", out var uploadUrls))
        {
            throw new NotebookLmException("O NotebookLM não devolveu a URL de upload.");
        }

        var uploadUrl = uploadUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(uploadUrl) || !Uri.TryCreate(uploadUrl, UriKind.Absolute, out var uploadUri))
        {
            throw new NotebookLmException("URL de upload inválida.");
        }

        if (!uploadUri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) &&
            !uploadUri.Host.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotebookLmException("A URL de upload não é um host Google.");
        }

        using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUri);
        ApplyBrowserHeaders(upload, uploadUri);
        upload.Headers.TryAddWithoutValidation("x-goog-authuser", _config.AuthUser);
        upload.Headers.TryAddWithoutValidation("x-goog-upload-command", "upload, finalize");
        upload.Headers.TryAddWithoutValidation("x-goog-upload-offset", "0");
        upload.Content = new ByteArrayContent(bytes);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var uploadResponse = await _http.SendAsync(upload, cancellationToken).ConfigureAwait(false);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            throw new NotebookLmException(
                $"Falha ao enviar o arquivo ({(int)uploadResponse.StatusCode}).");
        }
    }

    private async Task<JsonNode?> CallAsync(
        string rpcId,
        JsonArray parameters,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await EnsureSessionAsync(cancellationToken, force: false).ConfigureAwait(false);
        var response = await PostRpcAsync(rpcId, parameters, sourcePath, cancellationToken).ConfigureAwait(false);
        if (response.Status is 401 or 403)
        {
            await EnsureSessionAsync(cancellationToken, force: true).ConfigureAwait(false);
            response = await PostRpcAsync(rpcId, parameters, sourcePath, cancellationToken).ConfigureAwait(false);
        }

        if (response.Status is < 200 or >= 300)
        {
            throw new NotebookLmException($"NotebookLM respondeu HTTP {response.Status} em {rpcId}.");
        }

        return RpcCodec.Decode(response.Body, rpcId);
    }

    private async Task<RpcHttpResult> PostRpcAsync(
        string rpcId,
        JsonArray parameters,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["rpcids"] = rpcId,
            ["source-path"] = sourcePath,
            ["f.sid"] = _sessionId ?? "",
            ["hl"] = _config.Language,
            ["rt"] = "c",
            ["authuser"] = _config.AuthUser,
        };
        var url = _config.BaseUrl + "/_/LabsTailwindUi/data/batchexecute?" +
                  string.Join("&", query.Select(pair =>
                      $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var uri = new Uri(url);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        ApplyBrowserHeaders(request, uri);
        request.Content = new StringContent(
            RpcCodec.BuildBody(rpcId, parameters, _csrf ?? ""),
            Encoding.UTF8,
            "application/x-www-form-urlencoded");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new RpcHttpResult((int)response.StatusCode, body);
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken, bool force)
    {
        if (!force && _csrf is not null && _sessionId is not null && _cookies is not null)
        {
            return;
        }

        _cookies = SessionStore.Load(_config);
        var home = new Uri(_config.BaseUrl + "/");
        using var request = new HttpRequestMessage(HttpMethod.Get, home);
        ApplyBrowserHeaders(request, home);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var finalHost = response.RequestMessage?.RequestUri?.Host ?? "";
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!NotebookLmConfig.IsNotebookHost(finalHost))
        {
            throw new NotebookLmException(
                "A sessão não abriu o NotebookLM. Rode `mcp-notebooklm login` com a conta corporativa.");
        }

        var csrf = RpcCodec.ExtractWiz(html, "SNlM0e");
        var session = RpcCodec.ExtractWiz(html, "FdrFJe");
        if (string.IsNullOrEmpty(csrf) || string.IsNullOrEmpty(session))
        {
            throw new NotebookLmException(
                "Não foi possível ler o token da página do NotebookLM. Rode `mcp-notebooklm login` de novo.");
        }

        _csrf = csrf;
        _sessionId = session;
    }

    private void ApplyBrowserHeaders(HttpRequestMessage request, Uri uri)
    {
        var origin = _config.BaseUrl;
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Referer", origin + "/");
        request.Headers.TryAddWithoutValidation("X-Same-Domain", "1");
        if (_cookies is not null)
        {
            var cookie = SessionStore.BuildCookieHeader(_cookies, uri);
            if (cookie.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
        }
    }

    private static string NotebookPath(string notebookId) => "/notebook/" + notebookId;

    private sealed record RpcHttpResult(int Status, string Body);

    private static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".html" or ".htm" => "text/html",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".epub" => "application/epub+zip",
        _ => "text/plain",
    };
}
