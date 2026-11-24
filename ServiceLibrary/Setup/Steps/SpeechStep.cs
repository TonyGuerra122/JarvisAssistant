namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Define onde o Whisper (transcrição) roda. Com device="auto", o CTranslate2 vê a GPU NVIDIA
/// e tenta usar CUDA — mas no Windows as DLLs do CUDA 12 (cublas64_12.dll, cuDNN 9) não vêm
/// com o pacote, e a transcrição falha com "cublas64_12.dll is not found or cannot be loaded".
/// Detectar "tem CUDA" olhando o PATH não é confiável (a DLL pode existir e mesmo assim não carregar),
/// então o padrão é a CPU com int8 — rápido o bastante para comandos de voz curtos.
/// Quem quiser GPU define device = "cuda" no config.toml do OpenJarvis: se o usuário já escolheu, não mexemos.
/// </summary>
public sealed class SpeechStep : InstallStep
{
    public override string Id => "setup-speech";
    public override string Title => "Transcrição de voz (Whisper)";

    public override bool IsEnabled(InstallerContext ctx) =>
        ctx.Options.Extras.Any(e => e.Equals("desktop", StringComparison.OrdinalIgnoreCase) || e.Equals("speech", StringComparison.OrdinalIgnoreCase));

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        var device = OpenJarvisConfigFile.Read("speech", "device");
        var model = OpenJarvisConfigFile.Read("speech", "model");
        return Task.FromResult(device is not null && !device.Equals("auto", StringComparison.OrdinalIgnoreCase) && model is not null);
    }

    public override Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        Report(ctx, "usando a CPU (mais estável que a GPU no Windows)");
        if (OpenJarvisConfigFile.Read("speech", "device") is null or "auto") UseCpu();
        // O padrão do OpenJarvis ("base") erra muito em português; "small" é bem melhor e ainda leve na CPU.
        if (OpenJarvisConfigFile.Read("speech", "model") is null) OpenJarvisConfigFile.Write("speech", "model", DefaultModel);
        return Task.CompletedTask;
    }

    /// <summary>Modelo Whisper padrão: precisão bem melhor que o "base" em pt-BR.</summary>
    public const string DefaultModel = "small";

    public override string? DoneDetail(InstallerContext ctx) =>
        $"{OpenJarvisConfigFile.Read("speech", "model") ?? DefaultModel}, device {OpenJarvisConfigFile.Read("speech", "device") ?? "cpu"}";

    /// <summary>Grava device="cpu" e compute_type="int8" no config.toml do OpenJarvis.</summary>
    public static void UseCpu()
    {
        OpenJarvisConfigFile.Write("speech", "device", "cpu");
        OpenJarvisConfigFile.Write("speech", "compute_type", "int8");
    }
}
