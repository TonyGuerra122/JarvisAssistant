namespace ServiceLibrary.Setup.Steps;

/// <summary>Clona (ou atualiza) o repositório do OpenJarvis em &lt;InstallRoot&gt;\src.</summary>
public sealed class RepositoryStep : InstallStep
{
    public override string Id => "setup-repo";
    public override string Title => "Código do OpenJarvis";

    private static bool IsCloned(InstallerContext ctx) => Directory.Exists(Path.Combine(ctx.Options.SourceDir, ".git"));

    public override Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct) =>
        Task.FromResult(IsCloned(ctx) && !(ctx.Options.Update && !ctx.SourceChanged));

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        var git = ctx.Git ?? throw new InstallException("git não encontrado.");
        var src = ctx.Options.SourceDir;
        Directory.CreateDirectory(ctx.Options.InstallRoot);

        ProcessResult r;
        if (IsCloned(ctx))
        {
            Report(ctx, "atualizando (git pull)");
            r = await ctx.RunAsync(this, git, ["-C", src, "pull", "--ff-only"], timeout: TimeSpan.FromMinutes(10), ct: ct);
            if (!r.Ok) throw new InstallException("git pull falhou: " + r.Tail(2));
        }
        else
        {
            // Uma pasta src sobrando de uma tentativa interrompida impede o clone.
            if (Directory.Exists(src) && Directory.EnumerateFileSystemEntries(src).Any())
            {
                var backup = src + ".old-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Directory.Move(src, backup);
                Log.Info($"Pasta src incompleta movida para {backup}");
            }

            Report(ctx, "clonando " + ctx.Options.RepoUrl);
            r = await ctx.RunAsync(this, git, ["clone", "--filter=blob:none", ctx.Options.RepoUrl, src],
                timeout: TimeSpan.FromMinutes(20), ct: ct);
            if (!r.Ok) throw new InstallException("git clone falhou: " + r.Tail(2));
        }

        ctx.SourceChanged = true;
    }

    public override string? DoneDetail(InstallerContext ctx) => ctx.Options.SourceDir;
}
