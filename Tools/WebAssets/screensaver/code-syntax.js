const keywords = new Set(('abstract as async await base break case catch checked class const continue default delegate do else enum event explicit extern false finally fixed for foreach get if implicit in init interface internal is let lock namespace new null operator out override params partial private protected public readonly record ref required return sealed set sizeof stackalloc static struct switch this throw true try typeof unchecked unsafe using var virtual void volatile when where while with yield fn loop continuing discard enable requires diagnostic alias override workgroup uniform storage read write read_write function private').split(' '));
const types = new Set(('bool byte sbyte char decimal double dynamic float int long nint nuint object short string uint ulong ushort f16 f32 i32 u32 vec2 vec3 vec4 mat2x2 mat3x3 mat4x4 array atomic ptr sampler sampler_comparison texture_2d texture_cube texture_depth_2d').split(' '));
// Other languages' words, only outside C# and WGSL (where "type" or "match" are names). Same lists as CodeSyntax.cs.
const otherKeywords = new Set(('def elif lambda import from pass raise except global nonlocal and or not None True False self nil end then local ' +
    'func package go defer chan select range type map impl pub mut match mod crate trait use dyn move unsafe loop ' +
    'function export extends implements instanceof typeof undefined of declare module require begin rescue ensure ' +
    'unless until elsif module fun val object companion when data sealed suspend package import throws final super ' +
    'echo fi esac done then param foreach begin process').split(' '));
const hashCommentExtensions = new Set('py pyi rb pl pm sh bash zsh ps1 psm1 r jl ex exs nim cr gd'.split(' '));
const dashCommentExtensions = new Set('lua sql hs elm'.split(' '));
export const syntaxColors = { plain: '#b9cbd1', keyword: '#f47980', type: '#78cdda', method: '#edc779',
    string: '#80dfa0', number: '#e7a57b', comment: '#668277', punctuation: '#78909c', directive: '#c5a0e8' };

// Scan from the start of the file so randomly selected excerpts retain multiline comment/string state.
export function highlightSource(lines, path = '') {
    const extension = (path.match(/\.([^./]+)$/)?.[1] ?? '').toLowerCase();
    const hashComments = hashCommentExtensions.has(extension), dashComments = dashCommentExtensions.has(extension);
    const cFamily = !extension || extension === 'cs' || extension === 'wgsl';
    let blockDepth = 0, rawQuotes = 0, verbatim = false;
    return lines.map(text => {
        const kinds = Array(text.length).fill('plain');
        let i = 0;
        const paint = (start, end, kind) => kinds.fill(kind, start, end);
        while (i < text.length) {
            const start = i;
            if (blockDepth > 0 || !rawQuotes && !verbatim && text.startsWith('/*', i)) {
                if (!blockDepth) { blockDepth++; i += 2; }
                while (i < text.length) {
                    if (text.startsWith('/*', i)) { blockDepth++; i += 2; }
                    else if (text.startsWith('*/', i)) { blockDepth--; i += 2; if (!blockDepth) break; }
                    else i++;
                }
                paint(start, i, 'comment');
            } else if (rawQuotes || verbatim || /["']/.test(text[i]) || text.startsWith('@"', i)) {
                if (rawQuotes) {
                    const closing = text.indexOf('"'.repeat(rawQuotes), i);
                    i = closing < 0 ? text.length : closing + rawQuotes;
                    if (closing >= 0) rawQuotes = 0;
                } else if (verbatim || text.startsWith('@"', i)) {
                    if (!verbatim) { verbatim = true; i += 2; }
                    while (i < text.length) {
                        if (text[i++] !== '"') continue;
                        if (text[i] === '"') { i++; continue; }
                        verbatim = false; break;
                    }
                } else {
                    const quote = text[i++];
                    if (quote === '"' && text.startsWith('"""', start)) {
                        rawQuotes = 3;
                        while (text[start + rawQuotes] === '"') rawQuotes++;
                        i = start + rawQuotes;
                        const closing = text.indexOf('"'.repeat(rawQuotes), i);
                        if (closing >= 0) { i = closing + rawQuotes; rawQuotes = 0; } else i = text.length;
                    } else while (i < text.length) {
                        const ch = text[i++];
                        if (ch === '\\') i = Math.min(text.length, i + 1);
                        else if (ch === quote) break;
                    }
                }
                paint(start, i, 'string');
            } else if (text.startsWith('//', i) && !hashComments || hashComments && text[i] === '#'
                || dashComments && text.startsWith('--', i)) { paint(i, text.length, 'comment'); break; }
            else if (text[i] === '#') { paint(i, text.length, 'directive'); break; }
            else if (/[0-9]/.test(text[i])) {
                i += text.slice(i).match(/^(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|[0-9][0-9_]*(?:\.[0-9_]+)?(?:[eE][+-]?[0-9_]+)?)[uUlLfFdDmMhH]*/)[0].length;
                paint(start, i, 'number');
            } else if (/[a-zA-Z_]/.test(text[i])) {
                while (i < text.length && /[a-zA-Z0-9_]/.test(text[i])) i++;
                const word = text.slice(start, i);
                let next = i;
                while (next < text.length && /\s/.test(text[next])) next++;
                const kind = keywords.has(word) || !cFamily && otherKeywords.has(word) ? 'keyword' : types.has(word) ? 'type'
                    : text[next] === '(' ? 'method' : /[A-Z]/.test(word[0]) ? 'type' : 'plain';
                paint(start, i, kind);
            } else { if ('{}[]();,.<>:=+-*/!&|?@'.includes(text[i])) kinds[i] = 'punctuation'; i++; }
        }
        const tokens = [];
        for (let start = 0; start < text.length;) {
            let end = start + 1;
            while (end < text.length && kinds[end] === kinds[start]) end++;
            tokens.push([text.slice(start, end), syntaxColors[kinds[start]]]); start = end;
        }
        return tokens;
    });
}
