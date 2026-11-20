using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ServiceLibrary.Commands;

// Ferramentas de temporizador, alarme e lembrete (a lógica de agenda fica em ReminderService).
public sealed partial class CommandRunner
{
    private ReminderService? _reminders;

    private ReminderService Reminders => _reminders ??= new ReminderService(_notify, _config.UserTitle);

    /// <summary>Liga o agendador (e avisa o que passou com o app fechado). Chamar após a inicialização.</summary>
    public void Start() => Reminders.Start();

    public void Dispose() => _reminders?.Dispose();

    private static void AddReminderTools(Action<JsonObject, string> add)
    {
        add(Tool("temporizador", "Temporizador de contagem regressiva ('daqui a 10 minutos'). Fala quando termina.",
            Props(("acao", Choice("Ação", ["criar", "listar", "cancelar"])),
                  ("minutos", Int("Para 'criar': daqui a quantos minutos")),
                  ("mensagem", Str("Para 'criar': o que lembrar (opcional)")),
                  ("id", Int("Para 'cancelar': número do temporizador; sem número, cancela todos"))),
            required: ["acao"]), "temporizador");

        add(Tool("alarme", "Alarme em um horário do dia, com som e fala. Pode repetir em certos dias. 'adiar' é a soneca do último alarme que tocou.",
            Props(("acao", Choice("Ação", ["criar", "listar", "cancelar", "adiar"])),
                  ("hora", Str("Para 'criar': horário em 24 h, no formato HH:mm (ex.: 07:30)")),
                  ("data", Str("Para 'criar' (opcional): data no formato AAAA-MM-DD; sem data é o próximo horário que chegar")),
                  ("repetir", Str("Para 'criar' (opcional): todos_os_dias, dias_uteis, fins_de_semana, ou dias como 'seg,qua,sex'")),
                  ("mensagem", Str("Para 'criar': rótulo do alarme (opcional)")),
                  ("minutos", Int("Para 'adiar': por quantos minutos (padrão 10)")),
                  ("id", Int("Para 'cancelar': número do alarme; sem número, cancela todos"))),
            required: ["acao"]), "alarme");

        add(Tool("lembrete", "Aviso falado em um momento: 'me lembre de X às 15h' ou 'daqui a 30 minutos'. Fica salvo mesmo se o app fechar.",
            Props(("acao", Choice("Ação", ["criar", "listar", "cancelar"])),
                  ("mensagem", Str("Para 'criar': o que lembrar")),
                  ("quando", Str("Para 'criar': data e hora no formato AAAA-MM-DD HH:mm (use a data de hoje do contexto)")),
                  ("minutos", Int("Para 'criar': alternativa ao 'quando', daqui a quantos minutos")),
                  ("id", Int("Para 'cancelar': número do lembrete; sem número, cancela todos"))),
            required: ["acao"]), "lembrete");
    }

    // ------------------------------------------------------------------ temporizador

    private string TimerTool(JsonObject args)
    {
        var acao = S(args, "acao");
        switch (acao)
        {
            case "criar":
            {
                if (I(args, "minutos") is null or <= 0) return "Diga em quantos minutos.";
                var mins = Math.Min(I(args, "minutos")!.Value, 24 * 60);
                var r = Reminders.Add(ReminderKind.Timer, DateTime.Now.AddMinutes(mins), S(args, "mensagem"));
                return r is null ? TooMany() : $"Temporizador {r.Id} criado para {r.At:HH:mm} (daqui a {mins} minuto(s)).";
            }
            case "listar": return ListText(ReminderKind.Timer, "temporizadores");
            case "cancelar": return CancelText(ReminderKind.Timer, I(args, "id"), "temporizador");
            default: return "Ação de temporizador desconhecida.";
        }
    }

    // ------------------------------------------------------------------ alarme

