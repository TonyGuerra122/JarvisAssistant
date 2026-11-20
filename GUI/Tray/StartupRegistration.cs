using Microsoft.Win32;

namespace GUI.Tray;

/// <summary>Abrir junto com o Windows: uma entrada em HKCU\...\Run (não precisa de administrador).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JarvisAssistant";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    /// <summary>Liga ou desliga. O "--startup" faz o app abrir escondido na bandeja.</summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enabled && Environment.ProcessPath is { Length: > 0 } exe)
            key.SetValue(ValueName, $"\"{exe}\" --startup");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
