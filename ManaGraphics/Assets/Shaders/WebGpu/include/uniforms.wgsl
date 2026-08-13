// Shared uniform layouts, included rather than copy-pasted. These MUST stay field-for-field in step with
// ManaWebGpuFrameUniform / ManaWebGpuObjectUniform in ManaWebGpuContext.Uniforms.cs -- WGSL derives
// offsets from declared order.
//
// Shaders used to declare only the prefix they read, which meant any new field had to be added to every
// file that declared enough of the struct to reach it. Declaring the whole thing everywhere costs
// nothing (the buffers are sized for the full structs regardless) and removes that trap.

struct FrameUniform {
    view_projection : mat4x4<f32>,
    camera_position : vec4<f32>,
    irradiance_factors : vec4<f32>,
    light_positions : array<vec4<f32>, 4>,
    light_colors : array<vec4<f32>, 4>,
    time : f32,
    webgpu_debug_mode : f32,
    vertex_normal_length : f32,
    _padding2 : f32,
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
    flat_shading : f32,
    normal_strength : f32,
    // Matches ManaOpacityMode: 1 opaque, 2 cutout, 3 blend. Auto is resolved before it reaches here.
    opacity_mode : f32,
    // Texture coordinate scale in xy, offset in zw. Applies to every map on the material.
    uv_transform : vec4<f32>,
};
