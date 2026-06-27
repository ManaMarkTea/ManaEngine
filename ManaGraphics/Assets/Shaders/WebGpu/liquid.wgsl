struct TransformUniform {
    model_projection : mat4x4<f32>,
    time : f32,
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
fn vs_main(@location(0) position : vec3<f32>, @location(1) color : vec4<f32>, @location(2) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = transform.model_projection * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.local_pos = position;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let t = transform.time * 2.0;
    let green_wave = 0.5 + 0.5 * sin(input.local_pos.x * 4.0 + t) * cos(input.local_pos.z * 4.0 + t);
    return vec4<f32>(0.1, 0.8 * green_wave + 0.2, 0.4, 0.85);
}
