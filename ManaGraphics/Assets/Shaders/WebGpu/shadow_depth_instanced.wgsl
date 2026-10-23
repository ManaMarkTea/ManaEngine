// Depth-only shadow draw for many opaque copies of one mesh at once (ManaWebGpuShadowPass batches them): each
// instance's model matrix from an array reached by dynamic offset. No fragment stage -- opaque casters only write
// depth; cutouts, which need their textures, go through shadow_depth.wgsl one by one.

struct ShadowUniform {
    light_view_projection : mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> shadow : ShadowUniform;
@group(1) @binding(0) var<storage, read> models : array<mat4x4<f32>>;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @builtin(instance_index) instance : u32) -> @builtin(position) vec4<f32> {
    return shadow.light_view_projection * (models[instance] * vec4<f32>(position, 1.0));
}
