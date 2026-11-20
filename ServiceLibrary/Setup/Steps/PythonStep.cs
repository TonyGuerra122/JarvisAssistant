using System.Text.RegularExpressions;

namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Python 3.11–3.13 (o 3.14 ainda não tem numpy para Windows).
/// Se não houver, o uv baixa um Python próprio — sem mexer no Python do sistema.
/// </summary>
public sealed partial class PythonStep : InstallStep
{
    public override string Id => "setup-python";
    public override string Title => "Python 3.11 – 3.13";

    public override async Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        // 1. Python já gerenciado pelo uv (o que o "uv sync" vai preferir usar).
        if (ctx.Uv is not null)
        {
            var found = await ProcessRunner.RunAsync(ctx.Uv,
                ["python", "find", ">=3.11,<3.14"], timeout: TimeSpan.FromSeconds(30), ct: ct);
            var path = found.Output.Trim().Split('\n').LastOrDefault()?.Trim();
            if (found.Ok && File.Exists(path) && await ProbeAsync(path, ctx, ct)) return true;
        }

        // 2. Python do sistema no PATH. Ignora o atalho da Microsoft Store (que só abre a loja).
        foreach (var candidate in ProcessRunner.FindAllOnPath("python").Concat(ProcessRunner.FindAllOnPath("python3")))
        {
            if (await ProbeAsync(candidate, ctx, ct)) return true;
        }

        return false;
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        if (ctx.Uv is null) throw new InstallException("uv não encontrado.");

        Report(ctx, $"baixando Python {ctx.Options.PythonVersion} pelo uv");
        var result = await ctx.RunAsync(this, ctx.Uv, ["python", "install", ctx.Options.PythonVersion],
            timeout: TimeSpan.FromMinutes(15), ct: ct);
        if (!result.Ok) throw new InstallException("Não consegui instalar o Python: " + result.Tail(2));
    }

    public override string? DoneDetail(InstallerContext ctx) => $"{ctx.PythonVersion} ({ctx.Python})";

    private static async Task<bool> ProbeAsync(string exe, InstallerContext ctx, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(exe, ["--version"], timeout: TimeSpan.FromSeconds(15), ct: ct);
        var m = VersionRegex().Match(r.Output);
        if (!r.Ok || !m.Success) return false;

        var minor = int.Parse(m.Groups[2].Value);
        if (m.Groups[1].Value != "3" || minor is < 11 or > 13) return false;

        ctx.Python = exe;
        ctx.PythonVersion = m.Value;
        return true;
    }

    [GeneratedRegex(@"(\d+)\.(\d+)\.\d+")]
    private static partial Regex VersionRegex();
}
