// Screen-space ambient occlusion (ManaWebGpuAmbientOcclusionPass), at half resolution from the prepass depth: each
// pixel's view-space point and normal are rebuilt from depth, and samples in the hemisphere over it test how much of
// it the scene in front hides. Out: occlusion in r (1 = open), for ssao_blur.wgsl.

struct AoParams {
    projection : mat4x4<f32>,
    inverse_projection : mat4x4<f32>,
    // xy full-resolution size in pixels, zw half-resolution size.
    size : vec4<f32>,
    // x radius in metres, y strength (the exponent on what is left open), z frame number for the noise.
    settings : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : AoParams;
@group(0) @binding(1) var depth_texture : texture_depth_2d;
@group(0) @binding(2) var ao_out : texture_storage_2d<rgba8unorm, write>;

const SAMPLES : u32 = 16u;

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

// Evenly spread, more of them near the surface: the near ones are what corners and contacts hide.
fn kernel(i : u32) -> vec3<f32> {
    let golden = 2.39996323;
    let t = (f32(i) + 0.5) / f32(SAMPLES);
    let z = sqrt(1.0 - t);
    let r = sqrt(t);
    let phi = f32(i) * golden;
    let scale = mix(0.15, 1.0, t * t);
    return vec3<f32>(cos(phi) * r, sin(phi) * r, z) * scale;
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

    // A turn per pixel (interleaved gradient noise) so the samples' pattern becomes noise the blur removes.
    let noise = fract(52.9829189 * fract(dot(vec2<f32>(id.xy) + params.settings.z * 5.588238, vec2<f32>(0.06711056, 0.00583715))));
    let angle = noise * 6.2831853;
    var helper = vec3<f32>(0.0, 1.0, 0.0);
    if (abs(n.y) > 0.9) {
        helper = vec3<f32>(1.0, 0.0, 0.0);
    }
    let t0 = normalize(cross(helper, n));
    let b0 = cross(n, t0);
    let tangent = t0 * cos(angle) + b0 * sin(angle);
    let bitangent = cross(n, tangent);

    let radius = params.settings.x;
    var occlusion = 0.0;
    for (var i = 0u; i < SAMPLES; i = i + 1u) {
        let k = kernel(i);
        let sample_point = p + (tangent * k.x + bitangent * k.y + n * k.z) * radius;
        let clip = params.projection * vec4<f32>(sample_point, 1.0);
        let uv = vec2<f32>(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
        if (any(uv < vec2<f32>(0.0)) || any(uv > vec2<f32>(1.0))) {
            continue;
        }
        let scene = view_position(vec2<i32>(uv * params.size.xy));
        // Hidden when the scene there is nearer the camera than the sample; only counted within reach, so a wall far
        // behind an edge does not darken it.
        let hidden = select(0.0, 1.0, scene.z >= sample_point.z + 0.02 * radius);
        let reach = smoothstep(0.0, 1.0, radius / max(abs(p.z - scene.z), 1e-4));
        occlusion = occlusion + hidden * reach;
    }

    let open = pow(saturate(1.0 - occlusion / f32(SAMPLES)), params.settings.y);
    textureStore(ao_out, vec2<i32>(id.xy), vec4<f32>(open, 0.0, 0.0, 1.0));
}
