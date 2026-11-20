using Microsoft.Web.WebView2.Core;
using ServiceLibrary;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace GUI.Views;

/// <summary>
/// Classe inicial de execução do Programa
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppConfig _config;
    private Assistant? _assistant;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        _config = AppConfig.Load();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Os dados do WebView2 não podem ficar em Arquivos de Programas
            // (sem permissão de escrita), então vão para %LOCALAPPDATA%\JarvisAssistant.
            var userData = Path.Combine(AppPaths.LocalData, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 2, 6, 17);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 indisponível", ex);
            Web.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Visible;
            Fallback.Text =
                "Não consegui iniciar o WebView2.\n\n" +
                "Instale o \"Microsoft Edge WebView2 Runtime\" (winget install Microsoft.EdgeWebView2Runtime) " +
                "e abra o J.A.R.V.I.S de novo.\n\nPressione Alt+F4 para sair.";
            return;
        }

        var core = Web.CoreWebView2;
        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        core.SetVirtualHostNameToFolderMapping("jarvis.local", webDir, CoreWebView2HostResourceAccessKind.Deny);

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = _config.Debug;
        core.Settings.AreDevToolsEnabled = _config.Debug;

        _assistant = new Assistant(_config, PostToHud);
        core.WebMessageReceived += OnWebMessage;
        core.Navigate("https://jarvis.local/index.html");
    }

    /// <summary>Envia uma mensagem para o JavaScript do HUD (thread-safe).</summary>
    private void PostToHud(object message)
    {
        if (_closing) return;
        var json = JsonSerializer.Serialize(message, JsonOpts);
        Dispatcher.InvokeAsync(() =>
        {
            try { Web.CoreWebView2?.PostWebMessageAsJson(json); }
            catch (Exception ex) { Log.Error("Falha ao enviar mensagem ao HUD", ex); }
        });
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(e.WebMessageAsJson); }
        catch { return; }
        if (msg is null || _assistant is null) return;

        var type = msg["type"]?.GetValue<string>();
        switch (type)
        {
            case "ready":
                _ = _assistant.BootAsync();
                break;
            case "ptt": // push-to-talk (segurar espaço)
                var down = msg["down"]?.GetValue<bool>() ?? false;
                if (down) _assistant.StartListening();
                else _ = _assistant.StopListeningAsync();
                break;
            case "text":
                var text = msg["text"]?.GetValue<string>() ?? "";
                _ = _assistant.HandleTextAsync(text);
                break;
            case "weather":
                _assistant.WeatherSummary = msg["summary"]?.GetValue<string>();
                break;
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "exit":
                Close();
                break;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        _assistant?.Dispose();
    }
}