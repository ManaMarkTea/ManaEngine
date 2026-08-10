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
    _padding0 : f32,
    _padding1 : f32,
    _padding2 : f32,
};

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_position : vec3<f32>,
    @location(3) world_normal : vec3<f32>,
};

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;
@group(1) @binding(3) var environment_map : texture_cube<f32>;
@group(1) @binding(4) var irradiance_map : texture_cube<f32>;
@group(1) @binding(5) var prefiltered_reflection_map : texture_cube<f32>;
@group(1) @binding(6) var brdf_lut : texture_2d<f32>;
@group(1) @binding(7) var clamp_sampler : sampler;

const PI : f32 = 3.14159265359;

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

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let sampled = textureSample(color_texture, color_sampler, input.uv0);
    let vertex_color_linear = pow(max(input.color.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let material_albedo_linear = pow(max(object.material_albedo.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let vertex_albedo = vertex_color_linear * sampled.rgb * material_albedo_linear;
    let alpha = input.color.a * sampled.a * object.material_albedo.a;

    let n = normalize(input.world_normal);
    let v = normalize(frame.camera_position.xyz - input.world_position);

    let metallic = saturate(object.material_factors.x);
    // Floor from Filament/Bevy (perceptualRoughnessToRoughness): below ~0.089 GGX loses precision and
    // the surface becomes a mirror that aliases badly.
    let roughness = clamp(object.material_factors.y, 0.089, 1.0);
    let specular_roughness = roughness;
    let ao = saturate(object.material_factors.z);

    let f0 = mix(vec3<f32>(0.04), vertex_albedo, metallic);
    let exposure = frame.irradiance_factors.x;
    let ibl_intensity = frame.irradiance_factors.y;

    var direct = vec3<f32>(0.0);
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
        direct = direct + (kd * vertex_albedo / PI + specular) * radiance * ndotl;
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
    let ambient = (kd_ambient * diffuse_ibl + specular_ibl) * ao;
    let emissive_color = pow(max(object.material_emission.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let emissive_strength = object.material_emission.a;
    let emissive = emissive_color * emissive_strength;

    var color = ambient + direct + emissive;
    var output_alpha = alpha;

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
