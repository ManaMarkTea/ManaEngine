// Screen-space ambient occlusion (ManaWebGpuAmbientOcclusionPass), at half resolution from the prepass depth, by the
// visibility bitmask of Therrien, Levesque and Gilet, "Screen Space Indirect Lighting with Visibility Bitmask" (2023):
// GTAO's slices and horizon march, but each depth sample is a surface of fixed thickness that hides only the sectors
// (32 a slice) between its front and back, so light passes behind thin things -- chair legs, a table's edge -- rather
// than everything behind them reading as solid (https://arxiv.org/abs/2301.11376). Out: occlusion in r (1 = open), for ssao_blur.wgsl.

struct AoParams {
    projection : mat4x4<f32>,
    inverse_projection : mat4x4<f32>,
    // xy full-resolution size in pixels, zw half-resolution size.
    size : vec4<f32>,
    // x radius in metres, y strength (the exponent on what is left open), z frame number for the noise, w thickness
    // in metres a depth sample is taken to have.
    settings : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : AoParams;
@group(0) @binding(1) var depth_texture : texture_depth_2d;
@group(0) @binding(2) var ao_out : texture_storage_2d<rgba8unorm, write>;

const SLICES : u32 = 3u;
const STEPS : u32 = 6u;
const SECTORS : f32 = 32.0;
const PI : f32 = 3.14159265;

// A point in the full-resolution depth as view space (camera at the origin looking down -z).
fn view_position(pixel : vec2<i32>) -> vec3<f32> {
    let size = vec2<i32>(params.size.xy);
    let clamped = clamp(pixel, vec2<i32>(0), size - vec2<i32>(1));
    let depth = textureLoad(depth_texture, clamped, 0);
    let uv = (vec2<f32>(clamped) + vec2<f32>(0.5)) / params.size.xy;
    let ndc = vec4<f32>(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    let view = params.inverse_projection * ndc;
    return view.xyz / view.w;
}

// The sectors from min_h to max_h (0..1 across the slice's half-circle) marked hidden.
fn hide_sectors(min_h : f32, max_h : f32, bits : u32) -> u32 {
    let start = min(u32(min_h * SECTORS), 31u);
    let count = min(u32(ceil((max_h - min_h) * SECTORS)), 32u - start);
    if (count == 0u) {
        return bits;
    }
    var mask = 0xFFFFFFFFu;
    if (count < 32u) {
        mask = 0xFFFFFFFFu >> (32u - count);
    }
    return bits | (mask << start);
}

@compute @workgroup_size(8, 8, 1)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let half_size = vec2<u32>(params.size.zw);
    if (any(id.xy >= half_size)) {
        return;
    }
    let pixel = vec2<i32>(id.xy) * 2;
    let depth = textureLoad(depth_texture, clamp(pixel, vec2<i32>(0), vec2<i32>(params.size.xy) - vec2<i32>(1)), 0);
    if (depth >= 1.0) {
        textureStore(ao_out, vec2<i32>(id.xy), vec4<f32>(1.0));
        return;
    }

    let p = view_position(pixel);
    // The normal from whichever neighbour on each axis lies nearer in depth: across an edge the far one is another surface.
    let left = view_position(pixel - vec2<i32>(1, 0));
    let right = view_position(pixel + vec2<i32>(1, 0));
    let up = view_position(pixel - vec2<i32>(0, 1));
    let down = view_position(pixel + vec2<i32>(0, 1));
    var dx = right - p;
    if (abs(p.z - left.z) < abs(right.z - p.z)) {
        dx = p - left;
    }
    var dy = down - p;
    if (abs(p.z - up.z) < abs(down.z - p.z)) {
        dy = p - up;
    }
    var n = normalize(cross(dy, dx));
    if (dot(n, -p) < 0.0) {
        n = -n;
    }

    // Interleaved gradient noise turns the slices and staggers the steps per pixel; the blur removes the pattern.
    let noise = fract(52.9829189 * fract(dot(vec2<f32>(id.xy) + params.settings.z * 5.588238, vec2<f32>(0.06711056, 0.00583715))));
    let jitter = fract(noise * 7.31 + 0.5);

    let radius = params.settings.x;
    let thickness = params.settings.w;
    let v = normalize(-p);
    // The radius in full-resolution pixels at this depth.
    let radius_pixels = min(radius * params.projection[1][1] * 0.5 * params.size.y / max(-p.z, 1e-3), 256.0);
    if (radius_pixels < 1.0) {
        textureStore(ao_out, vec2<i32>(id.xy), vec4<f32>(1.0));
        return;
    }

    var visible = 0.0;
    var weight = 0.0;
    for (var slice = 0u; slice < SLICES; slice = slice + 1u) {
        let phi = PI * (f32(slice) + noise) / f32(SLICES);
        let direction = vec2<f32>(cos(phi), sin(phi));
        // The slice plane holds the view direction and this screen direction (screen y runs down, view y up).
        let slice_direction = vec3<f32>(direction.x, direction.y, 0.0);
        let ortho = slice_direction - dot(slice_direction, v) * v;
        let axis = normalize(cross(slice_direction, v));
        let projected = n - axis * dot(n, axis);
        let projected_length = length(projected);
        if (projected_length < 1e-4) {
            continue;
        }
        var normal_angle = acos(clamp(dot(projected, v) / projected_length, -1.0, 1.0));
        if (dot(ortho, projected) < 0.0) {
            normal_angle = -normal_angle;
        }

        var bits = 0u;
        for (var side = 0u; side < 2u; side = side + 1u) {
            let side_sign = f32(side) * 2.0 - 1.0;
            for (var i = 0u; i < STEPS; i = i + 1u) {
                // Squared spacing: most samples near the pixel, where contacts are.
                let t = (f32(i) + jitter) / f32(STEPS);
                let span = max(t * t * radius_pixels, f32(i) + 1.0);
                let offset = vec2<f32>(direction.x, -direction.y) * span * side_sign;
                let sample_pixel = vec2<i32>(vec2<f32>(pixel) + offset);
                if (any(sample_pixel < vec2<i32>(0)) || any(sample_pixel >= vec2<i32>(params.size.xy))) {
                    break;
                }
                let front = view_position(sample_pixel) - p;
                let reach = length(front);
                // Samples on the surface's own plane (within ~3 degrees) hide nothing; their thickness would still mark
                // an edge sector each and grey every flat wall.
                if (reach < 1e-4 || reach > radius * 4.0 || dot(front, n) < reach * 0.05) {
                    continue;
                }
                let back = front - v * thickness;
                // Each edge as a signed angle from the view direction in the slice, then 0..1 across the half-circle round
                // the normal: a sample on the surface's own plane lands on its edge, 0 or 1.
                var horizons = acos(clamp(vec2<f32>(dot(front / reach, v), dot(normalize(back), v)), vec2<f32>(-1.0), vec2<f32>(1.0)));
                horizons = saturate((side_sign * horizons - normal_angle + PI * 0.5) / PI);
                bits = hide_sectors(min(horizons.x, horizons.y), max(horizons.x, horizons.y), bits);
            }
        }
        // Slices whose plane the normal lies across count for more, as in GTAO.
        visible = visible + (1.0 - f32(countOneBits(bits)) / SECTORS) * projected_length;
        weight = weight + projected_length;
    }

    let open = pow(saturate(visible / max(weight, 1e-4)), params.settings.y);
    textureStore(ao_out, vec2<i32>(id.xy), vec4<f32>(open, 0.0, 0.0, 1.0));
}
