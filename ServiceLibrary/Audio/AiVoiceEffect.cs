using NAudio.Dsp;
using NAudio.Wave;

namespace ServiceLibrary.Audio;

/// <summary>
/// Efeito sutil de "voz de assistente de IA", voltado para suavidade: graves quentes,
/// agudos amaciados e dois ecos bem discretos (um leve ar de sala).
/// No fim, um limitador suave evita estouro.
/// </summary>
public sealed class AiVoiceEffect : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly BiQuadFilter[] _bass;
    private readonly BiQuadFilter[] _presence;
    private readonly float[] _delay;
    private readonly int _tap1, _tap2;
    private int _pos;

    /// <param name="amount">0 = sem efeito, 1 = padrão. Escala os ecos e o realce.</param>
    public AiVoiceEffect(ISampleProvider source, float amount = 1f)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _channels = WaveFormat.Channels;
        var rate = WaveFormat.SampleRate;
        amount = Math.Clamp(amount, 0f, 2f);

        // Voz suave: graves quentes e agudos amaciados (corta o "chiado" do S e o brilho áspero).
        _bass = [.. Enumerable.Range(0, _channels).Select(_ => BiQuadFilter.LowShelf(rate, 180, 0.7f, 3f * amount))];
        _presence = [.. Enumerable.Range(0, _channels).Select(_ => BiQuadFilter.HighShelf(rate, 4500, 0.7f, -4f * amount))];

        // Ecos discretos em 23 ms e 47 ms (por canal, intercalado): um leve ar de "sala", sem metal.
        _tap1 = (int)(rate * 0.023) * _channels;
        _tap2 = (int)(rate * 0.047) * _channels;
        _delay = new float[_tap2 + _channels];
        Gain1 = 0.08f * amount;
        Gain2 = 0.05f * amount;
    }

    private float Gain1 { get; }
    private float Gain2 { get; }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var read = _source.Read(buffer);
        var len = _delay.Length;

        for (var i = 0; i < read; i++)
        {
            var ch = i % _channels;
            var dry = _presence[ch].Transform(_bass[ch].Transform(buffer[i]));

            var e1 = _delay[(_pos - _tap1 + len) % len];
            var e2 = _delay[(_pos - _tap2 + len) % len];
            _delay[_pos] = dry;
            _pos = (_pos + 1) % len;

            // tanh = limitador suave
            buffer[i] = MathF.Tanh((dry + e1 * Gain1 + e2 * Gain2) * 0.9f);
        }
        return read;
    }
}
