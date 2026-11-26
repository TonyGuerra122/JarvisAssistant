namespace PluginLibrary.Sdk;

/// <summary>Um comando que o J.A.R.V.I.S pode executar.</summary>
public interface IJarvisCommand
{
    /// <summary>Nome único, sem espaços. Ex.: "espaco_em_disco".</summary>
    string Name { get; }

    /// <summary>Explica ao modelo QUANDO usar o comando. Capriche: é isso que ele lê.</summary>
    string Description { get; }

    /// <summary>Se true, o app pede confirmação (Enter/Esc) antes de executar.</summary>
    bool RequiresConfirmation { get; }

    /// <summary>Argumentos que o modelo deve preencher. Vazio se o comando não precisa de nenhum.</summary>
    IReadOnlyList<CommandParameter> Parameters => [];

    /// <summary>Executa e devolve um texto curto com o resultado (o modelo transforma em fala).</summary>
    Task<string> ExecuteAsync(CommandContext context, CancellationToken ct);
}
