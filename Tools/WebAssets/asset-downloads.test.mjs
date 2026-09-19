import assert from 'node:assert/strict';
import { downloadAsset, readBytes } from './asset-downloads.js';

const bytes = new Uint8Array([1, 2, 3]);
const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
const stored = new Map();
globalThis.caches = { open: async () => ({
    match: async url => stored.get(url)?.clone(),
    put: async (url, response) => stored.set(url, response),
    delete: async url => stored.delete(url)
}) };
let requests = 0;
globalThis.fetch = async () => { requests++; return new Response(bytes); };
const url = 'https://example.test/AssetLibrary/model.glb';
const [a, b] = await Promise.all([downloadAsset(url, hash, 3), downloadAsset(url, hash, 3)]);
assert.deepEqual(readBytes(a), bytes);
assert.deepEqual(readBytes(b), bytes);
assert.equal(requests, 1, 'concurrent downloads are deduplicated');
await downloadAsset(url, hash, 3);
assert.equal(requests, 1, 'persistent cache avoids the network');
stored.set(url, new Response(new Uint8Array([3, 2, 1])));
await downloadAsset(url, hash, 3);
assert.equal(requests, 2, 'corrupt cache entry is replaced');
globalThis.fetch = async () => new Response('missing', { status: 404 });
await assert.rejects(downloadAsset(url + '?new', hash, 3), /404/);
globalThis.fetch = async () => new Response(bytes);
await downloadAsset(url + '?new', hash, 3);
globalThis.caches.open = async () => { throw new Error('quota'); };
assert.deepEqual(readBytes(await downloadAsset(url + '?private', hash, 3)), bytes);
await assert.rejects(downloadAsset(url + '?bad-hash', 'bad', 3), /checksum/);
await assert.rejects(downloadAsset(url + '?truncated', hash, 4), /incomplete/);
console.log('Asset download checks passed: deduplication, cache reuse, corruption, retry, quota fallback, integrity.');
