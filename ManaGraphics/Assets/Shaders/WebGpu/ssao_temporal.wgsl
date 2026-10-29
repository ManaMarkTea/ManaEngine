// Temporal accumulation of the ambient occlusion (ManaWebGpuAmbientOcclusionPass), after ssao_blur.wgsl: each pixel's
// point is found where it was last frame, and a little of this frame is blended into what was there (BLEND). ssao.wgsl turns its
// noise every frame, so the noise averages away rather than shimmering. History is dropped where the point was not on
// screen or something else stood there (its distance from that frame's camera differs), and clamped to this frame's
// neighbourhood so a moved object leaves no ghost. Out: r occlusion (1 = open), g distance from the camera, for next frame.

struct AoParams {
    projection : mat4x4<f32>,
    inverse_projection : mat4x4<f32>,
    // xy full-resolution size in pixels, zw half-resolution size.
    size : vec4<f32>,
    settings : vec4<f32>,
    inverse_view : mat4x4<f32>,
    previous_view_projection : mat4x4<f32>,
    // xyz last frame's camera position, w 1 when there is a history to use.
    previous_camera : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : AoParams;
@group(0) @binding(1) var depth_texture : texture_depth_2d;
@group(0) @binding(2) var ao_current : texture_2d<f32>;
@group(0) @binding(3) var ao_history : texture_2d<f32>;
@group(0) @binding(4) var history_sampler : sampler;
@group(0) @binding(5) var ao_out : texture_storage_2d<rgba16float, write>;

// About sixteen frames averaged (a quarter second): 13x steadier than none at 5%, 7x at 10%.
const BLEND : f32 = 0.06;

fn view_position(pixel : vec2<i32>) -> vec3<f32> {
    let size = vec2<i32>(params.size.xy);
    let clamped = clamp(pixel, vec2<i32>(0), size - vec2<i32>(1));
    let depth = textureLoad(depth_texture, clamped, 0);
    let uv = (vec2<f32>(clamped) + vec2<f32>(0.5)) / params.size.xy;
    let ndc = vec4<f32>(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    let view = params.inverse_projection * ndc;
    return view.xyz / view.w;
}

@compute @workgroup_size(8, 8, 1)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let half_size = vec2<i32>(params.size.zw);
    let centre = vec2<i32>(id.xy);
    if (any(centre >= half_size)) {
        return;
    }
    let current = textureLoad(ao_current, centre, 0).r;
    let pixel = clamp(centre * 2, vec2<i32>(0), vec2<i32>(params.size.xy) - vec2<i32>(1));
    if (textureLoad(depth_texture, pixel, 0) >= 1.0) {
        textureStore(ao_out, centre, vec4<f32>(1.0, 0.0, 0.0, 1.0));
        return;
    }

    let p = view_position(pixel);
    let distance_now = length(p);
    var result = current;
    if (params.previous_camera.w > 0.5) {
        let world = (params.inverse_view * vec4<f32>(p, 1.0)).xyz;
        let clip = params.previous_view_projection * vec4<f32>(world, 1.0);
        if (clip.w > 1e-4) {
            let ndc = clip.xy / clip.w;
            let uv = vec2<f32>(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
            if (all(uv >= vec2<f32>(0.0)) && all(uv <= vec2<f32>(1.0))) {
                // History texel c holds full-resolution pixel 2c, so a still camera lands on texel centres exactly.
                let texel = (uv * params.size.xy - vec2<f32>(0.5)) * 0.5;
                var history = textureSampleLevel(ao_history, history_sampler, (texel + vec2<f32>(0.5)) / params.size.zw, 0.0);
                let expected = distance(world, params.previous_camera.xyz);
                // The same surface: within 5% (and 5 cm) of where that frame saw it. At an edge the bilinear mix holds
                // the far side's distance too; the nearest texel may still be this surface.
                if (abs(history.g - expected) >= expected * 0.05 + 0.05) {
                    let nearest = clamp(vec2<i32>(round(texel)), vec2<i32>(0), half_size - vec2<i32>(1));
                    history = textureLoad(ao_history, nearest, 0);
                }
                if (abs(history.g - expected) < expected * 0.05 + 0.05) {
                    var low = current;
                    var high = current;
                    for (var y = -1; y <= 1; y = y + 1) {
                        for (var x = -1; x <= 1; x = x + 1) {
                            let at = clamp(centre + vec2<i32>(x, y), vec2<i32>(0), half_size - vec2<i32>(1));
                            let neighbour = textureLoad(ao_current, at, 0).r;
                            low = min(low, neighbour);
                            high = max(high, neighbour);
                        }
                    }
                    result = mix(clamp(history.r, low - 0.1, high + 0.1), current, BLEND);
                }
            }
        }
    }
    textureStore(ao_out, centre, vec4<f32>(result, distance_now, 0.0, 1.0));
}
