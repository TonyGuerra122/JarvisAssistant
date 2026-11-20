namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// espeak-ng: o Kokoro (voz do OpenJarvis) usa para pronunciar palavras fora do dicionário.
/// Opcional — sem ele a voz funciona, só erra mais em nomes próprios.
/// </summary>
public sealed class EspeakStep : InstallStep
{
    public override string Id => "setup-espeak";
    public override string Title => "espeak-ng (pronúncia da voz)";
    public override bool Required => false;

    public override bool IsEnabled(InstallerContext ctx) =>
        ctx.Options.InstallEspeak && ctx.Options.Extras.Contains("voice", StringComparer.OrdinalIgnoreCase);

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct) =>
        Task.FromResult(ProcessRunner.FindOnPath("espeak-ng") is not null || GitStep.Existing(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "eSpeak NG", "espeak-ng.exe")) is not null);

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        Report(ctx, "instalando pelo winget");
        if (!await ctx.WingetInstallAsync(this, "eSpeak-NG.eSpeak-NG", ct))
            throw new InstallException("Não consegui instalar o espeak-ng (opcional).");
    }
}
