// Cascaded shadow map for the scene's single directional "sun" light (frame.sun_direction.w > 0 when one
// exists and casts shadows this frame -- see ManaWebGpuContext.Shadows.cs / ManaWorld.CreateRenderScene).
// Any shader can #include this to test a world position against it. Each includer must separately declare
// `frame : FrameUniform` at @group(0)@binding(0) the way every shader here already does -- this file only
// adds the two bindings after it plus the lookup function, so it stays a plain addition on top rather than
// something that could conflict with a shader's own group 0 declaration.

@group(0) @binding(1) var shadow_map : texture_depth_2d_array;
@group(0) @binding(2) var shadow_sampler : sampler_comparison;

// Cascade picked by Euclidean distance from the camera rather than strict view-space Z (the frame uniform
// does not carry a separate View matrix, only the combined view_projection) -- close enough at the FOVs
// this engine uses, and simpler than threading another matrix through just for this.
fn mana_sun_shadow_factor(world_position : vec3<f32>, ndotl : f32) -> f32 {
    if (ndotl <= 0.0) {
        return 0.0;
    }

    let camera_distance = length(frame.camera_position.xyz - world_position);

    var cascade = 2u;
    var light_view_projection = frame.cascade_view_projections[2];
    if (camera_distance < frame.cascade_split_far.x) {
        cascade = 0u;
        light_view_projection = frame.cascade_view_projections[0];
    } else if (camera_distance < frame.cascade_split_far.y) {
        cascade = 1u;
        light_view_projection = frame.cascade_view_projections[1];
    }

    let light_clip = light_view_projection * vec4<f32>(world_position, 1.0);
    if (light_clip.w <= 0.0) {
        return 1.0;
    }

    let light_ndc = light_clip.xyz / light_clip.w;

    // Outside this cascade's ortho box entirely -- unshadowed rather than clamped into a wrong edge.
    if (light_ndc.x < -1.0 || light_ndc.x > 1.0 || light_ndc.y < -1.0 || light_ndc.y > 1.0
        || light_ndc.z < 0.0 || light_ndc.z > 1.0) {
        return 1.0;
    }

    let shadow_uv = light_ndc.xy * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5, 0.5);
    let shadow_depth = light_ndc.z;

    // 3x3 manual PCF: the comparison sampler itself is Nearest (see CreateShadowMapResources), so the
    // softening comes entirely from this multi-tap average rather than hardware-filtered comparison,
    // which wgpu backends support inconsistently.
    let texel_size = 1.0 / f32(2048);
    var occluded_sum = 0.0;
    for (var ox = -1; ox <= 1; ox = ox + 1) {
        for (var oy = -1; oy <= 1; oy = oy + 1) {
            let offset = vec2<f32>(f32(ox), f32(oy)) * texel_size;
            occluded_sum = occluded_sum + textureSampleCompareLevel(
                shadow_map, shadow_sampler, shadow_uv + offset, i32(cascade), shadow_depth);
        }
    }

    return occluded_sum / 9.0;
}
