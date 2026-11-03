// Imports pixelfx effects (https://github.com/patch-gg/pixelfx) as Mana sprite sheets: renders each effect with
// pixelfx's own code, crops the frames to what the effect draws, packs them into one PNG and writes a .manasheet
// beside it with the effect's fps, loop and pivot -- played by a PixelFx script (ManaPixelFxComponent).
//
//   node Tools/PixelFx/import_pixelfx.mjs fire/torch explosions/small-explosion
//   node Tools/PixelFx/import_pixelfx.mjs fire "ice/*" --pixelfx ../pixelfx --ppu 20
//   node Tools/PixelFx/import_pixelfx.mjs fire/torch --opts '{"palette":["#102","#215","#43a","#8cf","#fff"]}' --as blue-torch
//
// Options: --pixelfx DIR (a pixelfx checkout; default ../pixelfx beside this repo, or PIXELFX), --out DIR
// (default Content/Sprites/PixelFx), --ppu N (pixels a metre, default 20: the stock 38 px character stands ~1.9 m),
// --opts JSON (the effect's options: palette, seed, flip...), --seed N, --as NAME (file name, for a recoloured copy),
// --max N (largest sheet side, default 2048: a 4096 one is 64 MB of GPU memory; one that would drop below 15 fps
// to fit may grow to 4096 instead).
// Re-importing keeps each sheet's id, so levels and prefabs that use it follow.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { randomUUID } from "node:crypto";

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
  console.error("usage: node Tools/PixelFx/import_pixelfx.mjs <id|category|pattern>... [--pixelfx DIR] [--out DIR] [--ppu N] [--opts JSON] [--as NAME]");
  process.exit(2);
}

const pixelfx = resolve(flags.pixelfx ?? process.env.PIXELFX ?? join(REPO, "..", "pixelfx"));
if (!existsSync(join(pixelfx, "tools", "lib", "fxlib.mjs"))) {
  console.error(`no pixelfx checkout at ${pixelfx}: git clone https://github.com/patch-gg/pixelfx there, or pass --pixelfx`);
  process.exit(2);
}
const { resolveIds, loadEffect, renderFrames, canvas } = await import(pathToFileURL(join(pixelfx, "tools", "lib", "fxlib.mjs")).href);

const outRoot = resolve(REPO, flags.out ?? join("Content", "Sprites", "PixelFx"));
const ppu = +(flags.ppu ?? 20), maxSide = +(flags.max ?? 2048);
const options = flags.opts ? JSON.parse(flags.opts) : undefined;
const resolved = resolveIds(ids);
if (flags.as && resolved.length !== 1) {
  console.error("--as names one effect's copy; give it a single id");
  process.exit(2);
}

/** The smallest rectangle holding every drawn pixel of every frame, one pixel of room round it. */
function drawnBounds(r) {
  let x0 = r.w, y0 = r.h, x1 = -1, y1 = -1;
  for (const f of r.frames) {
    for (let y = 0; y < r.h; y++) {
      for (let x = 0; x < r.w; x++) {
        if (f.clear[(y * r.w + x) * 4 + 3] > 0) {
          x0 = Math.min(x0, x); x1 = Math.max(x1, x);
          y0 = Math.min(y0, y); y1 = Math.max(y1, y);
        }
      }
    }
  }
  if (x1 < 0) {
    return { x: 0, y: 0, w: 1, h: 1 };
  }
  x0 = Math.max(0, x0 - 1); y0 = Math.max(0, y0 - 1);
  x1 = Math.min(r.w - 1, x1 + 1); y1 = Math.min(r.h - 1, y1 + 1);
  return { x: x0, y: y0, w: x1 - x0 + 1, h: y1 - y0 + 1 };
}

function crop(rgba, w, b) {
  const out = new Uint8ClampedArray(b.w * b.h * 4);
  for (let y = 0; y < b.h; y++) {
    out.set(rgba.subarray(((b.y + y) * w + b.x) * 4, ((b.y + y) * w + b.x + b.w) * 4), y * b.w * 4);
  }
  return out;
}

