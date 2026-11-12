// The retro screen filter (ManaWebGpuRetroFilter, settings in ManaRetroFilterSettings): the tone-mapped picture read
// back in big pixels, colours cut to a few levels with a 4x4 Bayer dither, then a worn CRT / VHS look over it --
// sideways line jitter and a rolling tracking band, red and blue fringes, scanlines, grain, vignette and an optional
// bulge. Drawn into the window in the tone map's place, with the selection outline crisp above it.

struct RetroParams {
    // width, height, pixel size (screen pixels a big pixel), seconds.
    screen : vec4<f32>,
    // colour levels, dither, scanlines, chromatic aberration (big pixels).
    look : vec4<f32>,
    // noise, glitch, vignette, curvature.
    wear : vec4<f32>,
    // saturation, 1 when the colour texture decodes to linear (an sRGB window), 1 when scoped (effect_mask says where), unused.
    colour : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : RetroParams;
@group(0) @binding(1) var color_tex : texture_2d<f32>;
@group(0) @binding(2) var outline_mask : texture_2d<f32>;
// The post effect mask (ManaWebGpuPostEffectMaskPass): where a scoped filter applies. Unscoped, unread.
@group(0) @binding(3) var effect_mask : texture_2d<f32>;

#include "include/outline_composite.wgsl"

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) uv : vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) vertex_index : u32) -> VertexOutput {
    var positions = array<vec2<f32>, 3>(vec2<f32>(-1.0, -1.0), vec2<f32>(3.0, -1.0), vec2<f32>(-1.0, 3.0));
    var output : VertexOutput;
    let position = positions[vertex_index];
    output.position = vec4<f32>(position, 0.0, 1.0);
    output.uv = position * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5, 0.5);
    return output;
}

fn hash21(p : vec2<f32>) -> f32 {
    var q = fract(p * vec2<f32>(123.34, 456.21));
    q = q + dot(q, q + 45.32);
    return fract(q.x * q.y);
}

// 0..1 thresholds, so a flat colour between two levels turns into a fine even mix of both.
fn bayer4(cell : vec2<i32>) -> f32 {
    var thresholds = array<f32, 16>(
        0.0, 8.0, 2.0, 10.0,
        12.0, 4.0, 14.0, 6.0,
        3.0, 11.0, 1.0, 9.0,
        15.0, 7.0, 13.0, 5.0);
    let x = u32(cell.x) & 3u;
    let y = u32(cell.y) & 3u;
    return (thresholds[y * 4u + x] + 0.5) / 16.0;
}

fn to_gamma(linear : vec3<f32>) -> vec3<f32> {
    let low = linear * 12.92;
    let high = 1.055 * pow(max(linear, vec3<f32>(0.0)), vec3<f32>(1.0 / 2.4)) - 0.055;
    return select(high, low, linear <= vec3<f32>(0.0031308));
}

fn to_linear(gamma : vec3<f32>) -> vec3<f32> {
    let low = gamma / 12.92;
    let high = pow((max(gamma, vec3<f32>(0.0)) + 0.055) / 1.055, vec3<f32>(2.4));
    return select(high, low, gamma <= vec3<f32>(0.04045));
}

// The picture at one big pixel's centre, in gamma space (where colour levels are spaced as the eye sees them).
fn block_colour(block : vec2<f32>) -> vec3<f32> {
    let size = params.screen.xy;
    let pixel_size = params.screen.z;
    let centre = clamp(block * pixel_size + vec2<f32>(pixel_size * 0.5), vec2<f32>(0.0), size - vec2<f32>(1.0));
    let colour = textureLoad(color_tex, vec2<i32>(centre), 0).rgb;
    if (params.colour.y > 0.5) {
        return to_gamma(colour);
    }
    return colour;
}

