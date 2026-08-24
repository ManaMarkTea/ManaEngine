// Offscreen thumbnail render for the mesh-asset picker (MeshThumbnailCache /
// ManaWebGpuContext.Thumbnail.cs). Deliberately unlit except for one fixed ambient+directional term,
// never the scene's real lights -- a cached thumbnail's look must not depend on whatever the throwaway
// world's default lighting happens to be, matching the tuned "three-quarter key light" look
// MeshThumbnailRasterizer.cs (the CPU fallback for this feature, kept for the OpenGL backend) already
// established.
//
// Struct layout intentionally mirrors scene_capture.wgsl's TransformUniform field-for-field, even though
// most of the trailing fields go unused here, so this shader can share ManaWebGpuContext's existing
// _sceneCaptureBindGroupLayout / _sceneCapturePipelineLayout instead of needing a fourth bind group and
// pipeline layout pair. irradiance_factors.xyz is repurposed to carry a fixed world-space key-light
// direction -- see BuildThumbnailUniform in ManaWebGpuContext.Thumbnail.cs.
struct TransformUniform {
    model_projection : mat4x4<f32>,
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    camera_position : vec4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    material_emission : vec4<f32>,
    irradiance_factors : vec4<f32>, // .xyz = world-space key light direction, unit length
    light_positions : array<vec4<f32>, 4>,
    light_colors : array<vec4<f32>, 4>,
    time : f32,
    webgpu_debug_mode : f32,
    _padding1 : f32,
    _padding2 : f32,
};

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_normal : vec3<f32>,
};

@group(0) @binding(0) var<uniform> transform : TransformUniform;
@group(0) @binding(1) var color_sampler : sampler;
@group(0) @binding(2) var color_texture : texture_2d<f32>;

// Matches MeshThumbnailRasterizer.cs's AmbientLight / AlphaCutoff constants, so an asset that falls back
// to the CPU rasterizer (OpenGL backend, or a failed GPU render) doesn't visibly change look when it
// later regenerates through this path instead.
const AMBIENT_LIGHT : f32 = 0.28;
const ALPHA_CUTOFF : f32 = 0.5;

@vertex
fn vs_main(
    @location(0) position : vec3<f32>,
    @location(1) normal : vec3<f32>,
    @location(2) color : vec4<f32>,
    @location(3) uv0 : vec2<f32>
) -> VertexOutput {
    var output : VertexOutput;
    output.position = transform.model_projection * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.world_normal = normalize((transform.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let sampled = textureSample(color_texture, color_sampler, input.uv0);
    let alpha = input.color.a * sampled.a * transform.material_albedo.a;

    // Matches MeshThumbnailRasterizer.FillTriangle's cutout exactly: a transparent fragment is not part
    // of the surface, so it must write neither colour nor depth. `discard` does both in one step -- this
    // is precisely the bug the CPU path needed a dedicated fix for (a "transparent" pixel wrongly
    // winning the depth test against whatever should show through), so this pipeline must not alpha
    // blend instead (blending here would still write depth, since DepthWriteEnabled stays true, which
    // would reproduce the exact bug).
    if (alpha < ALPHA_CUTOFF) {
        discard;
    }

    let vertex_color_linear = pow(max(input.color.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let material_albedo_linear = pow(max(transform.material_albedo.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let base_color = vertex_color_linear * sampled.rgb * material_albedo_linear;

    // Fixed ambient + one directional key light, mirroring MeshThumbnailRasterizer.Shade -- using the
    // real vertex normal rather than that CPU fallback's "flip toward camera" heuristic, which exists
    // there only to compensate for triangles with no normals at all.
    var normal = input.world_normal;
    if (dot(normal, normal) <= 0.0000000001) {
        normal = vec3<f32>(0.0, 0.0, 1.0);
    }
    let n_dot_l = max(dot(normal, transform.irradiance_factors.xyz), 0.0);
    let shade = AMBIENT_LIGHT + (1.0 - AMBIENT_LIGHT) * n_dot_l;

    // Alpha always written fully opaque for any fragment that survives the cutoff above -- the cached
    // thumbnail is a solid cutout sprite, not a partially-transparent render, matching
    // MeshThumbnailRasterizer's `pixels[offset+3] = 0xFF`.
    return vec4<f32>(base_color * shade, 1.0);
}
