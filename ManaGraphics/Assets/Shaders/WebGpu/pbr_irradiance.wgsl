struct TransformUniform {
    model_projection : mat4x4<f32>,
    time : f32,
    _padding0 : f32,
    _padding1 : f32,
    _padding2 : f32,
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    camera_position : vec4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    irradiance_factors : vec4<f32>,
    irradiance_light_color : vec4<f32>,
    irradiance_light_direction : vec4<f32>,
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
    let d = normalize(direction);
    let sky = vec3<f32>(0.36, 0.50, 0.74);
    let horizon = vec3<f32>(0.78, 0.80, 0.76);
    let ground = vec3<f32>(0.28, 0.25, 0.22);
    let sky_mix = smoothstep(-0.15, 0.85, d.y);
    let base = mix(ground, mix(horizon, sky, sky_mix), smoothstep(-0.75, 0.15, d.y));
    let sun_dir = normalize(vec3<f32>(0.35, 0.82, 0.45));
    let sun = vec3<f32>(1.0, 0.86, 0.58) * pow(max(dot(d, sun_dir), 0.0), 96.0) * 4.0;
    return base + sun;
}

fn mana_irradiance(normal : vec3<f32>) -> vec3<f32> {
    let n = normalize(normal);
    let up = mana_environment_radiance(vec3<f32>(0.0, 1.0, 0.0));
    let side = mana_environment_radiance(normalize(vec3<f32>(n.x, 0.25, n.z)));
    let down = mana_environment_radiance(vec3<f32>(0.0, -1.0, 0.0));
    let upper_weight = smoothstep(-0.15, 0.75, n.y);
    let lower_weight = smoothstep(0.20, -0.85, n.y);
    let horizon_weight = max(0.0, 1.0 - upper_weight - lower_weight);
    return (up * upper_weight + side * horizon_weight + down * lower_weight) * 1.15;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let sampled = textureSample(color_texture, color_sampler, input.uv0);
    let vertex_albedo = input.color.rgb * sampled.rgb * transform.material_albedo.rgb;
    let alpha = input.color.a * sampled.a * transform.material_albedo.a;

    let n = normalize(input.world_normal);
    let v = normalize(transform.camera_position.xyz - input.world_position);
    let l = normalize(transform.irradiance_light_direction.xyz);
    let h = normalize(v + l);

    let metallic = saturate(transform.material_factors.x);
    let roughness = clamp(transform.material_factors.y, 0.04, 1.0);
    let ao = saturate(transform.material_factors.z);

    let f0 = mix(vec3<f32>(0.04), vertex_albedo, metallic);
    let exposure = transform.irradiance_factors.x;
    let ibl_intensity = transform.irradiance_factors.y;
    let light_intensity = transform.irradiance_factors.z;
    let radiance = transform.irradiance_light_color.rgb * light_intensity;
    let ndotl = max(dot(n, l), 0.0);

    let ndf = distribution_ggx(n, h, roughness);
    let g = geometry_smith(n, v, l, roughness);
    let f = fresnel_schlick(max(dot(h, v), 0.0), f0);
    let specular = (ndf * g * f) / max(4.0 * max(dot(n, v), 0.0) * ndotl, 0.0001);

    let kd = (vec3<f32>(1.0) - f) * (1.0 - metallic);
    let direct = (kd * vertex_albedo / PI + specular) * radiance * ndotl;

    let irradiance = mana_irradiance(n);
    let diffuse_ibl = irradiance * vertex_albedo * ibl_intensity;
    let reflection = reflect(-v, n);
    let reflection_radiance = mana_environment_radiance(reflection);
    let reflection_strength = mix(0.35, 1.85, metallic) * pow(1.0 - roughness, 0.35);
    let mirror_boost = metallic * pow(1.0 - roughness, 3.0) * 2.25;
    let specular_ibl = reflection_radiance * f * (reflection_strength + mirror_boost) * ibl_intensity;
    let ambient = (kd * diffuse_ibl + specular_ibl) * ao;

    var color = ambient + direct;
    color = color * exposure;
    color = color / (color + vec3<f32>(1.0));
    return vec4<f32>(color, alpha);
}
