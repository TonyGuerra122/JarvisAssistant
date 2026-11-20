using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ServiceLibrary.Setup.Steps;

/// <summary>
/// Baixa no Ollama o modelo que o OpenJarvis vai usar de fato (ver <see cref="InstallerOptions.ResolveModel"/>),
/// com progresso. Roda depois do <see cref="ConfigStep"/> para seguir o modelo que o "jarvis init" escolheu.
/// </summary>
public sealed class ModelStep : InstallStep
{
    public override string Id => "setup-model";
    public override string Title => "Modelo de linguagem";

    // Sem modelo o app ainda abre (e o usuário pode baixar outro depois).
    public override bool Required => false;

    public override bool IsEnabled(InstallerContext ctx) => ctx.Options.ResolveModel() is not null;

    public override async Task<bool> IsSatisfiedAsync(InstallerContext ctx, CancellationToken ct)
    {
        var want = Normalize(ctx.Options.ResolveModel()!);
        var json = await ctx.Http.GetFromJsonAsync<JsonNode>($"{OllamaStep.ApiUrl}/api/tags", ct);
        return json?["models"]?.AsArray().Any(m => Normalize(m?["name"]?.GetValue<string>() ?? "") == want) == true;
    }

    public override async Task InstallAsync(InstallerContext ctx, CancellationToken ct)
    {
        var model = ctx.Options.ResolveModel()!;
        Report(ctx, $"baixando {model}");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{OllamaStep.ApiUrl}/api/pull")
        {
            Content = JsonContent.Create(new { model, stream = true })
        };
        using var res = await ctx.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        // Resposta em linhas JSON: {status, total, completed} … {status:"success"} ou {error}.
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(ct));
        var lastPct = -1;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            var node = JsonNode.Parse(line);
            if (node?["error"]?.GetValue<string>() is { } error) throw new InstallException($"Falha ao baixar {model}: {error}");

            var status = node?["status"]?.GetValue<string>() ?? "";
            var total = node?["total"]?.GetValue<long>() ?? 0;
            var done = node?["completed"]?.GetValue<long>() ?? 0;

            if (total > 0)
            {
                var pct = (int)(done * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    Report(ctx, $"baixando {model}: {done / 1_048_576} / {total / 1_048_576} MB", pct);
                }
            }
            else if (status.Length > 0)
            {
                Report(ctx, $"{model}: {status}");
            }
        }
    }

    public override string? DoneDetail(InstallerContext ctx) => ctx.Options.ResolveModel();

    private static string Normalize(string name) => name.Contains(':') ? name.ToLowerInvariant() : name.ToLowerInvariant() + ":latest";
}
