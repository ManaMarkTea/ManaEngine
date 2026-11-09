# Mana audio

`Context.Audio` is initialized by `BaseGameMode` for either graphics backend. Desktop uses
Silk.NET.OpenAL and bundled OpenAL Soft; browser builds use `mana_audio.js` through .NET JS interop.
Headless contexts use a silent implementation. If opening a device fails, startup logs the error and
continues silently (`IsAvailable == false`).

In the desktop or web editor, choose **Play → Test Audio** for a short, quiet tone.

## Play a clip

Put WAV files under `Assets/Audio/`. They copy to desktop output as `Assets/Audio/...`;
the existing web asset publisher includes them in the downloadable catalog, outside the boot bundle.

```csharp
using ManaCore.Audio;

var jump = await Context.Audio.LoadClipAsync("Assets/Audio/jump.wav");
Context.Audio.Play(jump); // Overlapping one-shot. No need to retain/dispose this handle.

var ambience = Context.Audio.Play(jump, new()
{
    Loop = true,
    Position = new Vector3(10, 0, -5),
    ReferenceDistance = 2,
    MaxDistance = 80,
    Bus = ManaAudioBus.Sfx
});
ambience.Volume = 0.3f;
ambience.Pitch = 0.9f;
ambience.Position = new Vector3(12, 0, -5);
ambience.Stop(); // Dispose does the same thing.

Context.Audio.MasterVolume = 0.8f;
Context.Audio.SetBusVolume(ManaAudioBus.Music, 0.5f);
```

Clips decode once per path and device buffers are shared between voices. Load/preload outside the
frame loop; don't block on async loading in WASM. The cache lasts until the audio system is disposed.
File loads accept cancellation without cancelling other callers sharing that load. Failed loads can
be retried. For generated PCM, construct `ManaAudioClip` from interleaved `short` samples.

WAV support: mono/stereo PCM8/16/24/32 and IEEE float32, 8–192 kHz, up to 64 MiB of encoded sample data.
Compressed/extensible WAV, MP3/Ogg, streaming music, seeking, pause/resume and effects aren't implemented.
`Music` is currently a volume bus for buffered clips, not a streaming decoder.

## Scene emitters

```csharp
using Mana.Code.Components;

var emitter = new ManaAudioSourceComponent(node)
{
    ClipPath = "Assets/Audio/engine.wav",
    Loop = true,
    Volume = 0.4f
};
node.Components.Add(emitter);
```

Add **Audio Source** to a node in the editor; multiple sources are supported and saved in `.manalevel`.
The inspector provides Preview/Stop and settings for clip paths, looping, volume, pitch, spatial
attenuation, bus, fade and delay. `Variants` accepts semicolon-separated paths played in rotation.
`LocalOffset` places the emitter relative to its owner. Spatial clips must be mono; stereo is for UI/music.

`IManaAudioEmitter` exposes `PrepareAsync()`, `Play()`, `PlayOneShot()` and `Stop()`.
`PlayOnBegin` starts an emitter automatically; looping voices follow their owner's transform and
fade on start/stop. `EndPlay` stops immediately and cancels delayed playback. Editor removal disposes
sources; dispose them yourself when removing directly in code. The level loader prefetches browser
audio assets and starts clip preparation without blocking the frame loop. Unloaded one-shots are
dropped; requested loops start when ready.

For gameplay, turn off `PlayOnBegin` and assign a `Cue`. Components emit local events with
`ManaAudioCueRouter.EmitAt(node, ManaSoundCue.Pickup)`. Sources with `Global` enabled receive
`world.AudioCues.Emit(...)` instead, which suits player health, movement and victory feedback.
Gameplay code selects the event; the level selects its sound and mix.

**Sample Course** has 99 saved sources: player cues on Player Start, pickups/checkpoints, enemy
feedback, hazards, platform motors and stops, harbor ambience, and surface footstep variants.
Open the level and press F5. Footsteps use distance actually traveled and the physics ground node;
surfaces without an assigned cue use the player's fallback. Global player cues are non-spatial.

The listener follows the same main camera pose used for rendering. Both backends use inverse-distance
attenuation, shared reference/max distance and rolloff values, and no Doppler. Their spatial mixers
can sound slightly different. At most 64 voices can be live; extra requests return inactive handles.
Completed sources are reclaimed during `Update`. Ending play or replacing a level stops all voices;
closing the game disposes the device and its buffers. Retained stopped handles remain safe to inspect.

## Browser initialization

Both web entry points await `ManaAudioFactory.InitializeBrowserAsync()` before creating a game mode;
`WebAssets.props` deploys the JS module. A new web entry point must do the same.

Web Audio may start suspended. The backend resumes it directly in pointer/key event handlers.
One-shots requested while suspended are dropped so old effects don't burst out on unlock; loops
remain scheduled and start on resume. `IsUnlocked` exposes that state. Mixing stays in Web Audio,
with no managed sample callbacks, AudioWorklets, worker threads or shared-memory requirement.

## Checks

```sh
dotnet run --project ManaTests -- Audio
dotnet run --project ManaTests -- --audio-smoke
dotnet build ManaEditor.Web
dotnet build ManaEditor.Editor.Web
dotnet build Tools/AudioSmoke.Web -t:Rebuild
dotnet publish Tools/AudioSmoke.Web -c Release -p:RunAOTCompilation=true
python -m http.server 8766 --bind 127.0.0.1 --directory Artifacts/Bin/AudioSmoke.Web/Debug/net10.0/browser-wasm/AppBundle
```

The regular suite is headless. `--audio-smoke` requires an actual default output device and plays a
tone. Open `http://127.0.0.1:8766` for the WASM test, then click **Unlock Audio**. It explicitly suspends
a real AudioContext to exercise locked-device behavior consistently across autoplay policies, then
tests the C# → JS PCM upload, completion, looping, positioning and disposal. Rebuild after editing
the harness JS because the SDK may retain it on an incremental build.

The Release AOT harness is emitted to `Artifacts/Bin/AudioSmoke.Web/Release/net10.0/browser-wasm/AppBundle`;
serve that directory to check the compiled version.

OpenAL Soft's native package includes Windows, macOS (including arm64) and Linux binaries. Windows
device playback and Chrome WASM execution (including Release AOT) were checked locally; macOS/Linux playback needs checks on
those hosts. OpenAL Soft is LGPL; keep the package's license notices with distributed native binaries.
