namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Visual Studio Build Tools (C++). A extensão Rust obrigatória do OpenJarvis
/// (openjarvis-rust) é compilada na instalação e precisa do linker da Microsoft (link.exe).
/// O Rust em si o maturin baixa sozinho.
/// </summary>
public sealed class BuildToolsStep : InstallStep
{
    private const string WingetId = "Microsoft.VisualStudio.2022.BuildTools";
    private const string Override = "--wait --quiet --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended";

    public override string Id => "setup-msvc";
    public override string Title => "Compilador C++ (Visual Studio Build Tools)";

    public override async Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        // link.exe da Microsoft no PATH (o do Git/MSYS não serve).
        foreach (var link in ProcessRunner.FindAllOnPath("link.exe"))
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(link);
            if (info.CompanyName?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true) return true;
        }

        // Fora do "Developer Command Prompt" o link.exe não fica no PATH: pergunta ao vswhere.
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return false;

        var r = await ProcessRunner.RunAsync(vswhere,
            ["-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
             "-find", @"VC\Tools\MSVC\**\bin\Host*\*\link.exe"],
            timeout: TimeSpan.FromSeconds(30), ct: ct);

        return r.Ok && r.Output.Split('\n').Any(l => File.Exists(l.Trim()));
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        if (!ctx.Options.InstallBuildTools)
            throw new InstallException(
                "Faltam as ferramentas C++ da Microsoft. Instale com: winget install --id " + WingetId +
                $" --override \"{Override}\" — ou ative OpenJarvisInstallBuildTools no config.json.");

        Report(ctx, "instalando (vários GB, o Windows vai pedir permissão — pode levar 10 a 30 minutos)");

        // O --override é essencial: sem ele o Build Tools instala sem nenhum compilador.
        if (!await ctx.WingetInstallAsync(this, WingetId, ct, "--override", Override))
            throw new InstallException("Não consegui instalar o Visual Studio Build Tools. Veja: https://visualstudio.microsoft.com/downloads/ (\"Desktop development with C++\").");
    }
}