@fragment
fn fs_present(input : VertexOutput) -> @location(0) vec4<f32> {
    let size = params.screen.xy;
    let pixel_size = params.screen.z;
    let seconds = params.screen.w;
    let blocks = ceil(size / pixel_size);
    let glitch = params.wear.y;
    let scoped = params.colour.z > 0.5;

    // The CRT's glass: the picture bulges out, and what falls past its edge is black. Not when only some things are filtered.
    var uv = input.uv;
    let centred = uv * 2.0 - vec2<f32>(1.0);
    let curvature = select(params.wear.w, 0.0, scoped);
    let bulge = centred + centred * (centred.yx * centred.yx) * (curvature * 0.25);
    uv = bulge * 0.5 + vec2<f32>(0.5);
    let inside = all(uv >= vec2<f32>(0.0)) && all(uv <= vec2<f32>(1.0));

    // VHS: every line jitters a little, a tracking band rolls down the screen shoving its lines aside, and the strip
    // along the bottom (the head switch) tears sideways.
    let row = floor(uv.y * blocks.y);
    let tick = floor(seconds * 24.0);
    let band_centre = fract(seconds * 0.06);
    var band_distance = abs(uv.y - band_centre);
    band_distance = min(band_distance, 1.0 - band_distance);
    let band = smoothstep(0.05, 0.0, band_distance) * glitch;
    let tear = smoothstep(0.965, 0.995, uv.y) * glitch;
    let jitter = (hash21(vec2<f32>(row, tick)) - 0.5);
    let shift = jitter * (0.0015 * glitch + band * 0.02 + tear * 0.06);
    let shifted = vec2<f32>(uv.x + shift, uv.y);

    let screen_pixel = shifted * size;
    let block = floor(screen_pixel / pixel_size);

    // Scoped: a big pixel is filtered when what its centre shows is covered, so the covered things' edges go blocky too;
    // everything else is the picture as drawn.
    if (scoped) {
        let centre = clamp(block * pixel_size + vec2<f32>(pixel_size * 0.5), vec2<f32>(0.0), size - vec2<f32>(1.0));
        if (textureLoad(effect_mask, vec2<i32>(centre), 0).r < 0.5) {
            let here = clamp(input.uv * size, vec2<f32>(0.0), size - vec2<f32>(1.0));
            return mana_outline_composite(textureLoad(color_tex, vec2<i32>(here), 0).rgb, input.uv, outline_mask);
        }
    }
    let fringe = vec2<f32>(round(params.look.w * (1.0 + band * 3.0)), 0.0);
    var colour = vec3<f32>(
        block_colour(block + fringe).r,
        block_colour(block).g,
        block_colour(block - fringe).b);

    let grey = dot(colour, vec3<f32>(0.299, 0.587, 0.114));
    colour = mix(vec3<f32>(grey), colour, params.colour.x);
    // The band is washed out and noisier, as a mistracked tape is.
    colour = mix(colour, vec3<f32>(grey) * 1.15 + 0.05, band * 0.5);

    let grain = (hash21(block + vec2<f32>(fract(seconds * 7.13) * 97.0, tick)) - 0.5) * params.wear.x * (0.12 + band * 0.4);
    colour = colour + vec3<f32>(grain);

    // A few levels a channel, dithered per big pixel so the steps break up into a pattern rather than bands.
    let levels = max(params.look.x - 1.0, 1.0);
    let threshold = (bayer4(vec2<i32>(block)) - 0.5) * params.look.y;
    colour = clamp(floor(colour * levels + 0.5 + threshold) / levels, vec3<f32>(0.0), vec3<f32>(1.0));

    // Scanlines: each row of big pixels dims toward its top and bottom edge.
    let within = fract(screen_pixel.y / pixel_size);
    let scan = 1.0 - params.look.z * (1.0 - sin(within * 3.14159265));
    colour = colour * scan;

    let vignette = 1.0 - params.wear.z * dot(centred, centred) * 0.45;
    colour = colour * clamp(vignette, 0.0, 1.0);

    if (!inside) {
        colour = vec3<f32>(0.0);
    }
    if (params.colour.y > 0.5) {
        colour = to_linear(colour);
    }
    return mana_outline_composite(colour, uv, outline_mask);
}
