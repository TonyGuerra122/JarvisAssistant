using System.Diagnostics;

namespace ServiceLibrary;

/// <summary>
/// Inicia e encerra o "jarvis serve" em segundo plano, sem janela.
/// Só encerra o servidor que ele mesmo iniciou.
/// </summary>
public sealed class OpenJarvisServer(AppConfig config) : IDisposable
{
    private readonly AppConfig _config = config;
    private Process? _process;

    public bool StartedByUs => _process is { HasExited: false };

    /// <summary>O servidor que iniciamos já fechou (normalmente por erro).</summary>
    public bool Exited => _process is { HasExited: true };

    public string CommandPath => _config.ResolveJarvisCommand();
    public bool IsInstalled => File.Exists(CommandPath);

    public void Start()
    {
        if (StartedByUs) return;

        var uri = new Uri(_config.OpenJarvisUrl);
        var args = $"serve --host {uri.Host} --port {uri.Port}";
        if (!string.IsNullOrWhiteSpace(_config.Agent)) args += $" --agent {_config.Agent}";
        if (!string.IsNullOrWhiteSpace(_config.Model)) args += $" --model \"{_config.Model}\"";

        var logFile = AppPaths.OpenJarvisLogFile;
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // A saída vai direto para um arquivo (e não por pipe), para o servidor
            // continuar funcionando mesmo se o app fechar sem encerrá-lo.
            // O cmd /c remove a primeira e a última aspa da linha, por isso a linha
            // inteira vai entre um par de aspas extras (uma no início, outra no fim).
            Arguments = $"/c \"\"{CommandPath}\" {args} > \"{logFile}\" 2>&1\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(CommandPath) ?? AppContext.BaseDirectory
        };

        // Saída em UTF-8 e sem cores, para o log ficar legível
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["OPENJARVIS_NO_UPDATE_CHECK"] = "1";

        _process = Process.Start(psi);
        Log.Info($"jarvis serve iniciado (PID {_process?.Id}): cmd.exe {psi.Arguments}");
    }

    /// <summary>Últimas linhas do log do servidor, para mostrar em caso de falha.</summary>
    public static string TailLog(int lines = 3)
    {
        try
        {
            using var fs = new FileStream(AppPaths.OpenJarvisLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var all = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" | ", all.TakeLast(lines).Select(l => l.Trim()));
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                // Mata cmd.exe + uv + python (a árvore inteira)
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
                Log.Info("jarvis serve encerrado");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao encerrar o jarvis server", ex);
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        if (_config.StopOpenJarvisOnExit) Stop();
    }
}
