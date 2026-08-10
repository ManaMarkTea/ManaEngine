// Border/overlay lines whose width follows camera distance, so they stay readable from orbit without
// blanketing the terrain up close (see ManaLineProfile.ScreenRibbon).
//
// Vertices arrive on the CENTERLINE with their signed unit offset in vertex colour, and are widened
// here. Doing it on the CPU would mean regenerating the whole ribbon every time the camera moved far
// enough -- roughly 37 whole-mesh rebuilds over a single descent. Per-vertex is also more correct: a
// border can span a wide distance range, which one global thickness cannot represent.
#include "include/uniforms.wgsl"

struct CustomMaterialUniform {
    // World units of half-width per unit of camera distance. Constant screen width wants width
    // proportional to distance; this is that constant of proportionality.
    //PROPERTY(default=0.004, min=0.0001, max=0.05)
    widthPerDistance : f32,

    //PROPERTY(default=0.004, min=0.0, max=1.0)
    minHalfWidth : f32,

    //PROPERTY(default=0.5, min=0.01, max=8.0)
    maxHalfWidth : f32,
};

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) world_normal : vec3<f32>,
};

@vertex
fn vs_main(
    @location(0) position : vec3<f32>,
    @location(1) normal : vec3<f32>,
    @location(2) color : vec4<f32>,
    @location(3) uv0 : vec2<f32>
) -> VertexOutput {
    let center = (object.model * vec4<f32>(position, 1.0)).xyz;

    // Offset direction is a rotation, not a point, so w = 0.
    let offset_direction = (object.model * vec4<f32>(color.xyz, 0.0)).xyz;

    let distance_to_camera = length(frame.camera_position.xyz - center);
    let half_width = clamp(
        distance_to_camera * custom.widthPerDistance,
        custom.minHalfWidth,
        custom.maxHalfWidth);

    var output : VertexOutput;
    output.clip_position = frame.view_projection * vec4<f32>(center + (offset_direction * half_width), 1.0);
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // Unlit on purpose: these are map annotations, not surfaces. Albedo and emission stay live-editable
    // on the material because nothing about colour is baked into the geometry.
    let albedo = pow(max(object.material_albedo.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let emissive = pow(max(object.material_emission.rgb, vec3<f32>(0.0)), vec3<f32>(2.2))
        * object.material_emission.a;
    let exposure = frame.irradiance_factors.x;
    return vec4<f32>((albedo + emissive) * exposure, object.material_albedo.a);
}
