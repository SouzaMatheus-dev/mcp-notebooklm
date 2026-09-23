namespace McpNotebookLM.Services;

public sealed class NotebookLmException : Exception
{
    public NotebookLmException(string message) : base(message)
    {
    }
}
