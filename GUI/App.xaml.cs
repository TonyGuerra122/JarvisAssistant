using GUI.Views;
using ServiceLibrary;
using System.Windows;
using System.Windows.Threading;

namespace GUI;

public partial class App : Application
{
    private Mutex? _singleInstance;

    /// <summary>True quando o Windows abriu o app pela pasta Inicializar.</summary>
    public static bool LaunchedAtStartup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new(true, @"Local\JarvisAssistant.SingleInstance", out bool first);
        if (!first)
        {
            // Já tem um J.A.R.V.I.S aberto: não abre outro.
            Shutdown();
            return;
        }

        LaunchedAtStartup = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Erro não tratado", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

}
