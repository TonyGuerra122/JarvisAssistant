namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// "uv sync --extra desktop --extra voice --group desktop-native": servidor, Whisper,
/// Kokoro e a extensão Rust. Considera pronto quando o ambiente importa o OpenJarvis
/// e foi montado com os mesmos extras e a mesma versão do código.
/// </summary>
public sealed class DependenciesStep : InstallStep
{
    private const string MarkerName = ".jarvisassistant-sync";

    public override string Id => "setup-deps";
    public override string Title => "Dependências do OpenJarvis";

    private static string MarkerPath(InstallerContext ctx) => Path.Combine(ctx.Options.SourceDir, ".venv", MarkerName);

    public override async Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        if (ctx.SourceChanged || !File.Exists(ctx.Options.VenvPython) || !File.Exists(MarkerPath(ctx))) return false;
        if (await File.ReadAllTextAsync(MarkerPath(ctx), ct) != await FingerprintAsync(ctx, ct)) return false;

        var r = await ProcessRunner.RunAsync(ctx.Options.VenvPython,
            ["-c", "import openjarvis, openjarvis_rust, fastapi, faster_whisper"],
            ctx.Options.SourceDir, timeout: TimeSpan.FromMinutes(2), ct: ct);
        return r.Ok;
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        var uv = ctx.Uv ?? throw new InstallException("uv não encontrado.");

        List<string> args = ["sync"];
        foreach (var e in ctx.Options.Extras) args.AddRange(["--extra", e]);
        foreach (var g in ctx.Options.Groups) args.AddRange(["--group", g]);

        // Ambiente antigo criado com o Python da Store: refaz do zero com o Python do uv.
        ResetStoreEnvironment(ctx);
        if (ctx.Python is { Length: > 0 } && !PythonStep.IsStorePython(ctx.Python))
            args.AddRange(["--python", ctx.Python]);

        Report(ctx, "uv " + string.Join(' ', args) + " (a primeira vez leva vários minutos)");
        var r = await ctx.RunAsync(this, uv, args, ctx.Options.SourceDir, TimeSpan.FromMinutes(90), ct: ct);

        if (!r.Ok)
        {
            // Saída completa em arquivo: a mensagem do HUD é curta demais para um erro de compilação.
            var logFile = Path.Combine(AppPaths.LocalData, "install-deps.log");
            try
            {
                await File.WriteAllTextAsync(logFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  uv {string.Join(' ', args)}  (código {r.ExitCode})\n" +
                    $"Python: {ctx.Python} {ctx.PythonVersion}\n\n{r.Output}", ct);
            }
            catch (Exception ex) { Log.Error("Não consegui salvar install-deps.log", ex); }

            throw new InstallException($"uv sync falhou (código {r.ExitCode}). {Diagnose(r.Output)} Detalhes em {logFile}");
        }

        await File.WriteAllTextAsync(MarkerPath(ctx), await FingerprintAsync(ctx, ct), ct);
        ctx.SourceChanged = false;
    }

    /// <summary>
    /// Se o .venv foi criado pelo Python da Microsoft Store (pyvenv.cfg aponta para WindowsApps), apaga-o,
    /// junto com o cache do puccinialin (rustup) que ficou corrompido pelo isolamento da Store.
    /// </summary>
    private static void ResetStoreEnvironment(InstallerContext ctx)
    {
        try
        {
            var venv = Path.Combine(ctx.Options.SourceDir, ".venv");
            var cfg = Path.Combine(venv, "pyvenv.cfg");
            if (!File.Exists(cfg) || !PythonStep.IsStorePython(File.ReadAllText(cfg))) return;

            Log.Info("Ambiente Python criado pelo Python da Store; recriando com o Python do uv.");
            Directory.Delete(venv, recursive: true);

            var puccinialin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "puccinialin");
            if (Directory.Exists(puccinialin)) Directory.Delete(puccinialin, recursive: true);
        }
        catch (Exception ex) { Log.Error("Não consegui limpar o ambiente antigo do Python", ex); }
    }

    /// <summary>Procura na saída do uv/maturin/cargo a causa mais provável e devolve uma dica em português.</summary>
    private static string Diagnose(string output)
    {
        bool Has(string text) => output.Contains(text, StringComparison.OrdinalIgnoreCase);

        if (Has("WindowsApps") && (Has("rustup") || Has("puccinialin")))
            return "O Python da Microsoft Store isola os arquivos e quebra a instalação do Rust. Desinstale-o (Configurações > Aplicativos) ou deixe o J.A.R.V.I.S usar o Python do uv.";
        if (Has("kernel32.lib") || Has("LNK1181") || Has("windows.h"))
            return "Falta o Windows SDK junto do Build Tools C++ (abra o Visual Studio Installer e marque \"Desenvolvimento para desktop com C++\").";
        if (Has("link.exe"))
            return "Falta o linker C++ (Visual Studio Build Tools).";
        if (Has("rustup") || Has("cargo") || Has("rustc") || Has("puccinialin"))
            return "Não consegui instalar/usar o Rust que o maturin precisa (rede, proxy ou antivírus bloqueando o rustup).";
        if (Has("dns error") || Has("error sending request") || Has("timed out") || Has("connection"))
            return "Falha de rede ao baixar pacotes (internet, proxy ou firewall).";
        if (Has("os error 206") || Has("path too long") || Has("filename or extension is too long"))
            return "Caminho longo demais: ative \"caminhos longos\" no Windows.";
        if (Has("os error 5") || Has("access is denied") || Has("os error 32") || Has("being used by another process"))
            return "Acesso negado/arquivo em uso (antivírus ou outro programa mexendo na pasta do OpenJarvis).";
        if (Has("WindowsApps"))
            return "O Python da Microsoft Store pode estar atrapalhando; desinstale-o ou instale o Python pelo python.org.";

        // Sem causa conhecida: mostra as últimas linhas com "error" (ou as últimas, se não houver).
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        var errors = lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).TakeLast(2).ToList();
        return string.Join(" | ", (errors.Count > 0 ? errors : lines.TakeLast(2).ToList()).Select(l => l.Length > 140 ? l[..140] + "…" : l));
    }

    /// <summary>Extras + grupos + commit atual: se algo mudar, o sync roda de novo.</summary>
    private static async Task<string> FingerprintAsync(InstallerContext ctx, CancellationToken ct)
    {
        var head = "";
        if (ctx.Git is not null)
        {
            var r = await ProcessRunner.RunAsync(ctx.Git, ["-C", ctx.Options.SourceDir, "rev-parse", "HEAD"],
                timeout: TimeSpan.FromSeconds(20), ct: ct);
            if (r.Ok) head = r.Output.Trim();
        }
        return $"extras={string.Join(',', ctx.Options.Extras.Order())};groups={string.Join(',', ctx.Options.Groups.Order())};head={head}";
    }
}
