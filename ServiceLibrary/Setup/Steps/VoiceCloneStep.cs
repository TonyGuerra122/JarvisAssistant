using ServiceLibrary.VoiceClone;

namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Voz clonada (XTTS-v2): copia o voice_server.py, cria o ambiente Python dele com o uv
/// (PyTorch com CUDA), prepara o áudio de referência e baixa o modelo (~1,8 GB).
/// Ambiente separado do OpenJarvis, para as dependências de um não quebrarem o outro.
/// Só roda quando o app está configurado com Voice = "clone".
/// </summary>
public sealed class VoiceCloneStep : InstallStep
{
    private const string MarkerName = ".jarvisassistant-voice";

    public override string Id => "setup-voiceclone";
    public override string Title => "Voz clonada (XTTS-v2)";

    // Sem ela, o app volta para a voz do OpenJarvis.
    public override bool Required => false;

    public override bool IsEnabled(InstallerContext ctx) => !string.IsNullOrWhiteSpace(ctx.Options.CloneVoiceFile);

    private static string Marker => Path.Combine(VoiceCloneServer.Dir, MarkerName);

    public override async Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct) =>
        VoiceCloneServer.IsInstalled && File.Exists(Marker) &&
        await File.ReadAllTextAsync(Marker, ct) == Fingerprint(ctx);

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        var uv = ctx.Uv ?? throw new InstallException("uv não encontrado.");
        var source = Environment.ExpandEnvironmentVariables(ctx.Options.CloneVoiceFile);
        if (!File.Exists(source)) throw new InstallException($"Áudio da voz não encontrado: {source}");

        Directory.CreateDirectory(VoiceCloneServer.Dir);
        foreach (var file in new[] { "pyproject.toml", "voice_server.py" })
        {
            var from = Path.Combine(VoiceCloneServer.BundledDir, file);
            if (!File.Exists(from)) throw new InstallException($"Arquivo do app faltando: {from}");
            File.Copy(from, Path.Combine(VoiceCloneServer.Dir, file), overwrite: true);
        }

        Report(ctx, "instalando PyTorch + XTTS (~3 GB na primeira vez)");
        var sync = await ctx.RunAsync(this, uv, ["sync", "--python", "3.12"], VoiceCloneServer.Dir, TimeSpan.FromMinutes(60), ct: ct);
        if (!sync.Ok) throw new InstallException("uv sync da voz clonada falhou: " + sync.Tail(3));

        Report(ctx, "preparando o áudio de referência");
        var prep = await ctx.RunAsync(this, VoiceCloneServer.Python, [VoiceCloneServer.Script, "prepare", source],
            VoiceCloneServer.Dir, TimeSpan.FromMinutes(5), ct: ct);
        if (!prep.Ok) throw new InstallException("Não consegui preparar o áudio: " + prep.Tail(2));

        Report(ctx, "baixando o modelo XTTS-v2 (~1,8 GB)");
        var env = new Dictionary<string, string> { ["COQUI_TOS_AGREED"] = "1" };
        var dl = await ctx.RunAsync(this, VoiceCloneServer.Python, [VoiceCloneServer.Script, "download"],
            VoiceCloneServer.Dir, TimeSpan.FromMinutes(60), env, ct);
        if (!dl.Ok) throw new InstallException("Não consegui baixar o modelo de voz: " + dl.Tail(2));

        await File.WriteAllTextAsync(Marker, Fingerprint(ctx), ct);
    }

    public override string? DoneDetail(InstallerContext ctx) => Path.GetFileName(ctx.Options.CloneVoiceFile);

    /// <summary>Se o áudio ou os arquivos do serviço mudarem, refaz a instalação/preparação.</summary>
    private static string Fingerprint(InstallerContext ctx)
    {
        static string Stamp(string path) => File.Exists(path) ? $"{new FileInfo(path).Length}@{File.GetLastWriteTimeUtc(path):O}" : "-";
        var source = Environment.ExpandEnvironmentVariables(ctx.Options.CloneVoiceFile);
        return string.Join(';',
            "src=" + Path.GetFullPath(source) + "|" + Stamp(source),
            "py=" + Stamp(Path.Combine(VoiceCloneServer.BundledDir, "pyproject.toml")),
            "srv=" + Stamp(Path.Combine(VoiceCloneServer.BundledDir, "voice_server.py")));
    }
}
