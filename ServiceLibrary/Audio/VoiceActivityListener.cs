using NAudio.Wave;

namespace ServiceLibrary.Audio;

/// <summary>
/// Microfone sempre aberto. Fica escutando em segundo plano e entrega cada fala completa (WAV 16 kHz mono)
/// por <see cref="Utterance"/>. Nada é gravado em disco: o áudio só existe na memória, até a transcrição.
/// </summary>
public sealed class VoiceActivityListener(float sensitivity) : IDisposable
{
    private readonly UtteranceDetector _detector = new(sensitivity);
    private readonly Lock _gate = new();
    private WaveIn? _wave;
    private CancellationTokenSource? _stop;

    /// <summary>Uma fala completa, em WAV.</summary>
    public event Action<byte[]>? Utterance;

    /// <summary>Volume do microfone, de 0 a 1 (para a esfera).</summary>
    public event Action<float>? Level;

    /// <summary>Quando devolve true, o áudio é descartado (o Jarvis está falando: seria só o eco dele).</summary>
    public Func<bool>? ShouldIgnore { get; set; }

    public bool IsRunning => _wave is not null;

    public void Start()
    {
        lock (_gate)
        {
            if (_wave is not null) return;
            _stop = new CancellationTokenSource();
            Open();
        }
    }

    private void Open()
    {
        var wave = new WaveIn
        {
            WaveFormat = new WaveFormat(UtteranceDetector.SampleRate, 16, 1),
            BufferMilliseconds = UtteranceDetector.FrameMs,
            DeviceNumber = 0,
        };
        wave.DataAvailable += OnData;
        wave.RecordingStopped += OnStopped;
        wave.StartRecording();
        _wave = wave;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (ShouldIgnore?.Invoke() == true)
            {
                _detector.Reset();
                Level?.Invoke(0);
                return;
            }

            // Cada bloco pode vir com menos de 50 ms; o detector espera blocos inteiros.
            var data = e.Buffer.AsSpan(0, e.BytesRecorded);
            for (int i = 0; i < data.Length; i += UtteranceDetector.FrameBytes)
            {
                var chunk = data.Slice(i, Math.Min(UtteranceDetector.FrameBytes, data.Length - i));
                var pcm = _detector.Feed(chunk);
                Level?.Invoke(_detector.Level);
                if (pcm is not null) Raise(pcm);
            }
        }
        catch (Exception ex) { Log.Error("Falha ao processar o áudio do microfone", ex); }
    }

    private void Raise(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(UtteranceDetector.SampleRate, 16, 1)))
            writer.Write(pcm, 0, pcm.Length);
        var wav = ms.ToArray();

        // Fora da thread do áudio: a transcrição é demorada.
        _ = Task.Run(() =>
        {
            try { Utterance?.Invoke(wav); }
            catch (Exception ex) { Log.Error("Falha ao tratar uma fala", ex); }
        });
    }

    /// <summary>Microfone desconectado ou ocupado: tenta reabrir a cada 5 segundos.</summary>
    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return; // parada normal (Stop)
        Log.Error("O microfone parou; tentando reabrir", e.Exception);

        _ = Task.Run(async () =>
        {
            CancellationToken ct;
            lock (_gate)
            {
                if (_stop is null) return;
                ct = _stop.Token;
                _wave?.Dispose();
                _wave = null;
            }
            _detector.Reset();

            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
                try
                {
                    lock (_gate)
                    {
                        if (_stop is null || _wave is not null) return;
                        if (!MicRecorder.HasMicrophone) continue;
                        Open();
                    }
                    Log.Info("Microfone reaberto.");
                    return;
                }
                catch (Exception ex) { Log.Error("Ainda sem microfone", ex); }
            }
        });
    }

    public void Stop()
    {
        lock (_gate)
        {
            _stop?.Cancel();
            _stop = null;
            if (_wave is null) return;
            _wave.DataAvailable -= OnData;
            _wave.RecordingStopped -= OnStopped;
            try { _wave.StopRecording(); } catch { /* já parado */ }
            _wave.Dispose();
            _wave = null;
        }
        _detector.Reset();
        Level?.Invoke(0);
    }

    public void Dispose() => Stop();
}
