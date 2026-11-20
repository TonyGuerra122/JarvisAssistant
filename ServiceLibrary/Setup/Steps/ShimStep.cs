namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Cria &lt;InstallRoot&gt;\bin\jarvis.cmd (igual ao do instalador oficial) e
/// coloca essa pasta no PATH do usuário, para "jarvis" funcionar em qualquer terminal.
/// </summary>
public sealed class ShimStep : InstallStep
{
    // %~dp0 = pasta do próprio .cmd; o "uv" vem do PATH.
    private const string ShimContent =
        "@echo off\r\n" +
        "setlocal\r\n" +
        "set \"SRC=%~dp0..\\src\"\r\n" +
        "uv run --project \"%SRC%\" jarvis %*\r\n";

    public override string Id => "setup-shim";
    public override string Title => "Comando jarvis";

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        var path = ctx.Options.ShimPath;
        var ok = File.Exists(path) && File.ReadAllText(path) == ShimContent;
        if (ok && ctx.Options.AddToUserPath) ok = IsOnUserPath(ctx.Options.BinDir);
        return Task.FromResult(ok);
    }

    public override Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.Options.BinDir);
        File.WriteAllText(ctx.Options.ShimPath, ShimContent, System.Text.Encoding.ASCII);

        if (ctx.Options.AddToUserPath && !IsOnUserPath(ctx.Options.BinDir))
        {
            var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
            var updated = string.IsNullOrEmpty(user) ? ctx.Options.BinDir : $"{user.TrimEnd(';')};{ctx.Options.BinDir}";
            Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        }
        ProcessRunner.PrependToPath(ctx.Options.BinDir);
        return Task.CompletedTask;
    }

    public override string? DoneDetail(InstallerContext ctx) => ctx.Options.ShimPath;

    /// <summary>Compara já expandido: entradas antigas podem estar como %LOCALAPPDATA%\…</summary>
    private static bool IsOnUserPath(string dir) =>
        (Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(e => Environment.ExpandEnvironmentVariables(e).TrimEnd('\\').Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
}
