struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_position : vec3<f32>,
    @location(3) world_normal : vec3<f32>,
};

#include "include/uniforms.wgsl"
#include "include/point_lights.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
#include "include/shadows.wgsl"
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;
@group(1) @binding(3) var environment_map : texture_cube<f32>;
@group(1) @binding(4) var irradiance_map : texture_cube<f32>;
@group(1) @binding(5) var prefiltered_reflection_map : texture_cube<f32>;
@group(1) @binding(6) var brdf_lut : texture_2d<f32>;
@group(1) @binding(7) var clamp_sampler : sampler;

// The rest of the material's maps. Every one is always bound: an unassigned channel gets a neutral 1x1
// -- white for the multiplied ones, (0.5, 0.5, 1) for the normal -- so "no map" needs no branch here.
@group(1) @binding(8) var normal_texture : texture_2d<f32>;
@group(1) @binding(9) var emission_texture : texture_2d<f32>;
@group(1) @binding(10) var metallic_texture : texture_2d<f32>;
@group(1) @binding(11) var roughness_texture : texture_2d<f32>;
@group(1) @binding(12) var occlusion_texture : texture_2d<f32>;
@group(1) @binding(13) var opacity_texture : texture_2d<f32>;

const PI : f32 = 3.14159265359;

// How much sky and fill light is left in the sun's shadow; the floor keeps 0.35 of its light there (grid.wgsl).
const ShadowedAmbient : f32 = 0.45;

@vertex
fn vs_main(
    @location(0) position : vec3<f32>,
    @location(1) normal : vec3<f32>,
    @location(2) color : vec4<f32>,
    @location(3) uv0 : vec2<f32>
) -> VertexOutput {
    var output : VertexOutput;
    let world = object.model * vec4<f32>(position, 1.0);
    output.clip_position = frame.view_projection * object.model * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.world_position = world.xyz;
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    return output;
}

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

/// A tangent frame built from screen-space derivatives rather than a vertex attribute.
///
/// The meshes here carry no tangents, and adding the attribute would mean touching every generator and
/// every loader. Reconstructing it costs four derivatives and is exact for the flat-shaded case: the UV
/// gradients across the triangle are precisely what a tangent basis encodes. The one thing it cannot do
/// is give a sensible frame where the UVs are degenerate, which is why it falls back to the geometric
/// normal there rather than producing a NaN.
fn mana_perturb_normal(n : vec3<f32>, world_position : vec3<f32>, uv : vec2<f32>, sampled : vec3<f32>, strength : f32) -> vec3<f32> {
    let dp_dx = dpdx(world_position);
    let dp_dy = dpdy(world_position);
    let duv_dx = dpdx(uv);
    let duv_dy = dpdy(uv);

    let determinant = duv_dx.x * duv_dy.y - duv_dy.x * duv_dx.y;
    if (abs(determinant) < 1e-12) {
        return n;
    }

    let inverse = 1.0 / determinant;
    let tangent_raw = (dp_dx * duv_dy.y - dp_dy * duv_dx.y) * inverse;

    // Gram-Schmidt: the raw tangent is only orthogonal to the normal on a perfectly flat, evenly mapped
    // surface, and a skewed basis tilts the lighting in a way that reads as a shading error.
    let tangent = normalize(tangent_raw - n * dot(n, tangent_raw));
    if (any(tangent != tangent)) {
        return n;
    }

    let bitangent = normalize(cross(n, tangent));

    // A neutral map is (0.5, 0.5, 1), so this decodes to straight up and the mix below is a no-op.
    let tangent_normal = normalize(sampled * 2.0 - vec3<f32>(1.0));
    let perturbed = normalize(tangent * tangent_normal.x + bitangent * tangent_normal.y + n * tangent_normal.z);

    return normalize(mix(n, perturbed, saturate(strength)));
}

fn fresnel_schlick(cos_theta : f32, f0 : vec3<f32>) -> vec3<f32> {
    return f0 + (vec3<f32>(1.0) - f0) * pow(1.0 - saturate(cos_theta), 5.0);
}

