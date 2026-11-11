// Writes what pixelfx itself draws, as references for the C# port (ManaPixelFx): for every frame of one run at the
// effect's fps -- as its sheet renderer plays it, light eased off the edges -- a hash of the colour buffer and of the
// light buffer. PixelFxParityChecks plays the port the same way and compares, frame by frame.
//
//   node Tools/PixelFx/parity.mjs fire/torch fire/campfire      (or a category: fire)
//
// Options: --pixelfx DIR (a pixelfx checkout; default ../References/pixelfx, then ../pixelfx, or PIXELFX).
// Writes ManaTests/Data/PixelFx/<category>/<name>.json. Run it for every effect ported, and again when one changes.

import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const REPO = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const args = process.argv.slice(2), flags = {}, ids = [];
for (let i = 0; i < args.length; i++) {
  if (args[i].startsWith("--")) {
    flags[args[i].slice(2)] = args[i + 1];
    i++;
  } else {
    ids.push(args[i]);
  }
}
if (ids.length === 0) {
  console.error("usage: node Tools/PixelFx/parity.mjs <id|category|pattern>... [--pixelfx DIR]");
  process.exit(2);
}
const besideRepo = [join(REPO, "..", "References", "pixelfx"), join(REPO, "..", "pixelfx")].find(dir => existsSync(dir));
const pixelfx = resolve(flags.pixelfx ?? process.env.PIXELFX ?? besideRepo ?? join(REPO, "..", "References", "pixelfx"));
if (!existsSync(join(pixelfx, "tools", "lib", "fxlib.mjs"))) {
  console.error(`no pixelfx checkout at ${pixelfx}: git clone https://github.com/patch-gg/pixelfx into ../References/pixelfx, or pass --pixelfx`);
  process.exit(2);
}
const { resolveIds, loadEffect, renderFrames } = await import(pathToFileURL(join(pixelfx, "tools", "lib", "fxlib.mjs")).href);

// 32-bit FNV-1a over a buffer's bytes, as hex: the C# side hashes its buffers the same way.
function fnv(words) {
  const bytes = new Uint8Array(words.buffer, words.byteOffset, words.byteLength);
  let h = 0x811c9dc5;
  for (let i = 0; i < bytes.length; i++) {
    h ^= bytes[i];
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, "0");
}

let failed = 0;
for (const id of resolveIds(ids)) {
  try {
    const fx = await loadEffect(id, join(pixelfx, "src", "fx"));
    const r = renderFrames(fx, { bg: "none" });
    const out = join(REPO, "ManaTests", "Data", "PixelFx", ...id.split("/")) + ".json";
    mkdirSync(dirname(out), { recursive: true });
    const frames = r.frames.map((f) => `${fnv(f.raw)} ${fnv(f.light)}`);
    writeFileSync(out, JSON.stringify({ id, fps: r.fps, size: [r.w, r.h], frames }, null, 1) + "\n");
    console.log(`${id}  ${frames.length} frames  ->  ${relative(REPO, out)}`);
  } catch (e) {
    failed++;
    console.error(`${id}  FAILED: ${e.stack ?? e}`);
  }
}
process.exit(failed ? 1 : 0);
