using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceLibrary;

/// <summary>
/// Cliente da API local do OpenJarvis ("jarvis serve").
/// Rotas usadas: /health, /v1/models, /v1/chat/completions,
/// /v1/speech/health, /v1/speech/transcribe, /v1/speech/tts/health, /v1/speech/synthesize.
/// </summary>
public sealed class OpenJarvisClient : IDisposable
{
    private readonly HttpClient _http;

    public OpenJarvisClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            // Modelos locais na CPU podem demorar na primeira resposta.
            Timeout = TimeSpan.FromMinutes(3)
        };
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var res = await _http.GetAsync("health", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Primeiro modelo da lista. O OpenJarvis coloca o modelo configurado em primeiro.</summary>
    public async Task<string?> GetDefaultModelAsync(CancellationToken ct = default)
    {
        var json = await _http.GetFromJsonAsync<JsonNode>("v1/models", ct);
        return json?["data"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
    }

    public async Task<(bool Available, string? Detail)> SpeechHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetFromJsonAsync<JsonNode>("v1/speech/health", ct);
            var ok = json?["available"]?.GetValue<bool>() ?? false;
            return (ok, json?["voice_id"]?.ToString() ?? json?["reason"]?.ToString());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Available, string? Detail)> TtsHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetFromJsonAsync<JsonNode>("v1/speech/tts/health", ct);
            var ok = json?["available"]?.GetValue<bool>() ?? false;
            return (ok, json?["voice_id"]?.ToString() ?? json?["reason"]?.ToString());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string> TranscribeAsync(byte[] wav, string? language, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "audio.wav");
        if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language), "language");

        using var res = await _http.PostAsync("v1/speech/transcribe", form, ct);
        await EnsureOk(res, "transcrição");
        var json = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
        return json?["text"]?.GetValue<string>()?.Trim() ?? string.Empty;
    }

    public async Task<string> ChatAsync(string model, IEnumerable<ChatTurn> messages, int maxTokens, CancellationToken ct = default)
    {
        var body = new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = maxTokens,
            temperature = 0.6,
            stream = false
        };

        using var res = await _http.PostAsJsonAsync("v1/chat/completions", body, ct);
        await EnsureOk(res, "resposta do modelo");
        var json = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim() ?? string.Empty;
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voiceId, float? speed = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object> { ["text"] = text };
        if (!string.IsNullOrWhiteSpace(voiceId)) body["voice_id"] = voiceId;
        if (speed is > 0) body["speed"] = speed.Value;
        using var res = await _http.PostAsJsonAsync("v1/speech/synthesize", body, ct);
        await EnsureOk(res, "síntese de voz");

        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Chat com suporte a ferramentas (function calling). Devolve a mensagem do
    /// assistente inteira: pode ter "content" (texto) e/ou "tool_calls" (ações pedidas).
    /// </summary>
    public async Task<JsonObject> ChatRawAsync(string model, JsonArray messages, JsonArray? tools, int maxTokens, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone(),
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.4,
            ["stream"] = false,
        };
        if (tools is { Count: > 0 }) body["tools"] = tools.DeepClone();

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync("v1/chat/completions", content, ct);
        await EnsureOk(res, "resposta do modelo");
        var json = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
        return json?["choices"]?[0]?["message"] as JsonObject ?? [];
    }

    private static async Task EnsureOk(HttpResponseMessage res, string what)
    {
        if (res.IsSuccessStatusCode) return;
        var details = await res.Content.ReadAsStringAsync();

        throw new HttpRequestException($"Falha na {what} ({(int)res.StatusCode}): {details}");
    }

    public void Dispose() => _http.Dispose();
}
