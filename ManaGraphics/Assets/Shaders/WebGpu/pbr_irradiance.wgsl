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
@group(0) @binding(3) var environment_texture : texture_2d<f32>;

const PI : f32 = 3.14159265359;
const INV_PI : f32 = 0.31830988618;
const INV_TWO_PI : f32 = 0.15915494309;

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

fn direction_to_equirect_uv(direction : vec3<f32>) -> vec2<f32> {
    let d = normalize(direction);
    let u = fract(atan2(d.z, d.x) * INV_TWO_PI + 0.5);
    let v = asin(clamp(d.y, -1.0, 1.0)) * INV_PI + 0.5;
    return vec2<f32>(u, 1.0 - v);
}

fn mana_environment_radiance(direction : vec3<f32>) -> vec3<f32> {
    return textureSample(environment_texture, color_sampler, direction_to_equirect_uv(direction)).rgb;
}

fn mana_environment_reflection(direction : vec3<f32>, roughness : f32) -> vec3<f32> {
    let d = normalize(direction);
    let sharp = mana_environment_radiance(d);
    let blur_radius = roughness * roughness * 0.45;
    let up = vec3<f32>(0.0, 1.0, 0.0);
    let side = normalize(cross(select(up, vec3<f32>(1.0, 0.0, 0.0), abs(d.y) > 0.96), d));
    let tangent = normalize(cross(d, side));
    let blurred = (
        sharp +
        mana_environment_radiance(normalize(d + side * blur_radius)) +
        mana_environment_radiance(normalize(d - side * blur_radius)) +
        mana_environment_radiance(normalize(d + tangent * blur_radius)) +
        mana_environment_radiance(normalize(d - tangent * blur_radius))
    ) * 0.2;
    return mix(sharp, blurred, saturate(roughness));
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
    let diffuse_ibl = irradiance * vertex_albedo * ibl_intensity;
    let reflection = reflect(-v, n);
    let reflection_radiance = mana_environment_reflection(reflection, roughness);
    let reflection_strength = mix(0.35, 1.0, metallic);
    let specular_ibl = reflection_radiance * f * reflection_strength * ibl_intensity;
    let ambient = (kd * diffuse_ibl + specular_ibl) * ao;

    var color = ambient + direct;
    color = color * exposure;
    color = color / (color + vec3<f32>(1.0));
    return vec4<f32>(color, alpha);
}
