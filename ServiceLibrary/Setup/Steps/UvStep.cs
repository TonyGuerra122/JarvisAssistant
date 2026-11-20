namespace ServiceLibrary.Setup.Steps;

/// <summary>uv (astral.sh): gerencia o Python e as dependências do OpenJarvis.</summary>
public sealed class UvStep : InstallStep
{
    private static readonly string UvDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    public override string Id => "setup-uv";
    public override string Title => "uv (gerenciador de pacotes Python)";

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        ctx.Uv = ProcessRunner.FindOnPath("uv") ?? GitStep.Existing(Path.Combine(UvDir, "uv.exe"));

        // O jarvis.cmd chama "uv" pelo PATH; garante que este processo (e o servidor que ele inicia) o encontre.
        if (ctx.Uv is not null) ProcessRunner.PrependToPath(Path.GetDirectoryName(ctx.Uv)!);
        return Task.FromResult(ctx.Uv is not null);
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        Report(ctx, "baixando o instalador oficial");
        var result = await ctx.RunAsync(this, "powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
             "irm https://astral.sh/uv/install.ps1 -UseBasicParsing | iex"],
            timeout: TimeSpan.FromMinutes(10), ct: ct);

        ProcessRunner.RefreshPath();
        if (!result.Ok && !File.Exists(Path.Combine(UvDir, "uv.exe")))
        {
            // Plano B: winget.
            if (!await ctx.WingetInstallAsync(this, "astral-sh.uv", ct))
                throw new InstallException("Não consegui instalar o uv: " + result.Tail(2));
        }
    }

    public override string? DoneDetail(InstallerContext ctx) => ctx.Uv;
}
