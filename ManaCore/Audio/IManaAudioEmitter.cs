namespace ManaCore.Audio;

public interface IManaAudioEmitter
{
    Task PrepareAsync();
    void Play();
    IManaAudioVoice? PlayOneShot();
    void Stop();
}

public enum ManaSoundCue
{
    None, Pickup, Checkpoint, HazardContact, EnemyAlert, EnemyStomp, EnemyDefeat,
    PlatformMove, PlatformStop, Jump, Land, Footstep, Hurt, LowHealth, Death, Respawn,
    Goal, AllCoins, Fall,
    // Added at the end: cues are saved by name, but keep the numbers of the ones before.
    Shoot
}
