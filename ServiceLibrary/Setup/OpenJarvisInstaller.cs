using ServiceLibrary.Setup.Steps;

namespace ServiceLibrary.Setup;

/// <summary>Resultado da instalação. <see cref="Warnings"/> = passos opcionais que falharam.</summary>
public sealed record InstallResult(bool Success, string? Error, IReadOnlyList<string> Warnings);

/// <summary>
/// Instala e configura o OpenJarvis e tudo de que ele depende, seguindo o install.ps1 oficial
/// (https://github.com/open-jarvis/OpenJarvis), mas passo a passo e com progresso para o HUD.
///
/// <code>
/// var installer = new OpenJarvisInstaller(InstallerOptions.FromConfig(config));
/// var result = await installer.InstallAsync(new Progress&lt;InstallProgress&gt;(p => ...));
/// </code>
///
/// Pode ser chamado sempre: passos já prontos são pulados rapidamente.
/// </summary>
public sealed class OpenJarvisInstaller(InstallerOptions options)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public InstallerOptions Options { get; } = options;

    /// <summary>Ordem importa: cada passo usa o que os anteriores encontraram (git, uv…).</summary>
    public IReadOnlyList<InstallStep> Steps { get; } =
    [
        new WindowsStep(),
        new GitStep(),
        new UvStep(),
        new PythonStep(),
        new BuildToolsStep(),
        new RepositoryStep(),
        new DependenciesStep(),
        new ShimStep(),
        new ConfigStep(),
        new SpeechStep(),
        new OllamaStep(),
        new ModelStep(),
        new EspeakStep(),
        new VoiceCloneStep(),
    ];

    /// <summary>Checagem rápida (sem rodar nada): o jarvis.cmd e o ambiente Python existem?</summary>
    public bool LooksInstalled => File.Exists(Options.ShimPath) && File.Exists(Options.VenvPython);

    /// <summary>Instalação completa (todos os passos).</summary>
    public Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default) =>
        RunStepsAsync(Steps, progress, ct);

    /// <summary>
    /// Checagem rápida para toda abertura do app: Ollama rodando e o modelo do OpenJarvis baixado.
    /// </summary>
    public Task<InstallResult> EnsureRuntimeAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default) =>
        RunStepsAsync(Steps.Where(s => s is UvStep or SpeechStep or OllamaStep or ModelStep or VoiceCloneStep), progress, ct);

    private async Task<InstallResult> RunStepsAsync(IEnumerable<InstallStep> steps, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        using var ctx = new InstallerContext(Options, progress);
        List<string> warnings = [];
        try
        {
            ProcessRunner.RefreshPath();

            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                if (!step.IsEnabled(ctx)) continue;

                ctx.Report(new InstallProgress(step.Id, step.Title, StepStatus.Working, "verificando"));
                try
                {
                    if (!await step.IsSatisfiedAsync(ctx, ct))
                    {
                        await step.InstallAsync(ctx, ct);
                        if (!await step.IsSatisfiedAsync(ctx, ct))
                            throw new InstallException("a instalação terminou, mas a verificação ainda falha. Reiniciar o Windows pode resolver.");
                    }
                    ctx.Report(new InstallProgress(step.Id, step.Title, StepStatus.Done, step.DoneDetail(ctx)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var message = ex is InstallException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                    Log.Error($"Setup: {step.Title} falhou", ex);

                    if (step.Required)
                    {
                        ctx.Report(new InstallProgress(step.Id, step.Title, StepStatus.Failed, message));
                        return new InstallResult(false, $"{step.Title}: {message}", warnings);
                    }

                    warnings.Add($"{step.Title}: {message}");
                    ctx.Report(new InstallProgress(step.Id, step.Title, StepStatus.Skipped, message));
                }
            }

            return new InstallResult(true, null, warnings);
        }
        finally
        {
            Gate.Release();
        }
    }
}
