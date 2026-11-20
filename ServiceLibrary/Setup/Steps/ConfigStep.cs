namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Gera o config.toml do OpenJarvis com "jarvis init" (detecta CPU/GPU/RAM e escolhe
/// engine e modelo). Sem isso, o próprio "jarvis" gera na primeira execução e pode
/// escolher um modelo diferente do que foi baixado.
/// </summary>
public sealed class ConfigStep : InstallStep
{
    public override string Id => "setup-config";
    public override string Title => "Configuração do OpenJarvis";

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct) =>
        Task.FromResult(OpenJarvisConfigFile.Exists);

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        var jarvis = Path.Combine(ctx.Options.SourceDir, ".venv", "Scripts", "jarvis.exe");
        if (!File.Exists(jarvis)) throw new InstallException("jarvis.exe não encontrado no ambiente do OpenJarvis.");

        Report(ctx, "detectando hardware (jarvis init)");
        var r = await ctx.RunAsync(this, jarvis, ["init"], ctx.Options.SourceDir,
            TimeSpan.FromMinutes(5), new Dictionary<string, string> { ["OPENJARVIS_NO_UPDATE_CHECK"] = "1" }, ct);
        if (!r.Ok) throw new InstallException("jarvis init falhou: " + r.Tail(2));
    }

    public override string? DoneDetail(InstallerContext ctx) =>
        OpenJarvisConfigFile.DefaultModel is { } m ? $"modelo padrão {m}" : OpenJarvisConfigFile.Path;
}