fn fresnel_schlick_roughness(cos_theta : f32, f0 : vec3<f32>, roughness : f32) -> vec3<f32> {
    return f0 + (max(vec3<f32>(1.0 - roughness), f0) - f0) * pow(1.0 - saturate(cos_theta), 5.0);
}

fn distribution_ggx(n : vec3<f32>, h : vec3<f32>, roughness : f32) -> f32 {
    let a = roughness * roughness;
    let a2 = a * a;
    let ndoth = max(dot(n, h), 0.0);
    let ndoth2 = ndoth * ndoth;
    let denom = (ndoth2 * (a2 - 1.0) + 1.0);
    if (a2 <= 0.0) {
        return 0.0;
    }
    return a2 / (PI * denom * denom);
}

fn geometry_schlick_ggx(ndotv : f32, roughness : f32) -> f32 {
    let r = roughness + 1.0;
    let k = (r * r) / 8.0;
    return ndotv / max(ndotv * (1.0 - k) + k, 0.0001);
}

fn geometry_smith(n : vec3<f32>, v : vec3<f32>, l : vec3<f32>, roughness : f32) -> f32 {
    let ndotv = max(dot(n, v), 0.0);
    let ndotl = max(dot(n, l), 0.0);
    return geometry_schlick_ggx(ndotv, roughness) * geometry_schlick_ggx(ndotl, roughness);
}

fn mana_environment_radiance(direction : vec3<f32>) -> vec3<f32> {
    return textureSample(environment_map, clamp_sampler, normalize(direction)).rgb;
}

// Bevy's radiance_sample_direction. Rough surfaces sample along N rather than R -- R swings toward the
// horizon at grazing angles, which is what makes matte terrain shimmer along ridgelines.
fn mana_radiance_direction(n : vec3<f32>, r : vec3<f32>, roughness : f32) -> vec3<f32> {
    let smoothness = saturate(1.0 - roughness);
    let lerp_factor = smoothness * (sqrt(smoothness) + roughness);
    return mix(n, r, lerp_factor);
}

fn mana_environment_reflection(direction : vec3<f32>, roughness : f32) -> vec3<f32> {
    // Inverse of the prefilter compute's roughness = mip / (mipCount - 1). Queried, not hardcoded --
    // the mip count follows PrefilterCubeSize.
    let max_lod = max(f32(textureNumLevels(prefiltered_reflection_map)) - 1.0, 0.0);
    return textureSampleLevel(prefiltered_reflection_map, clamp_sampler, normalize(direction), saturate(roughness) * max_lod).rgb;
}

fn mana_irradiance(normal : vec3<f32>) -> vec3<f32> {
    return textureSample(irradiance_map, clamp_sampler, normalize(normal)).rgb;
}

fn debug_color_to_surface(value : vec3<f32>) -> vec3<f32> {
    // OpenGL debug modes write raw RGB into a non-sRGB default framebuffer.
    // This WebGPU target is sRGB, so pre-linearize diagnostic colors to display
    // the same values after the attachment's linear-to-sRGB conversion.
    return pow(max(value, vec3<f32>(0.0)), vec3<f32>(2.2));
}

