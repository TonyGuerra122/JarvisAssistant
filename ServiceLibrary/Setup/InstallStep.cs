namespace ServiceLibrary.Setup;

public enum StepStatus
{
    /// <summary>Verificando / instalando.</summary>
    Working,
    /// <summary>Pronto (já estava ou acabou de ser instalado).</summary>
    Done,
    /// <summary>Opcional e não instalado; a instalação continua.</summary>
    Skipped,
    /// <summary>Falhou. Se o passo for obrigatório, a instalação para.</summary>
    Failed
}

/// <summary>Uma atualização de progresso. <see cref="Percent"/> só existe em downloads.</summary>
public sealed record InstallProgress(string StepId, string Title, StepStatus Status, string? Detail = null, double? Percent = null)
{
    public override string ToString() =>
        $"[{Status}] {Title}" + (Detail is { Length: > 0 } ? $" — {Detail}" : "") + (Percent is { } p ? $" ({p:0}%)" : "");
}

/// <summary>Falha de um passo, com uma mensagem que pode ser mostrada ao usuário.</summary>
public sealed class InstallException(string message) : Exception(message);

/// <summary>
/// Um passo da instalação. Cada passo é idempotente: <see cref="IsSatisfiedAsync"/>
/// diz se já está pronto, e <see cref="InstallAsync"/> só roda quando não está.
/// </summary>
public abstract class InstallStep
{
    /// <summary>Identificador curto e estável (usado como id da linha no HUD).</summary>
    public abstract string Id { get; }

    /// <summary>Texto mostrado ao usuário.</summary>
    public abstract string Title { get; }

    /// <summary>Se falhar, a instalação para? Passos opcionais só geram aviso.</summary>
    public virtual bool Required => true;

    /// <summary>O passo deve rodar com estas opções? (ex.: espeak só se pedido)</summary>
    public virtual bool IsEnabled(InstallerContext ctx) => true;

    public abstract Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct);

    /// <summary>Instala. Deve lançar <see cref="InstallException"/> com uma mensagem clara em caso de falha.</summary>
    public abstract Task InstallAsync(InstallerContext ctx, CancellationToken ct);

    /// <summary>Texto mostrado quando já estava pronto (ex.: versão encontrada).</summary>
    public virtual string? DoneDetail(InstallerContext ctx) => null;

    protected void Report(InstallerContext ctx, string detail, double? percent = null) =>
        ctx.Report(new InstallProgress(Id, Title, StepStatus.Working, detail, percent));
}
