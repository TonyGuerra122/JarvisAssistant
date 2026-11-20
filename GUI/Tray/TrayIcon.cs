using System.Windows;
using Forms = System.Windows.Forms;

namespace GUI.Tray;

/// <summary>Ícone na bandeja do Windows: é por onde o Jarvis é aberto, configurado e encerrado quando roda em segundo plano.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _listen;
    private readonly Forms.ToolStripMenuItem _startup;

    public TrayIcon(
        Action show,
        Func<bool> isListening, Action<bool> setListening,
        Func<bool?> startWithWindows, Action<bool> setStartWithWindows,
        Action exit)
    {
        var open = new Forms.ToolStripMenuItem("Abrir J.A.R.V.I.S") { Font = new System.Drawing.Font(Forms.SystemInformation.MenuFont, System.Drawing.FontStyle.Bold) };
        open.Click += (_, _) => show();

        _listen = new Forms.ToolStripMenuItem("Microfone sempre aberto") { CheckOnClick = false };
        _listen.Click += (_, _) => setListening(!isListening());

        _startup = new Forms.ToolStripMenuItem("Iniciar com o Windows");
        _startup.Click += (_, _) => setStartWithWindows(startWithWindows() != true);

        var quit = new Forms.ToolStripMenuItem("Sair");
        quit.Click += (_, _) => exit();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([open, new Forms.ToolStripSeparator(), _listen, _startup, new Forms.ToolStripSeparator(), quit]);

        // Marca/desmarca os itens toda vez que o menu abre.
        menu.Opening += (_, _) =>
        {
            _listen.Checked = isListening();
            var startup = startWithWindows();
            _startup.Enabled = startup is not null; // null = rodando fora do instalador (dev)
            _startup.Checked = startup == true;
        };

        _icon = new Forms.NotifyIcon
        {
            Text = "J.A.R.V.I.S",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
    }

    public void Balloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/jarvis.ico"))?.Stream;
            if (stream is not null) return new System.Drawing.Icon(stream);
        }
        catch { /* usa o ícone padrão abaixo */ }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false; // sem isto o ícone fica "fantasma" na bandeja até passar o mouse
        _icon.Dispose();
    }
}
