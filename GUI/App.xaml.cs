using GUI.Views;
using ServiceLibrary;
using System.Windows;
using System.Windows.Threading;

namespace GUI;

public partial class App : Application
{
    private const string ShowEventName = @"Local\JarvisAssistant.Show";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;

    /// <summary>True quando o Windows abriu o app junto com o login (ele começa escondido na bandeja).</summary>
    public static bool LaunchedAtStartup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new(true, @"Local\JarvisAssistant.SingleInstance", out bool first);
        if (!first)
        {
            // Já tem um J.A.R.V.I.S rodando (talvez escondido na bandeja): pede para ele aparecer.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch { /* o outro ainda está iniciando */ }
            Shutdown();
            return;
        }

        LaunchedAtStartup = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        ListenForShowRequests(window);
    }

    /// <summary>Quando alguém abre o programa de novo, mostra a janela que já existe.</summary>
    private void ListenForShowRequests(MainWindow window)
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var signal = _showSignal;
        new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                    Dispatcher.BeginInvoke(window.ShowFromTray);
            }
            catch (ObjectDisposedException) { /* app fechando */ }
        })
        { IsBackground = true, Name = "JarvisShowSignal" }.Start();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Erro não tratado", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
