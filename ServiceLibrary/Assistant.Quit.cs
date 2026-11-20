using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ServiceLibrary;

// Encerrar o Jarvis por voz ("Jarvis, encerrar"). Frases claras são reconhecidas aqui mesmo, sem depender do
// modelo (modelos pequenos erram chamadas de ferramenta); paráfrases ("pode ir dormir") chegam pela ferramenta
// "encerrar_jarvis". Nos dois casos vale a política da ação (padrão: pede confirmação, que também aceita "sim").
public sealed partial class Assistant
{
    private bool _quitRequested;

    /// <summary>O usuário pediu para encerrar o programa por completo (disparado depois da despedida).</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Se o texto é um pedido claro de encerrar o Jarvis, executa (com a confirmação da política) e devolve a fala
    /// de resposta. Devolve null se não for um pedido desse tipo, e o texto segue para o modelo.
    /// </summary>
    private async Task<string?> TryQuitPhraseAsync(string text, CancellationToken ct)
    {
        if (!_config.CommandsEnabled || !IsQuitPhrase(text)) return null;

        await _commands.ExecuteAsync("encerrar_jarvis", new JsonObject(), ct);
        return _quitRequested
            ? $"Até logo, {_config.UserTitle}."
            : $"Certo, {_config.UserTitle}. Continuo por aqui.";
    }

    /// <summary>Chamado ao fim de cada ciclo de conversa: fecha o app se o encerramento foi confirmado.</summary>
    private void FinishQuitIfRequested()
    {
        if (!_quitRequested) return;
        _quitRequested = false;
        Log.Info("Encerramento pedido por voz; fechando o J.A.R.V.I.S.");
        QuitRequested?.Invoke();
    }

    /// <summary>
    /// Frases como "encerrar", "encerra o Jarvis", "pode fechar o assistente", "sair", "tchau".
    /// Verbos ambíguos (fechar, desligar) só valem se o alvo for o Jarvis; nunca "fecha tudo" nem "desliga o PC".
    /// </summary>
    private static bool IsQuitPhrase(string text)
    {
        var s = Words(text);
        s = Fillers().Replace(s, " ").Trim();
        s = Regex.Replace(s, @"\s+", " ");
        return QuitAlone().IsMatch(s) || QuitWithTarget().IsMatch(s);
    }

    private static string Words(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\b(por favor|pode|poderia|agora|ja|entao|ok|obrigado|obrigada|de vez|por completo|totalmente)\b")]
    private static partial Regex Fillers();

    // Verbos que só podem significar "sair" (sozinhos já valem).
    [GeneratedRegex(@"^(encerr\w*|finaliz\w*|desativ\w*|sair|sai|saia)( o| a)?( jarvis| assistente)?$")]
    private static partial Regex QuitAlone();

    // Verbos ambíguos: exigem dizer o alvo.
    [GeneratedRegex(@"^(fech\w*|deslig\w*|termin\w*|mata\w*)( o| a)? (jarvis|assistente)$")]
    private static partial Regex QuitWithTarget();
}
