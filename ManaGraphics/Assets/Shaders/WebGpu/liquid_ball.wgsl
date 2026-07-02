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

// Custom per-material uniforms (group 1). Field names match ManaMaterial.SetShaderUniform(name,
// value) exactly; ManaWebGpuContext parses this struct directly out of this file (by declaration
// order) to build the name->slot mapping, so adding a new tunable parameter here needs no C#
// renderer changes — just add a field and a matching SetShaderUniform call in
// VoxelEditorGameMode.cs. The "slot N" comments are just human-readable byte-offset annotations;
// declaration order is what actually determines the slot. Scalars live in .x; colors use .xyz.
// This simpler WGSL version of the liquid effect doesn't have a natural use for every knob the
// GLSL raymarched version has yet (surfaceStrength/poolContourStrength/glassEdgeStrength/
// shallowColorTint) — they're still declared here so the values are ready to wire in without
// touching C#, they're just not read in fs_main below yet.
struct CustomMaterialUniform {
    liquidFillLevel : vec4<f32>, // slot 0
    liquidWaveStrength : vec4<f32>, // slot 1
    liquidWaterOpacity : vec4<f32>, // slot 2
    liquidSurfaceStrength : vec4<f32>, // slot 3
    liquidPoolContourStrength : vec4<f32>, // slot 4
    glassEdgeStrength : vec4<f32>, // slot 5
    glassAirAlphaz : vec4<f32>, // slot 6
    deepColorTint : vec4<f32>, // slot 7
    shallowColorTint : vec4<f32>, // slot 8
};

@group(1) @binding(0) var<uniform> custom : CustomMaterialUniform;

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
    let wave = sin(input.local_pos.x * 5.0 + transform.time * 3.0) * 0.05 * custom.liquidWaveStrength.x;

    let glass_fresnel = 1.0 - max(0.0, dot(normalize(input.local_pos), vec3<f32>(0.0, 0.0, 1.0)));

    if (height < custom.liquidFillLevel.x + wave) {
        return vec4<f32>(custom.deepColorTint.xyz, 0.7 * custom.liquidWaterOpacity.x);
    } else {
        return vec4<f32>(0.9, 0.95, 1.0, (0.1 + glass_fresnel * 0.3) * custom.glassAirAlphaz.x);
    }
}
