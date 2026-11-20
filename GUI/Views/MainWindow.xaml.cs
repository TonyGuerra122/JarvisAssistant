using GUI.Tray;
using GUI.Updates;
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
    private UpdateService? _updates;
    private TrayIcon? _tray;
    private bool _closing;
    private bool _exiting;          // true = o usuário pediu para sair de verdade (e não só fechar a janela)
    private bool _startHidden;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        _config = AppConfig.Load();
        _startHidden = App.LaunchedAtStartup && _config.RunInBackground;
        if (_startHidden)
        {
            // Aberto pelo Windows no login: nasce minimizado e some na bandeja assim que o HUD carregar.
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
        }

        _tray = new TrayIcon(
            show: ShowFromTray,
            isListening: () => _assistant?.AlwaysListening ?? _config.AlwaysListen,
            setListening: on => Dispatcher.Invoke(() => _assistant?.SetAlwaysListening(on)),
            startWithWindows: () => _updates?.CurrentVersion is null ? null : _config.StartWithWindows,
            setStartWithWindows: SetStartWithWindows,
            exit: () => Dispatcher.Invoke(Exit));

        Application.Current.SessionEnding += (_, _) => _exiting = true; // não segura o desligamento do Windows
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
            if (_startHidden) ShowFromTray(); // sem HUD não adianta esconder: mostra o aviso de erro
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
        _assistant.QuitRequested += () => Dispatcher.InvokeAsync(Exit);
        _updates = new UpdateService(_config, PostToHud);
        _updates.ReadyToInstall += OnUpdateReady;
        core.WebMessageReceived += OnWebMessage;
        core.Navigate("https://jarvis.local/index.html");

        ApplyStartupSetting();
        if (_startHidden) HideToTray();
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
                _updates?.Start();
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
            case "confirmReply":
                var id = msg["id"]?.GetValue<int>() ?? -1;
                var ok = msg["ok"]?.GetValue<bool>() ?? false;
                _assistant.ResolveConfirm(id, ok);
                break;
            case "toggleListen":
                _assistant.SetAlwaysListening(!_assistant.AlwaysListening);
                break;
            case "applyUpdate":
                // Instala e reabre: a janela fecha pelo caminho normal, para encerrar os serviços filhos.
                if (_updates?.ApplyAndRestart(hidden: !IsVisible) == true) Exit();
                break;
            case "minimize":
                if (_config.RunInBackground) HideToTray();
                else WindowState = WindowState.Minimized;
                break;
            case "exit":
                Exit();
                break;
        }
    }

    // ------------------------------------------------------------------ segundo plano

    /// <summary>Esconde a janela; o Jarvis continua rodando (e ouvindo) pela bandeja.</summary>
    private void HideToTray()
    {
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray?.Balloon("J.A.R.V.I.S continua rodando",
            _assistant?.AlwaysListening == true
                ? $"Diga \"{_config.WakeWord}\" ou clique no ícone. Para sair, use o menu do ícone."
                : "Clique no ícone para abrir. Para sair, use o menu do ícone.");
    }

    /// <summary>Traz a janela de volta (clique no ícone, segunda abertura do programa).</summary>
    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Maximized;
        Activate();
    }

    /// <summary>Encerra de verdade (fechar a janela só esconde, quando RunInBackground está ligado).</summary>
    private void Exit()
    {
        _exiting = true;
        Close();
    }

    private void SetStartWithWindows(bool on)
    {
        _config.StartWithWindows = on;
        _config.Save();
        ApplyStartupSetting();
    }

    /// <summary>Registra (ou remove) a abertura com o Windows. Só no app instalado: no dotnet run não mexe em nada.</summary>
    private void ApplyStartupSetting()
    {
        if (_updates?.CurrentVersion is null) return;
        try { StartupRegistration.Set(_config.StartWithWindows); }
        catch (Exception ex) { Log.Error("Não consegui atualizar a abertura com o Windows", ex); }
    }

    /// <summary>
    /// Atualização baixada. Com a janela visível, o HUD mostra o botão. Em segundo plano ninguém veria,
    /// então instala sozinho assim que o Jarvis estiver quieto (e reabre escondido).
    /// </summary>
    private void OnUpdateReady()
    {
        _ = Task.Run(async () =>
        {
            while (!_exiting)
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                var idle = await Dispatcher.InvokeAsync(() => !IsVisible && (_assistant?.IsIdle ?? true));
                if (!idle) continue;

                var ok = await Dispatcher.InvokeAsync(() => _updates?.ApplyAndRestart(hidden: true) == true);
                if (ok) await Dispatcher.InvokeAsync(Exit);
                return;
            }
        });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _config.RunInBackground)
        {
            e.Cancel = true; // fechar a janela (Alt+F4) só esconde
            HideToTray();
            return;
        }

        _closing = true;
        _updates?.ApplyOnExit();
        _updates?.Dispose();
        _assistant?.Dispose();
        _tray?.Dispose();
    }
}
