using ServiceLibrary;
using Velopack;
using Velopack.Sources;

namespace GUI.Updates;

/// <summary>
/// Atualização automática via Velopack + GitHub Releases. Fluxo: procura (ao abrir e a cada 6 h),
/// baixa em segundo plano, avisa o HUD e aplica ao fechar o app (ou na hora, se o usuário pedir).
/// Só funciona no app instalado; rodando pelo <c>dotnet run</c> não faz nada.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>A atualização terminou de baixar.</summary>
    public event Action? ReadyToInstall;

    private readonly Action<object> _post;
    private readonly UpdateManager? _manager;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private UpdateInfo? _ready;
    private bool _applyQueued;

    public UpdateService(AppConfig config, Action<object> post)
    {
        _post = post;
        if (!config.AutoUpdate || string.IsNullOrWhiteSpace(config.UpdateRepo)) return;

        try { _manager = new UpdateManager(new GithubSource(config.UpdateRepo, null, config.UpdatePrerelease)); }
        catch (Exception ex) { Log.Error("Não consegui iniciar o atualizador", ex); }
    }

    /// <summary>Versão instalada, ou null se o app não veio do instalador.</summary>
    public string? CurrentVersion => _manager?.IsInstalled == true ? _manager.CurrentVersion?.ToString() : null;

    public void Start()
    {
        if (_manager is not { IsInstalled: true })
        {
            Log.Info("Atualizações automáticas desligadas (app não instalado ou AutoUpdate=false).");
            return;
        }

        Log.Info($"Atualizador ativo. Versão instalada: {CurrentVersion}");
        _ = Task.Run(() => LoopAsync(_stop.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                await CheckOnceAsync(ct);
                await Task.Delay(Interval, ct);
            }
        }
        catch (OperationCanceledException) { /* app fechando */ }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        try
        {
            if (_ready is not null || _manager is null) return; // já tem uma baixada esperando

            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) { Log.Info("Atualizador: já está na versão mais recente."); return; }

            var version = info.TargetFullRelease.Version.ToString();
            Log.Info($"Atualizador: baixando a versão {version}");
            _post(new { type = "update", status = "downloading", version });

            await _manager.DownloadUpdatesAsync(info, cancelToken: ct);
            lock (_gate) _ready = info;
            ReadyToInstall?.Invoke();

            Log.Info($"Atualizador: versão {version} baixada e pronta.");
            _post(new { type = "update", status = "ready", version });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Sem internet, repositório sem versões, limite do GitHub...: tenta de novo no próximo ciclo.
            Log.Error("Atualizador: falha ao procurar/baixar atualização", ex);
            _post(new { type = "update", status = "error" });
        }
    }

    /// <summary>Instala ao fechar o app e reabre. Quem chama deve fechar a janela em seguida.</summary>
    public bool ApplyAndRestart(bool hidden = false) => Queue(restart: true, hidden);

    /// <summary>Instala em silêncio depois que o app fechar, sem reabrir. Usado ao sair normalmente.</summary>
    public void ApplyOnExit() => Queue(restart: false);

    private bool Queue(bool restart, bool hidden = false)
    {
        lock (_gate)
        {
            if (_ready is null || _manager is null) return false;
            if (_applyQueued && !restart) return true;
            try
            {
                _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: restart, restartArgs: hidden ? ["--startup"] : []);
                _applyQueued = true;
                Log.Info($"Atualizador: versão {_ready.TargetFullRelease.Version} será instalada ao fechar (reabrir: {restart}).");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Atualizador: não consegui agendar a instalação", ex);
                return false;
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}
