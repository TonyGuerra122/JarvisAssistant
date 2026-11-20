using Velopack;

namespace GUI;

/// <summary>
/// Ponto de entrada. O Velopack precisa rodar ANTES de tudo: é ele que trata os argumentos do
/// instalador/atualizador (criar atalhos, desinstalar...) e encerra o processo quando for o caso.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => GUI.Tray.StartupRegistration.Set(false)) // não deixa a entrada "abrir com o Windows" órfã
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
