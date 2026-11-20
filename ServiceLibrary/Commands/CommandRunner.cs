using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLibrary.Commands;

/// <summary>
/// Ações que o J.A.R.V.I.S pode executar no PC.
/// O modelo só PEDE uma ação (tool call); quem decide e executa é este código,
/// e somente o que está nesta lista. Ações arriscadas pedem confirmação.
/// </summary>
public sealed partial class CommandRunner(AppConfig config, Func<string, Task<bool>> confirm)
{
    private readonly AppConfig _config = config;
    private readonly Func<string, Task<bool>> _confirm = confirm;

    // ------------------------------------------------------------------ definição das ferramentas


    /// <summary>Lista de ferramentas no formato da OpenAI, enviada junto com cada pergunta.</summary>
    public JsonArray BuildTools()
    {
        var tools = new JsonArray
        {
            Tool("abrir_programa", "Abre um programa do computador.",
                Props(("nome", Choice("Nome do programa", _config.Programs.Keys)))),
            Tool("abrir_site", "Abre um site no navegador.",
                Props(("url", Str("Endereço completo, começando com https://")))),
            Tool("pesquisar_web", "Pesquisa algo no Google, abrindo o navegador.",
                Props(("consulta", Str("O que pesquisar")))),
            Tool("controlar_midia", "Controla a música ou o vídeo tocando e o volume do PC.",
                Props(("acao", Choice("Ação", ["tocar_pausar", "proxima", "anterior", "aumentar_volume", "diminuir_volume", "mudo"])))),
            Tool("bloquear_tela", "Bloqueia o computador (tela de login).", Props()),
            Tool("energia", "Desliga ou reinicia o computador, ou cancela um desligamento agendado.",
                Props(("acao", Choice("Ação", ["desligar", "reiniciar", "cancelar"])),
                      ("minutos", Int("Daqui a quantos minutos. Padrão: 1"))),
                required: ["acao"]),
        };

        if (_config.Routines.Count > 0)
            tools.Add(Tool("executar_rotina", "Executa uma rotina criada pelo usuário.",
                Props(("nome", Choice("Nome da rotina", _config.Routines.Keys)))));

        if (_config.AllowPowerShell)
            tools.Add(Tool("executar_powershell",
                "Executa um comando do PowerShell. Use só quando nenhuma outra ferramenta servir. O usuário confirma antes.",
                Props(("comando", Str("Comando do PowerShell")))));

        return tools;
    }

    /// <summary>Descrição curta da ação, para mostrar na legenda.</summary>
    public static string Describe(string name, JsonObject args) => name switch
    {
        "abrir_programa" => $"abrindo {S(args, "nome")}",
        "abrir_site" => $"abrindo {S(args, "url")}",
        "pesquisar_web" => $"pesquisando \"{S(args, "consulta")}\"",
        "controlar_midia" => $"mídia: {S(args, "acao").Replace('_', ' ')}",
        "bloquear_tela" => "bloqueando a tela",
        "energia" => $"energia: {S(args, "acao")}",
        "executar_rotina" => $"rotina \"{S(args, "nome")}\"",
        "executar_powershell" => "comando do PowerShell",
        _ => name,
    };


    // ------------------------------------------------------------------ execução

    /// <summary>Executa a ação e devolve um texto curto com o resultado (vai de volta para o modelo).</summary>
    public async Task<string> ExecuteAsync(string name, JsonObject args)
    {
        try
        {
            Log.Info($"Ação: {name} {args.ToJsonString()}");
            return name switch
            {
                "abrir_programa" => OpenProgram(S(args, "nome")),
                "abrir_site" => OpenSite(S(args, "url")),
                "pesquisar_web" => Search(S(args, "consulta")),
                "controlar_midia" => Media(S(args, "acao")),
                "bloquear_tela" => LockScreen(),
                "energia" => await PowerAsync(S(args, "acao"), I(args, "minutos") ?? 1),
                "executar_rotina" => await RoutineAsync(S(args, "nome")),
                "executar_powershell" when _config.AllowPowerShell => await PowerShellAsync(S(args, "comando")),
                _ => $"A ferramenta \"{name}\" não existe.",
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Ação {name} falhou", ex);
            return "Falhou: " + ex.Message;
        }
    }

    private string OpenProgram(string nome)
    {
        if (!_config.Programs.TryGetValue(nome, out var target))
            return $"Não conheço o programa \"{nome}\". Disponíveis: {string.Join(", ", _config.Programs.Keys)}.";
        ShellOpen(target);
        return $"Programa \"{nome}\" aberto.";
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

    private static string Media(string acao)
    {
        (byte key, int times, string done) = acao switch
        {
            "tocar_pausar" => (VK_MEDIA_PLAY_PAUSE, 1, "Reprodução alternada."),
            "proxima" => (VK_MEDIA_NEXT_TRACK, 1, "Próxima faixa."),
            "anterior" => (VK_MEDIA_PREV_TRACK, 1, "Faixa anterior."),
            "aumentar_volume" => (VK_VOLUME_UP, 5, "Volume aumentado."),
            "diminuir_volume" => (VK_VOLUME_DOWN, 5, "Volume diminuído."),
            "mudo" => (VK_VOLUME_MUTE, 1, "Mudo alternado."),
            _ => ((byte)0, 0, "Ação de mídia desconhecida."),
        };
        for (int i = 0; i < times; i++)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        return done;
    }

    private static string LockScreen()
    {
        LockWorkStation();
        return "Tela bloqueada.";
    }

    private async Task<string> PowerAsync(string acao, int minutos)
    {
        if (acao == "cancelar")
        {
            RunHidden("shutdown.exe", "/a");
            return "Desligamento cancelado.";
        }

        minutos = Math.Clamp(minutos, 0, 24 * 60);
        var verbo = acao == "reiniciar" ? "Reiniciar" : "Desligar";
        if (!await _confirm($"{verbo} o computador em {minutos} minuto(s)?"))
            return "O usuário não autorizou.";

        RunHidden("shutdown.exe", $"{(acao == "reiniciar" ? "/r" : "/s")} /t {minutos * 60}");
        return $"{verbo} agendado para daqui a {minutos} minuto(s). Para cancelar, peça para cancelar o desligamento.";
    }

    private async Task<string> RoutineAsync(string nome)
    {
        if (!_config.Routines.TryGetValue(nome, out var command))
            return $"Não existe a rotina \"{nome}\".";
        if (_config.ConfirmRoutines && !await _confirm($"Executar a rotina \"{nome}\"?"))
            return "O usuário não autorizou.";
        return await RunPowerShellAsync(command);
    }

    private async Task<string> PowerShellAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Comando vazio.";
        // Comando livre: SEMPRE confirma, mostrando o comando exato.
        if (!await _confirm($"Executar no PowerShell:\n{command}"))
            return "O usuário não autorizou.";
        return await RunPowerShellAsync(command);
    }

    // ------------------------------------------------------------------ utilitários

    private static void ShellOpen(string target) =>
       Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private static void RunHidden(string file, string args) =>
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();

    private static async Task<string> RunPowerShellAsync(string command)
    {
        // -EncodedCommand evita qualquer problema com aspas e acentos.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui abrir o PowerShell.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            return "O comando passou de 60 segundos e foi interrompido.";
        }

        var output = (await stdout).Trim();
        var error = (await stderr).Trim();
        var text = p.ExitCode == 0 ? "Concluído." : $"Terminou com erro (código {p.ExitCode}).";
        if (output.Length > 0) text += " Saída: " + Cut(output, 600);
        if (error.Length > 0) text += " Erros: " + Cut(error, 400);
        return text;
    }

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

    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
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