// Unlit: the value the tone map (ACES, hdr_tonemap.wgsl) turns back into this colour, so flat art shows as drawn.
// Exact up to 0.9; brighter is held there, where only the whitest parts bloom a touch.
fn mana_unlit(colour : vec3<f32>) -> vec3<f32> {
    let y = min(max(colour, vec3<f32>(0.0)), vec3<f32>(0.9));
    let qa = 2.51 - 2.43 * y;
    let qb = 0.03 - 0.59 * y;
    let qc = -0.14 * y;
    return (-qb + sqrt(qb * qb - 4.0 * qa * qc)) / (2.0 * qa);
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // One transform for every map, so a tiled brick stays registered across albedo, normal and roughness.
    let uv = input.uv0 * object.uv_transform.xy + object.uv_transform.zw;

    let sampled = textureSample(color_texture, color_sampler, uv);
    let vertex_color_linear = pow(max(input.color.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let material_albedo_linear = pow(max(object.material_albedo.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let vertex_albedo = vertex_color_linear * sampled.rgb * material_albedo_linear;
    let opacity_sample = textureSample(opacity_texture, color_sampler, uv).r;
    let alpha = input.color.a * sampled.a * object.material_albedo.a * opacity_sample;

    let smooth_n = normalize(input.world_normal);

    // Flat shading from screen-space derivatives: the cross of the world-position gradients IS the
    // triangle's geometric normal, so faceting needs no duplicated vertices. Derivatives must be taken
    // in uniform control flow, hence computed unconditionally and selected after. The sign flip keeps it
    // in the same hemisphere as the interpolated normal, since winding varies across procedural meshes.
    let derivative_n = normalize(cross(dpdx(input.world_position), dpdy(input.world_position)));
    let facet_n = select(-derivative_n, derivative_n, dot(derivative_n, smooth_n) >= 0.0);
    let geometric_n = select(smooth_n, facet_n, object.flat_shading > 0.5);

    let normal_sample = textureSample(normal_texture, color_sampler, uv).rgb;
    let n = mana_perturb_normal(geometric_n, input.world_position, uv, normal_sample, object.normal_strength);

    let v = normalize(frame.camera_position.xyz - input.world_position);

    // Each map multiplies its scalar, and an absent one samples white -- so the scalar alone is what you
    // get, and a map narrows it from there. That is the glTF convention and what a slider beside a map
    // slot implies.
    // Channels follow the glTF packing: occlusion in R, roughness in G, metallic in B, all in one
    // texture. A standalone greyscale map has the same value in every channel, so reading the packed
    // channel is right for both -- whereas reading R gave the packed map's unused channel, which is
    // usually 1 and made every glTF material fully metallic and fully rough.
    let metallic = saturate(object.material_factors.x * textureSample(metallic_texture, color_sampler, uv).b);
    // Floor from Filament/Bevy (perceptualRoughnessToRoughness): below ~0.089 GGX loses precision and
    // the surface becomes a mirror that aliases badly.
    let roughness = clamp(object.material_factors.y * textureSample(roughness_texture, color_sampler, uv).g, 0.089, 1.0);
    let specular_roughness = roughness;
    let ao = saturate(object.material_factors.z * textureSample(occlusion_texture, color_sampler, uv).r);

    let f0 = mix(vec3<f32>(0.04), vertex_albedo, metallic);
    let exposure = frame.irradiance_factors.x;
    let ibl_intensity = frame.irradiance_factors.y;

    // The sun's shadow, worked out once: it also dims the sky and the fill lights below, as the floor's does
    // (grid.wgsl), or an object in shadow keeps most of its light and reads as unshadowed on a dark floor.
    // Only where something else blocks the sun: a face turned away keeps its sky light, faded in over the
    // terminator so round things get no hard line there.
    var sun_shadow = 1.0;
    var shade = 1.0;
    if (frame.sun_direction.w > 0.5) {
        let sun_ndotl = dot(n, normalize(-frame.sun_direction.xyz));
        sun_shadow = mana_sun_shadow_factor(input.world_position, n, max(sun_ndotl, 0.0001));
        shade = mix(1.0, mix(ShadowedAmbient, 1.0, sun_shadow), smoothstep(0.0, 0.25, sun_ndotl));
    }

    var fill = vec3<f32>(0.0);
    for (var i = 0u; i < 4u; i = i + 1u) {
        let light_pos = frame.light_positions[i].xyz;
        let light_color = frame.light_colors[i].xyz;

        let l = normalize(light_pos - input.world_position);
        let h = normalize(v + l);
        let distance = length(light_pos - input.world_position);
        let attenuation = 1.0 / (distance * distance);
        let radiance = light_color * attenuation;

        let ndotl = max(dot(n, l), 0.0);

        let ndf = distribution_ggx(n, h, specular_roughness);
        let g = geometry_smith(n, v, l, specular_roughness);
        let f = fresnel_schlick(max(dot(h, v), 0.0), f0);
        let specular = (ndf * g * f) / max(4.0 * max(dot(n, v), 0.0) * ndotl, 0.0001);

        let kd = (vec3<f32>(1.0) - f) * (1.0 - metallic);
        fill = fill + (kd * vertex_albedo / PI + specular) * radiance * ndotl;
    }

    var direct = fill * shade;

    // Placed point lights -- lamps, torches. Inverse square, windowed to reach zero at the light's range so a
    // lamp lights its surroundings and not the whole level.
    for (var i = 0u; i < 16u; i = i + 1u) {
        let light_color = frame.local_light_colors[i].rgb;
        let range = frame.local_light_positions[i].w;
        if (range <= 0.0) {
            continue;
        }

        let to_light = frame.local_light_positions[i].xyz - input.world_position;
        let distance = max(length(to_light), 0.05);
        let radiance = light_color * mana_point_light_attenuation(distance, range);

        let l = to_light / distance;
        let h = normalize(v + l);
        let ndotl = max(dot(n, l), 0.0);

        let ndf = distribution_ggx(n, h, specular_roughness);
        let g = geometry_smith(n, v, l, specular_roughness);
        let f = fresnel_schlick(max(dot(h, v), 0.0), f0);
        let specular = (ndf * g * f) / max(4.0 * max(dot(n, v), 0.0) * ndotl, 0.0001);

        let kd = (vec3<f32>(1.0) - f) * (1.0 - metallic);
        direct = direct + (kd * vertex_albedo / PI + specular) * radiance * ndotl;
    }

    // The scene's single directional "sun" (frame.sun_direction.w > 0 -- see ManaWorld.CreateRenderScene),
    // separate from the 4-slot point light grid above. No attenuation: a directional light has none by
    // definition. Only this term is shadow-tested; the point lights above never cast shadows in this pass.
    if (frame.sun_direction.w > 0.5) {
        let l = normalize(-frame.sun_direction.xyz);
        let h = normalize(v + l);
        let radiance = frame.sun_color.rgb * frame.sun_color.a;

        let ndotl = max(dot(n, l), 0.0);

        let ndf = distribution_ggx(n, h, specular_roughness);
        let g = geometry_smith(n, v, l, specular_roughness);
        let f = fresnel_schlick(max(dot(h, v), 0.0), f0);
        let specular = (ndf * g * f) / max(4.0 * max(dot(n, v), 0.0) * ndotl, 0.0001);

        let kd = (vec3<f32>(1.0) - f) * (1.0 - metallic);
        direct = direct + (kd * vertex_albedo / PI + specular) * radiance * ndotl * sun_shadow;
    }

    let irradiance = mana_irradiance(n);
    let F = fresnel_schlick_roughness(max(dot(n, v), 0.0), f0, roughness);
    let kd_ambient = (vec3<f32>(1.0) - F) * (1.0 - metallic);
    let diffuse_ibl = irradiance * vertex_albedo * ibl_intensity;

    let reflection = reflect(-v, n);
    let reflection_radiance = mana_environment_reflection(mana_radiance_direction(n, reflection, roughness), roughness);
    let ndotv = max(dot(n, v), 0.0);
    let brdf = textureSample(brdf_lut, clamp_sampler, vec2<f32>(ndotv, roughness)).rg;
    // f0, not F: the LUT already integrates Fresnel (Karis split-sum is F0 * A + B). Using F here
    // applies it twice and blows out grazing angles.
    let specular_ibl = reflection_radiance * (f0 * brdf.x + brdf.y) * ibl_intensity;
    let ambient = (kd_ambient * diffuse_ibl + specular_ibl) * ao * shade;
    // With an emission map, it says what glows (glTF's rule). Without one, emission shows the surface's own
    // colour -- texture and all -- as if in full daylight, tinted by the emission colour: a glowing coin still
    // looks like the coin, where a white stand-in map painted it over in one flat colour.
    let emission_sample = textureSample(emission_texture, color_sampler, uv).rgb;
    let emission_source = select(vertex_albedo, emission_sample, object.outline_parameters.z > 0.5);
    let emissive_color = pow(max(object.material_emission.rgb, vec3<f32>(0.0)), vec3<f32>(2.2)) * emission_source;
    let emissive_strength = object.material_emission.a;
    let emissive = emissive_color * emissive_strength;

    var color = ambient + direct + emissive;
    if (object.outline_parameters.w > 0.5) {
        color = mana_unlit(vertex_albedo);
    }

    // ManaOpacityMode: 1 opaque, 2 cutout, 3 blend. Cutout discards below the threshold and is otherwise
    // fully solid, which is what foliage and grates want -- no sorting, no depth-write cost. Blend keeps
    // the alpha for the blend state to use, and needs back-to-front ordering to be correct. Opaque throws
    // the alpha away entirely, which is the default and right for almost everything.
    //
    // The discard sits here rather than at the top on purpose: every derivative in this shader -- the
    // flat-shading facet normal and the tangent frame -- has already been taken, and derivatives after a
    // discard are undefined.
    let opacity_mode = u32(object.opacity_mode + 0.5);
    if (opacity_mode == 2u && alpha < object.material_factors.w) {
        discard;
    }

    var output_alpha = select(1.0, alpha, opacity_mode == 3u);

    // object.debug_mode_override < 0 means "no per-object override, use the frame's global mode".
    let effective_debug_mode = select(frame.webgpu_debug_mode, object.debug_mode_override, object.debug_mode_override >= 0.0);
    let debug_mode = u32(effective_debug_mode + 0.5);
    if (debug_mode == 1u) {
        // Mode 1: View Vector (V)
        color = v * 0.5 + vec3<f32>(0.5);
    } else if (debug_mode == 2u) {
        // Mode 2: World Normal (N)
        color = n * 0.5 + vec3<f32>(0.5);
    } else if (debug_mode == 3u) {
        // Mode 3: Reflection Vector (R)
        color = reflection * 0.5 + vec3<f32>(0.5);
    } else if (debug_mode == 4u) {
        // Mode 4: Cubemap direction
        color = normalize(reflection) * 0.5 + vec3<f32>(0.5);
    } else if (debug_mode == 5u) {
        // Mode 5: Reflection Radiance
        color = reflection_radiance * ibl_intensity;
    } else if (debug_mode == 6u) {
        // Mode 6: BRDF Scale
        color = (F * brdf.x + brdf.y) * ibl_intensity;
    } else if (debug_mode == 7u) {
        // Mode 7: Linear Albedo
        color = vertex_albedo;
    } else if (debug_mode == 8u) {
        // Mode 8: F0
        color = f0;
    } else if (debug_mode == 9u) {
        // Mode 9: Fresnel
        color = F;
    } else if (debug_mode == 10u) {
        // Mode 10: BRDF LUT
        color = vec3<f32>(brdf, 0.0);
    } else if (debug_mode == 11u) {
        // Mode 11: Diffuse IBL
        color = kd_ambient * diffuse_ibl;
    } else if (debug_mode == 12u) {
        // Mode 12: Specular IBL
        color = specular_ibl;
    } else if (debug_mode == 13u) {
        // Mode 13: Direct Lighting
        color = direct;
    } else if (debug_mode == 14u) {
        // Mode 14: Final Linear
        color = color * exposure;
    } else {
        // Mode 0: Normal shading
        color = color * exposure;
    }
    if (debug_mode != 0u) {
        color = debug_color_to_surface(color);
        output_alpha = -1.0;
    }
    return vec4<f32>(color, output_alpha);
}
