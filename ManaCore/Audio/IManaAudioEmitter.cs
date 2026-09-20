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
    Goal, AllCoins, Fall
}
