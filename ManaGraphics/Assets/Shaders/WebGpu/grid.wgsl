struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) local_pos : vec3<f32>,
}

#include "include/uniforms.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;

fn srgb_to_linear_channel(value : f32) -> f32 {
    if (value <= 0.04045) {
        return value / 12.92;
    }

    return pow((value + 0.055) / 1.055, 2.4);
}

fn srgb_to_linear(value : vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        srgb_to_linear_channel(value.r),
        srgb_to_linear_channel(value.g),
        srgb_to_linear_channel(value.b)
    );
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = frame.view_projection * object.model * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.local_pos = position;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let uv = input.uv0 * 256.0;
    
    // Compute derivative for antialiasing
    let derivative = fwidth(uv);
    
    let aGrid = abs(fract(uv / 256.0 - 0.5) - 0.5) / (derivative / 256.0);
    let bGrid = abs(fract(uv / 8.0 - 0.5) - 0.5) / (derivative / 8.0);
    let cGrid = abs(fract(uv / 1.0 - 0.5) - 0.5) / (derivative / 1.0);
    
    let fillEven = vec4<f32>(0.7764706, 0.76862746, 0.78039217, 1.0);
    let fillOdd = vec4<f32>(0.7490196, 0.7411765, 0.7529412, 1.0);
    
    let fillMod = floor(uv / 8.0);
    let oddness = fract((fillMod.x + fillMod.y) / 2.0) * 2.0;
    
    let cellStrokeDynamic = (fillEven + fillOdd) / 2.2;
    let strokeDynamic = (fillEven + fillOdd) / 2.4;
    
    var colorVal = mix(fillEven, fillOdd, smoothstep(0.0, 1.0, oddness));
    
    // Apply grid lines using smoothstep
    colorVal = mix(colorVal, cellStrokeDynamic, 1.0 - smoothstep(0.0, 1.0, min(cGrid.x, cGrid.y)));
    colorVal = mix(colorVal, strokeDynamic, 1.0 - smoothstep(0.0, 2.0, min(bGrid.x, bGrid.y)));
    colorVal = mix(colorVal, strokeDynamic, 1.0 - smoothstep(0.0, 4.0, min(aGrid.x, aGrid.y)));
    
    return vec4<f32>(srgb_to_linear(colorVal.rgb), -1.0);
}
