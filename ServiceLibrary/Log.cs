namespace ServiceLibrary;

public static class Log
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERRO", ex is null ? message : $"{message}: {ex}");


    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var file = AppPaths.LogFile;

                // Evita que o log cresça sem limite.
                if (File.Exists(file) && new FileInfo(file).Length > 2_000_000)
                {
                    File.Delete(file);
                }

                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Log é best-effort.
        }
    }
}
