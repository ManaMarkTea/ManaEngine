// Depth-only pass for one cascade of the scene's directional shadow map. See
// ManaWebGpuContext.Shadows.cs for the cascade split/fit math and the pass that uses this pipeline.
//
// Group 0 here is NOT the shared FrameUniform (group 0 in every other shader) -- it is this pass's own
// single light-space matrix, rewritten once per cascade before that cascade's draws. Group 1 reuses the
// ordinary per-object bind group (ManaWebGpuContext.Uniforms.cs/ObjectUniform) so every scene mesh's
// existing bind group works here unmodified; this shader reads only its albedo and opacity for cutouts.

#include "include/uniforms.wgsl"

struct ShadowUniform {
    light_view_projection : mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> shadow : ShadowUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;
@group(1) @binding(13) var opacity_texture : texture_2d<f32>;

struct VertexInput {
    @location(0) position : vec3<f32>,
    @location(1) color : vec4<f32>,
    @location(2) uv0 : vec2<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) alpha : f32,
    @location(1) uv0 : vec2<f32>,
};

@vertex
fn vs_main(input : VertexInput) -> VertexOutput {
    var output : VertexOutput;
    output.clip_position = shadow.light_view_projection * (object.model * vec4<f32>(input.position, 1.0));
    output.alpha = input.color.a;
    output.uv0 = input.uv0;
    return output;
}

// Writes no colour: it only throws away the see-through parts of a cutout (a sprite, a leaf, a grate), the same
// test the lit pass makes (pbr_irradiance.wgsl), so a picture casts its own shape rather than its whole quad.
// Blended surfaces cast where they are at least half solid.
@fragment
fn fs_main(input : VertexOutput) {
    let opacity_mode = u32(object.opacity_mode + 0.5);
    if (opacity_mode < 2u) {
        return;
    }
    let uv = input.uv0 * object.uv_transform.xy + object.uv_transform.zw;
    let alpha = input.alpha * textureSample(color_texture, color_sampler, uv).a * object.material_albedo.a
        * textureSample(opacity_texture, color_sampler, uv).r;
    let cutoff = select(0.5, object.material_factors.w, opacity_mode == 2u);
    if (alpha < cutoff) {
        discard;
    }
}
