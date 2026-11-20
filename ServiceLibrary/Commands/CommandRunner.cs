using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLibrary.Commands;

/// <summary>
/// Ações que o J.A.R.V.I.S pode executar no PC.
/// O modelo só PEDE uma ação (tool call); quem decide e executa é este código,
/// e somente o que está nesta lista. Cada ação segue a política da config
/// (auto / confirmar / bloquear) e as arriscadas sempre pedem confirmação.
/// </summary>
/// <param name="config">Configuração do app.</param>
/// <param name="confirm">Pergunta ao usuário (texto, cancelamento) e devolve se ele autorizou.</param>
/// <param name="notify">Fala um aviso por iniciativa própria (usado pelos temporizadores).</param>
public sealed partial class CommandRunner(AppConfig config, Func<string, CancellationToken, Task<bool>> confirm, Func<string, Task> notify) : IDisposable
{
    private readonly AppConfig _config = config;
    private readonly Func<string, CancellationToken, Task<bool>> _confirm = confirm;
    private readonly Func<string, Task> _notify = notify;

    /// <summary>Ações que SEMPRE confirmam, mesmo se a política disser "auto".</summary>
    private static readonly HashSet<string> AlwaysConfirm = ["energia", "executar_powershell", "fechar_programa"];

    // ------------------------------------------------------------------ política

    private string PolicyOf(string name)
    {
        if (_config.ActionPolicies.TryGetValue(name, out var p))
        {
            p = p.Trim().ToLowerInvariant();
            if (p is "auto" or "confirmar" or "bloquear") return p;
        }
        return AppConfig.DefaultActionPolicies().GetValueOrDefault(name, "auto");
    }

    /// <summary>Ações que fazem a própria confirmação (com texto mais específico) em vez da genérica.</summary>
    private static bool ConfirmsInternally(string name) =>
        AlwaysConfirm.Contains(name) || name == "executar_rotina";

    // ------------------------------------------------------------------ definição das ferramentas

