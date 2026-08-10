#include "include/uniforms.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;

@vertex
fn vs_main(@location(0) position : vec3<f32>) -> @builtin(position) vec4<f32> {
    return frame.view_projection * object.model * vec4<f32>(position, 1.0);
}

@fragment
fn fs_main() -> @location(0) vec4<f32> {
    let width_code = round(clamp(object.outline_parameters.x, 1.0, 4.0)) - 1.0;
    let opacity_code = round(clamp(object.outline_color.a, 0.0, 1.0) * 63.0);
    let packed_outline = (width_code * 64.0 + opacity_code) / 255.0;
    return vec4<f32>(clamp(object.outline_color.rgb, vec3<f32>(0.0), vec3<f32>(1.0)), packed_outline);
}
