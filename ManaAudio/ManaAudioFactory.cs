using ManaCore.Audio;

namespace ManaAudio;

public static class ManaAudioFactory
{
    /// <summary>Call from each web entry point before constructing a game mode.</summary>
    public static Task InitializeBrowserAsync()
    {
#if MANA_BROWSER
        return OperatingSystem.IsBrowser() ? Browser.ManaBrowserAudioBackend.InitializeAsync() : Task.CompletedTask;
#else
        return Task.CompletedTask;
#endif
    }

    /// <summary>A platform's own backend, used before the built-in ones: the phone's AVAudioEngine.</summary>
    public static Func<IManaAudioBackend>? PlatformBackend { get; set; }

    public static IManaAudio Create(Action<string>? reportUnavailable = null)
    {
        try
        {
            if (PlatformBackend is { } platform)
            {
                return new ManaAudioSystem(platform());
            }
#if MANA_BROWSER
            return OperatingSystem.IsBrowser()
                ? new ManaAudioSystem(new Browser.ManaBrowserAudioBackend())
                : CreateSilent();
#else
            return new ManaAudioSystem(new Desktop.ManaOpenAlBackend());
#endif
        }
        catch (Exception ex)
        {
            reportUnavailable?.Invoke($"Audio unavailable: {ex.Message}");
            return CreateSilent();
        }
    }

    public static IManaAudio CreateSilent() => new ManaAudioSystem(new ManaNullAudioBackend());
}
