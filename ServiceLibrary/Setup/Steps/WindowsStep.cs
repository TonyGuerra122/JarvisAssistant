namespace ServiceLibrary.Setup.Steps;

/// <summary>Windows 10 1809 (build 17763) ou mais novo, e o winget (usado para instalar o resto).</summary>
public sealed class WindowsStep : InstallStep
{
    private const int MinBuild = 17763;

    public override string Id => "setup-os";
    public override string Title => "Verificando o Windows";

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        if (Environment.OSVersion.Version.Build < MinBuild)
            throw new InstallException($"O OpenJarvis precisa do Windows 10 1809 (build {MinBuild}) ou mais novo. Este é o build {Environment.OSVersion.Version.Build}.");

        ctx.Winget = ProcessRunner.FindOnPath("winget")
            ?? Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe"));
        return Task.FromResult(true);
    }

    public override Task InstallAsync(InstallerContext ctx, CancellationToken ct) => Task.CompletedTask;

    public override string? DoneDetail(InstallerContext ctx) =>
        $"build {Environment.OSVersion.Version.Build}" + (ctx.Winget is null ? " (winget não encontrado)" : "");

    private static string? Existing(string path) => File.Exists(path) ? path : null;
}
