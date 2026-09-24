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

        if (args.Length > 0 && string.Equals(args[0], "publicar", StringComparison.OrdinalIgnoreCase))
        {
            return await PublishCommand.Run(args).ConfigureAwait(false);
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
                    "As tools se chamam status_autenticacao, listar_notebooks, criar_notebook, listar_fontes, " +
                    "adicionar_documento_texto, adicionar_documento_arquivo, adicionar_documento_url, " +
                    "atualizar_fonte e publicar_documentacao. " +
                    "Para um manual: publicar_documentacao com pasta de .md ou textos em JSON. " +
                    "Se o notebook já existe, passe notebookId para acrescentar fontes. " +
                    "Uma pasta grande sai em lotes de 8. Repita com o notebookId, ou use `mcp-notebooklm publicar --pasta`. " +
                    "Título com o mesmo conteúdo é pulado; conteúdo diferente é trocado. " +
                    "Para revisar uma RFC ou ADR, use atualizar_fonte ou publicar_documentacao com notebookId. " +
                    "Falha de negócio ou HTTP volta com isError=true. Este servidor não apaga notebooks.";
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }
}
