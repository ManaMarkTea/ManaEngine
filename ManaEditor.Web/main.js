import { dotnet } from './_framework/dotnet.js'
import { installTouchControls } from './touch-controls.js';

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
    // ManaWriteDownloadSize task in ManaEditor.Web.csproj). It is fetched but NOT awaited: waiting
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

// --- Settings ----------------------------------------------------------------------------------------
// Graphics choices live in the URL (?sky=, ?rendering=), which the engine reads once as it starts: changing one reloads
// the page with it, keeping the level. Unset means automatic -- phones get the light path, everything else the full one.
const settings = [
    { key: 'sky', label: 'Sky', options: [['', 'Automatic'], ['simple', 'Simple'], ['stars', 'Simple + real stars'], ['full', 'Full atmosphere']] },
    { key: 'rendering', label: 'Graphics', options: [['', 'Automatic'], ['mobile', 'Phone'], ['desktop', 'Desktop']] },
    { key: 'resolution', label: 'Resolution', options: [['', 'Full (lowers itself when slow)'], ['full', 'Full (never lower)'], ['1080', '1080p'], ['720', '720p']] }
];

function mountSettings() {
    const params = new URLSearchParams(location.search);
    for (const container of document.querySelectorAll('[data-mana-settings]')) {
        for (const setting of settings) {
            const label = document.createElement('label');
            label.className = 'mana-setting';
            label.append(setting.label);
            const select = document.createElement('select');
            for (const [value, text] of setting.options) {
                select.append(new Option(text, value, false, (params.get(setting.key) ?? '') === value));
            }
            select.addEventListener('change', () => {
                const next = new URLSearchParams(location.search);
                if (select.value) {
                    next.set(setting.key, select.value);
                } else {
                    next.delete(setting.key);
                }
                location.search = next.toString();
            });
            label.append(select);
            container.append(label);
        }
    }
}

// The URL without the level: back to the selector, keeping the settings.
function searchWith(level) {
    const params = new URLSearchParams(location.search);
    if (level) {
        params.set('level', level);
    } else {
        params.delete('level');
    }
    return params.toString();
}

mountSettings();

// --- Level selector ----------------------------------------------------------------------------------
// The page opens on a list of levels; picking one reloads it with ?level=, and only then does the engine download.
// levels.json is written at publish time from Content/Levels (see Tools/WebAssets/publish_assets.py).
async function showLevelSelector() {
    document.getElementById('manaLoading').remove();
    const select = document.getElementById('manaSelect');
    const list = document.getElementById('manaLevels');
    select.classList.add('mana-open');

    let levels = [];
    try {
        const response = await fetch('levels.json', { cache: 'no-store' });
        levels = response.ok ? (await response.json()).levels : [];
    } catch (error) {
        console.warn('[ManaSelect] levels.json unavailable:', error);
    }
    if (levels.length === 0) {
        // An older publish without the list: the game still plays its default level.
        levels = [{ name: 'Sample Course', file: 'Content/Levels/Sample Course.manalevel', description: '' }];
    }

    for (const level of levels) {
        const card = document.createElement('button');
        card.className = 'mana-level';
        card.title = level.name;
        const picture = document.createElement('div');
        picture.className = 'mana-level-picture';
        picture.textContent = level.name.split(' ').map(word => word[0]).join('').slice(0, 2).toUpperCase();
        if (level.picture) {
            picture.style.backgroundImage = `url("${encodeURI(level.picture)}")`;
            picture.textContent = '';
        }
        const body = document.createElement('div');
        body.className = 'mana-level-body';
        const name = document.createElement('div');
        name.className = 'mana-level-name';
        name.textContent = level.name;
        const description = document.createElement('div');
        description.className = 'mana-level-description';
        description.textContent = level.description || '';
        body.append(name, description);
        card.append(picture, body);
        card.addEventListener('click', () => {
            location.search = searchWith(level.file);
        });
        list.append(card);
    }
    list.querySelector('button')?.focus();
}

// --- Pause menu ------------------------------------------------------------------------------------------
// Escape while playing opens it and pauses the game; Resume (or Escape again) carries on, Restart goes back to the
// start of the level, and Choose another level returns to the selector -- a fresh page, so a fresh game.
function installPauseMenu(game, graphics) {
    const menu = document.getElementById('manaMenu');
    const hint = document.getElementById('manaHint');
    let ready = false;
    const touchReady = installTouchControls(graphics, () => setOpen(true), game);

    // Playable once the loading screen has gone; before that there is nothing to pause.
    const hide = globalThis.ManaLoading.hide;
    globalThis.ManaLoading.hide = function () {
        hide();
        ready = true;
        touchReady();
        hint.classList.add('mana-shown');
        setTimeout(() => hint.classList.remove('mana-shown'), 5000);
    };

    const setOpen = open => {
        globalThis.ManaMenuOpen = open;
        document.dispatchEvent(new Event('mana-pause'));
        menu.classList.toggle('mana-open', open);
        game.SetPaused(open);
        if (open) {
            // Whatever was held when the menu opened is let go, or the character walks on after Resume.
            graphics.OnFocusLost();
            document.getElementById('manaResume').focus();
        } else {
            document.getElementById('manaCanvas').focus?.();
        }
    };

    // Capture phase, so the game's own key handler never sees this Escape.
    window.addEventListener('keydown', event => {
        // A game with a pause of its own (Train Yard Wars) gets Escape; its own menu has Quit.
        if (event.code !== 'Escape' || !ready || (!globalThis.ManaMenuOpen && game.ModeHandlesEscape())) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        setOpen(!globalThis.ManaMenuOpen);
    }, { capture: true });

    document.getElementById('manaResume').addEventListener('click', () => setOpen(false));
    document.getElementById('manaRestart').addEventListener('click', () => {
        game.Restart();
        setOpen(false);
    });
    document.getElementById('manaQuit').addEventListener('click', () => {
        location.search = searchWith(null);
    });
}

const startup = new URLSearchParams(location.search);
const editorMode = (startup.get('mode') ?? document.body.dataset.startMode) === 'editor';
const chosenLevel = startup.get('level');

if (editorMode) {
    // The editor draws its own version and frame rate in its status bar.
    document.getElementById('manaBuildInfo').style.display = 'none';
}

if (!editorMode && !chosenLevel) {
    await showLevelSelector();
} else try {
    installDownloadProgress();

    const runtime = await dotnet.withApplicationArguments(...(editorMode ? ["--editor"] : [chosenLevel])).create();
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
    if (!editorMode) {
        const gameExports = await runtime.getAssemblyExports('ManaEditor.Web');
        installPauseMenu(gameExports.ManaEditor.Web.BrowserGameControls,
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop);
    }

    // Does not wait for the game: browser bring-up is async and non-blocking, so this returns
    // long before there is anything on the canvas. The overlay is dismissed from the C# side,
    // after the first real frame -- see BrowserPlayGameMode.
    await runMain();
} catch (error) {
    // Without this a failed boot is a black page and a stack trace in a console nobody had open.
    globalThis.ManaLoading.fail(error && error.stack ? error.stack : String(error));
    throw error;
}
