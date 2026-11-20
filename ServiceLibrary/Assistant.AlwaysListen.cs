using ServiceLibrary.Audio;
using System.Globalization;
using System.Text;

namespace ServiceLibrary;

// Modo "sempre ouvindo": o microfone fica aberto, cada fala é transcrita e só vira comando se começar
// (ou terminar) com a palavra de ativação, ou se vier logo depois de uma resposta do Jarvis.
// O áudio nunca vai para o disco, e o texto de falas que não eram para ele é descartado sem registro.
public sealed partial class Assistant
{
    private static readonly TimeSpan EchoTail = TimeSpan.FromMilliseconds(800);
    private static readonly char[] EdgePunctuation = [' ', ',', '.', '!', '?', ';', ':', '-', '…', '"', '\''];
    private static readonly string[] YesWords = ["sim", "confirmo", "confirmado", "autorizo", "pode", "claro", "isso", "manda", "vai", "faca", "certo", "positivo"];
    private static readonly string[] NoWords = ["nao", "cancela", "cancelar", "cancele", "pare", "para", "negativo", "deixa", "esquece"];

    private VoiceActivityListener? _vad;
    private DateTime _lastSpokeAt = DateTime.MinValue;
    private DateTime _followUpUntil = DateTime.MinValue;
    private int _processingUtterance;      // 1 enquanto uma fala está sendo transcrita/tratada
    private bool _confirmAllowsVoice;
    private float _lastLevelSent;

    /// <summary>O microfone está aberto esperando a palavra de ativação?</summary>
    public bool AlwaysListening => _vad?.IsRunning == true;

    /// <summary>Sem conversa, fala nem confirmação em andamento (bom momento para reiniciar o app).</summary>
    public bool IsIdle => _state is "idle" or "offline" && !_mic.IsRecording;

    /// <summary>Liga ou desliga o modo "sempre ouvindo" e lembra a escolha no config.json.</summary>
    public void SetAlwaysListening(bool on)
    {
        _config.AlwaysListen = on;
        _config.Save();
        if (on) StartAlwaysListening();
        else StopAlwaysListening();
    }

    private void StartAlwaysListening()
    {
        if (_bootState != 2 || !_canListen || !_config.AlwaysListen) { SendListenMode(); return; }

        if (_vad is null)
        {
            _vad = new VoiceActivityListener(_config.ListenSensitivity) { ShouldIgnore = ShouldIgnoreMicrophone };
            _vad.Level += OnVadLevel;
            _vad.Utterance += wav => _ = OnUtteranceAsync(wav);
        }

        try
        {
            _vad.Start();
            Log.Info($"Microfone sempre aberto (palavra de ativação: \"{_config.WakeWord}\").");
        }
        catch (Exception ex)
        {
            Log.Error("Não consegui abrir o microfone no modo sempre ouvindo", ex);
            Caption("system", "Não consegui abrir o microfone para ouvir sempre. Segure Espaço para falar.");
        }
        SendListenMode();
    }

    private void StopAlwaysListening()
    {
        _vad?.Stop();
        SendListenMode();
    }

    private void SendListenMode() =>
        _post(new { type = "listenMode", always = AlwaysListening, word = _config.WakeWord });

    /// <summary>Descarta o áudio quando seria só o eco do próprio Jarvis ou quando ele já está ocupado.</summary>
    private bool ShouldIgnoreMicrophone() =>
        _bootState != 2
        || !_online
        || _state is "speaking" or "thinking" or "listening" or "booting" or "offline"
        || _mic.IsRecording
        || Volatile.Read(ref _processingUtterance) != 0
        || DateTime.UtcNow - _lastSpokeAt < EchoTail;

    /// <summary>Anima a esfera com a voz, sem inundar o HUD de mensagens.</summary>
    private void OnVadLevel(float level)
    {
        if (_state != "idle" && level != 0) return;
        if (Math.Abs(level - _lastLevelSent) < 0.04f && level != 0) return;
        _lastLevelSent = level;
        _post(new { type = "level", value = level });
    }

    // ------------------------------------------------------------------ uma fala

