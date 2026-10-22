# Mana starter sounds

25 original synthesized effects: 10 UI cues and 15 gameplay sounds, including four footstep variants.
The character is soft, clean and slightly playful. These are stylized effects, not natural recordings.

Open **preview.html** in your browser to audition individual sounds or play the whole pack.

All files are mono, 48 kHz, 16-bit PCM WAV, compatible with both Mana audio backends and spatial emitters.
UI and footsteps have lower peaks than gameplay events; all clips have digital headroom and short
edge fades. Peak matching is not perceptual loudness matching, so adjust bus/voice volumes in context.
These are one-shots, including the goal cue; don't enable looping on footsteps or UI sounds.

```csharp
var click = await Context.Audio.LoadClipAsync("Assets/Audio/Starter/UI/click.wav");
Context.Audio.Play(click, new() { Bus = ManaCore.Audio.ManaAudioBus.Ui });

var pickup = await Context.Audio.LoadClipAsync("Assets/Audio/Starter/Gameplay/pickup.wav");
Context.Audio.Play(pickup, new() { Position = pickupPosition });
```

Preload during scene/UI setup. The existing desktop asset-copy rule and browser downloadable asset
catalog already include this directory. The pack supplies files; gameplay/UI event wiring is separate.
Alternate or randomize `footstep_01` through `footstep_04` to reduce repetition.

## Provenance and regeneration

Generated specifically for this project by `Tools/Audio/generate_starter_pack.py`, using analytic
oscillators, envelopes and seeded filtered noise. No third-party recordings, samples or sound models
are used. This pack does not incorporate the Kenney files listed below.

Run `python Tools/Audio/generate_starter_pack.py` to regenerate the WAVs, preview and manifest. Python's
standard library is sufficient. `manifest.json` records durations, levels and hashes. The generator
checks the written WAV format, nonzero content, headroom and silent first/last samples.

## CC0 alternatives

Verified on the publishers' pages on 2026-09-20:

- [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds): 100 UI sounds; CC0.
- [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds): 130 impact/foley sounds; CC0.
- [Kenney Digital Audio](https://kenney.nl/assets/digital-audio): 60 electronic effects; CC0.

Keep each downloaded pack's included license file with imported assets. Convert any unsupported
formats to PCM WAV before using them with the current Mana loader.
