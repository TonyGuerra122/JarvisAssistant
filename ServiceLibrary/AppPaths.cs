namespace ServiceLibrary;

public static class AppPaths
{
    private const string AppFolder = "JarvisAssistant";

    /// <summary>Nome antigo (com erro de grafia) das pastas de dados; migrado na primeira execução.</summary>
    private const string LegacyAppFolder = "JarvisAssistent";

    /// <summary>%APPDATA%\JarvisAssistant — configurações (config.json). Mantido ao desinstalar.</summary>
    public static string Roaming { get; } = Ensure(Environment.SpecialFolder.ApplicationData);

    /// <summary>%LOCALAPPDATA%\JarvisAssistant — cache do WebView2 e logs.</summary>
    public static string LocalData { get; } = Ensure(Environment.SpecialFolder.LocalApplicationData);

    public static string ConfigFile => Path.Combine(Roaming, "config.json");
    public static string LogFile => Path.Combine(LocalData, "jarvis.log");
    public static string OpenJarvisLogFile => Path.Combine(LocalData, "openjarvis-serve.log");

    private static string Ensure(Environment.SpecialFolder root)
    {
        var baseDir = Environment.GetFolderPath(root);
        var dir = Path.Combine(baseDir, AppFolder);
        var legacy = Path.Combine(baseDir, LegacyAppFolder);

        // Leva os dados da pasta com o nome antigo para a nova, uma única vez.
        if (!Directory.Exists(dir) && Directory.Exists(legacy))
        {
            try { Directory.Move(legacy, dir); }
            catch { /* pasta em uso: começa com uma pasta nova */ }
        }

        Directory.CreateDirectory(dir);
        return dir;
    }
}