    /// <summary>Lista de ferramentas no formato da OpenAI, enviada junto com cada pergunta.</summary>
    public JsonArray BuildTools()
    {
        var tools = new JsonArray();
        void Add(JsonObject tool, string name)
        {
            if (PolicyOf(name) != "bloquear") tools.Add(tool);
        }

        Add(Tool("abrir_programa", "Abre um programa do computador (os conhecidos ou qualquer um do Menu Iniciar).",
            Props(("nome", Str("Nome do programa. Conhecidos: " + string.Join(", ", _config.Programs.Keys))))), "abrir_programa");
        Add(Tool("abrir_site", "Abre um site no navegador.",
            Props(("url", Str("Endereço completo, começando com https://")))), "abrir_site");
        Add(Tool("pesquisar_web", "Pesquisa algo no Google, abrindo o navegador.",
            Props(("consulta", Str("O que pesquisar")))), "pesquisar_web");
        Add(Tool("controlar_midia", "Controla a música ou o vídeo que está tocando.",
            Props(("acao", Choice("Ação", ["tocar_pausar", "proxima", "anterior"])))), "controlar_midia");
        Add(Tool("volume", "Controla o volume do computador.",
            Props(("acao", Choice("Ação", ["definir", "aumentar", "diminuir", "mutar", "desmutar", "consultar"])),
                  ("nivel", Int("Para 'definir': volume de 0 a 100")),
                  ("passo", Int("Para 'aumentar'/'diminuir': quanto mudar (padrão 10)"))),
            required: ["acao"]), "volume");
        Add(Tool("brilho", "Controla o brilho da tela (só em notebooks e telas compatíveis).",
            Props(("acao", Choice("Ação", ["definir", "aumentar", "diminuir", "consultar"])),
                  ("nivel", Int("Para 'definir': brilho de 0 a 100")),
                  ("passo", Int("Para 'aumentar'/'diminuir': quanto mudar (padrão 10)"))),
            required: ["acao"]), "brilho");
        Add(Tool("bloquear_tela", "Bloqueia o computador (tela de login).", Props()), "bloquear_tela");
        Add(Tool("energia", "Desliga ou reinicia o computador, ou cancela um desligamento agendado.",
            Props(("acao", Choice("Ação", ["desligar", "reiniciar", "cancelar"])),
                  ("minutos", Int("Daqui a quantos minutos. Padrão: 1"))),
            required: ["acao"]), "energia");
        Add(Tool("listar_janelas", "Lista os programas com janela aberta agora.", Props()), "listar_janelas");
        Add(Tool("fechar_programa", "Fecha um programa aberto (pede para ele fechar, sem forçar). O usuário confirma antes.",
            Props(("nome", Str("Nome do programa ou parte do título da janela")))), "fechar_programa");
        Add(Tool("info_sistema", "Informa bateria, uso de processador, memória, disco e rede do computador.",
            Props(("item", Choice("O que consultar", ["tudo", "bateria", "cpu", "memoria", "disco", "rede"]))),
            required: []), "info_sistema");
        Add(Tool("temporizador", "Cria, lista ou cancela temporizadores e lembretes falados.",
            Props(("acao", Choice("Ação", ["criar", "listar", "cancelar"])),
                  ("minutos", Int("Para 'criar': daqui a quantos minutos")),
                  ("mensagem", Str("Para 'criar': o que lembrar (opcional)"))),
            required: ["acao"]), "temporizador");
        Add(Tool("area_transferencia", "Lê ou escreve na área de transferência (copiar/colar).",
            Props(("acao", Choice("Ação", ["ler", "escrever"])),
                  ("texto", Str("Para 'escrever': o texto a copiar"))),
            required: ["acao"]), "area_transferencia");
        Add(Tool("abrir_pasta", "Abre uma pasta ou arquivo (documentos, imagens etc.) das pastas permitidas.",
            Props(("caminho", Str("Caminho completo, ou: downloads, documentos, área de trabalho, imagens, músicas, vídeos")))), "abrir_pasta");
        Add(Tool("buscar_arquivo", "Procura arquivos ou pastas pelo nome nas pastas permitidas.",
            Props(("nome", Str("Parte do nome do arquivo")),
                  ("pasta", Str("Onde procurar (opcional): downloads, documentos, área de trabalho, imagens, músicas, vídeos"))),
            required: ["nome"]), "buscar_arquivo");
        Add(Tool("spotify", "Controla o Spotify do computador: tocar uma música/artista/playlist pela busca, ver o que está tocando, volume só do Spotify, modo aleatório e repetição. Para pausar e trocar de faixa use controlar_midia.",
            Props(("acao", Choice("Ação", ["tocar", "tocando_agora", "volume", "aleatorio", "repetir"])),
                  ("consulta", Str("Para 'tocar': música, artista ou playlist")),
                  ("nivel", Int("Para 'volume': de 0 a 100"))),
            required: ["acao"]), "spotify");
        Add(Tool("buscar_youtube", "Abre a busca de um vídeo no YouTube, no navegador.",
            Props(("consulta", Str("O que procurar")))), "buscar_youtube");
        Add(Tool("captura_tela", "Tira uma captura da tela e salva em Imagens.", Props()), "captura_tela");

        if (_config.Routines.Count > 0)
            Add(Tool("executar_rotina", "Executa uma rotina criada pelo usuário.",
                Props(("nome", Choice("Nome da rotina", _config.Routines.Keys)))), "executar_rotina");

        if (_config.AllowPowerShell)
            Add(Tool("executar_powershell",
                "Executa um comando do PowerShell. Use só quando nenhuma outra ferramenta servir. O usuário confirma antes.",
                Props(("comando", Str("Comando do PowerShell")))), "executar_powershell");

        return tools;
    }

    /// <summary>Descrição curta da ação, para mostrar na legenda.</summary>
    public static string Describe(string name, JsonObject args) => name switch
    {
        "abrir_programa" => $"abrindo {S(args, "nome")}",
        "abrir_site" => $"abrindo {S(args, "url")}",
        "pesquisar_web" => $"pesquisando \"{S(args, "consulta")}\"",
        "controlar_midia" => $"mídia: {S(args, "acao").Replace('_', ' ')}",
        "volume" => $"volume: {S(args, "acao")} {S(args, "nivel")}".TrimEnd(),
        "brilho" => $"brilho: {S(args, "acao")} {S(args, "nivel")}".TrimEnd(),
        "bloquear_tela" => "bloqueando a tela",
        "energia" => $"energia: {S(args, "acao")}",
        "listar_janelas" => "listando janelas abertas",
        "fechar_programa" => $"fechando {S(args, "nome")}",
        "info_sistema" => "consultando o sistema",
        "temporizador" => $"temporizador: {S(args, "acao")}",
        "area_transferencia" => $"área de transferência: {S(args, "acao")}",
        "abrir_pasta" => $"abrindo {S(args, "caminho")}",
        "buscar_arquivo" => $"procurando \"{S(args, "nome")}\"",
        "spotify" => $"Spotify: {S(args, "acao")} {S(args, "consulta")}".TrimEnd(),
        "buscar_youtube" => $"YouTube: \"{S(args, "consulta")}\"",
        "captura_tela" => "capturando a tela",
        "executar_rotina" => $"rotina \"{S(args, "nome")}\"",
        "executar_powershell" => "comando do PowerShell",
        _ => name,
    };

