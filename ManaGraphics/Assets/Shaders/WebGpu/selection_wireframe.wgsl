// The editor selection's edges (Overlays/ManaWebGpuSelectionWireframeOverlay): wireframe.wgsl's lines, blended
// faintly in the selection outline's blue.
#include "include/uniforms.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> @builtin(position) vec4<f32> {
    // Pulled a hair toward the camera so the edges win the depth test against their own faces instead of dashing.
    let world = (object.model * vec4<f32>(position, 1.0)).xyz;
    let pulled = mix(world, frame.camera_position.xyz, 0.003);
    return frame.view_projection * vec4<f32>(pulled, 1.0);
}

@fragment
fn fs_main() -> @location(0) vec4<f32> {
    return vec4<f32>(0.30, 0.72, 1.0, 0.22);
}