    private async Task OnUtteranceAsync(byte[] wav)
    {
        if (Interlocked.Exchange(ref _processingUtterance, 1) != 0) return;
        try
        {
            string text;
            try { text = await TranscribeWithRecoveryAsync(wav, CancellationToken.None); }
            catch (Exception ex)
            {
                Log.Error("Falha ao transcrever uma fala (modo sempre ouvindo)", ex);
                return;
            }
            if (string.IsNullOrWhiteSpace(text)) return;

            // Respondendo a uma confirmação: "sim" / "não" bastam, sem palavra de ativação.
            if (TryVoiceConfirm(text)) return;
            if (_state == "confirm") return; // esperando Enter/Esc ou "sim"/"não"; o resto é ignorado

            var command = ExtractCommand(text);
            if (command is null) return; // não era comigo: descarta sem registrar

            if (command.Length == 0)
            {
                // Só chamaram pelo nome: responde e deixa a próxima fala valer sem a palavra de ativação.
                await RunTurnAsync(_ => Task.FromResult<string?>(null), $"Pois não, {_config.UserTitle}?");
                return;
            }

            await RunTurnAsync(_ =>
            {
                Caption("user", command);
                return Task.FromResult<string?>(command);
            });
        }
        finally
        {
            Volatile.Write(ref _processingUtterance, 0);
        }
    }

    /// <summary>
    /// Devolve o comando (sem a palavra de ativação), "" se só chamaram pelo nome, ou null se a fala
    /// não era para o Jarvis. Dentro da janela de acompanhamento, qualquer fala vale.
    /// </summary>
    private string? ExtractCommand(string text)
    {
        var wake = LettersOnly(_config.WakeWord);
        var tokens = text.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);

        // "Jarvis, abre o Spotify" / "Ei, Jarvis, abre o Spotify" (a palavra entre as 3 primeiras).
        for (int i = 0; i < Math.Min(3, tokens.Length); i++)
            if (IsWakeWord(tokens[i], wake))
                return string.Join(' ', tokens.Skip(i + 1)).Trim(EdgePunctuation);

        // "Abre o Spotify, Jarvis"
        if (tokens.Length > 1 && IsWakeWord(tokens[^1], wake))
            return string.Join(' ', tokens.Take(tokens.Length - 1)).Trim(EdgePunctuation);

        if (DateTime.UtcNow < _followUpUntil) return text.Trim();
        return null;
    }

    /// <summary>Igual à palavra de ativação ou com um erro de transcrição (Jarvi, Jarves, Jarvis...).</summary>
    private static bool IsWakeWord(string token, string wake)
    {
        var word = LettersOnly(token);
        if (word.Length == 0 || wake.Length == 0) return false;
        if (word == wake) return true;
        return word.Length >= 5 && wake.Length >= 5 && Levenshtein(word, wake) <= 1;
    }

    /// <summary>Minúsculas, sem acentos e só letras e números.</summary>
    private static string LettersOnly(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    // ------------------------------------------------------------------ confirmação por voz

    /// <summary>Se há uma confirmação aberta e a fala é "sim" ou "não", responde por você.</summary>
    private bool TryVoiceConfirm(string text)
    {
        int id;
        lock (_gate)
        {
            if (_pendingConfirm is null || !_confirmAllowsVoice) return false;
            id = _confirmSeq;
        }

        var words = text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Aggregate(new StringBuilder(), (sb, c) => sb.Append(char.IsLetter(c) ? c : ' '))
            .ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // "Não" vence "sim" (ex.: "sim, não, cancela"): na dúvida, não executa.
        if (words.Any(w => NoWords.Contains(w))) { ResolveConfirm(id, false); return true; }
        if (words.Any(w => YesWords.Contains(w))) { ResolveConfirm(id, true); return true; }
        return false;
    }

    // ------------------------------------------------------------------ ganchos do restante do Assistant

    /// <summary>Abre a janela em que a próxima fala vale sem dizer o nome.</summary>
    private void MarkFollowUp() =>
        _followUpUntil = _config.FollowUpSeconds > 0 ? DateTime.UtcNow.AddSeconds(_config.FollowUpSeconds) : DateTime.MinValue;

    private async Task SpeakAsync(string text, CancellationToken ct)
    {
        try { await SpeakCoreAsync(text, ct); }
        finally { _lastSpokeAt = DateTime.UtcNow; }
    }
}
