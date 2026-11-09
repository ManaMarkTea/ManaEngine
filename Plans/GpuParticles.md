# GPU particles

Open `Content/Levels/Particle Garden.manalevel` (the CPU fires and fountain up front, the GPU showcase behind),
select **GPU Fountain**, then change **Count** on its **GPU Particle Emitter** component. The fountain starts with
100,000 particles.
Any node can receive the component through Add Component. Its settings save with levels and prefabs.

```powershell
dotnet run --project ManaEditor -- --level "Content/Levels/Particle Garden.manalevel"

# Builds Release, warms up, sweeps counts, prints FPS / slowdown / worst frame / hitches.
./Tools/Automation/benchmark-gpu-particles.ps1
```

The sweep measures 0, 10K, 100K, 250K, 500K, 1M, 2M, 4M, 8M and 10M particles, then repeats the zero baseline.
Each count gets 90 settling frames and 600 measurement frames. Reports and screenshots go to
`Artifacts/Automation/gpu-particles*`; `profile_dump` includes the particle count and emitter count.
`gpu_particles` is also an automation command: `{ "type": "gpu_particles", "name": "GPU Fountain", "count": 1000000 }`.

## What this supports

WebGPU draws camera-facing discs using six generated vertices per instance, one draw and one 336-byte
uniform upload per emitter. The shader derives seeded launch directions and staggered ages from the instance
index, then evaluates ballistic motion (including optional analytic drag), size and color over the lifetime.
Each emitter also has a 16 KiB ring of 256 birth-transform samples. This is shared by every particle and uploaded
only when its contents change; stationary emitters stop uploading it once old moving poses have expired.
There are no per-particle CPU arrays, sorts, mesh rebuilds or uploads. Count changes immediately, up to 10 million
per emitter; zero draws no particles. Disabling the emitter hides it and pauses its clock.

This is a continuously recycled, prewarmed stream with a fixed lifetime, additive blending, depth testing,
and soft or hard disc edges. Particles retain their emitter pose at birth: moving or rotating the node affects
new particles, while emitted particles continue in world space. Gravity is a world-space acceleration.
Emitter scale affects spawn radius; launch speed and billboard size are in world units. Birth poses are sampled
at lifetime / 254 seconds (about 16 ms for a four-second lifetime). Integer birth indices keep the selected pose
fixed throughout each particle's life, preventing jitter while dragging the emitter. Launch directions repeat
each lifetime. Changing lifetime resets the transform history; changing Count redistributes the prewarmed slots.
It draws into
the HDR scene before post processing, so bright particles bloom. It does not write depth or cast shadows.

Alpha sorting, mesh collisions and textured sprites are outside this GPU path (see Swirl, turbulence and
looks below for what was added since). The existing CPU `ParticleEmitter` remains available for its richer effects and OpenGL.
`GpuParticleEmitter` renders on WebGPU only; OpenGL does not render this component.

Motion regression: `node Tools/WebAssets/gpu-particles.test.mjs` reads back the production shader's computed
positions in Chrome and checks retained birth poses, rotated launch direction, world gravity and ring/lifetime
wrap. `Tools/Automation/Checks/gpu-particles-world.json` moves and rotates the live emitter between screenshots.

## Performance and validation

Measurements are for this tiny-disc fountain at 1920 x 1080 on an NVIDIA RTX 5070 Ti, using the Release editor
and its ordinary UI/HDR rendering. The renderer presents with FIFO/vsync, and this run is capped at 120 FPS.
Holding that cap establishes performance at that frame budget; it does not establish uncapped GPU throughput
or the absolute maximum possible count. Larger particles and a closer camera increase overlap and pixel cost;
smoke-sized billboards can slow down at far smaller counts. Compare against the zero baseline on your machine.

Final sweep on 2026-10-08 (601 measured frames per sample, zero recorded hitches throughout):

| Particles | FPS | Worst frame (ms) |
| ---: | ---: | ---: |
| 0 (baseline) | 119.9 | 11.1 |
| 100,000 | 120.0 | 10.6 |
| 1,000,000 | 120.0 | 11.3 |
| 2,000,000 | 120.0 | 11.6 |
| 4,000,000 | 120.0 | 11.2 |
| 8,000,000 | 120.0 | 10.9 |
| 10,000,000 | 120.0 | 15.9 |
| 0 (after sweep) | 120.0 | 10.9 |

Raw local results: `Artifacts/Automation/gpu-particles-final.png.*.txt` and
`Artifacts/gpu-particles-final-summary.txt`. This establishes 10M within the capped frame rate for this
effect; the configurable 10M safety cap is not a measured hardware limit.

