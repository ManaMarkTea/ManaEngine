// Smooths the half-resolution occlusion (ssao.wgsl) over a 5x5 neighbourhood, weighting each neighbour by how near it
// lies in depth, so the noise goes but the dark of a corner does not bleed onto the surface in front of it.

struct AoParams {
    projection : mat4x4<f32>,
    inverse_projection : mat4x4<f32>,
    size : vec4<f32>,
    settings : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : AoParams;
@group(0) @binding(1) var depth_texture : texture_depth_2d;
@group(0) @binding(2) var ao_raw : texture_2d<f32>;
@group(0) @binding(3) var ao_out : texture_storage_2d<rgba8unorm, write>;

fn view_depth(half_pixel : vec2<i32>) -> f32 {
    let size = vec2<i32>(params.size.xy);
    let pixel = clamp(half_pixel * 2, vec2<i32>(0), size - vec2<i32>(1));
    let depth = textureLoad(depth_texture, pixel, 0);
    let view = params.inverse_projection * vec4<f32>(0.0, 0.0, depth, 1.0);
    return view.z / view.w;
}

@compute @workgroup_size(8, 8, 1)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let half_size = vec2<i32>(params.size.zw);
    let centre = vec2<i32>(id.xy);
    if (any(centre >= half_size)) {
        return;
    }
    let depth = view_depth(centre);
    var sum = 0.0;
    var weight = 0.0;
    for (var y = -2; y <= 2; y = y + 1) {
        for (var x = -2; x <= 2; x = x + 1) {
            let pixel = clamp(centre + vec2<i32>(x, y), vec2<i32>(0), half_size - vec2<i32>(1));
            let difference = abs(view_depth(pixel) - depth) / max(abs(depth), 0.1);
            let w = exp(-difference * 60.0);
            sum = sum + textureLoad(ao_raw, pixel, 0).r * w;
            weight = weight + w;
        }
    }
    textureStore(ao_out, centre, vec4<f32>(sum / max(weight, 1e-4), 0.0, 0.0, 1.0));
}
