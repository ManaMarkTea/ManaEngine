// The environment drawn as a background, behind every mesh. Sampled from EnvironmentMapResource -- the
// same equirectangular sky image the PBR pipeline already converted to a cube map for reflections, here
// just shown directly rather than blurred into indirect lighting.
//
// No vertex buffer: a full-screen triangle, the same oversized-triangle trick used by the UI's backdrop
// blur reduction shader. Depth write is off and the compare is Always in the pipeline that uses this, so
// the sky draws first and never blocks or is blocked by anything drawn after it -- it is a background, not
// geometry.

struct SkyboxUniform {
    // View with its translation zeroed, then projected and inverted -- so unprojecting a screen position
    // through this yields a direction, not a point that would still need the camera's position subtracted
    // out of it.
    inverse_direction_view_projection : mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> sky : SkyboxUniform;
@group(0) @binding(1) var sky_sampler : sampler;
@group(0) @binding(2) var sky_texture : texture_cube<f32>;

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) ndc : vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) vertex_index : u32) -> VertexOutput {
    let uv = vec2<f32>(f32((vertex_index << 1u) & 2u), f32(vertex_index & 2u));
    let ndc = uv * 2.0 - 1.0;

    var output : VertexOutput;
    output.ndc = ndc;
    // z = 1.0 (the far plane in WebGPU's [0,1] depth range) and w = 1.0: this quad is not going through a
    // real projection, so there is no perspective divide to do it for us. The fragment shader undoes the
    // rest of the projection itself.
    output.clip_position = vec4<f32>(ndc, 1.0, 1.0);
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // Unprojected at the far plane, then normalized: distance along the ray does not matter, only its
    // direction, which is the one thing a background can show.
    let far = sky.inverse_direction_view_projection * vec4<f32>(input.ndc, 1.0, 1.0);
    let direction = normalize(far.xyz / far.w);

    return textureSample(sky_texture, sky_sampler, direction);
}
