import { dotnet } from './_framework/dotnet.js'

// --- Download progress ---------------------------------------------------
// The overlay in index.html is already on screen by the time this module runs; what it has no
// way to know on its own is how far the ~58 MB of runtime, assemblies and bundled assets has
// got. Nothing in C# can report that, because none of it is running yet.
//
// dotnet.js exposes onDownloadResourceProgress(loadedCount, totalCount), but that counts FILES,
// and the split here is hopeless for a progress bar: dotnet.native.wasm alone is 23 MB of the
// total across ~60 resources, so a file count sits near zero through most of the wait and then
// leaps. Counting bytes off the wire is the only honest version.
//
// dotnet.js calls globalThis.fetch by lookup at each call site rather than capturing a
// reference at module load (confirmed in the shipped dotnet.js), so replacing it here -- before
// dotnet.create() -- is enough to see every resource request.
function installDownloadProgress() {
    const originalFetch = globalThis.fetch.bind(globalThis);

    let received = 0;

    // Two denominators, and the bar uses whichever is larger.
    //
    // seenTotal is the sum of the Content-Lengths that have actually arrived, and on its own it is
    // close to useless: a resource only joins it once its own response headers are in, so early on
    // the ratio is "bytes received over bytes of what has already started", which sits near 1 no
    // matter how little of the app is down. Measured before this was added, the bar read
    // "1.1 / 1.1 MB, 70%" one second in and then did not move for the rest of the download.
    //
    // manifestTotal is the real figure, measured off the produced bundle at publish time (see the
    // ManaWriteDownloadSize task in ManaVoxel.Web.csproj). It is fetched but NOT awaited: waiting
    // on it would delay every real download by a round trip to save a few hundred milliseconds of
    // imprecision. It lands within the first few frames and the bar corrects itself, and until it
    // does, seenTotal keeps the display honest.
    let seenTotal = 0;
    let manifestTotal = 0;

    const denominator = () => Math.max(manifestTotal, seenTotal);

    // originalFetch, not the wrapper below: the manifest must not count towards its own total.
    // Every failure path is silent on purpose -- a build that never ran the task, an older bundle,
    // a host serving the app without it. The screen still works, just with the lazier denominator.
    originalFetch('mana-download-size.json', { cache: 'no-store' })
        .then(response => (response.ok ? response.json() : null))
        .then(manifest => {
            if (manifest && manifest.totalBytes > 0) {
                manifestTotal = manifest.totalBytes;
            }
        })
        .catch(() => { });

    globalThis.fetch = async function (input, init) {
        const response = await originalFetch(input, init);

        try {
            const url = typeof input === 'string' ? input : (input && input.url) || '';
            // _framework covers everything: the bundled VFS assets (environment map, asset zips)
            // are deployed to _framework/supportFiles, not beside it.
            const isAppResource = url.includes('_framework/');
            const length = Number(response.headers.get('content-length'));

            if (!isAppResource || !response.ok || !response.body || !length) {
                return response;
            }

            seenTotal += length;

            const reader = response.body.getReader();
            const counted = new ReadableStream({
                async pull(controller) {
                    const { done, value } = await reader.read();
                    if (done) {
                        controller.close();
                        return;
                    }
                    received += value.byteLength;
                    globalThis.ManaLoading.download(received, denominator());
                    controller.enqueue(value);
                },
                cancel(reason) {
                    return reader.cancel(reason);
                }
            });

            // Headers are copied wholesale, and that is load-bearing rather than tidiness:
            // dotnet.native.wasm goes through WebAssembly.instantiateStreaming, which refuses
            // any response not served as application/wasm and falls back to buffering the whole
            // 23 MB into an ArrayBuffer instead. Dropping the content-type here would silently
            // make the largest download slower and hungrier for memory.
            return new Response(counted, {
                status: response.status,
                statusText: response.statusText,
                headers: response.headers
            });
        } catch (error) {
            // A progress bar is never worth breaking the boot for. Anything unexpected in the
            // wrapping above and the caller still gets the response it asked for, untouched.
            console.warn('[ManaLoading] progress instrumentation skipped:', error);
            return response;
        }
    };
}

try {
    installDownloadProgress();

    const runtime = await dotnet.create();
    const { runMain } = runtime;

    // Downloads are done; from here the engine's own load owns the bar, reported from C# through
    // mana_interop.js. Set once here so the screen never shows a stale "Downloading" line during
    // the gap before the first C# report.
    globalThis.ManaLoading.engine('Starting engine', 0);

    // mana_webgpu.c's EM_JS blocks and mana_interop.js both call back into C# through
    // globalThis.ManaExports (JS scopes totally separate from this module -- EM_JS is injected
    // directly into dotnet.native.js's own scope, and mana_interop.js is a JSHost.ImportAsync
    // module -- so a global is the only thing all three sides can reach). Without this, those
    // call sites' `if (globalThis.ManaExports)` guards silently no-op instead of throwing, which
    // is why the adapter/device request stalled with no visible error at all. getAssemblyExports
    // lives on the runtime instance dotnet.create() returns, not as a named export of dotnet.js.
    globalThis.ManaExports = await runtime.getAssemblyExports('ManaGraphics');

    // Does not wait for the game: browser bring-up is async and non-blocking, so this returns
    // long before there is anything on the canvas. The overlay is dismissed from the C# side,
    // after the first real frame -- see BrowserPlayGameMode.
    await runMain();
} catch (error) {
    // Without this a failed boot is a black page and a stack trace in a console nobody had open.
    globalThis.ManaLoading.fail(error && error.stack ? error.stack : String(error));
    throw error;
}
