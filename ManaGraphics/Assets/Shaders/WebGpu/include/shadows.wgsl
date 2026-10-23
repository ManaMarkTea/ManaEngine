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
// Which cascade a point is shadowed from: 0 nearest, 2 farthest (the debug view's colours).
fn mana_sun_cascade(world_position : vec3<f32>) -> u32 {
    let camera_distance = length(frame.camera_position.xyz - world_position);
    if (camera_distance < frame.cascade_split_far.x) {
        return 0u;
    }
    if (camera_distance < frame.cascade_split_far.y) {
        return 1u;
    }
    return 2u;
}

fn mana_sun_shadow_factor(world_position : vec3<f32>, normal : vec3<f32>, ndotl : f32) -> f32 {
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

    // Biased in metres, a shadow texel or so, not as a share of the light box's depth: that box runs 200 m back for
    // tall casters, so a depth-fraction bias came to metres at a low sun and lifted shadows off their casters.
    // The ortho matrix's own scales give this cascade's texel size and depth range.
    let m = light_view_projection;
    let map_size = vec2<f32>(textureDimensions(shadow_map));
    let texel_metres = 2.0 / (length(vec3<f32>(m[0].x, m[1].x, m[2].x)) * map_size.x);
    let depth_per_metre = length(vec3<f32>(m[0].z, m[1].z, m[2].z));
    let sin_light = sqrt(max(1.0 - ndotl * ndotl, 0.0));
    let offset_position = world_position + normalize(normal) * texel_metres * (0.5 + 1.5 * sin_light);

    let light_clip = light_view_projection * vec4<f32>(offset_position, 1.0);
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

    // The normal offset above keeps a surface from shadowing itself; this covers what is left of a texel's depth.
    let shadow_depth = light_ndc.z - texel_metres * 0.5 * depth_per_metre;

    // A 3x3 tent of bilinear comparisons, done by hand over the 4x4 texels it touches: the comparison sampler is
    // Nearest (see CreateShadowMapResources), and plain nearest taps drew every edge as a staircase of texels.
    // Each texel's weight is how much of the tent covers it -- 1 inside, the fractional position at the rim.
    let texel = shadow_uv * map_size - vec2<f32>(0.5);
    let base = floor(texel);
    let f = texel - base;
    var wx = array<f32, 4>(1.0 - f.x, 1.0, 1.0, f.x);
    var wy = array<f32, 4>(1.0 - f.y, 1.0, 1.0, f.y);
    var lit = 0.0;
    for (var ix = 0; ix < 4; ix = ix + 1) {
        for (var iy = 0; iy < 4; iy = iy + 1) {
            let uv = (base + vec2<f32>(f32(ix - 1), f32(iy - 1)) + vec2<f32>(0.5)) / map_size;
            lit = lit + wx[ix] * wy[iy] * textureSampleCompareLevel(shadow_map, shadow_sampler, uv, i32(cascade), shadow_depth);
        }
    }

    return lit / 9.0;
}
