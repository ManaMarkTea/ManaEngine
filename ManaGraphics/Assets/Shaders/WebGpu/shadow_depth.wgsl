// Depth-only pass for one cascade of the scene's directional shadow map. See
// ManaWebGpuContext.Shadows.cs for the cascade split/fit math and the pass that uses this pipeline.
//
// Group 0 here is NOT the shared FrameUniform (group 0 in every other shader) -- it is this pass's own
// single light-space matrix, rewritten once per cascade before that cascade's draws. Group 1 reuses the
// ordinary per-object bind group (ManaWebGpuContext.Uniforms.cs/ObjectUniform) so every scene mesh's
// existing bind group works here unmodified; this shader just ignores the material bindings in it.

#include "include/uniforms.wgsl"

struct ShadowUniform {
    light_view_projection : mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> shadow : ShadowUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;

struct VertexInput {
    @location(0) position : vec3<f32>,
};

@vertex
fn vs_main(input : VertexInput) -> @builtin(position) vec4<f32> {
    let world_position = object.model * vec4<f32>(input.position, 1.0);
    return shadow.light_view_projection * world_position;
}
