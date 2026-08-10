struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
}

#include "include/uniforms.wgsl"

@group(0)
@binding(0)
var<uniform> frame : FrameUniform;

@group(1)
@binding(0)
var<uniform> object : ObjectUniform;

@group(1)
@binding(1)
var color_sampler : sampler;

@group(1)
@binding(2)
var color_texture : texture_2d<f32>;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = frame.view_projection * object.model * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let texel = textureSample(color_texture, color_sampler, input.uv0);
    let color = texel * input.color;
    return vec4<f32>(color.rgb, -1.0);
}
