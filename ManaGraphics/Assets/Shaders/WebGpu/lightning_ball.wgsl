struct TransformUniform {
    model_projection : mat4x4<f32>,
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    camera_position : vec4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    material_emission : vec4<f32>,
    irradiance_factors : vec4<f32>,
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
    @location(2) local_pos : vec3<f32>,
}

@group(0) @binding(0) var<uniform> transform : TransformUniform;
@group(0) @binding(1) var color_sampler : sampler;
@group(0) @binding(2) var color_texture : texture_2d<f32>;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = transform.model_projection * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.local_pos = position;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let p = input.local_pos;
    let t = transform.time * 5.0;
    let pulse = sin(t + p.x * 10.0) * cos(t + p.y * 10.0) * sin(t + p.z * 10.0);
    let glow = 0.5 + 0.5 * sin(transform.time * 2.0);
    
    let dist = length(p);
    let edge = smoothstep(0.8, 1.0, dist);
    
    let base_col = vec4<f32>(0.2, 0.6, 1.0, 0.8);
    let lightning_col = vec4<f32>(0.9, 0.95, 1.0, 0.95);
    
    let pulse_val = pulse * 0.5 + 0.5;
    let col = mix(base_col, lightning_col, pulse_val);
    
    let boost = mix(1.0, 8.0, pow(pulse_val, 4.0));
    return vec4<f32>(col.rgb * (1.2 + glow * 0.3) * boost, col.a * (1.0 - edge));
}