    // ------------------------------------------------------------------ execução

    /// <summary>Executa a ação e devolve um texto curto com o resultado (vai de volta para o modelo).</summary>
    public async Task<string> ExecuteAsync(string name, JsonObject args, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string result;
        try
        {
            Log.Info($"Ação: {name} {args.ToJsonString()}");
            result = await DispatchAsync(name, args, ct);
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Ação {name} cancelada ({sw.ElapsedMilliseconds} ms)");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Ação {name} falhou", ex);
            result = "Falhou: " + ex.Message;
        }
        Log.Info($"Ação {name} → {Cut(result, 200)} ({sw.ElapsedMilliseconds} ms)");
        return result;
    }

    private async Task<string> DispatchAsync(string name, JsonObject args, CancellationToken ct)
    {
        var policy = PolicyOf(name);
        if (policy == "bloquear") return "Esta ação está bloqueada nas configurações do usuário.";
        if (policy == "confirmar" && !ConfirmsInternally(name))
        {
            var allowed = await _confirm($"Executar: {Describe(name, args)}?", ct);
            Log.Info($"Confirmação de {name}: {(allowed ? "autorizada" : "negada")}");
            if (!allowed) return "O usuário não autorizou.";
        }

        return name switch
        {
            "abrir_programa" => await OpenProgramAsync(S(args, "nome"), ct),
            "abrir_site" => OpenSite(S(args, "url")),
            "pesquisar_web" => Search(S(args, "consulta")),
            "controlar_midia" => Media(S(args, "acao")),
            "volume" => Volume(S(args, "acao"), I(args, "nivel"), I(args, "passo")),
            "brilho" => await BrightnessAsync(S(args, "acao"), I(args, "nivel"), I(args, "passo"), ct),
            "bloquear_tela" => LockScreen(),
            "energia" => await PowerAsync(S(args, "acao"), I(args, "minutos") ?? 1, ct),
            "listar_janelas" => ListWindows(),
            "fechar_programa" => await CloseProgramAsync(S(args, "nome"), ct),
            "info_sistema" => await SystemInfoAsync(S(args, "item"), ct),
            "temporizador" => Timer(S(args, "acao"), I(args, "minutos"), S(args, "mensagem")),
            "area_transferencia" => await ClipboardAsync(S(args, "acao"), S(args, "texto"), ct),
            "abrir_pasta" => OpenPath(S(args, "caminho")),
            "buscar_arquivo" => await FindFilesAsync(S(args, "nome"), S(args, "pasta"), ct),
            "spotify" => await SpotifyAsync(S(args, "acao"), S(args, "consulta"), I(args, "nivel"), ct),
            "buscar_youtube" => SearchYouTube(S(args, "consulta")),
            "captura_tela" => await ScreenshotAsync(ct),
            "executar_rotina" => await RoutineAsync(S(args, "nome"), ct),
            "executar_powershell" when _config.AllowPowerShell => await PowerShellAsync(S(args, "comando"), ct),
            _ => $"A ferramenta \"{name}\" não existe.",
        };
    }

    // ------------------------------------------------------------------ programas, sites, mídia

    private async Task<string> OpenProgramAsync(string nome, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "Qual programa?";

        if (_config.Programs.TryGetValue(nome, out var target))
        {
            // "browser:" = navegador padrão do Windows.
            ShellOpen(target.Equals("browser:", StringComparison.OrdinalIgnoreCase) ? "https://www.google.com" : target);
            return $"Programa \"{nome}\" aberto.";
        }

        // Não está na lista: procura no Menu Iniciar, mas só abre com confirmação.
        var found = FindStartMenuApp(nome);
        if (found is null)
            return $"Não encontrei \"{nome}\" no computador. Conhecidos: {string.Join(", ", _config.Programs.Keys)}.";

        var title = Path.GetFileNameWithoutExtension(found);
        if (!await _confirm($"Abrir \"{title}\" (encontrado no Menu Iniciar)?", ct))
            return "O usuário não autorizou.";
        ShellOpen(found);
        return $"Programa \"{title}\" aberto.";
    }

    private static string? FindStartMenuApp(string name)
    {
        var wanted = Normalize(name);
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        ];

        string? partial = null;
        foreach (var root in roots.Where(Directory.Exists))
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", opts))
            {
                var title = Normalize(Path.GetFileNameWithoutExtension(lnk));
                if (title.Contains("uninstall") || title.Contains("desinstal")) continue;
                if (title == wanted) return lnk;
                if (partial is null && title.Contains(wanted)) partial = lnk;
            }
        }
        return partial;
    }

    /// <summary>Minúsculas e sem acentos, para comparar nomes.</summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    private static string OpenSite(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            return "Endereço inválido.";
        ShellOpen(uri.AbsoluteUri);
        return $"Site {uri.Host} aberto.";
    }

    private static string Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Nada para pesquisar.";
        ShellOpen("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
        return $"Pesquisa por \"{query}\" aberta no navegador.";
    }

    private static string SearchYouTube(string consulta)
    {
        if (string.IsNullOrWhiteSpace(consulta)) return "O que procurar?";
        ShellOpen("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(consulta));
        return $"Busca por \"{consulta}\" aberta no YouTube. Ainda é preciso escolher o vídeo.";
    }

    private static string Media(string acao)
    {
        (byte key, string done) = acao switch
        {
            "tocar_pausar" => (VK_MEDIA_PLAY_PAUSE, "Reprodução alternada."),
            "proxima" => (VK_MEDIA_NEXT_TRACK, "Próxima faixa."),
            "anterior" => (VK_MEDIA_PREV_TRACK, "Faixa anterior."),
            _ => ((byte)0, "Ação de mídia desconhecida."),
        };
        if (key == 0) return done;
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return done;
    }

    private static string LockScreen()
    {
        LockWorkStation();
        return "Tela bloqueada.";
    }

    private async Task<string> PowerAsync(string acao, int minutos, CancellationToken ct)
    {
        if (acao == "cancelar")
        {
            RunHidden("shutdown.exe", "/a");
            return "Desligamento cancelado.";
        }

        minutos = Math.Clamp(minutos, 0, 24 * 60);
        var verbo = acao == "reiniciar" ? "Reiniciar" : "Desligar";
        var ok = await _confirm($"{verbo} o computador em {minutos} minuto(s)?", ct);
        Log.Info($"Confirmação de energia ({acao}): {(ok ? "autorizada" : "negada")}");
        if (!ok) return "O usuário não autorizou.";

        RunHidden("shutdown.exe", $"{(acao == "reiniciar" ? "/r" : "/s")} /t {minutos * 60}");
        return $"{verbo} agendado para daqui a {minutos} minuto(s). Para cancelar, peça para cancelar o desligamento.";
    }

    // ------------------------------------------------------------------ rotinas e PowerShell livre

    private async Task<string> RoutineAsync(string nome, CancellationToken ct)
    {
        if (!_config.Routines.TryGetValue(nome, out var command))
            return $"Não existe a rotina \"{nome}\".";
        if ((_config.ConfirmRoutines || PolicyOf("executar_rotina") == "confirmar")
            && !await _confirm($"Executar a rotina \"{nome}\"?", ct))
            return "O usuário não autorizou.";
        return await RunPowerShellAsync(command, ct);
    }

    private async Task<string> PowerShellAsync(string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Comando vazio.";
        // Comando livre: SEMPRE confirma, mostrando o comando exato.
        if (!await _confirm($"Executar no PowerShell:\n{command}", ct))
            return "O usuário não autorizou.";
        return await RunPowerShellAsync(command, ct);
    }

    // ------------------------------------------------------------------ temporizadores

    private readonly Lock _timersGate = new();
    private readonly List<ReminderTimer> _timers = [];
    private int _timerSeq;

    private sealed record ReminderTimer(int Id, DateTime At, string Message, CancellationTokenSource Cts);

    private string Timer(string acao, int? minutos, string mensagem)
    {
        switch (acao)
        {
            case "criar":
            {
                if (minutos is null or <= 0) return "Diga em quantos minutos.";
                var mins = Math.Min(minutos.Value, 24 * 60);
                var text = string.IsNullOrWhiteSpace(mensagem)
                    ? $"{_config.UserTitle}, o temporizador de {mins} minuto(s) terminou."
                    : $"{_config.UserTitle}, lembrete: {mensagem.Trim()}.";
                var cts = new CancellationTokenSource();
                ReminderTimer timer;
                lock (_timersGate)
                {
                    timer = new ReminderTimer(++_timerSeq, DateTime.Now.AddMinutes(mins), text, cts);
                    _timers.Add(timer);
                }
                _ = RunTimerAsync(timer, TimeSpan.FromMinutes(mins));
                return $"Temporizador criado para {timer.At:HH:mm} (daqui a {mins} minuto(s)).";
            }
            case "listar":
                lock (_timersGate)
                {
                    if (_timers.Count == 0) return "Não há temporizadores ativos.";
                    return string.Join("; ", _timers.Select(t => $"às {t.At:HH:mm}: {Cut(t.Message, 60)}"));
                }
            case "cancelar":
                lock (_timersGate)
                {
                    var n = _timers.Count;
                    foreach (var t in _timers) t.Cts.Cancel();
                    _timers.Clear();
                    return n == 0 ? "Não havia temporizadores." : $"{n} temporizador(es) cancelado(s).";
                }
            default:
                return "Ação de temporizador desconhecida.";
        }
    }

    private async Task RunTimerAsync(ReminderTimer timer, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, timer.Cts.Token);
            lock (_timersGate) _timers.Remove(timer);
            await _notify(timer.Message);
        }
        catch (OperationCanceledException) { /* cancelado */ }
        catch (Exception ex) { Log.Error("Falha ao avisar o temporizador", ex); }
    }

    public void Dispose()
    {
        lock (_timersGate)
        {
            foreach (var t in _timers) t.Cts.Cancel();
            _timers.Clear();
        }
    }

    // ------------------------------------------------------------------ utilitários

    private static void ShellOpen(string target) =>
       Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private static void RunHidden(string file, string args) =>
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();

    /// <summary>Roda um script e devolve código de saída, saída e erros (sem formatar).</summary>
    private static async Task<(int ExitCode, string Output, string Error, bool TimedOut)> RunPowerShellRawAsync(
        string command, CancellationToken ct, int timeoutSeconds = 60)
    {
        // -EncodedCommand evita qualquer problema com aspas e acentos; -Sta é exigido pela área de transferência.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Sta -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui abrir o PowerShell.");
        var stdout = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = p.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested(); // interrompido pelo usuário: propaga
            return (-1, "", "", true);
        }

        return (p.ExitCode, (await stdout).Trim(), (await stderr).Trim(), false);
    }

    private static async Task<string> RunPowerShellAsync(string command, CancellationToken ct)
    {
        var (code, output, error, timedOut) = await RunPowerShellRawAsync(command, ct);
        if (timedOut) return "O comando passou de 60 segundos e foi interrompido.";

        var text = code == 0 ? "Concluído." : $"Terminou com erro (código {code}).";
        if (output.Length > 0) text += " Saída: " + Cut(output, 600);
        if (error.Length > 0) text += " Erros: " + Cut(error, 400);
        return text;
    }

    /// <summary>Texto seguro para dentro de aspas simples do PowerShell.</summary>
    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string S(JsonObject args, string key) => args[key]?.ToString()?.Trim() ?? "";

    private static int? I(JsonObject args, string key) =>
        int.TryParse(args[key]?.ToString(), out var v) ? v : null;

    private static JsonObject Tool(string name, string description, JsonObject props, string[]? required = null)
    {
        required ??= [.. props.Select(p => p.Key)];
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = new JsonArray([.. required.Select(r => (JsonNode?)JsonValue.Create(r))]),
                },
            },
        };
    }

    private static JsonObject Props(params (string Name, JsonObject Schema)[] items)
    {
        var o = new JsonObject();
        foreach (var (n, s) in items) o[n] = s;
        return o;
    }

    private static JsonObject Str(string d) => new() { ["type"] = "string", ["description"] = d };

    private static JsonObject Int(string d) => new() { ["type"] = "integer", ["description"] = d };

    private static JsonObject Choice(string d, IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["description"] = d,
        ["enum"] = new JsonArray([.. values.Select(v => (JsonNode?)JsonValue.Create(v))])
    };

    // ------------------------------------------------------------------ Win32

    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();
}
