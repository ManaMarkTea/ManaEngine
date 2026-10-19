// Source files from a public GitHub repository: code only, a random sample. Same rules as GitHubSources.cs.
const codeExtensions = new Set(('cs fs fsx vb c h cc cpp cxx hpp hh hxx inl m mm swift java kt kts scala groovy go rs zig nim d ' +
    'py pyi rb php pl pm lua dart js mjs cjs jsx ts tsx vue svelte ex exs erl hrl hs ml mli clj cljs ' +
    'elm r jl cr gd wgsl hlsl glsl vert frag comp shader metal cu sh bash zsh ps1 psm1 sql v sv vhd asm s').split(' '));
const skippedFolders = new Set(('node_modules vendor vendors third_party thirdparty external externals extern dist build out bin obj ' +
    'target packages .git .github generated __generated__ deps').split(' '));
export const sampleSize = 48;
const historyKey = 'code-screensaver.repositories';

/** "owner/name" or a github.com URL (optionally /tree/branch); null for anything that is not GitHub. */
export function parseRepository(text) {
    text = (text || '').trim();
    const match = text.match(/^([A-Za-z0-9-]+)\/([A-Za-z0-9._-]+?)(?:\.git)?$/)
        || text.match(/^(?:https?:\/\/)?(?:www\.)?github\.com\/([A-Za-z0-9-]+)\/([A-Za-z0-9._-]+?)(?:\.git)?(?:\/tree\/([^/?#]+))?(?:[/?#].*)?$/i);
    if (!match || match[2] === '.' || match[2] === '..') return null;
    const branch = match[3] ? decodeURIComponent(match[3]) : null;
    const owner = match[1], name = match[2];
    return { owner, name, branch, key: branch ? `https://github.com/${owner}/${name}/tree/${branch}` : `${owner}/${name}`,
        label: branch ? `${owner}/${name}@${branch}` : `${owner}/${name}` };
}

export function isCodePath(path, size) {
    if (size < 600 || size > 160000 || /\.min\.js$/i.test(path)) return false;
    const parts = path.split('/');
    if (parts.slice(0, -1).some(part => skippedFolders.has(part.toLowerCase()))) return false;
    const dot = parts.at(-1).lastIndexOf('.');
    return dot > 0 && codeExtensions.has(parts.at(-1).slice(dot + 1).toLowerCase());
}

async function json(url, signal) {
    const response = await fetch(url, { signal, headers: { Accept: 'application/vnd.github+json' } });
    if (response.ok) return response.json();
    throw new Error(response.status === 404 ? 'not found (public repositories only)'
        : response.status === 403 || response.status === 429 ? 'GitHub rate limit reached; try again later'
        : `GitHub answered ${response.status}`);
}

export async function loadRepository(repository, signal) {
    const api = `https://api.github.com/repos/${repository.owner}/${repository.name}`;
    const branch = repository.branch || (await json(api, signal)).default_branch || 'main';
    const tree = await json(`${api}/git/trees/${encodeURIComponent(branch)}?recursive=1`, signal);
    const candidates = tree.tree.filter(entry => entry.type === 'blob' && isCodePath(entry.path, entry.size ?? 0)).map(entry => entry.path);
    if (!candidates.length) throw new Error('no code files found');
    for (let i = candidates.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [candidates[i], candidates[j]] = [candidates[j], candidates[i]];
    }
    const sample = candidates.slice(0, sampleSize).sort();
    const encode = path => path.split('/').map(encodeURIComponent).join('/');
    const files = [];
    // A few at a time: raw.githubusercontent.com is generous, but not with dozens at once.
    for (let start = 0; start < sample.length; start += 6) {
        await Promise.all(sample.slice(start, start + 6).map(async path => {
            try {
                const response = await fetch(`https://raw.githubusercontent.com/${repository.owner}/${repository.name}/${encode(branch)}/${encode(path)}`, { signal });
                if (!response.ok) return;
                const text = await response.text();
                if (text.includes('\0')) return;
                const lines = text.replace(/^﻿/, '').replace(/\r/g, '').replace(/\t/g, '    ').split('\n');
                if (lines.length >= 24) files.push({ path: `${repository.owner}/${repository.name}/${path}`, lines });
            } catch (error) { if (error.name === 'AbortError') throw error; }
        }));
    }
    if (!files.length) throw new Error('no code files could be read');
    return files.sort((a, b) => a.path < b.path ? -1 : 1);
}

/** { last, recent }: the repository shown last ('' for ManaRenderer) and the recent ones, in this browser. */
export function readHistory() {
    try {
        const stored = JSON.parse(localStorage.getItem(historyKey) || '{}');
        return { last: typeof stored.last === 'string' ? stored.last : '', recent: Array.isArray(stored.recent) ? stored.recent.filter(r => typeof r === 'string') : [] };
    } catch { return { last: '', recent: [] }; }
}

export function useRepository(key) {
    const history = readHistory();
    history.last = key;
    if (key) history.recent = [key, ...history.recent.filter(r => r.toLowerCase() !== key.toLowerCase())].slice(0, 10);
    try { localStorage.setItem(historyKey, JSON.stringify(history)); } catch { /* Private windows can refuse storage. */ }
    return history;
}
