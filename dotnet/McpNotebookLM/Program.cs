using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using McpNotebookLM.Services;
using McpNotebookLM.Tools;

namespace McpNotebookLM;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "login", StringComparison.OrdinalIgnoreCase))
        {
            return LoginLauncher.Run();
        }

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton<NotebookLmConfig>();
        builder.Services.AddSingleton<NotebookLmClient>();
        builder.Services.AddSingleton<NotebookLmTools>();
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInstructions =
                    "Servidor MCP do NotebookLM em notebook.google.com. " +
                    "A autenticação é a sessão Google corporativa gravada por `mcp-notebooklm login`. " +
                    "Nunca peça, repita ou grave cookies, tokens ou NOTEBOOKLM_AUTH_JSON. " +
                    "Para publicar documentação: PublicarDocumentacao, ou CriarNotebook seguido de " +
                    "AdicionarDocumentoArquivo, AdicionarDocumentoTexto e AdicionarDocumentoUrl. " +
                    "Confirme com ListarNotebooks e ListarFontes. Este servidor não apaga notebooks.";
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }
}
