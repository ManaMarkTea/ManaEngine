using Mana;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics;

Console.WriteLine("ManaVoxel.Web booting (browser-wasm)...");

// Boot smoke test: prove the full ManaCore + Mana + ManaGraphics reference graph loads and
// runs real engine code under Mono's wasm runtime, not just that each project compiles in
// isolation (that was already verified per-project; this is the first time they've run
// together as one published app). Deliberately does NOT attempt real windowing or WebGPU
// device creation yet -- M1 (browser IWindow/IInputContext) and M2 (the real WebGPU shim)
// aren't implemented, so this stays honest about what's proven vs. still stubbed rather
// than faking a working play-mode experience. See WebPort-Plan.md for both milestones.

var world = new ManaWorld();
Console.WriteLine($"ManaWorld constructed OK ({world.Meshes.Count} meshes, {world.Scenes.Count} scenes at boot).");

Console.WriteLine($"ManaGraphicsBackendKind values: {string.Join(", ", Enum.GetNames<ManaGraphicsBackendKind>())}");

using var nativeContext = new ManaBrowserWebGpuNativeContext();
var resolved = nativeContext.TryGetProcAddress("wgpuCreateInstance", out _);
Console.WriteLine(
    resolved
        ? "UNEXPECTED: wgpuCreateInstance resolved -- M2 shim already implemented?"
        : "Confirmed (expected): wgpuCreateInstance not resolved yet -- M2's WebGPU shim is still a stub.");

Console.WriteLine("Boot smoke test complete. Next real milestones: M1 (browser window/input), M2 (WebGPU shim).");
