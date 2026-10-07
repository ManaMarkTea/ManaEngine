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
    // Cascaded shadow maps for the scene's single directional "sun" light (ManaRenderScene.HasSun).
    // See ManaWebGpuContext.Shadows.cs for how these are computed.
    cascade_view_projections : array<mat4x4<f32>, 3>,
    // xyz = view-space far distance of cascades 0..2 (the split scheme), w unused.
    cascade_split_far : vec4<f32>,
    // xyz = normalized direction the light travels (surface-to-light is -sun_direction), w = 1 when a
    // shadow-casting sun exists this frame, 0 otherwise.
    sun_direction : vec4<f32>,
    // rgb = color, a = intensity.
    sun_color : vec4<f32>,
    // Placed point lights (ManaRenderScene.LocalLight*): xyz position, w range; rgb colour x intensity.
    local_light_positions : array<vec4<f32>, 16>,
    local_light_colors : array<vec4<f32>, 16>,
    // ManaWind: xy = unit direction it blows toward (world XZ), z = speed now in m/s, w = gustiness 0..1.
    wind : vec4<f32>,
    // xy = how far the air has carried things (world XZ metres), z = that path's length, w = gust now 0..1.
    wind_motion : vec4<f32>,
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
