using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ServiceLibrary.Commands;

public enum ReminderKind { Timer, Alarm, Reminder }

/// <summary>Um temporizador, alarme ou lembrete agendado.</summary>
public sealed class Reminder
{
    public int Id { get; set; }
    public ReminderKind Kind { get; set; }

    /// <summary>Próxima vez que dispara (hora local).</summary>
    public DateTime At { get; set; }

    public string Message { get; set; } = "";

    /// <summary>Dias em que repete: bit n = <see cref="DayOfWeek"/> n. 0 = dispara uma vez só.</summary>
    public int RepeatDays { get; set; }

    /// <summary>Hora do dia ("HH:mm") dos alarmes que repetem.</summary>
    public string Time { get; set; } = "";
}

/// <summary>
/// Temporizadores, alarmes e lembretes. Ficam salvos em disco, então sobrevivem ao fechamento do app:
/// o que passou enquanto ele estava fechado é avisado (se foi há menos de 12 h) na próxima abertura.
/// </summary>
public sealed class ReminderService : IDisposable
{
    private static readonly TimeSpan MaxMissedAge = TimeSpan.FromHours(12);
    public const int MaxItems = 50;

    private readonly Func<string, Task> _notify;
    private readonly string _title;
    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly List<Reminder> _items = [];
    private readonly CancellationTokenSource _stop = new();
    private Task _fireChain = Task.CompletedTask;
    private bool _started;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ReminderService(Func<string, Task> notify, string userTitle, string? file = null)
    {
        _notify = notify;
        _title = userTitle;
        _file = file ?? Path.Combine(AppPaths.LocalData, "reminders.json");
        Load();
    }

    /// <summary>Último item que tocou: serve para o "soneca" (adiar).</summary>
    public Reminder? LastFired { get; private set; }

    // ------------------------------------------------------------------ API

    public Reminder? Add(ReminderKind kind, DateTime at, string message, int repeatDays = 0, string time = "")
    {
        lock (_gate)
        {
            if (_items.Count >= MaxItems) return null;
            var id = _items.Count == 0 ? 1 : _items.Max(r => r.Id) + 1;
            var r = new Reminder { Id = id, Kind = kind, At = at, Message = message.Trim(), RepeatDays = repeatDays, Time = time };
            _items.Add(r);
            Save();
            return r;
        }
    }

    public List<Reminder> List(ReminderKind kind)
    {
        lock (_gate) return [.. _items.Where(r => r.Kind == kind).OrderBy(r => r.At)];
    }

    /// <summary>Cancela um item (por id) ou todos do tipo (id nulo). Devolve quantos saíram.</summary>
    public int Cancel(ReminderKind kind, int? id)
    {
        lock (_gate)
        {
            var n = _items.RemoveAll(r => r.Kind == kind && (id is null || r.Id == id));
            if (n > 0) Save();
            return n;
        }
    }

    /// <summary>Liga o agendador e avisa o que passou com o app fechado. Chamar depois da inicialização.</summary>
    public void Start()
    {
        List<Reminder> missed = [];
        lock (_gate)
        {
            if (_started) return;
            _started = true;

            var now = DateTime.Now;
            foreach (var r in _items.Where(r => r.At < now.AddMinutes(-1)).ToList())
            {
                if (now - r.At < MaxMissedAge) missed.Add(Clone(r));
                Advance(r, now);
            }
            Save();
        }

        if (missed.Count > 0)
        {
            var text = $"{_title}, enquanto eu estava fechado, passou: " +
                       string.Join("; ", missed.Select(m => $"{Label(m)} das {m.At:HH:mm}" + (m.Message.Length > 0 ? $" ({m.Message})" : "")));
            Enqueue(() => _notify(text));
        }

        _ = Task.Run(() => LoopAsync(_stop.Token));
    }

    public void Dispose() => _stop.Cancel();

    // ------------------------------------------------------------------ agendador

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                List<Reminder> due;
                lock (_gate)
                {
                    var now = DateTime.Now;
                    due = [.. _items.Where(r => r.At <= now).Select(Clone)];
                    foreach (var r in _items.Where(r => r.At <= now).ToList()) Advance(r, now);
                    if (due.Count > 0) Save();
                }

