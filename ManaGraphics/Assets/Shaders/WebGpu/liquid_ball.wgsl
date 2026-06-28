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
    let height = input.local_pos.y;
    let wave = sin(input.local_pos.x * 5.0 + transform.time * 3.0) * 0.05;
    
    let glass_fresnel = 1.0 - max(0.0, dot(normalize(input.local_pos), vec3<f32>(0.0, 0.0, 1.0)));
    
    if (height < -0.14 + wave) {
        return vec4<f32>(0.1, 0.4, 0.8, 0.7);
    } else {
        return vec4<f32>(0.9, 0.95, 1.0, 0.1 + glass_fresnel * 0.3);
    }
}
