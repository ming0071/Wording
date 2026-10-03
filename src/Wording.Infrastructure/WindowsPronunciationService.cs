using System.Speech.Synthesis;
using Wording.Core;

namespace Wording.Infrastructure;

public sealed class WindowsPronunciationService : IPronunciationService, IPracticeSpeech
{
    private readonly SpeechSynthesizer synthesizer = new();
    private readonly bool available;
    public string Status { get; }
    public bool IsAvailable => available;
    public PlaybackState Playback => !available ? PlaybackState.Stopped : synthesizer.State switch
    { SynthesizerState.Speaking => PlaybackState.Playing, SynthesizerState.Paused => PlaybackState.Paused, _ => PlaybackState.Stopped };
    public event Action? PlaybackChanged;

    public WindowsPronunciationService()
    {
        synthesizer.StateChanged += (_, _) => PlaybackChanged?.Invoke();
        var voices = synthesizer.GetInstalledVoices().Where(x => x.Enabled).ToArray();
        foreach (var voice in voices.Where(x => x.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
                     .OrderByDescending(x => x.VoiceInfo.Culture.Name == "en-US"))
        {
            try
            {
                synthesizer.SelectVoice(voice.VoiceInfo.Name);
                available = true;
                Status = $"Windows 合成語音 · {voice.VoiceInfo.Name}";
                return;
            }
            catch (InvalidOperationException) { /* Installed voices may be unavailable under the current Windows account. */ }
        }
        Status = "沒有可用的 Windows 英文語音；請檢查 Windows 語音安裝與帳戶權限。";
    }

    public void Speak(string text) => Play(text, 0);

    public void Play(string text, int rate)
    {
        if (!available) throw new InvalidOperationException(Status);
        if (string.IsNullOrWhiteSpace(text)) return;
        Stop();
        synthesizer.Rate = Math.Clamp(rate, -2, 2);
        synthesizer.SpeakAsync(text);
    }

    public void Pause() { if (available && synthesizer.State == SynthesizerState.Speaking) synthesizer.Pause(); }
    public void Resume() { if (available && synthesizer.State == SynthesizerState.Paused) synthesizer.Resume(); }
    public void Stop()
    {
        if (!available) return;
        synthesizer.SpeakAsyncCancelAll();
        if (synthesizer.State == SynthesizerState.Paused) synthesizer.Resume();
    }
    public void Dispose() { Stop(); synthesizer.Dispose(); }
}