                foreach (var r in due)
                {
                    LastFired = r;
                    Log.Info($"Disparou {r.Kind} #{r.Id}: {r.Message}");
                    Enqueue(() => FireAsync(r));
                }

                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { /* app fechando */ }
        catch (Exception ex) { Log.Error("O agendador de lembretes parou", ex); }
    }

    /// <summary>Fila única: dois avisos ao mesmo tempo falariam um por cima do outro.</summary>
    private void Enqueue(Func<Task> work)
    {
        lock (_gate)
        {
            _fireChain = _fireChain.ContinueWith(async _ =>
            {
                try { await work(); }
                catch (Exception ex) { Log.Error("Falha ao avisar um lembrete", ex); }
            }, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task FireAsync(Reminder r)
    {
        if (r.Kind == ReminderKind.Alarm) await PlayAlarmToneAsync();
        await _notify(SpokenText(r));
    }

    private string SpokenText(Reminder r) => r.Kind switch
    {
        ReminderKind.Timer => r.Message.Length == 0
            ? $"{_title}, o temporizador terminou."
            : $"{_title}, o temporizador terminou: {r.Message}.",
        ReminderKind.Alarm => r.Message.Length == 0
            ? $"{_title}, é hora do alarme das {r.At:HH:mm}."
            : $"{_title}, alarme das {r.At:HH:mm}: {r.Message}.",
        _ => r.Message.Length == 0
            ? $"{_title}, você pediu um aviso agora."
            : $"{_title}, lembrete: {r.Message}.",
    };

    private static string Label(Reminder r) => r.Kind switch
    {
        ReminderKind.Timer => "o temporizador",
        ReminderKind.Alarm => "o alarme",
        _ => "o lembrete",
    };

    /// <summary>Depois de disparar: repete no próximo dia válido ou remove.</summary>
    private void Advance(Reminder r, DateTime now)
    {
        if (r.RepeatDays != 0 && TimeSpan.TryParse(r.Time, out var t))
        {
            var next = NextOccurrence(t, r.RepeatDays, null, now);
            if (next is not null) { r.At = next.Value; return; }
        }
        _items.Remove(r);
    }

    private static Reminder Clone(Reminder r) => new()
    {
        Id = r.Id, Kind = r.Kind, At = r.At, Message = r.Message, RepeatDays = r.RepeatDays, Time = r.Time,
    };

    // ------------------------------------------------------------------ cálculo de horários

    /// <summary>
    /// Próxima vez em que <paramref name="time"/> acontece. Sem repetição: hoje (ou na data dada);
    /// se a hora de hoje já passou, amanhã. Com repetição: o próximo dia marcado na máscara.
    /// </summary>
    public static DateTime? NextOccurrence(TimeSpan time, int repeatMask, DateTime? date, DateTime now)
    {
        if (repeatMask == 0)
        {
            var at = (date?.Date ?? now.Date) + time;
            if (at > now) return at;
            return date is null ? at.AddDays(1) : null; // data explícita no passado: inválida
        }

        for (int i = 0; i <= 7; i++)
        {
            var at = now.Date.AddDays(i) + time;
            if (at > now && (repeatMask & (1 << (int)at.DayOfWeek)) != 0) return at;
        }
        return null;
    }

    /// <summary>Entende "todos os dias", "dias úteis", "fim de semana" ou uma lista como "seg, qua e sex".</summary>
    public static bool TryParseRepeat(string text, out int mask)
    {
        mask = 0;
        var s = Normalize(text);
        if (s is "" or "nao" or "uma vez" or "nenhum" or "nunca") return true;
        if (s.Contains("todo") && (s.Contains("dia"))) { mask = 0b1111111; return true; }
        if (s is "diario" or "diariamente") { mask = 0b1111111; return true; }
        if (s.Contains("util") || s.Contains("uteis")) { mask = 0b0111110; return true; }
        if (s.Contains("fim de semana") || s.Contains("fins de semana")) { mask = 0b1000001; return true; }

        string[] days = ["dom", "seg", "ter", "qua", "qui", "sex", "sab"]; // índice = DayOfWeek
        foreach (var token in s.Split([' ', ',', ';', '/', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var i = Array.FindIndex(days, d => token.StartsWith(d, StringComparison.Ordinal));
            if (i >= 0) mask |= 1 << i;
        }
        return mask != 0;
    }

    public static string DescribeRepeat(int mask) => mask switch
    {
        0 => "uma vez",
        0b1111111 => "todos os dias",
        0b0111110 => "dias úteis",
        0b1000001 => "fins de semana",
        _ => string.Join(", ", new[] { "dom", "seg", "ter", "qua", "qui", "sex", "sáb" }.Where((_, i) => (mask & (1 << i)) != 0)),
    };

    /// <summary>"hoje às 07:30", "amanhã às 07:30" ou "21/11 às 07:30".</summary>
    public static string DescribeWhen(DateTime at)
    {
        var days = (at.Date - DateTime.Today).Days;
        var day = days == 0 ? "hoje" : days == 1 ? "amanhã" : at.ToString("dd/MM");
        return $"{day} às {at:HH:mm}";
    }

    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ som e disco

    /// <summary>Três bipes agudos, para o alarme chamar atenção antes da fala.</summary>
    private static async Task PlayAlarmToneAsync()
    {
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var tone = new SignalGenerator(44100, 1) { Gain = 0.25, Frequency = 880, Type = SignalGeneratorType.Sin };
                var beep = new OffsetSampleProvider(tone) { Take = TimeSpan.FromMilliseconds(250) };
                using var output = new WaveOut();
                output.Init(beep.ToWaveProvider());
                output.Play();
                await Task.Delay(350);
            }
        }
        catch (Exception ex) { Log.Error("Não consegui tocar o som do alarme", ex); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var list = JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(_file), Json);
            if (list is not null) _items.AddRange(list);
        }
        catch (Exception ex) { Log.Error("Não consegui ler reminders.json", ex); }
    }

    // Chamado sempre dentro do lock.
    private void Save()
    {
        try { File.WriteAllText(_file, JsonSerializer.Serialize(_items, Json)); }
        catch (Exception ex) { Log.Error("Não consegui salvar reminders.json", ex); }
    }
}
