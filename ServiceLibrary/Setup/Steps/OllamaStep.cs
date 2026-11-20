using System.Diagnostics;

namespace ServiceLibrary.Setup.Steps;

/// <summary>Ollama (o motor que roda o modelo local): instala se faltar e garante que está rodando.</summary>
public sealed class OllamaStep : InstallStep
{
    public const string ApiUrl = "http://127.0.0.1:11434";
    private const string SetupUrl = "https://ollama.com/download/OllamaSetup.exe";

    public override string Id => "setup-ollama";
    public override string Title => "Ollama";

    public override async Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        ctx.Ollama = Find();
        return ctx.Ollama is not null && await IsRunningAsync(ctx, ct);
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        if (ctx.Ollama is null)
        {
            await DownloadAndInstallAsync(ctx, ct);
            ProcessRunner.RefreshPath();
            ctx.Ollama = Find() ?? throw new InstallException("O instalador do Ollama rodou, mas o ollama.exe não apareceu. Instale por https://ollama.com.");
        }

        // O instalador abre o app da bandeja, que sobe o serviço; dá alguns segundos antes de iniciar à mão.
        Report(ctx, "aguardando o serviço do Ollama");
        for (var i = 0; i < 60; i++)
        {
            if (await IsRunningAsync(ctx, ct)) return;
            if (i == 5)
            {
                Process.Start(new ProcessStartInfo(ctx.Ollama, "serve")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(ctx.Ollama)!
                });
            }
            await Task.Delay(1000, ct);
        }
        throw new InstallException("O Ollama está instalado, mas não respondeu em 60 segundos.");
    }

    public override string? DoneDetail(InstallerContext ctx) => ctx.Ollama;

    private async Task DownloadAndInstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        var setup = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
        try
        {
            using (var res = await ctx.Http.GetAsync(SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength;
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(setup);

                var buffer = new byte[1 << 16];
                long read = 0, lastReport = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (read - lastReport > 4_000_000)
                    {
                        lastReport = read;
                        Report(ctx, $"baixando {read / 1_048_576} MB", total > 0 ? read * 100.0 / total : null);
                    }
                }
            }

            // O instalador é NSIS: o modo silencioso é /S (maiúsculo).
            Report(ctx, "instalando");
            var result = await ProcessRunner.RunAsync(setup, ["/S"], timeout: TimeSpan.FromMinutes(15), ct: ct);
            if (!result.Ok) throw new InstallException($"O instalador do Ollama falhou (código {result.ExitCode}).");
        }
        catch (HttpRequestException ex)
        {
            throw new InstallException("Não consegui baixar o Ollama: " + ex.Message);
        }
        finally
        {
            try { File.Delete(setup); } catch { /* em uso */ }
        }
    }

    internal static async Task<bool> IsRunningAsync(InstallerContext ctx, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var res = await ctx.Http.GetAsync($"{ApiUrl}/api/tags", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string? Find() =>
        ProcessRunner.FindOnPath("ollama") ?? GitStep.Existing(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"));
}
