using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ServiceLibrary.Audio;

public sealed class SpeechPlayer
{
    public event Action<float>? Level;

    /// <summary>Tom da voz em semitons (negativo = mais grave). 0 = original.</summary>
    public float PitchSemitones { get; set; }

    /// <summary>Intensidade do efeito de "voz de IA". 0 = desligado.</summary>
    public float EffectAmount { get; set; }

    public async Task PlayAsync(byte[] wav, CancellationToken ct)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var samples = BuildChain(reader.ToSampleProvider());

        var meter = new MeteringSampleProvider(samples, Math.Max(1, samples.WaveFormat.SampleRate / 30));
        meter.StreamVolume += (_, e) =>
        {
            var peak = e.MaxSampleValues?.Length > 0 ? e.MaxSampleValues.Max() : 0f;
            Level?.Invoke(Math.Min(1f, peak * 1.4f));
        };

        using var output = new WaveOut();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => done.TrySetResult(true);
        output.Init(meter);
        output.Play();

        using (ct.Register(() => output.Stop()))
        {
            await done.Task;
        }
        Level?.Invoke(0);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Aplica tom e efeito a um WAV e devolve outro WAV (16 bits), sem tocar. Útil para testar.</summary>
    public byte[] Render(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var chain = BuildChain(reader.ToSampleProvider());
        var output = new MemoryStream();
        using (var writer = new WaveFileWriter(output, new WaveFormat(chain.WaveFormat.SampleRate, 16, chain.WaveFormat.Channels)))
        {
            var buffer = new float[chain.WaveFormat.SampleRate * chain.WaveFormat.Channels];
            int n;
            while ((n = chain.Read(buffer.AsSpan())) > 0) writer.WriteSamples(buffer, 0, n);
        }
        return output.ToArray();
    }

    private ISampleProvider BuildChain(ISampleProvider source)
    {
        var chain = source;

        if (Math.Abs(PitchSemitones) > 0.01f)
        {
            // O pitch shifter tem atraso interno (janela de FFT): sem esse silêncio no fim,
            // a última sílaba seria cortada.
            chain = new OffsetSampleProvider(chain) { LeadOut = TimeSpan.FromMilliseconds(250) };
            // Oversampling 16 (o padrão é 4): bem menos "voz robótica" no ajuste de tom.
            chain = new SmbPitchShiftingSampleProvider(chain, 2048, 16, MathF.Pow(2f, PitchSemitones / 12f));
        }

        if (EffectAmount > 0.01f) chain = new AiVoiceEffect(chain, EffectAmount);
        return chain;
    }
}
