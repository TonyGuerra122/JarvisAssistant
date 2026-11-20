using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace ServiceLibrary.Commands;

// Controle do Spotify de desktop SEM a API oficial: título da janela, volume do app (Core Audio),
// UI Automation para clicar no resultado e atalhos de teclado. É "melhor esforço": o Spotify expõe
// pouco da interface, então cada ação diz com honestidade o que conseguiu confirmar.
public sealed partial class CommandRunner
{
    private const string SpotifyProcess = "Spotify";

    private async Task<string> SpotifyAsync(string acao, string consulta, int? nivel, CancellationToken ct) => acao switch
    {
        "tocar" => await SpotifyPlayAsync(consulta, ct),
        "tocando_agora" => SpotifyNowPlaying(),
        "volume" => SpotifyVolume(nivel),
        "aleatorio" => await SpotifyShortcutAsync("^s", "Atalho de modo aleatório enviado (não consigo confirmar o estado).", ct),
        "repetir" => await SpotifyShortcutAsync("^r", "Atalho de repetição enviado (não consigo confirmar o estado).", ct),
        _ => "Ação do Spotify desconhecida.",
    };

    /// <summary>Janela principal do Spotify (o app tem vários processos; só um tem janela).</summary>
    private static (IntPtr Handle, string Title)? SpotifyWindow()
    {
        var all = Process.GetProcessesByName(SpotifyProcess);
        try
        {
            foreach (var p in all)
            {
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) return (p.MainWindowHandle, p.MainWindowTitle);
            }
            return null;
        }
        finally { foreach (var p in all) p.Dispose(); }
    }

    private static string SpotifyNowPlaying()
    {
        var running = Process.GetProcessesByName(SpotifyProcess);
        var isRunning = running.Length > 0;
        foreach (var p in running) p.Dispose();
        if (!isRunning) return "O Spotify não está aberto.";

        var win = SpotifyWindow();
        if (win is null) return "O Spotify está aberto, mas sem janela visível; não consigo ver o que toca.";

        var title = win.Value.Title.Trim();
        // Tocando: "Artista - Música". Pausado ou parado: só "Spotify", "Spotify Free" ou "Spotify Premium".
        if (title.Length == 0 || title.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) || !title.Contains(" - "))
            return "Nada tocando agora (ou está pausado).";
        return $"Tocando agora: {title}.";
    }

    private static string SpotifyVolume(int? nivel)
    {
        if (nivel is null) return "Diga o volume de 0 a 100.";
        var level = Math.Clamp(nivel.Value, 0, 100) / 100f;

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();

        var changed = 0;
        for (int i = 0; i < manager.Sessions.Count; i++)
        {
            var session = manager.Sessions[i];
            try
            {
                using var proc = Process.GetProcessById((int)session.GetProcessID);
                if (!proc.ProcessName.Equals(SpotifyProcess, StringComparison.OrdinalIgnoreCase)) continue;
                session.SimpleAudioVolume.Volume = level;
                changed++;
            }
            catch (ArgumentException) { /* processo já saiu */ }
        }

        return changed > 0
            ? $"Volume do Spotify em {(int)Math.Round(level * 100)}%."
            : "O Spotify ainda não está emitindo som; toque algo primeiro e tente de novo.";
    }

    private static async Task<string> SpotifyShortcutAsync(string keys, string done, CancellationToken ct)
    {
        var win = SpotifyWindow();
        if (win is null) return "O Spotify não está aberto com janela visível.";
        Focus(win.Value.Handle);
        await Task.Delay(400, ct);

        var (code, _, err, _) = await RunPowerShellRawAsync(
            $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait({PsQuote(keys)})", ct, 15);
        return code == 0 ? done : "Falhou: " + Cut(err, 200);
    }

    private static async Task<string> SpotifyPlayAsync(string consulta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consulta)) return "O que tocar?";

        ShellOpen("spotify:search:" + Uri.EscapeDataString(consulta));

        // Espera a janela aparecer (o app pode estar fechado).
        (IntPtr Handle, string Title)? win = null;
        for (int i = 0; i < 24 && win is null; i++)
        {
            await Task.Delay(500, ct);
            win = SpotifyWindow();
        }
        if (win is null) return $"Abri a busca por \"{consulta}\", mas a janela do Spotify não apareceu. Toque o resultado manualmente.";

        Focus(win.Value.Handle);
        await Task.Delay(2000, ct); // a página de busca precisa carregar

        var firstWord = Normalize(consulta.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        var script = $$"""
            Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
            $word = {{PsQuote(firstWord)}}
            $p = Get-Process Spotify -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
            if (-not $p) { 'NOWINDOW'; exit }
            $AE = [System.Windows.Automation.AutomationElement]
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
            $count = 0
            for ($try = 0; $try -lt 8; $try++) {
                $root = $AE::FromHandle($p.MainWindowHandle)
                $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
                $count = $buttons.Count
                $cands = @()
                foreach ($b in $buttons) {
                    $n = [string]$b.Current.Name
                    # Cartões têm nome "Tocar <título>"; o botão do player é só "Tocar"/"Play".
                    if ($n -match '^(Play|Tocar|Reproduzir)\s+\S') {
                        $norm = ($n.Normalize([Text.NormalizationForm]::FormD) -replace '\p{Mn}', '').ToLower()
                        $cands += [pscustomobject]@{ El = $b; Name = $n; Match = $norm.Contains($word) }
                    }
                }
                $pick = ($cands | Where-Object { $_.Match } | Select-Object -First 1)
                if (-not $pick) { $pick = $cands | Select-Object -First 1 }
                if ($pick) {
                    $pick.El.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                    "OK|" + $pick.Name
                    exit
                }
                Start-Sleep -Seconds 1
            }
            "NOTFOUND|$count"
            """;
        var (code, output, err, timedOut) = await RunPowerShellRawAsync(script, ct, 30);
        if (timedOut || code != 0)
            return $"Busca por \"{consulta}\" aberta, mas não consegui clicar no resultado. Toque manualmente. {Cut(err, 120)}".Trim();

        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        if (line.StartsWith("OK|"))
            return $"Cliquei em \"{line[3..]}\" no Spotify. Posso conferir o que está tocando se quiser.";
        return $"Busca por \"{consulta}\" aberta, mas o Spotify não expôs o botão de tocar ao Windows. Toque o resultado manualmente.";
    }

    private static void Focus(IntPtr handle)
    {
        ShowWindow(handle, 9); // SW_RESTORE
        SetForegroundWindow(handle);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
