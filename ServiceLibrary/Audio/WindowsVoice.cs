using System.Speech.Synthesis;

namespace ServiceLibrary.Audio;

/// <summary>Voz de reserva: as vozes instaladas no próprio Windows (ex.: Maria, Daniel).</summary>
public sealed class WindowsVoice
{
    private readonly string? _voiceName;

    public WindowsVoice(string preferredName)
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();

            var chosen =
                voices.FirstOrDefault(v => !string.IsNullOrWhiteSpace(preferredName) &&
                    v.Name?.Contains(preferredName, StringComparison.OrdinalIgnoreCase) == true)
                ?? voices.FirstOrDefault(v => v.Culture?.Name?.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) == true)
                ?? voices.FirstOrDefault(v => v.Culture?.TwoLetterISOLanguageName == "pt")
                ?? voices.FirstOrDefault();

            _voiceName = chosen?.Name;
            IsPortuguese = chosen?.Culture?.TwoLetterISOLanguageName == "pt";
        }
        catch (Exception ex)
        {
            Log.Error("Vozes do Windows indisponíveis", ex);
        }
    }

    public bool Available => _voiceName is not null;
    public bool IsPortuguese { get; }
    public string Name => _voiceName ?? "nenhuma";

    public Task<byte[]> SynthesizeAsync(string text) => Task.Run(() =>
    {
        using var synth = new SpeechSynthesizer();
        if (_voiceName is not null) synth.SelectVoice(_voiceName);
        using var ms = new MemoryStream();
        synth.SetOutputToWaveStream(ms);
        synth.Speak(text);
        synth.SetOutputToNull();
        return ms.ToArray();
    });
}
