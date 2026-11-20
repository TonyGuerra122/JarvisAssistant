using System.Runtime.Versioning;
using NAudio.Wave;

namespace ServiceLibrary.Audio;

/// <summary>Grava o microfone em WAV 16 kHz mono (formato que o Whisper prefere).</summary>
public sealed class MicRecorder : IDisposable
{
    private WaveIn? _wave;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<bool>? _stopped;
    private DateTime _startedAt;

    ///<summary>Volume atual do microfone, de 0 a 1</summary>
    public event Action<float>? Level;

    public static bool HasMicrophone => WaveIn.DeviceCount > 0;
    public bool IsRecording => _wave is not null;

    public void Start()
    {
        if (IsRecording) return;

        _buffer = new MemoryStream();
        var format = new WaveFormat(16_000, 16, 1);
        _writer = new WaveFileWriter(_buffer, format);
        _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _wave = new WaveIn { WaveFormat = format, BufferMilliseconds = 50, DeviceNumber = 0 };
        _wave.DataAvailable += OnData;
        _wave.RecordingStopped += (_, _) => _stopped.TrySetResult(true);
        _startedAt = DateTime.UtcNow;
        _wave.StartRecording();

    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);

        // RMS do bloco, para a esfera à sua voz.
        double sum = 0.0;
        int samples = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            double s = BitConverter.ToInt16(e.Buffer, i) / 32768.0;
            sum += s * s;
        }

        var rms = samples > 0 ? Math.Sqrt(sum / samples) : 0;
        Level?.Invoke((float)Math.Min(1.0, rms * 6));
    }

    /// <summary>Para a gravação e devolve o WAV, ou null se foi curto demais.</summary>
    public async Task<byte[]?> StopAsync(TimeSpan minDuration)
    {
        if (_wave is null || _stopped is null) return null;

        var duration = DateTime.UtcNow - _startedAt;
        _wave.StopRecording();
        await Task.WhenAny(_stopped.Task, Task.Delay(2000));

        _wave.Dispose();
        _wave = null;
        _writer?.Dispose(); // fecha o cabeçalho do WAV
        _writer = null;

        var bytes = _buffer?.ToArray();
        _buffer?.Dispose();
        _buffer = null;
        Level?.Invoke(0);

        return duration < minDuration ? null : bytes;
    }

    public void Dispose()
    {
        try { _wave?.StopRecording(); } catch { /* ignorado */ }
        _wave?.Dispose();
        _writer?.Dispose();
        _buffer?.Dispose();
    }
}
