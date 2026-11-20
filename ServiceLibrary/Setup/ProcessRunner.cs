using System.Diagnostics;
using System.Text;

namespace ServiceLibrary.Setup;

/// <summary>Resultado de um processo externo.</summary>
public sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;

    /// <summary>Últimas linhas da saída, para mensagens de erro curtas.</summary>
    public string Tail(int lines = 4) =>
        string.Join(" | ", Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines).Select(l => l.Trim()));
}

/// <summary>
/// Executa programas externos (winget, git, uv, ollama…) sem janela,
/// repassando cada linha de saída e procurando executáveis no PATH.
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        Action<string>? onLine = null,
        IDictionary<string, string>? env = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // stdin fechado: nada fica esperando resposta do usuário
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["UV_NO_PROGRESS"] = "1";
        if (env is not null)
            foreach (var (k, v) in env) psi.Environment[k] = v;

        var output = new StringBuilder();
        void Handle(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (output) output.AppendLine(line);
            onLine?.Invoke(line.Trim());
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, $"Não consegui executar {fileName}: {ex.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) cts.CancelAfter(t);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* já saiu */ }
            if (ct.IsCancellationRequested) throw;
            return new ProcessResult(-1, output + $"\nTempo esgotado executando {fileName}.");
        }

        Log.Info($"{Path.GetFileName(fileName)} {string.Join(' ', psi.ArgumentList)} → {process.ExitCode}");
        return new ProcessResult(process.ExitCode, output.ToString());
    }

    /// <summary>Procura um executável no PATH (como o "where" do Windows). Retorna todos os encontrados.</summary>
    public static IEnumerable<string> FindAllOnPath(string name)
    {
        var exts = Path.HasExtension(name)
            ? [""]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';');

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim().Trim('"'), name + ext); }
                catch { continue; }
                if (File.Exists(candidate) && seen.Add(candidate)) yield return candidate;
            }
        }
    }

    public static string? FindOnPath(string name) => FindAllOnPath(name).FirstOrDefault();

    /// <summary>
    /// Recarrega o PATH (Máquina + Usuário) do registro para este processo.
    /// Sem isso, programas recém-instalados pelo winget ficam "invisíveis" até reabrir o app.
    /// As entradas %VAR% são expandidas (o instalador do Python grava assim).
    /// </summary>
    public static void RefreshPath()
    {
        var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";

        // Mantém o que já estava no processo (ex.: pastas adicionadas por nós) e acrescenta o novo.
        var entries = $"{machine};{user};{current}"
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(Environment.ExpandEnvironmentVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Environment.SetEnvironmentVariable("PATH", string.Join(';', entries));
    }

    /// <summary>Coloca uma pasta no início do PATH deste processo.</summary>
    public static void PrependToPath(string dir)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (current.Split(';').Any(p => p.Equals(dir, StringComparison.OrdinalIgnoreCase))) return;
        Environment.SetEnvironmentVariable("PATH", $"{dir};{current}");
    }
}
