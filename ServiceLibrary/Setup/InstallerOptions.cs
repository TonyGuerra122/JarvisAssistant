namespace ServiceLibrary.Setup;

/// <summary>Opções da instalação do OpenJarvis. Os padrões espelham o install.ps1 oficial.</summary>
public sealed class InstallerOptions
{
    /// <summary>Pasta raiz. Padrão: %LOCALAPPDATA%\OpenJarvis (a mesma do instalador oficial).</summary>
    public string InstallRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenJarvis");

    public string RepoUrl { get; set; } = "https://github.com/open-jarvis/OpenJarvis.git";

    /// <summary>
    /// Extras do pyproject passados ao "uv sync". "desktop" = servidor + Whisper (ouvir);
    /// "voice" = Kokoro (a voz que o J.A.R.V.I.S usa para falar).
    /// </summary>
    public List<string> Extras { get; set; } = ["desktop", "voice"];

    /// <summary>Grupos de dependência do uv. "desktop-native" compila a extensão Rust obrigatória.</summary>
    public List<string> Groups { get; set; } = ["desktop-native"];

    /// <summary>Versão do Python que o uv baixa se não houver 3.11–3.13 no sistema.</summary>
    public string PythonVersion { get; set; } = "3.13";

    /// <summary>Modelo escolhido pelo app (config.json "Model"). Tem prioridade sobre o do OpenJarvis.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Modelo baixado só se nem <see cref="Model"/> nem o config.toml do OpenJarvis definirem um.
    /// Vazio = não baixa nenhum.
    /// </summary>
    public string StarterModel { get; set; } = "qwen3.5:2b";

    /// <summary>Qual modelo o OpenJarvis vai usar de fato: app → config.toml (se o engine for Ollama) → StarterModel.</summary>
    public string? ResolveModel()
    {
        if (!string.IsNullOrWhiteSpace(Model)) return Model;

        var engine = OpenJarvisConfigFile.Read("engine", "default");
        if (engine is null || engine.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            if (OpenJarvisConfigFile.DefaultModel is { } fromConfig) return fromConfig;
        }

        return string.IsNullOrWhiteSpace(StarterModel) ? null : StarterModel;
    }

    /// <summary>
    /// Instalar o Visual Studio Build Tools (C++) se faltar? São vários GB e pedem UAC.
    /// Se false, a instalação para com instruções.
    /// </summary>
    public bool InstallBuildTools { get; set; } = true;

    /// <summary>Instalar o espeak-ng (melhora a pronúncia do Kokoro em palavras fora do dicionário).</summary>
    public bool InstallEspeak { get; set; } = true;

    /// <summary>Áudio de referência da voz clonada. Vazio = não instala a voz clonada.</summary>
    public string CloneVoiceFile { get; set; } = string.Empty;

    /// <summary>Atualiza o repositório (git pull) e reinstala as dependências mesmo se já estiverem prontas.</summary>
    public bool Update { get; set; }

    /// <summary>Adiciona a pasta bin ao PATH do usuário, para "jarvis" funcionar em qualquer terminal.</summary>
    public bool AddToUserPath { get; set; } = true;

    public string SourceDir => Path.Combine(InstallRoot, "src");
    public string BinDir => Path.Combine(InstallRoot, "bin");
    public string ShimPath => Path.Combine(BinDir, "jarvis.cmd");
    public string VenvPython => Path.Combine(SourceDir, ".venv", "Scripts", "python.exe");

    /// <summary>Monta as opções a partir do config.json do app.</summary>
    public static InstallerOptions FromConfig(AppConfig config)
    {
        var options = new InstallerOptions
        {
            Model = config.Model,
            StarterModel = config.OpenJarvisStarterModel,
            InstallBuildTools = config.OpenJarvisInstallBuildTools,
            InstallEspeak = !config.Voice.Equals("windows", StringComparison.OrdinalIgnoreCase),
            CloneVoiceFile = config.Voice.Equals("clone", StringComparison.OrdinalIgnoreCase) ? config.CloneVoiceFile : ""
        };

        if (config.OpenJarvisExtras is { Count: > 0 }) options.Extras = [.. config.OpenJarvisExtras];

        // Se o usuário apontou o jarvis.cmd para outro lugar (…\X\bin\jarvis.cmd), instala em …\X.
        var shim = config.ResolveJarvisCommand();
        if (Path.GetDirectoryName(Path.GetDirectoryName(shim)) is { Length: > 0 } root) options.InstallRoot = root;

        return options;
    }
}