    private string AlarmTool(JsonObject args)
    {
        var acao = S(args, "acao");
        switch (acao)
        {
            case "criar":
            {
                if (!TryParseTime(S(args, "hora"), out var time)) return "Não entendi a hora. Use o formato HH:mm, por exemplo 07:30.";
                if (!ReminderService.TryParseRepeat(S(args, "repetir").Replace('_', ' '), out var mask))
                    return "Não entendi a repetição. Use todos os dias, dias úteis, fins de semana ou dias como seg,qua,sex.";

                DateTime? date = null;
                if (S(args, "data") is { Length: > 0 } d)
                {
                    if (!DateTime.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                        return "Não entendi a data. Use AAAA-MM-DD.";
                    date = parsed.Date;
                }

                var next = ReminderService.NextOccurrence(time, mask, date, DateTime.Now);
                if (next is null) return "Essa data e hora já passaram.";

                var r = Reminders.Add(ReminderKind.Alarm, next.Value, S(args, "mensagem"), mask, time.ToString(@"hh\:mm"));
                if (r is null) return TooMany();
                var rep = mask == 0 ? "" : $", repete: {ReminderService.DescribeRepeat(mask)}";
                return $"Alarme {r.Id} criado para {ReminderService.DescribeWhen(r.At)}{rep}.";
            }
            case "listar": return ListText(ReminderKind.Alarm, "alarmes");
            case "cancelar": return CancelText(ReminderKind.Alarm, I(args, "id"), "alarme");
            case "adiar":
            {
                var last = Reminders.LastFired;
                if (last is null) return "Nenhum alarme tocou ainda para adiar.";
                var mins = Math.Clamp(I(args, "minutos") ?? 10, 1, 120);
                var r = Reminders.Add(ReminderKind.Alarm, DateTime.Now.AddMinutes(mins), last.Message);
                return r is null ? TooMany() : $"Alarme adiado: toca de novo às {r.At:HH:mm} (daqui a {mins} minuto(s)).";
            }
            default: return "Ação de alarme desconhecida.";
        }
    }

    // ------------------------------------------------------------------ lembrete

    private string ReminderTool(JsonObject args)
    {
        var acao = S(args, "acao");
        switch (acao)
        {
            case "criar":
            {
                var msg = S(args, "mensagem");
                if (msg.Length == 0) return "Do que devo lembrar?";

                DateTime at;
                if (I(args, "minutos") is > 0 and var mins)
                    at = DateTime.Now.AddMinutes(Math.Min(mins, 60 * 24 * 365));
                else if (S(args, "quando") is { Length: > 0 } q)
                {
                    if (!DateTime.TryParse(q, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out at))
                        return "Não entendi o momento. Use AAAA-MM-DD HH:mm.";
                    if (at <= DateTime.Now) return "Esse momento já passou.";
                }
                else return "Para quando? Diga a hora ou daqui a quantos minutos.";

                var r = Reminders.Add(ReminderKind.Reminder, at, msg);
                return r is null ? TooMany() : $"Lembrete {r.Id} criado para {ReminderService.DescribeWhen(r.At)}.";
            }
            case "listar": return ListText(ReminderKind.Reminder, "lembretes");
            case "cancelar": return CancelText(ReminderKind.Reminder, I(args, "id"), "lembrete");
            default: return "Ação de lembrete desconhecida.";
        }
    }

    // ------------------------------------------------------------------ comuns

    private string ListText(ReminderKind kind, string plural)
    {
        var items = Reminders.List(kind);
        if (items.Count == 0) return $"Não há {plural} ativos.";
        return string.Join("; ", items.Select(r =>
        {
            var text = $"{r.Id}: {ReminderService.DescribeWhen(r.At)}";
            if (r.RepeatDays != 0) text += $" ({ReminderService.DescribeRepeat(r.RepeatDays)})";
            if (r.Message.Length > 0) text += $" — {Cut(r.Message, 60)}";
            return text;
        }));
    }

    private string CancelText(ReminderKind kind, int? id, string singular)
    {
        var n = Reminders.Cancel(kind, id);
        if (n == 0) return id is null ? $"Não havia {singular}s." : $"Não existe {singular} {id}.";
        return n == 1 ? $"1 {singular} cancelado." : $"{n} {singular}s cancelados.";
    }

    private static string TooMany() => $"Já existem {ReminderService.MaxItems} itens agendados; cancele alguns antes.";

    /// <summary>Aceita "07:30", "7:30", "7h30", "7h" e "19".</summary>
    private static bool TryParseTime(string text, out TimeSpan time)
    {
        time = default;
        var m = Regex.Match(text.Trim(), @"^(\d{1,2})(?:[:hH](\d{2})?)?$");
        if (!m.Success) return false;
        var h = int.Parse(m.Groups[1].Value);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (h > 23 || min > 59) return false;
        time = new TimeSpan(h, min, 0);
        return true;
    }
}
