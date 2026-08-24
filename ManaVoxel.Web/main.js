import { dotnet } from './_framework/dotnet.js'

const runtime = await dotnet.create();
const { runMain } = runtime;

// mana_webgpu.c's EM_JS blocks and mana_interop.js both call back into C# through
// globalThis.ManaExports (JS scopes totally separate from this module -- EM_JS is injected
// directly into dotnet.native.js's own scope, and mana_interop.js is a JSHost.ImportAsync
// module -- so a global is the only thing all three sides can reach). Without this, those
// call sites' `if (globalThis.ManaExports)` guards silently no-op instead of throwing, which
// is why the adapter/device request stalled with no visible error at all. getAssemblyExports
// lives on the runtime instance dotnet.create() returns, not as a named export of dotnet.js.
globalThis.ManaExports = await runtime.getAssemblyExports('ManaGraphics');

await runMain();
