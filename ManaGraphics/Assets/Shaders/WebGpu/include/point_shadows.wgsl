// Shadows of the point lights that cast them (ManaWebGpuPointShadowMap): six 90-degree depth faces a light in one array,
// layer slot * 6 + face, with each face's view-projection alongside. A light's slot rides in its colour's w (slot + 1).
// Built only with MANA_POINT_SHADOWS, where the device allows a 17th texture; uses shadows.wgsl's comparison sampler.

@group(0) @binding(11) var point_shadow_map : texture_depth_2d_array;
@group(0) @binding(12) var<storage, read> point_shadow_matrices : array<mat4x4<f32>>;

// 1 lit, 0 in shadow, between at edges.
fn mana_point_shadow(slot : u32, light_position : vec3<f32>, world_position : vec3<f32>, surface_normal : vec3<f32>) -> f32 {
    let to_point = world_position - light_position;
    let reach = abs(to_point);
    // The face is the axis the point lies furthest along: +X -X +Y -Y +Z -Z.
    var face = 0u;
    if (reach.x >= reach.y && reach.x >= reach.z) {
        face = select(1u, 0u, to_point.x > 0.0);
    } else if (reach.y >= reach.z) {
        face = select(3u, 2u, to_point.y > 0.0);
    } else {
        face = select(5u, 4u, to_point.z > 0.0);
    }
    let layer = slot * 6u + face;

    // Off the surface by a texel and a half: a 90-degree face's texel spans 2 * depth / size metres there. Turned to
    // face the light, so the back of a double-sided sheet is pushed the same way as its front.
    let size = f32(textureDimensions(point_shadow_map).x);
    let texel = 2.0 * max(reach.x, max(reach.y, reach.z)) / size;
    var normal = normalize(surface_normal);
    if (dot(normal, to_point) > 0.0) {
        normal = -normal;
    }
    let clip = point_shadow_matrices[layer] * vec4<f32>(world_position + normal * texel * 1.5, 1.0);
    if (clip.w <= 0.0) {
        return 1.0;
    }
    let ndc = clip.xyz / clip.w;
    if (ndc.z >= 1.0) {
        return 1.0;
    }
    let uv = vec2<f32>(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);

    // 3x3 PCF, kept inside the face: the next texel over belongs to another face's view.
    let edge = vec2<f32>(0.5 / size);
    var lit = 0.0;
    for (var y = -1; y <= 1; y = y + 1) {
        for (var x = -1; x <= 1; x = x + 1) {
            let at = clamp(uv + vec2<f32>(f32(x), f32(y)) / size, edge, vec2<f32>(1.0) - edge);
            lit = lit + textureSampleCompareLevel(point_shadow_map, shadow_sampler, at, i32(layer), ndc.z);
        }
    }
    return lit / 9.0;
}
