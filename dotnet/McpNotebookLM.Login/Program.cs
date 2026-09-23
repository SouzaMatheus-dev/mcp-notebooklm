using McpNotebookLM.Services;

namespace McpNotebookLM.Login;

internal static class Program
{
    [STAThread]
    private static int Main() => BrowserLogin.Run(new NotebookLmConfig());
}
