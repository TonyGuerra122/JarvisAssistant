using System.Text.Encodings.Web;
using System.Text.Json;

namespace ServiceLibrary;

/// <summary>
/// Configurações do J.A.R.V.I.S. Ficam em %APPDATA%\JarvisAssistant\config.json,
/// criado na primeira execução. Edite com o Bloco de Notas e reabra o app.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Como o Jarvis chama você.</summary>
    public string UserTitle { get; set; } = "senhor";

    /// <summary>Cidade usada para o clima.</summary>
    public string City { get; set; } = "São Paulo";

    /// <summary>Idioma da transcrição (código ISO, ex.: "pt").</summary>
    public string Language { get; set; } = "pt";

    /// <summary>Endereço do servidor do OpenJarvis.</summary>
    public string OpenJarvisUrl { get; set; } = "http://127.0.0.1:8000";

    /// <summary>Se o servidor não estiver rodando, o app inicia "jarvis serve" sozinho.</summary>
    public bool StartOpenJarvis { get; set; } = true;

    /// <summary>Ao fechar o app, encerra o servidor que ele mesmo iniciou.</summary>
    public bool StopOpenJarvisOnExit { get; set; } = true;

    /// <summary>Se o OpenJarvis não estiver instalado, o app instala ele e as dependências sozinho.</summary>
    public bool AutoInstallOpenJarvis { get; set; } = true;

    /// <summary>Permite instalar o Visual Studio Build Tools (C++, vários GB, pede UAC) se faltar.</summary>
    public bool OpenJarvisInstallBuildTools { get; set; } = true;

    /// <summary>
    /// Modelo baixado no Ollama só se nem Model nem o config.toml do OpenJarvis (jarvis init) definirem um.
    /// </summary>
    public string OpenJarvisStarterModel { get; set; } = "qwen3.5:2b";

    /// <summary>Extras do OpenJarvis instalados ("desktop" = ouvir, "voice" = voz Kokoro).</summary>
    public List<string> OpenJarvisExtras { get; set; } = ["desktop", "voice"];

    /// <summary>Caminho do jarvis.cmd. Vazio = %LOCALAPPDATA%\OpenJarvis\bin\jarvis.cmd.</summary>
    public string JarvisCommand { get; set; } = string.Empty;

    /// <summary>Agente do OpenJarvis para o chat. "simple" é o mais rápido em modelos pequenos.</summary>
    public string Agent { get; set; } = "simple";

    /// <summary>Modelo. Vazio = o modelo padrão do OpenJarvis.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Limite de tamanho da resposta (tokens).</summary>
    public int MaxTokens { get; set; } = 350;

    /// <summary>Quantas trocas anteriores o Jarvis lembra na conversa atual.</summary>
    public int HistoryTurns { get; set; } = 6;

    /// <summary>
    /// "openjarvis" (Kokoro), "windows" (vozes do Windows) ou "clone" (voz clonada de CloneVoiceFile, XTTS-v2).
    /// Se a escolhida falhar, o app cai para a próxima disponível.
    /// </summary>
    public string Voice { get; set; } = "openjarvis";

    /// <summary>Voz do OpenJarvis (Kokoro). "pm_alex" = masculina pt-BR. Vazio = a do config do OpenJarvis.</summary>
    public string OpenJarvisVoiceId { get; set; } = "pm_alex";

    /// <summary>
    /// Áudio (mp3/wav) com a voz a clonar, usado quando Voice = "clone". Só use a voz de quem autorizou.
    /// </summary>
    public string CloneVoiceFile { get; set; } = string.Empty;

    /// <summary>Porta local do serviço de voz clonada.</summary>
    public int CloneVoicePort { get; set; } = 8020;

    /// <summary>Velocidade da voz do OpenJarvis (1.0 = normal; menor = mais pausada, estilo mordomo).</summary>
    public float VoiceSpeed { get; set; } = 1.0f;

    /// <summary>Tom da voz em semitons (negativo = mais grave; 0 = original). Ignorado na voz clonada.</summary>
    public float VoicePitch { get; set; } = -0.5f;

    /// <summary>Efeito de "voz de IA" (graves + eco curto). 0 = desligado, 1 = sutil, 2 = forte.</summary>
    public float VoiceEffect { get; set; } = 0.7f;

    /// <summary>Nome de uma voz do Windows (ex.: "Microsoft Daniel"). Vazio = primeira voz pt-BR.</summary>
    public string WindowsVoiceName { get; set; } = "";

    /// <summary>Saudação ao terminar de iniciar.</summary>
    public bool GreetOnStart { get; set; } = true;

    /// <summary>Personalidade. {title} vira o UserTitle.</summary>
    public string Personality { get; set; } =
        "Você é J.A.R.V.I.S., o assistente pessoal do usuário. " +
        "Responda sempre em português do Brasil, com tom educado, calmo e levemente bem-humorado, " +
        "como um mordomo britânico muito competente. Chame o usuário de \"{title}\". " +
        "Suas respostas serão lidas em voz alta: seja breve (no máximo 3 frases, salvo se pedirem detalhes), " +
        "não use listas, markdown, emojis nem símbolos. Se não souber algo, diga com honestidade.";

    /// <summary>Liga DevTools (F12) e atalhos do navegador, para depurar o HUD.</summary>
    public bool Debug { get; set; } = false;

    /// <summary>Permite que o Jarvis execute ações no PC (abrir programas, mídia, rotinas...).</summary>
    public bool CommandsEnabled { get; set; } = true;

    /// <summary>Programas que ele pode abrir: nome falado → executável, caminho ou URI.</summary>
    public Dictionary<string, string> Programs { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["navegador"] = "msedge",
        ["bloco de notas"] = "notepad",
        ["calculadora"] = "calc",
        ["explorador de arquivos"] = "explorer",
        ["configurações"] = "ms-settings:",
        ["spotify"] = "spotify:",
    };

    /// <summary>Rotinas suas: nome → comando do PowerShell. Ex.: "modo trabalho".</summary>
    public Dictionary<string, string> Routines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abrir downloads"] = "Start-Process \"$env:USERPROFILE\\Downloads\"",
        ["esvaziar lixeira"] = "Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
    };

    /// <summary>Pede confirmação antes de rodar uma rotina.</summary>
    public bool ConfirmRoutines { get; set; } = true;

    /// <summary>
    /// Deixa o modelo escrever comandos livres de PowerShell (sempre com confirmação).
    /// Desligado por padrão: o modelo pode errar ou ser enganado por textos que ele lê.
    /// </summary>
    public bool AllowPowerShell { get; set; } = false;

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppConfig Load()
    {
        var file = AppPaths.ConfigFile;
        AppConfig? config;

        try
        {
            config = File.Exists(file)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(file), Opts)
                : new AppConfig();
        }
        catch (Exception ex)
        {
            Log.Error("config.json inválido, usando valores padrão", ex);
            return new AppConfig();
        }

        // O JSON não guarda o "ignorar maiúsculas" dos dicionários; recoloca aqui.
        config?.Programs = new Dictionary<string, string>(config.Programs ?? [], StringComparer.OrdinalIgnoreCase);
        config?.Routines = new Dictionary<string, string>(config.Routines ?? [], StringComparer.OrdinalIgnoreCase);

        // Regrava para acrescentar opções novas sem perder as do usuário.
        try { File.WriteAllText(file, JsonSerializer.Serialize(config, Opts)); }
        catch (Exception ex) { Log.Error("Não consegui salvar config.json", ex); }

        return config ?? new AppConfig();
    }

    public string ResolveJarvisCommand()
    {
        if (!string.IsNullOrWhiteSpace(JarvisCommand)) return Environment.ExpandEnvironmentVariables(JarvisCommand);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenJarvis",
            "bin",
            "jarvis.cmd"
        );
    }
}
