// Minimal shader for the wireframe view-option overlay (see ManaWebGpuContext.Demo.cs's
// DrawWireframeOverlay) -- drawn as a LineList over each mesh's own vertex buffer against a separate
// edge index buffer (ManaWebGpuSceneMesh.TryGetOrCreateWireframeIndexBuffer), not the mesh's normal
// triangle index buffer. Only declares the FrameUniform/ObjectUniform struct PREFIX it actually reads
// (view_projection, model) -- same convention colored_mesh.wgsl/grid.wgsl already use, since WGSL
// derives field offsets from declared order and this pipeline's buffers are sized for the full C#
// structs regardless of how much of them a given shader declares.
struct FrameUniform {
    view_projection : mat4x4<f32>,
};

struct ObjectUniform {
    model : mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> @builtin(position) vec4<f32> {
    return frame.view_projection * object.model * vec4<f32>(position, 1.0);
}

@fragment
fn fs_main() -> @location(0) vec4<f32> {
    return vec4<f32>(0.0, 0.0, 0.0, 1.0);
}
