struct FrameUniform {
    view_projection : mat4x4<f32>,
};

struct ObjectUniform {
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    material_emission : vec4<f32>,
    outline_color : vec4<f32>,
    outline_parameters : vec4<f32>,
    debug_mode_override : f32,
    _padding0 : f32,
    _padding1 : f32,
    _padding2 : f32,
};

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
