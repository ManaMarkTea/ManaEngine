struct TransformUniform {
    model_projection : mat4x4<f32>,
};

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
}

@group(0)
@binding(0)
var<uniform> transform : TransformUniform;

@group(0)
@binding(1)
var color_sampler : sampler;

@group(0)
@binding(2)
var color_texture : texture_2d<f32>;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) color : vec4<f32>, @location(2) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = transform.model_projection * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let texel = textureSample(color_texture, color_sampler, input.uv0);
    return texel * input.color;
}