/** Columns for n frames of w x h, as square a sheet as fits; null when no layout keeps both sides within max. */
function layout(n, w, h, max = maxSide) {
  let best = null;
  for (let cols = 1; cols <= n; cols++) {
    const rows = Math.ceil(n / cols), W = cols * w, H = rows * h;
    if (W > max || H > max) {
      continue;
    }
    const score = Math.max(W, H);
    if (!best || score < best.score) {
      best = { cols, rows, score };
    }
  }
  return best;
}

let failed = 0;
for (const id of resolved) {
  try {
    const fx = await loadEffect(id, join(pixelfx, "src", "fx"));
    const seed = flags.seed !== undefined ? +flags.seed : undefined;
    let r = renderFrames(fx, { bg: "none", seed, options });
    const b = drawnBounds(r);
    // Too many frames for one texture: keep every other one at half the rate, which pixel art takes well.
    // Below 15 fps an effect reads as choppy, so it takes a bigger sheet first.
    let step = 1, grid = layout(r.n, b.w, b.h);
    while (!grid && r.fps / (step * 2) >= 15) {
      step *= 2;
      grid = layout(Math.ceil(r.n / step), b.w, b.h);
    }
    grid ??= layout(Math.ceil(r.n / step), b.w, b.h, Math.max(maxSide, 4096));
    while (!grid && step < 8) {
      step *= 2;
      grid = layout(Math.ceil(r.n / step), b.w, b.h, Math.max(maxSide, 4096));
    }
    if (!grid) {
      throw new Error(`${b.w}x${b.h} frames do not fit a ${maxSide} px sheet`);
    }
    const kept = r.frames.filter((_, i) => i % step === 0);
    const sheet = canvas(grid.cols * b.w, grid.rows * b.h, [0, 0, 0, 0]);
    kept.forEach((f, i) => sheet.paste(crop(f.clear, r.w, b), b.w, b.h, (i % grid.cols) * b.w, Math.floor(i / grid.cols) * b.h));

    const [category, effect] = id.split("/");
    const name = flags.as ?? effect;
    const dir = join(outRoot, category);
    mkdirSync(dir, { recursive: true });
    const png = join(dir, `${name}.png`), file = join(dir, `${name}.manasheet`);
    writeFileSync(png, sheet.png());

    let assetId = randomUUID().replaceAll("-", "");
    if (existsSync(file)) {
      assetId = JSON.parse(readFileSync(file, "utf8")).id ?? assetId;
    }
    const [ox, oy] = fx.meta.origin;
    const title = flags.as ? flags.as.replaceAll("-", " ") : fx.meta.name ?? effect;
    const manasheet = {
      format: "mana-sprite-sheet",
      version: 1,
      id: assetId,
      name: title.charAt(0).toUpperCase() + title.slice(1),
      source: `pixelfx:${id}`,
      frames: [],
      texture: relative(REPO, png).replaceAll("\\", "/"),
      columns: grid.cols,
      rows: grid.rows,
      normalMap: "",
      pixelsPerUnit: ppu,
      // pixelfx's origin is a pixel from the top left; a sheet's pivot runs 0..1 across and up.
      pivot: [+((ox - b.x) / b.w).toFixed(4), +((b.y + b.h - oy) / b.h).toFixed(4)],
      animations: [{ name: "play", frames: kept.map((_, i) => i), framesPerSecond: r.fps / step, loop: !!fx.meta.loop }],
    };
    writeFileSync(file, JSON.stringify(manasheet, null, 2) + "\n");
    const kb = Math.round(readFileSync(png).length / 1024);
    console.log(`${id}  ${kept.length} frames ${b.w}x${b.h} @${r.fps / step}  ${fx.meta.loop ? "loop" : "one-shot"}  ->  ${relative(REPO, file)} (${sheet.w}x${sheet.h}, ${kb} KB)`);
  } catch (e) {
    failed++;
    console.error(`${id}  FAILED: ${e.stack ?? e}`);
  }
}
process.exit(failed ? 1 : 0);
