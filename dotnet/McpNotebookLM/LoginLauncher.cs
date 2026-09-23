using System.Diagnostics;

namespace McpNotebookLM;

internal static class LoginLauncher
{
    public static int Run()
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "McpNotebookLM.Login.exe");
        if (!File.Exists(helper))
        {
            Console.Error.WriteLine(
                "O login gráfico não está ao lado deste comando. No Windows, gere o pacote de novo " +
                "ou abra https://notebook.google.com/, entre com a conta corporativa e defina " +
                "NOTEBOOKLM_STORAGE_STATE ou NOTEBOOKLM_COOKIE.");
            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("O login gráfico exige Windows. Use NOTEBOOKLM_STORAGE_STATE.");
            return 1;
        }

        using var process = Process.Start(new ProcessStartInfo(helper) { UseShellExecute = false });
        if (process is null)
        {
            Console.Error.WriteLine("Não foi possível abrir a janela de login.");
            return 1;
        }

        process.WaitForExit();
        Console.Error.WriteLine(process.ExitCode == 0
            ? "Sessão salva. Os cookies ficam só no seu perfil de usuário."
            : "Login cancelado. Nenhuma sessão nova foi salva.");
        return process.ExitCode;
    }
}
