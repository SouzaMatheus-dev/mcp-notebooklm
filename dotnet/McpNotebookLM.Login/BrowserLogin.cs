using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace McpNotebookLM.Services;

internal static class BrowserLogin
{
    public static int Run(NotebookLmConfig config)
    {
        ApplicationConfiguration.Initialize();
        using var form = new LoginForm(config);
        Application.Run(form);
        return form.Saved ? 0 : 1;
    }

    private sealed class LoginForm : Form
    {
        private readonly NotebookLmConfig _config;
        private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
        private readonly Button _save = new() { Text = "Salvar sessão", Enabled = false, AutoSize = true };
        private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        public bool Saved { get; private set; }

        public LoginForm(NotebookLmConfig config)
        {
            _config = config;
            Text = "NotebookLM — login corporativo";
            Width = 1100;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            _save.Dock = DockStyle.Right;
            _save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
            bar.Controls.Add(_status);
            bar.Controls.Add(_save);
            Controls.Add(_web);
            Controls.Add(bar);
            Shown += async (_, _) => await StartAsync().ConfigureAwait(true);
        }

        private async Task StartAsync()
        {
            try
            {
                Directory.CreateDirectory(_config.ProfileDirectory);
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: Path.Combine(_config.ProfileDirectory, "webview2")).ConfigureAwait(true);
                await _web.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
                _web.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    var host = _web.Source?.Host ?? "";
                    var ready = NotebookLmConfig.IsNotebookHost(host);
                    _save.Enabled = ready;
                    _status.Text = ready
                        ? $"NotebookLM aberto em {host}. Clique em Salvar sessão."
                        : $"Conclua o login corporativo. Host atual: {host}";
                    if (!args.IsSuccess && string.IsNullOrEmpty(host))
                    {
                        _status.Text = "Falha ao navegar. Verifique a rede corporativa.";
                    }
                };
                _web.Source = new Uri(_config.BaseUrl + "/");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Não foi possível abrir o WebView2 (Edge). Instale o runtime Evergreen: " +
                    "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" + ex.Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Close();
            }
        }

        private async Task SaveAsync()
        {
            if (_web.CoreWebView2 is null)
            {
                return;
            }

            _save.Enabled = false;
            try
            {
                var manager = _web.CoreWebView2.CookieManager;
                var urls = new[]
                {
                    "https://notebook.google.com/",
                    "https://notebooklm.google.com/",
                    "https://accounts.google.com/",
                    "https://www.google.com/",
                };
                var merged = new Dictionary<string, SessionCookie>(StringComparer.Ordinal);
                foreach (var url in urls)
                {
                    var cookies = await manager.GetCookiesAsync(url).ConfigureAwait(true);
                    foreach (var cookie in cookies)
                    {
                        var key = cookie.Name + "|" + cookie.Domain + "|" + cookie.Path;
                        merged[key] = new SessionCookie(
                            cookie.Name,
                            cookie.Value,
                            cookie.Domain,
                            cookie.Path,
                            cookie.IsSecure,
                            cookie.IsHttpOnly);
                    }
                }

                if (merged.Count == 0)
                {
                    throw new NotebookLmException("Nenhum cookie do Google foi encontrado nessa janela.");
                }

                var host = _web.Source?.Host;
                var baseUrl = NotebookLmConfig.IsNotebookHost(host ?? "")
                    ? "https://" + host
                    : _config.BaseUrl;
                SessionStore.Save(_config, merged.Values.ToList(), baseUrl);
                Saved = true;
                var required = SessionStore.HasRequiredCookies(merged.Values.ToList());
                MessageBox.Show(
                    this,
                    $"Sessão salva em {_config.StoragePath}\nCookies: {merged.Count}. " +
                    (required
                        ? "A sessão do NotebookLM está completa."
                        : "Faltam SID ou __Secure-1PSIDTS. Confirme que o NotebookLM carregou antes de salvar."),
                    Text,
                    MessageBoxButtons.OK,
                    required ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                _save.Enabled = true;
            }
        }
    }
}
