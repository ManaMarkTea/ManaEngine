#include "include/uniforms.wgsl"
#include "include/camera_relative.wgsl"

// A post effect's mask: 1 where a mesh its scoped script covers is the nearest thing drawn.
@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;

@vertex
fn vs_main(@location(0) position : vec3<f32>) -> @builtin(position) vec4<f32> {
    return mana_clip_position(object, position);
}

@fragment
fn fs_main() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}
