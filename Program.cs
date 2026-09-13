namespace ShowTextOnly;

static class Program
{
    /// <summary>
    /// Opens the text window and returns when it is closed.
    /// </summary>
    /// <param name="arguments">Command line arguments: paths of text files to open together, concatenated in order.</param>
    [STAThread]
    static void Main(string[] arguments)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TextWindow(arguments));
    }
}
