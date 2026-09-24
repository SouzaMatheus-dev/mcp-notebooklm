using ModelContextProtocol.Protocol;
using McpNotebookLM.Services;
using McpNotebookLM.Tools;

namespace McpNotebookLM;

internal static class PublishCommand
{
    public static async Task<int> Run(string[] args)
    {
        string? pasta = null;
        string? notebook = null;
        string? titulo = null;
        var lote = 8;
        try
        {
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                switch (key)
                {
                    case "--pasta":
                        pasta = Next(args, ref i, key);
                        break;
                    case "--notebook":
                        notebook = Next(args, ref i, key);
                        break;
                    case "--titulo":
                        titulo = Next(args, ref i, key);
                        break;
                    case "--lote":
                        if (!int.TryParse(Next(args, ref i, key), out lote))
                        {
                            throw new NotebookLmException("lote precisa ser um número.");
                        }

                        break;
                    default:
                        throw new NotebookLmException(Usage);
                }
            }

            if (string.IsNullOrWhiteSpace(pasta))
            {
                throw new NotebookLmException(Usage);
            }

            using var client = new NotebookLmClient(new NotebookLmConfig());
            var tools = new NotebookLmTools(client);
            var id = notebook ?? "";
            for (var step = 0; step < 40; step++)
            {
                var result = await tools.PublicarDocumentacao(
                    titulo: titulo ?? "",
                    notebookId: id,
                    pasta: pasta,
                    lote: lote).ConfigureAwait(false);
                var text = string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(block => block.Text));
                Console.WriteLine(text);
                if (result.IsError == true)
                {
                    return 1;
                }

                if (text.Contains("concluída", StringComparison.Ordinal))
                {
                    return 0;
                }

                var marker = "notebookId=";
                var at = text.LastIndexOf(marker, StringComparison.Ordinal);
                if (at < 0)
                {
                    Console.Error.WriteLine("A publicação não devolveu notebookId para o próximo lote.");
                    return 1;
                }

                id = text[(at + marker.Length)..].Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('.');
            }

            Console.Error.WriteLine("A publicação passou de 40 lotes.");
            return 1;
        }
        catch (NotebookLmException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private const string Usage =
        "Uso: mcp-notebooklm publicar --pasta <diretorio> [--notebook <id>] [--titulo <nome>] [--lote 8]";

    private static string Next(string[] args, ref int index, string key)
    {
        if (index + 1 >= args.Length)
        {
            throw new NotebookLmException($"Falta o valor de {key}. {Usage}");
        }

        index++;
        return args[index];
    }
}
