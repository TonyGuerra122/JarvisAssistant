namespace ServiceLibrary.Audio;

/// <summary>
/// Separa falas do ruído de fundo num fluxo de áudio (PCM 16 kHz, 16 bits, mono), por energia.
/// Não faz reconhecimento: só diz "aqui começou e terminou uma fala". Sem dependências, para rodar o dia todo
/// gastando quase nada. O nível de ruído é aprendido sozinho, então ventilador e ar-condicionado não disparam.
/// </summary>
public sealed class UtteranceDetector(float sensitivity = 1f)
{
    public const int SampleRate = 16_000;
    public const int FrameMs = 50;
    public const int FrameBytes = SampleRate * FrameMs / 1000 * 2;

    private const int StartFrames = 2;        // 100 ms seguidos de voz para começar
    private const int PreRollFrames = 6;      // 300 ms antes do disparo (para não cortar o começo da palavra)
    private const int EndSilenceFrames = 14;  // 700 ms de silêncio terminam a fala
    private const int MaxFrames = 300;        // 15 s: corta falas intermináveis (TV ligada, por exemplo)
    private const int MinVoicedFrames = 6;    // menos de 300 ms de voz = estalo/tosse, descartado

    private readonly double _minThreshold = 0.012 / Math.Clamp(sensitivity, 0.2f, 5f);
    private readonly Queue<byte[]> _preRoll = new();
    private readonly List<byte[]> _frames = [];
    private double _floor = 0.01;
    private int _voiced, _silent, _voicedTotal;
    private bool _inSpeech;

    /// <summary>Energia (RMS) do último bloco, de 0 a 1.</summary>
    public float Level { get; private set; }

    public bool InSpeech => _inSpeech;

    /// <summary>Esquece a fala em andamento (usado quando o Jarvis está falando e o microfone seria só eco).</summary>
    public void Reset()
    {
        _preRoll.Clear();
        _frames.Clear();
        _voiced = _silent = _voicedTotal = 0;
        _inSpeech = false;
    }

    /// <summary>Recebe um bloco de 50 ms. Devolve o PCM da fala completa quando ela termina; senão, null.</summary>
    public byte[]? Feed(ReadOnlySpan<byte> frame)
    {
        var rms = Rms(frame);
        Level = (float)Math.Min(1.0, rms * 6);

        var threshold = Math.Max(_minThreshold, _floor * 2.5);
        var voiced = rms > threshold;
        var copy = frame.ToArray();

        if (!_inSpeech)
        {
            // Aprende o ruído de fundo só enquanto ninguém fala.
            if (!voiced) _floor = _floor * 0.98 + rms * 0.02;

            _preRoll.Enqueue(copy);
            while (_preRoll.Count > PreRollFrames) _preRoll.Dequeue();

            _voiced = voiced ? _voiced + 1 : 0;
            if (_voiced < StartFrames) return null;

            _inSpeech = true;
            _frames.AddRange(_preRoll);
            _preRoll.Clear();
            _silent = 0;
            _voicedTotal = _voiced;
            return null;
        }

        _frames.Add(copy);
        if (voiced) { _silent = 0; _voicedTotal++; }
        else _silent++;

        if (_silent < EndSilenceFrames && _frames.Count < MaxFrames) return null;

        var result = _voicedTotal >= MinVoicedFrames ? Join(_frames) : null;
        Reset();
        return result;
    }

    private static double Rms(ReadOnlySpan<byte> frame)
    {
        var samples = frame.Length / 2;
        if (samples == 0) return 0;

        double sum = 0;
        for (int i = 0; i + 1 < frame.Length; i += 2)
        {
            double s = BitConverter.ToInt16(frame[i..]) / 32768.0;
            sum += s * s;
        }
        return Math.Sqrt(sum / samples);
    }

    private static byte[] Join(List<byte[]> frames)
    {
        var all = new byte[frames.Sum(f => f.Length)];
        var offset = 0;
        foreach (var f in frames) { f.CopyTo(all, offset); offset += f.Length; }
        return all;
    }
}
