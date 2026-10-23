// The render-target viewer (ManaRenderTargetPreview): the frame's intermediate targets drawn small, 3 x 2, into one
// texture the UI shows. One full-screen triangle; each pixel works out which tile it is in and reads that target.

const COLUMNS : u32 = 3u;
const TILE : vec2<f32> = vec2<f32>(480.0, 270.0);
const LIGHT_TILES : vec2<f32> = vec2<f32>(16.0, 9.0);
const LIGHT_SLICES : u32 = 24u;

struct Params {
    inverse_projection : mat4x4<f32>,
    // x exposure, y placed-light count.
    info : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : Params;
@group(0) @binding(1) var scene_color : texture_2d<f32>;
@group(0) @binding(2) var scene_depth : texture_depth_2d;
@group(0) @binding(3) var shadow_map : texture_depth_2d_array;
@group(0) @binding(4) var<storage, read> light_cells : array<vec2<u32>>;

@vertex
fn vs_main(@builtin(vertex_index) index : u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4<f32>(uv * 2.0 - vec2<f32>(1.0), 0.0, 1.0);
}

// Grey levels as they should read on screen: the target is sRGB, so a display value is linearised on the way out.
fn display(value : vec3<f32>) -> vec4<f32> {
    return vec4<f32>(pow(clamp(value, vec3<f32>(0.0), vec3<f32>(1.0)), vec3<f32>(2.2)), 1.0);
}

fn heat(count : u32) -> vec3<f32> {
    if (count == 0u) {
        return vec3<f32>(0.03, 0.03, 0.1);
    }
    let t = clamp(f32(count) / 32.0, 0.0, 1.0);
    if (t < 0.33) {
        return mix(vec3<f32>(0.0, 0.1, 0.9), vec3<f32>(0.0, 0.85, 0.3), t / 0.33);
    }
    if (t < 0.66) {
        return mix(vec3<f32>(0.0, 0.85, 0.3), vec3<f32>(1.0, 0.9, 0.0), (t - 0.33) / 0.33);
    }
    return mix(vec3<f32>(1.0, 0.9, 0.0), vec3<f32>(1.0, 0.05, 0.0), (t - 0.66) / 0.34);
}

fn texel(local : vec2<f32>, size : vec2<u32>) -> vec2<i32> {
    return vec2<i32>(clamp(local * vec2<f32>(size), vec2<f32>(0.0), vec2<f32>(size) - vec2<f32>(1.0)));
}

@fragment
fn fs_main(@builtin(position) position : vec4<f32>) -> @location(0) vec4<f32> {
    let cell = floor(position.xy / TILE);
    let tile = u32(cell.x) + u32(cell.y) * COLUMNS;
    let local = (position.xy - cell * TILE) / TILE;

    if (tile == 0u) {
        // Scene colour at the frame's exposure, Reinhard-mapped: the light before the real tone map and bloom.
        let colour = textureLoad(scene_color, texel(local, textureDimensions(scene_color)), 0).rgb * params.info.x;
        return vec4<f32>(colour / (vec3<f32>(1.0) + colour), 1.0);
    }

    if (tile == 1u) {
        let depth = textureLoad(scene_depth, texel(local, textureDimensions(scene_depth)), 0);
        if (depth >= 1.0) {
            // Nothing drawn here: the sky.
            return display(vec3<f32>(0.1, 0.12, 0.25));
        }
        let ndc = vec2<f32>(local.x * 2.0 - 1.0, 1.0 - local.y * 2.0);
        let view = params.inverse_projection * vec4<f32>(ndc, depth, 1.0);
        let distance = max(-view.z / view.w, 0.0);
        return display(vec3<f32>(1.0 - clamp(log2(1.0 + distance) / log2(1001.0), 0.0, 1.0)));
    }

    if (tile <= 4u) {
        // The square map in the middle of its wide tile; nearer the sun is darker.
        let fitted = vec2<f32>((local.x - 0.5) * TILE.x / TILE.y + 0.5, local.y);
        if (fitted.x < 0.0 || fitted.x > 1.0) {
            return display(vec3<f32>(0.08));
        }
        let layer = i32(tile - 2u);
        let size = textureDimensions(shadow_map);
        let depth = textureLoad(shadow_map, texel(fitted, size), layer, 0);
        if (depth >= 0.9999) {
            // Cleared: nothing casts here.
            return display(vec3<f32>(0.1, 0.12, 0.25));
        }
        // Casters fill a thin band of the light's long box: stretched between the nearest and farthest found on a
        // coarse grid, so their shapes read.
        var low = depth;
        var high = depth;
        for (var y = 0u; y < 8u; y = y + 1u) {
            for (var x = 0u; x < 8u; x = x + 1u) {
                let probe = textureLoad(shadow_map, texel((vec2<f32>(f32(x), f32(y)) + vec2<f32>(0.5)) / 8.0, size), layer, 0);
                if (probe < 0.9999) {
                    low = min(low, probe);
                    high = max(high, probe);
                }
            }
        }
        let t = clamp((depth - low) / max(high - low, 0.0001), 0.0, 1.0);
        return display(vec3<f32>(0.15 + 0.85 * t));
    }

    // The busiest depth slice of each screen tile, over the tile grid.
    let light_tile = min(floor(local * LIGHT_TILES), LIGHT_TILES - vec2<f32>(1.0));
    var most = 0u;
    if (params.info.y > 0.5) {
        for (var slice = 0u; slice < LIGHT_SLICES; slice = slice + 1u) {
            let index = u32(light_tile.x) + u32(light_tile.y) * u32(LIGHT_TILES.x) + slice * u32(LIGHT_TILES.x * LIGHT_TILES.y);
            most = max(most, light_cells[index].y);
        }
    }
    let inside = local * LIGHT_TILES - light_tile;
    var colour = heat(most);
    if (inside.x < 0.03 || inside.y < 0.03) {
        colour = colour * 0.4;
    }
    return display(colour);
}
