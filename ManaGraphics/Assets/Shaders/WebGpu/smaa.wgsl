// SMAA 1x (Jimenez, Echevarria, Sousa and Gutierrez, "SMAA: Enhanced Subpixel Morphological Antialiasing", 2012),
// ported from the reference SMAA.hlsl (MIT -- ManaGraphics/Assets/Smaa/LICENSE.txt) at its HIGH preset without the
// diagonal search: luma edges (fs_edges), blending weights from the precomputed area and search tables (fs_weights),
// then the neighbourhood blend straight into the window with the selection outline over it (fs_present). WebGPU's
// texture origin is top-left as in Direct3D, so the reference's coordinates are used as they are.

struct SmaaParams {
    // 1 / width, 1 / height, width, height (SMAA_RT_METRICS).
    metrics : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : SmaaParams;
@group(0) @binding(1) var linear_sampler : sampler;
@group(0) @binding(2) var color_tex : texture_2d<f32>;
@group(0) @binding(3) var edges_tex : texture_2d<f32>;
@group(0) @binding(4) var area_tex : texture_2d<f32>;
@group(0) @binding(5) var search_tex : texture_2d<f32>;
@group(0) @binding(6) var blend_tex : texture_2d<f32>;
@group(0) @binding(7) var outline_mask : texture_2d<f32>;

#include "include/outline_composite.wgsl"

const THRESHOLD : f32 = 0.1;
const MAX_SEARCH_STEPS : f32 = 16.0;
const CORNER_ROUNDING_NORM : f32 = 0.25;
const LOCAL_CONTRAST_ADAPTATION_FACTOR : f32 = 2.0;
const AREATEX_MAX_DISTANCE : f32 = 16.0;
const AREATEX_PIXEL_SIZE : vec2<f32> = vec2<f32>(1.0 / 160.0, 1.0 / 560.0);
const AREATEX_SUBTEX_SIZE : f32 = 1.0 / 7.0;
const SEARCHTEX_SIZE : vec2<f32> = vec2<f32>(66.0, 33.0);
const SEARCHTEX_PACKED_SIZE : vec2<f32> = vec2<f32>(64.0, 16.0);

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

// HLSL's round, halves away from zero; WGSL's rounds them to even.
fn round_away(value : vec2<f32>) -> vec2<f32> {
    return sign(value) * floor(abs(value) + vec2<f32>(0.5));
}

// Luma edge detection wants gamma-corrected colour: sqrt is near enough, as FXAA's luma here uses.
fn luma(uv : vec2<f32>) -> f32 {
    let colour = textureSampleLevel(color_tex, linear_sampler, uv, 0.0).rgb;
    return dot(sqrt(max(colour, vec3<f32>(0.0))), vec3<f32>(0.2126, 0.7152, 0.0722));
}

// ---------------------------------------------------------------- pass 1: edges
@fragment
fn fs_edges(input : VertexOutput) -> @location(0) vec4<f32> {
    let uv = input.uv;
    let m = params.metrics;
    let offset0 = m.xyxy * vec4<f32>(-1.0, 0.0, 0.0, -1.0) + uv.xyxy;
    let offset1 = m.xyxy * vec4<f32>(1.0, 0.0, 0.0, 1.0) + uv.xyxy;
    let offset2 = m.xyxy * vec4<f32>(-2.0, 0.0, 0.0, -2.0) + uv.xyxy;

    let l = luma(uv);
    let l_left = luma(offset0.xy);
    let l_top = luma(offset0.zw);
    var delta = vec4<f32>(abs(vec2<f32>(l) - vec2<f32>(l_left, l_top)), 0.0, 0.0);
    var edges = step(vec2<f32>(THRESHOLD), delta.xy);
    if (dot(edges, vec2<f32>(1.0)) == 0.0) {
        discard;
    }

    let l_right = luma(offset1.xy);
    let l_bottom = luma(offset1.zw);
    delta = vec4<f32>(delta.xy, abs(vec2<f32>(l) - vec2<f32>(l_right, l_bottom)));
    var max_delta = max(delta.xy, delta.zw);

    let l_left_left = luma(offset2.xy);
    let l_top_top = luma(offset2.zw);
    delta = vec4<f32>(delta.xy, abs(vec2<f32>(l_left, l_top) - vec2<f32>(l_left_left, l_top_top)));
    max_delta = max(max_delta, delta.zw);
    let final_delta = max(max_delta.x, max_delta.y);

    // Local contrast adaptation: an edge beside a much stronger one is dropped.
    edges = edges * step(vec2<f32>(final_delta), LOCAL_CONTRAST_ADAPTATION_FACTOR * delta.xy);
    return vec4<f32>(edges, 0.0, 1.0);
}

// ---------------------------------------------------------------- pass 2: blending weights
fn edges_at(uv : vec2<f32>) -> vec2<f32> {
    return textureSampleLevel(edges_tex, linear_sampler, uv, 0.0).rg;
}

fn search_length(e : vec2<f32>, offset : f32) -> f32 {
    // The table is flipped vertically, with the left and right cases side by side.
    var scale = SEARCHTEX_SIZE * vec2<f32>(0.5, -1.0);
    var bias = SEARCHTEX_SIZE * vec2<f32>(offset, 1.0);
    scale = scale + vec2<f32>(-1.0, 1.0);
    bias = bias + vec2<f32>(0.5, -0.5);
    scale = scale / SEARCHTEX_PACKED_SIZE;
    bias = bias / SEARCHTEX_PACKED_SIZE;
    return textureSampleLevel(search_tex, linear_sampler, scale * e + bias, 0.0).r;
}

fn search_x_left(start : vec2<f32>, end : f32) -> f32 {
    var uv = start;
    var e = vec2<f32>(0.0, 1.0);
    while (uv.x > end && e.g > 0.8281 && e.r == 0.0) {
        e = edges_at(uv);
        uv = uv - vec2<f32>(2.0, 0.0) * params.metrics.xy;
    }
    let offset = -(255.0 / 127.0) * search_length(e, 0.0) + 3.25;
    return params.metrics.x * offset + uv.x;
}

fn search_x_right(start : vec2<f32>, end : f32) -> f32 {
    var uv = start;
    var e = vec2<f32>(0.0, 1.0);
    while (uv.x < end && e.g > 0.8281 && e.r == 0.0) {
        e = edges_at(uv);
        uv = uv + vec2<f32>(2.0, 0.0) * params.metrics.xy;
    }
    let offset = -(255.0 / 127.0) * search_length(e, 0.5) + 3.25;
    return -params.metrics.x * offset + uv.x;
}

fn search_y_up(start : vec2<f32>, end : f32) -> f32 {
    var uv = start;
    var e = vec2<f32>(1.0, 0.0);
    while (uv.y > end && e.r > 0.8281 && e.g == 0.0) {
        e = edges_at(uv);
        uv = uv - vec2<f32>(0.0, 2.0) * params.metrics.xy;
    }
    let offset = -(255.0 / 127.0) * search_length(e.gr, 0.0) + 3.25;
    return params.metrics.y * offset + uv.y;
}

fn search_y_down(start : vec2<f32>, end : f32) -> f32 {
    var uv = start;
    var e = vec2<f32>(1.0, 0.0);
    while (uv.y < end && e.r > 0.8281 && e.g == 0.0) {
        e = edges_at(uv);
        uv = uv + vec2<f32>(0.0, 2.0) * params.metrics.xy;
    }
    let offset = -(255.0 / 127.0) * search_length(e.gr, 0.5) + 3.25;
    return -params.metrics.y * offset + uv.y;
}

// The areas each side of an edge of this length with these crossing edges (the table holds them square-rooted).
fn area(dist : vec2<f32>, e1 : f32, e2 : f32) -> vec2<f32> {
    var uv = vec2<f32>(AREATEX_MAX_DISTANCE) * round_away(4.0 * vec2<f32>(e1, e2)) + dist;
    uv = AREATEX_PIXEL_SIZE * uv + 0.5 * AREATEX_PIXEL_SIZE;
    // SMAA 1x: subsample offset 0, so the first sub-table.
    return textureSampleLevel(area_tex, linear_sampler, uv, 0.0).rg;
}

fn horizontal_corners(weights : vec2<f32>, coords : vec4<f32>, d : vec2<f32>) -> vec2<f32> {
    let left_right = step(d.xy, d.yx);
    var rounding = (1.0 - CORNER_ROUNDING_NORM) * left_right;
    rounding = rounding / (left_right.x + left_right.y);
    var factor = vec2<f32>(1.0);
    factor.x = factor.x - rounding.x * textureSampleLevel(edges_tex, linear_sampler, coords.xy, 0.0, vec2<i32>(0, 1)).r;
    factor.x = factor.x - rounding.y * textureSampleLevel(edges_tex, linear_sampler, coords.zw, 0.0, vec2<i32>(1, 1)).r;
    factor.y = factor.y - rounding.x * textureSampleLevel(edges_tex, linear_sampler, coords.xy, 0.0, vec2<i32>(0, -2)).r;
    factor.y = factor.y - rounding.y * textureSampleLevel(edges_tex, linear_sampler, coords.zw, 0.0, vec2<i32>(1, -2)).r;
    return weights * saturate(factor);
}

fn vertical_corners(weights : vec2<f32>, coords : vec4<f32>, d : vec2<f32>) -> vec2<f32> {
    let left_right = step(d.xy, d.yx);
    var rounding = (1.0 - CORNER_ROUNDING_NORM) * left_right;
    rounding = rounding / (left_right.x + left_right.y);
    var factor = vec2<f32>(1.0);
    factor.x = factor.x - rounding.x * textureSampleLevel(edges_tex, linear_sampler, coords.xy, 0.0, vec2<i32>(1, 0)).g;
    factor.x = factor.x - rounding.y * textureSampleLevel(edges_tex, linear_sampler, coords.zw, 0.0, vec2<i32>(1, 1)).g;
    factor.y = factor.y - rounding.x * textureSampleLevel(edges_tex, linear_sampler, coords.xy, 0.0, vec2<i32>(-2, 0)).g;
    factor.y = factor.y - rounding.y * textureSampleLevel(edges_tex, linear_sampler, coords.zw, 0.0, vec2<i32>(-2, 1)).g;
    return weights * saturate(factor);
}

@fragment
fn fs_weights(input : VertexOutput) -> @location(0) vec4<f32> {
    let uv = input.uv;
    let m = params.metrics;
    let pixcoord = uv * m.zw;
    // Between pixels, so one bilinear fetch reads two edges (the reference's @PSEUDO_GATHER4).
    let offset0 = m.xyxy * vec4<f32>(-0.25, -0.125, 1.25, -0.125) + uv.xyxy;
    let offset1 = m.xyxy * vec4<f32>(-0.125, -0.25, -0.125, 1.25) + uv.xyxy;
    let offset2 = m.xxyy * (vec4<f32>(-2.0, 2.0, -2.0, 2.0) * MAX_SEARCH_STEPS) + vec4<f32>(offset0.xz, offset1.yw);

    var weights = vec4<f32>(0.0);
    let e = edges_at(uv);

    if (e.g > 0.0) {
        // Edge at north: how far it runs left and right, and what crosses it at each end.
        var coords = vec3<f32>(search_x_left(offset0.xy, offset2.x), offset1.y, 0.0);
        var d = vec2<f32>(coords.x, 0.0);
        let e1 = textureSampleLevel(edges_tex, linear_sampler, coords.xy, 0.0).r;
        coords.z = search_x_right(offset0.zw, offset2.y);
        d.y = coords.z;
        d = abs(round_away(m.zz * d - pixcoord.xx));
        let e2 = textureSampleLevel(edges_tex, linear_sampler, coords.zy, 0.0, vec2<i32>(1, 0)).r;
        var horizontal = area(sqrt(d), e1, e2);
        coords.y = uv.y;
        horizontal = horizontal_corners(horizontal, coords.xyzy, d);
        weights = vec4<f32>(horizontal, weights.zw);
    }

    if (e.r > 0.0) {
        // Edge at west: how far it runs up and down.
        var coords = vec3<f32>(offset0.x, search_y_up(offset1.xy, offset2.z), 0.0);
        var d = vec2<f32>(coords.y, 0.0);
        let e1 = textureSampleLevel(edges_tex, linear_sampler, coords.xy, 0.0).g;
        coords.z = search_y_down(offset1.zw, offset2.w);
        d.y = coords.z;
        d = abs(round_away(m.ww * d - pixcoord.yy));
        let e2 = textureSampleLevel(edges_tex, linear_sampler, coords.xz, 0.0, vec2<i32>(0, 1)).g;
        var vertical = area(sqrt(d), e1, e2);
        coords.x = uv.x;
        vertical = vertical_corners(vertical, coords.xyxz, d);
        weights = vec4<f32>(weights.xy, vertical);
    }

    return weights;
}

// ---------------------------------------------------------------- pass 3: neighbourhood blending, into the window
fn neighbourhood_blend(uv : vec2<f32>) -> vec3<f32> {
    let m = params.metrics;
    let offset = m.xyxy * vec4<f32>(1.0, 0.0, 0.0, 1.0) + uv.xyxy;
    var a = vec4<f32>(0.0);
    a.x = textureSampleLevel(blend_tex, linear_sampler, offset.xy, 0.0).a;
    a.y = textureSampleLevel(blend_tex, linear_sampler, offset.zw, 0.0).g;
    let here = textureSampleLevel(blend_tex, linear_sampler, uv, 0.0);
    a.w = here.x;
    a.z = here.z;

    if (dot(a, vec4<f32>(1.0)) < 1e-5) {
        return textureSampleLevel(color_tex, linear_sampler, uv, 0.0).rgb;
    }

    // Blend along whichever direction weighs more, mixing with that neighbour through the bilinear filter.
    let horizontal = max(a.x, a.z) > max(a.y, a.w);
    var blending_offset = vec4<f32>(0.0, a.y, 0.0, a.w);
    var blending_weight = a.yw;
    if (horizontal) {
        blending_offset = vec4<f32>(a.x, 0.0, a.z, 0.0);
        blending_weight = a.xz;
    }
    blending_weight = blending_weight / dot(blending_weight, vec2<f32>(1.0));
    let blending_coord = blending_offset * vec4<f32>(m.xy, -m.xy) + uv.xyxy;
    var colour = blending_weight.x * textureSampleLevel(color_tex, linear_sampler, blending_coord.xy, 0.0).rgb;
    colour = colour + blending_weight.y * textureSampleLevel(color_tex, linear_sampler, blending_coord.zw, 0.0).rgb;
    return colour;
}

@fragment
fn fs_present(input : VertexOutput) -> @location(0) vec4<f32> {
    return mana_outline_composite(neighbourhood_blend(input.uv), input.uv, outline_mask);
}
