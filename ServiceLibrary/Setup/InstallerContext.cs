namespace ServiceLibrary.Setup;

/// <summary>
/// Estado compartilhado entre os passos: opções, caminhos das ferramentas encontradas
/// e o canal de progresso.
/// </summary>
public sealed class InstallerContext(InstallerOptions options, IProgress<InstallProgress>? progress) : IDisposable
{
    public void Dispose() => Http.Dispose();

    public InstallerOptions Options { get; } = options;

    /// <summary>HttpClient para downloads e para a API do Ollama.</summary>
    public HttpClient Http { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string? Winget { get; set; }
    public string? Git { get; set; }
    public string? Uv { get; set; }
    public string? Python { get; set; }
    public string? PythonVersion { get; set; }
    public string? Ollama { get; set; }

    /// <summary>O repositório foi clonado/atualizado agora (força o uv sync a rodar de novo).</summary>
    public bool SourceChanged { get; set; }

    public void Report(InstallProgress p)
    {
        Log.Info("Setup: " + p);
        progress?.Report(p);
    }

    /// <summary>Executa um programa repassando a saída como detalhe do passo.</summary>
    public Task<ProcessResult> RunAsync(InstallStep step, string file, IEnumerable<string> args,
        string? workDir = null, TimeSpan? timeout = null, IDictionary<string, string>? env = null, CancellationToken ct = default) =>
        ProcessRunner.RunAsync(file, args, workDir,
            line => Report(new InstallProgress(step.Id, step.Title, StepStatus.Working, Shorten(line))),
            env, timeout, ct);

    /// <summary>
    /// Instala um pacote pelo winget. Retorna true se o winget terminou bem
    /// (ou disse que já estava instalado). Quem chama deve conferir de novo.
    /// </summary>
    public async Task<bool> WingetInstallAsync(InstallStep step, string id, CancellationToken ct, params string[] extraArgs)
    {
        Winget ??= ProcessRunner.FindOnPath("winget");
        if (Winget is null) return false;

        string[] args = ["install", "--id", id, "--exact", "--silent", "--source", "winget",
            "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity", .. extraArgs];
        var result = await RunAsync(step, Winget, args, timeout: TimeSpan.FromMinutes(60), ct: ct);
        ProcessRunner.RefreshPath();

        // 0x8A15002B = já instalado / sem atualização disponível.
        return result.Ok || result.ExitCode == unchecked((int)0x8A15002B);
    }

    private static string Shorten(string s) => s.Length <= 140 ? s : s[..140] + "…";
}
