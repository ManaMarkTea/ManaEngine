// Separate from boot progress: these downloads happen while the editor is already usable.
const active = new Map();
let panel;
let dismissTimer;

function status(text, failed = false) {
    if (typeof document === 'undefined') return;
    if (!panel) {
        panel = document.createElement('div');
        panel.setAttribute('role', 'status');
        panel.style.cssText = 'position:fixed;bottom:32px;right:16px;z-index:10000;max-width:420px;padding:12px 16px;border-radius:6px;background:#18212e;color:#fff;font:14px system-ui;pointer-events:none;box-shadow:0 2px 12px #0008';
        document.body.appendChild(panel);
    }
    clearTimeout(dismissTimer);
    panel.textContent = text;
    panel.style.display = 'block';
    if (failed) dismissTimer = setTimeout(() => panel.style.display = 'none', 12000);
}

export async function loadCatalog() {
    const response = await fetch(new URL('./asset-catalog.json', import.meta.url), { cache: 'no-cache' });
    if (!response.ok) throw new Error(`Asset catalog unavailable (${response.status}). Reload to retry.`);
    return response.text();
}

async function verifiedBytes(response, hash, size) {
    const bytes = new Uint8Array(await response.arrayBuffer());
    if (bytes.length !== size) throw new Error('Asset download is incomplete.');
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    const actual = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('');
    if (actual !== hash) throw new Error('Asset checksum mismatch.');
    return bytes;
}

async function fetchAsset(url, hash, size) {
    const address = new URL(url, import.meta.url).href;
    let cache;
    try {
        cache = await caches.open('mana-assets-v1');
        const cached = await cache.match(address);
        if (cached) {
            try { return await verifiedBytes(cached, hash, size); }
            catch { await cache.delete(address); }
        }
    } catch { /* Private mode/quota restrictions must not prevent uncached use. */ }

    status(`Downloading asset (${(size / 1048576).toFixed(1)} MB)…`);
    const response = await fetch(address, { cache: 'no-cache', signal: AbortSignal.timeout(120000) });
    if (!response.ok) throw new Error(`Asset download failed (${response.status}). Select the asset again to retry.`);
    let complete;
    if (response.body) {
        const reader = response.body.getReader();
        const chunks = [];
        let received = 0;
        for (;;) {
            const { done, value } = await reader.read();
            if (done) break;
            chunks.push(value);
            received += value.length;
            if (received > size) {
                await reader.cancel();
                throw new Error('Asset download exceeds its catalog size.');
            }
            status(`Downloading asset… ${Math.min(100, Math.round(received / Math.max(1, size) * 100))}%`);
        }
        complete = new Response(new Blob(chunks));
    } else complete = response;
    const bytes = await verifiedBytes(complete, hash, size);
    try { await cache?.put(address, new Response(bytes)); }
    catch { /* Cache storage is an optimization, not a requirement. */ }
    return bytes;
}

export async function downloadAsset(url, hash, size) {
    if (active.has(url)) return { bytes: await active.get(url) };
    const task = fetchAsset(url, hash, size);
    active.set(url, task);
    try {
        const bytes = await task;
        status('Asset ready.');
        dismissTimer = setTimeout(() => { if (panel) panel.style.display = 'none'; }, 2500);
        return { bytes };
    } catch (error) {
        status(`${error.message} Select the asset again to retry.`, true);
        throw error;
    } finally { active.delete(url); }
}

export function readBytes(result) { return result.bytes; }
