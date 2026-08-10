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

// Custom per-material uniforms (group 1). Field names match ManaMaterial.SetShaderUniform(name,
// value) exactly; ManaWebGpuContext parses this struct directly out of this file (by declaration
// order), so adding a new tunable parameter here needs no C# renderer changes — just add a field
// and a matching SetShaderUniform call in VoxelEditorGameMode.cs.
// This simpler WGSL version of the liquid effect doesn't have a natural use for every knob the
// GLSL raymarched version has yet (surfaceStrength/poolContourStrength/glassEdgeStrength/
// shallowColorTint) — they're still declared here so the values are ready to wire in without
// touching C#, they're just not read in fs_main below yet.
struct CustomMaterialUniform {
    //PROPERTY(default=-0.14, min=-1.0, max=1.0)
    liquidFillLevel : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    liquidWaveStrength : f32,

    //PROPERTY(default=1.0, min=0.0, max=1.0)
    liquidWaterOpacity : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    liquidSurfaceStrength : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    liquidPoolContourStrength : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    glassEdgeStrength : f32,

    //PROPERTY(default=0.2, min=0.0, max=1.0)
    glassAirAlpha : f32,

    //PROPERTY(default=[0.0,0.22,0.62], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    deepColorTint : vec3<f32>,
    
    //PROPERTY(default=[0.05,0.72,0.95], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    shallowColorTint : vec3<f32>,
};

@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

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
    let height = input.local_pos.y;
    let wave = sin(input.local_pos.x * 5.0 + frame.time * 3.0) * 0.05 * custom.liquidWaveStrength;

    let glass_fresnel = 1.0 - max(0.0, dot(normalize(input.local_pos), vec3<f32>(0.0, 0.0, 1.0)));

    if (height < custom.liquidFillLevel + wave) {
        return vec4<f32>(custom.deepColorTint, 0.7 * custom.liquidWaterOpacity);
    } else {
        return vec4<f32>(0.9, 0.95, 1.0, (0.1 + glass_fresnel * 0.3) * custom.glassAirAlpha);
    }
}