After the world-space birth-pose fix, the same sweep again held 119.9–120 FPS through 10M with zero recorded
hitches (`Artifacts/gpu-particles-world-summary.txt`). The 10M sample's worst frame was 10.8 ms. The live moving
emitter check and GPU readback regression separately verify that old particles keep their spawn poses.

`GPU: Particle Draw Encode` measures CPU command preparation, **not** GPU execution time. Use the whole-frame
FPS, worst frame and hitch results to judge visible slowdown.

Validation: desktop Debug and Release builds; the full headless suite; screenshots at 100K and 10M;
Chrome WGSL compilation, shader position readback for moving/rotated emitters and lifetime/ring wrap, and
an additive `rgba16float` / `depth24plus` render pipeline without validation errors. Full browser-wasm
publishing could not run here because the .NET `wasm-tools`
workload is missing; the browser C# / native interop path remains unverified in this session.

## Plane bounce

The garden's **GPU Bounce** is a floor and wall demo (the lava river runs down the volcano on a third
plane: a slope). Each emitter has two optional infinite planes
under **Bounce**, each with a world-space position and normal. The normal points into the permitted
half-space. Bounciness is shared: zero settles and one preserves normal impact speed; tangential speed
is preserved. Particle centers collide, without accounting for billboard size.

The shader replays up to eight impacts per lifetime, using exact ballistic crossing times or bounded
root finding for linear drag. After that limit, positions are constrained to the planes; this is intended
for simple floor/wall effects rather than long-lived particles in narrow enclosures. Changing planes or
bounciness also changes the reconstructed paths of existing particles. Disabled planes retain the original
free-flight fast path. Earlier 10M benchmarks used disabled bounce and do not measure bounce throughput.
The Chrome regression also checks restitution, resting contact, repeated two-plane impacts and drag.

Release bounce sample on the same RTX 5070 Ti at 1920 x 1080 (301 measured frames each):
zero particles 119.1 FPS; 100K 119.9 FPS with no hitches; 1M 119.1 FPS with one 27 ms hitch.
This short sample confirms the demo fits the 120 FPS budget at 100K, but does not establish a maximum
bounce count. Reports: `Artifacts/Automation/gpu-bounce-perf.png.*.txt`. Reproduce with
`Tools/Automation/Checks/gpu-particles-bounce.json` through `run-commands.ps1` using a Release editor.

## Global wind

GPU particles receive the same `ManaRenderScene.Wind` state as water and other world shaders,
including its current gust speed. **Wind Influence** defaults to 1; zero opts an emitter out.
Wind advects particles in world XZ at the global air velocity, and drag damps launch motion relative
to that moving air. Bounce reflects the resulting world velocity, retaining tangential wind motion.
This procedural path uses the current wind across a particle's reconstructed lifetime; editing wind
or gust changes can move existing trajectories. It does not retain a history of wind forces.

## Swirl, turbulence and looks

Everything stays a closed-form function of age, so there is still no per-particle state. After free flight
(and bounce), the shader turns the position about the birth pose's up axis: **Swirl Speed** degrees a second,
slowed by **Swirl Falloff** per metre from the axis (which shears streams into spirals), with **Pull** drawing
toward the axis per second (negative flings out). **Turbulence** then displaces by a smooth two-octave sine
field (**Turbulence Scale** metres, **Turbulence Speed**), growing from zero at birth. Bounce planes see the
free-flight path only, so a swirled or turbulent particle can cross a plane.

Emission: **Emit From Edge** (the rim of Radius), **Arms** with **Arm Width** (births bunched into spokes;
with swirl falloff they wind into a galaxy's arms), **Radial Speed** (out from the axis), and **Bursts** --
groups born together somewhere in the ball of Radius, a new place and colour every lifetime: fireworks.
**Speed Variation** gives sprays depth. Looks: **Streak Length** (seconds of path drawn as a camera-facing
capsule fading to its tail; costs a second path evaluation), **Hue Variation** (per particle, or per burst),
**Hue Shift** over life, **Twinkle**, **Size Variation**.

The garden shows each: a tornado (rim, swirl, outward pull), a galaxy (arms, radial speed, falloff), a black
hole (rim, arms, inward pull), a comet orbiting on a `Motion` script (world-space trail, turbulence), an
aurora (a node squashed flat into a line emitter), fireworks (bursts, streaks), a volcano (lava bombs and a
river sliding down a slope plane), a fireboat and a speedboat (water arcs and a wake on the water plane), and
the `Rocket` prefab (`Content/Prefabs/Rocket.manaprefab`, Kenney Space Kit) whose exhaust fans across its pad.
Foundry's rocket silo switches the same kind of thruster on for a launch.
