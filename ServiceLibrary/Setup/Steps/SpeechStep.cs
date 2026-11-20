namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Define onde o Whisper (transcrição) roda. Com device="auto", o CTranslate2 vê a GPU NVIDIA
/// e tenta usar CUDA — mas no Windows as DLLs do CUDA 12 (cublas64_12.dll, cuDNN 9) não vêm
/// com o pacote, e a transcrição falha com "cublas64_12.dll is not found".
/// Sem essas DLLs, grava device="cpu" + compute_type="int8" no config.toml do OpenJarvis.
/// Se o usuário já escolheu um device, não mexe.
/// </summary>
public sealed class SpeechStep : InstallStep
{
    private static readonly string[] CudaDlls = ["cublas64_12.dll", "cudnn_ops64_9.dll"];

    public override string Id => "setup-speech";
    public override string Title => "Transcrição de voz (Whisper)";

    public override bool IsEnabled(InstallerContext ctx) =>
        ctx.Options.Extras.Any(e => e.Equals("desktop", StringComparison.OrdinalIgnoreCase) || e.Equals("speech", StringComparison.OrdinalIgnoreCase));

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        var device = OpenJarvisConfigFile.Read("speech", "device");
        var ok = device is not null && !device.Equals("auto", StringComparison.OrdinalIgnoreCase)
                 || CudaAvailable(ctx);
        return Task.FromResult(ok);
    }

    public override Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        Report(ctx, "CUDA não encontrado — usando a CPU");
        OpenJarvisConfigFile.Write("speech", "device", "cpu");
        OpenJarvisConfigFile.Write("speech", "compute_type", "int8");
        return Task.CompletedTask;
    }

    public override string? DoneDetail(InstallerContext ctx) =>
        OpenJarvisConfigFile.Read("speech", "device") is { } d && d != "auto" ? $"device {d}" : "GPU (CUDA)";

    /// <summary>As DLLs do CUDA estão no PATH ou na pasta do ctranslate2 (que ele carrega sozinho)?</summary>
    private static bool CudaAvailable(InstallerContext ctx)
    {
        var ct2 = Path.Combine(ctx.Options.SourceDir, ".venv", "Lib", "site-packages", "ctranslate2");
        return CudaDlls.All(dll => ProcessRunner.FindOnPath(dll) is not null || File.Exists(Path.Combine(ct2, dll)));
    }
}
