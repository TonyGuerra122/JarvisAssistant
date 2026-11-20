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

        Report(ctx, "uv " + string.Join(' ', args) + " (a primeira vez leva vários minutos)");
        var r = await ctx.RunAsync(this, uv, args, ctx.Options.SourceDir, TimeSpan.FromMinutes(90), ct: ct);

        if (!r.Ok)
        {
            var hint = r.Output.Contains("link.exe", StringComparison.OrdinalIgnoreCase)
                ? " Falta o linker C++ (Visual Studio Build Tools)."
                : "";
            throw new InstallException($"uv sync falhou (código {r.ExitCode}).{hint} {r.Tail(3)}");
        }

        await File.WriteAllTextAsync(MarkerPath(ctx), await FingerprintAsync(ctx, ct), ct);
        ctx.SourceChanged = false;
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
