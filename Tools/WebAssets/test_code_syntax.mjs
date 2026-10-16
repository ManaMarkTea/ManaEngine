import assert from 'node:assert/strict';
import { highlightSource, syntaxColors as color } from './screensaver/code-syntax.js';

const lines = [
    'public Vector3 Move(int n = 0xFF) {',
    String.raw`var url = "https://example/\"quote"; // 42`,
    '/* outer', ' /* nested */ still comment', 'end */ return value;',
    'var raw = """', '// literal, not a comment', '""";',
    'var verbatim = @"', '/* literal */ ""quoted""";',
    'float v = 1.25e-3f; var s = 1.ToString();',
    '@group(0) fn shade() -> vec4<f32> {', '#if DEBUG'
];
const result = highlightSource(lines);
const at = (line, fragment) => {
    const index = lines[line].indexOf(fragment);
    assert.ok(index >= 0);
    let offset = 0;
    for (const [text, tint] of result[line]) {
        if (index < offset + text.length) return tint;
        offset += text.length;
    }
};
lines.forEach((line, i) => assert.equal(result[i].map(t => t[0]).join(''), line, 'highlighting preserves every character'));
assert.equal(at(0, 'public'), color.keyword);
assert.equal(at(0, 'Vector3'), color.type);
assert.equal(at(0, 'Move'), color.method);
assert.equal(at(0, 'int'), color.type);
assert.equal(at(0, '0xFF'), color.number);
assert.equal(at(1, 'https'), color.string);
assert.equal(at(1, '// 42'), color.comment);
assert.equal(at(3, 'still'), color.comment);
assert.equal(at(4, 'return'), color.keyword);
assert.equal(at(6, '// literal'), color.string);
assert.equal(at(9, '/* literal'), color.string);
assert.equal(at(10, '1.25e-3f'), color.number);
assert.equal(at(10, 'ToString'), color.method);
assert.equal(at(11, 'fn'), color.keyword);
assert.equal(at(11, 'vec4'), color.type);
assert.equal(at(12, '#if'), color.directive);
console.log('Syntax highlighting checks passed');
