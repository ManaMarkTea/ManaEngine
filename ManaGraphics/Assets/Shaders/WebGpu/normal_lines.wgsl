// Vertex-normal overlay (see Overlays/ManaWebGpuNormalLinesOverlay). Both ends of a line arrive at the
// surface position; uv0.x is 0 on the base and 1 on the tip, so the tip extrudes along the normal by
// vertex_normal_length.
#include "include/uniforms.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) color : vec4<f32>,
};

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    // Extruded in WORLD space, after the model matrix. Doing it in local space would multiply the
    // length by the object's scale, so the same setting gave metre-long lines on the terrain and
    // building-sized ones on a scaled-up model.
    let world_position = (object.model * vec4<f32>(position, 1.0)).xyz;
    let world_normal = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    let extruded = world_position + (world_normal * uv0.x * frame.vertex_normal_length);

    var output : VertexOutput;
    output.clip_position = frame.view_projection * vec4<f32>(extruded, 1.0);
    output.color = color;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    return input.color;
}
