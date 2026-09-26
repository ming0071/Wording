using System.Speech.Synthesis;
using WordTrail.Core;

namespace WordTrail.Infrastructure;

public sealed class WindowsPronunciationService : IPronunciationService
{
    private readonly SpeechSynthesizer synthesizer = new();
    private readonly bool available;
    public string Status { get; }

    public WindowsPronunciationService()
    {
        var voices = synthesizer.GetInstalledVoices().Where(x => x.Enabled).ToArray();
        var voice = voices.FirstOrDefault(x => x.VoiceInfo.Culture.Name == "en-US")
            ?? voices.FirstOrDefault(x => x.VoiceInfo.Culture.TwoLetterISOLanguageName == "en");
        available = voice is not null;
        if (voice is not null)
        {
            synthesizer.SelectVoice(voice.VoiceInfo.Name);
            Status = $"Windows 合成語音 · {voice.VoiceInfo.Name}";
        }
        else Status = "沒有可用的 Windows 英文語音；可先使用辭典查閱入口。";
    }

    public void Speak(string text)
    {
        if (!available) throw new InvalidOperationException(Status);
        if (string.IsNullOrWhiteSpace(text)) return;
        synthesizer.SpeakAsyncCancelAll();
        synthesizer.SpeakAsync(text);
    }

    public void Stop() => synthesizer.SpeakAsyncCancelAll();
    public void Dispose() { Stop(); synthesizer.Dispose(); }
}
