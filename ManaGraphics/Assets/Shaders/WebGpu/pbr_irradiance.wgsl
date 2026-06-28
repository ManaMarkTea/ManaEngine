struct TransformUniform {
    model_projection : mat4x4<f32>,
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    camera_position : vec4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    irradiance_factors : vec4<f32>,
    irradiance_light_color : vec4<f32>,
    irradiance_light_direction : vec4<f32>,
    time : f32,
    webgpu_debug_mode : f32,
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

@group(0) @binding(0) var<uniform> transform : TransformUniform;
@group(0) @binding(1) var color_sampler : sampler;
@group(0) @binding(2) var color_texture : texture_2d<f32>;
@group(0) @binding(3) var environment_map : texture_cube<f32>;
@group(0) @binding(4) var irradiance_map : texture_cube<f32>;
@group(0) @binding(5) var prefiltered_reflection_map : texture_cube<f32>;
@group(0) @binding(6) var brdf_lut : texture_2d<f32>;
@group(0) @binding(7) var clamp_sampler : sampler;

const PI : f32 = 3.14159265359;

@vertex
fn vs_main(
    @location(0) position : vec3<f32>,
    @location(1) normal : vec3<f32>,
    @location(2) color : vec4<f32>,
    @location(3) uv0 : vec2<f32>
) -> VertexOutput {
    var output : VertexOutput;
    let world = transform.model * vec4<f32>(position, 1.0);
    output.clip_position = transform.model_projection * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.world_position = world.xyz;
    output.world_normal = normalize((transform.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
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
    return a2 / max(PI * denom * denom, 0.0001);
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

fn mana_environment_reflection(direction : vec3<f32>, roughness : f32) -> vec3<f32> {
    let max_lod = 9.0;
    return textureSampleLevel(prefiltered_reflection_map, clamp_sampler, normalize(direction), saturate(roughness) * max_lod).rgb;
}

fn mana_irradiance(normal : vec3<f32>) -> vec3<f32> {
    return textureSample(irradiance_map, clamp_sampler, normalize(normal)).rgb;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let sampled = textureSample(color_texture, color_sampler, input.uv0);
    let vertex_color_linear = pow(max(input.color.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let material_albedo_linear = pow(max(transform.material_albedo.rgb, vec3<f32>(0.0)), vec3<f32>(2.2));
    let vertex_albedo = vertex_color_linear * sampled.rgb * material_albedo_linear;
    let alpha = input.color.a * sampled.a * transform.material_albedo.a;

    let n = normalize(input.world_normal);
    let v = normalize(transform.camera_position.xyz - input.world_position);
    let l = normalize(transform.irradiance_light_direction.xyz);
    let h = normalize(v + l);

    let metallic = saturate(transform.material_factors.x);
    let roughness = saturate(transform.material_factors.y);
    let specular_roughness = clamp(roughness, 0.025, 1.0);
    let ao = saturate(transform.material_factors.z);

    let f0 = mix(vec3<f32>(0.04), vertex_albedo, metallic);
    let exposure = transform.irradiance_factors.x;
    let ibl_intensity = transform.irradiance_factors.y;
    let light_intensity = transform.irradiance_factors.z;
    let radiance = transform.irradiance_light_color.rgb * light_intensity;
    let ndotl = max(dot(n, l), 0.0);

    let ndf = distribution_ggx(n, h, specular_roughness);
    let g = geometry_smith(n, v, l, specular_roughness);
    let f = fresnel_schlick(max(dot(h, v), 0.0), f0);
    let specular = (ndf * g * f) / max(4.0 * max(dot(n, v), 0.0) * ndotl, 0.0001);

    let kd = (vec3<f32>(1.0) - f) * (1.0 - metallic);
    let direct = (kd * vertex_albedo / PI + specular) * radiance * ndotl;

    let irradiance = mana_irradiance(n);
    let F = fresnel_schlick_roughness(max(dot(n, v), 0.0), f0, roughness);
    let kd_ambient = (vec3<f32>(1.0) - F) * (1.0 - metallic);
    let diffuse_ibl = irradiance * vertex_albedo * ibl_intensity;

    let reflection = reflect(-v, n);
    let reflection_radiance = mana_environment_reflection(reflection, roughness);
    let brdf = textureSample(brdf_lut, clamp_sampler, vec2<f32>(max(dot(n, v), 0.0), roughness)).rg;
    let specular_ibl = reflection_radiance * (F * brdf.x + brdf.y) * ibl_intensity;
    let ambient = (kd_ambient * diffuse_ibl + specular_ibl) * ao;

    var color = ambient + direct;

    let debug_mode = u32(transform.webgpu_debug_mode + 0.5);
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
        color = reflection_radiance;
    } else if (debug_mode == 6u) {
        // Mode 6: BRDF Scale
        color = F * brdf.x + brdf.y;
    } else {
        // Mode 0: Normal shading
        color = color * exposure;
        color = color / (color + vec3<f32>(1.0));
    }
    return vec4<f32>(color, alpha);
}
