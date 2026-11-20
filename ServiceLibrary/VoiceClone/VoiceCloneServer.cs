using System.Diagnostics;
using System.Net.Http.Json;

namespace ServiceLibrary.VoiceClone;

/// <summary>
/// Serviço local de voz clonada (XTTS-v2, ver voice_server.py), em %LOCALAPPDATA%\JarvisAssistant\VoiceClone.
/// Inicia o processo Python sem janela, fala com ele por HTTP e encerra ao fechar o app
/// (só o processo que ele mesmo iniciou).
/// </summary>
public sealed class VoiceCloneServer : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _port;
    private Process? _process;

    public VoiceCloneServer(int port)
    {
        _port = port;
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public static string Dir => Path.Combine(AppPaths.LocalData, "VoiceClone");
    public static string Python => Path.Combine(Dir, ".venv", "Scripts", "python.exe");
    public static string Script => Path.Combine(Dir, "voice_server.py");
    public static string Reference => Path.Combine(Dir, "reference.wav");
    public static string LogFile => Path.Combine(AppPaths.LocalData, "voice-clone.log");

    /// <summary>Pasta de onde o app copia o pyproject.toml e o voice_server.py.</summary>
    public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "VoiceClone");

    public static bool IsInstalled => File.Exists(Python) && File.Exists(Script) && File.Exists(Reference);

    /// <summary>O processo que iniciamos já fechou (normalmente por erro).</summary>
    public bool Exited => _process is { HasExited: true };

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var res = await _http.GetAsync("health", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        if (_process is { HasExited: false }) return;

        var psi = new ProcessStartInfo(Python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Dir
        };
        foreach (var a in new[] { "-u", Script, "serve", "--port", _port.ToString() }) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["COQUI_TOS_AGREED"] = "1";

        try { File.WriteAllText(LogFile, ""); } catch { /* best-effort */ }
        _process = Process.Start(psi);
        if (_process is null) return;

        _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        Log.Info($"Voz clonada iniciada (PID {_process.Id})");
    }

    /// <summary>Inicia (se preciso) e espera ficar pronto. Carregar o modelo leva de 20 s a 1 min.</summary>
    public async Task<bool> EnsureRunningAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (await IsHealthyAsync(ct)) return true;
        if (!IsInstalled) return false;

        Start();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !Exited)
        {
            await Task.Delay(1500, ct);
            if (await IsHealthyAsync(ct)) return true;
        }
        return false;
    }

    public async Task<byte[]> SynthesizeAsync(string text, float speed, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync("synthesize", new { text, speed, language = "pt" }, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Falha na voz clonada ({(int)res.StatusCode}): {await res.Content.ReadAsStringAsync(ct)}");
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Últimas linhas do log, para mostrar em caso de falha.</summary>
    public static string TailLog(int lines = 2)
    {
        try
        {
            using var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var all = new StreamReader(fs).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" | ", all.TakeLast(lines).Select(l => l.Trim()));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static readonly Lock LogGate = new();

    private static void AppendLog(string? line)
    {
        if (line is null) return;
        try { lock (LogGate) File.AppendAllText(LogFile, line + Environment.NewLine); }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
                Log.Info("Voz clonada encerrada");
            }
            catch (Exception ex) { Log.Error("Falha ao encerrar a voz clonada", ex); }
        }
        _process?.Dispose();
        _http.Dispose();
    }
}
