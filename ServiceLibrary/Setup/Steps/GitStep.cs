namespace ServiceLibrary.Setup.Steps;

/// <summary>git, para clonar e atualizar o repositório do OpenJarvis.</summary>
public sealed class GitStep : InstallStep
{
    public override string Id => "setup-git";
    public override string Title => "Git";

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        ctx.Git = ProcessRunner.FindOnPath("git") ?? Existing(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"));
        return Task.FromResult(ctx.Git is not null);
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        Report(ctx, "instalando pelo winget");
        if (!await ctx.WingetInstallAsync(this, "Git.Git", ct))
            throw new InstallException("Não consegui instalar o Git. Instale manualmente: winget install Git.Git");
    }

    public override string? DoneDetail(InstallerContext ctx) => ctx.Git;

    internal static string? Existing(params string[] paths) => paths.FirstOrDefault(File.Exists);
}
