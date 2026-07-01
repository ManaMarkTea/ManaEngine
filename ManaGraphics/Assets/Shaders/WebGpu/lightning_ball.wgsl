struct TransformUniform {
    model_projection : mat4x4<f32>,
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    camera_position : vec4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    material_emission : vec4<f32>,
    irradiance_factors : vec4<f32>,
    light_positions : array<vec4<f32>, 4>,
    light_colors : array<vec4<f32>, 4>,
    time : f32,
    webgpu_debug_mode : f32,
    _padding1 : f32,
    _padding2 : f32,
};

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) local_pos : vec3<f32>,
    @location(3) world_pos : vec3<f32>,
    @location(4) world_normal : vec3<f32>,
}

@group(0) @binding(0) var<uniform> transform : TransformUniform;
@group(0) @binding(1) var color_sampler : sampler;
@group(0) @binding(2) var color_texture : texture_2d<f32>;

const VOLUME_STEPS : u32 = 24u;

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = transform.model_projection * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.local_pos = position;
    output.world_pos = (transform.model * vec4<f32>(position, 1.0)).xyz;
    output.world_normal = normalize((transform.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    return output;
}

fn lightning_density(position : vec3<f32>, time : f32) -> f32 {
    let warped = position + sin(
        position.yzx * vec3<f32>(8.0, 7.0, 9.0) +
        vec3<f32>(time * 1.7, -time * 1.3, time * 1.1)
    ) * 0.09;

    let path_xy = vec2<f32>(
        sin(warped.z * 5.5 + time),
        cos(warped.z * 4.0 - time * 1.2)
    ) * 0.22;
    let path_yz = vec2<f32>(
        sin(warped.x * 6.0 - time * 0.8),
        cos(warped.x * 4.5 + time * 1.4)
    ) * 0.19;
    let path_zx = vec2<f32>(
        sin(warped.y * 5.0 + time * 1.3),
        cos(warped.y * 6.5 - time)
    ) * 0.17;

    let strand_xy = exp(-length(warped.xy - path_xy) * 34.0);
    let strand_yz = exp(-length(warped.yz - path_yz) * 38.0);
    let strand_zx = exp(-length(warped.zx - path_zx) * 42.0);
    let branching = 0.55 + 0.45 * sin(
        dot(warped, vec3<f32>(13.0, 17.0, 11.0)) + time * 6.0
    );

    return (strand_xy + strand_yz * 0.75 + strand_zx * 0.55) * branching;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let p = input.local_pos;
    let t = transform.time * 5.0;
    let pulse = sin(t + p.x * 10.0) * cos(t + p.y * 10.0) * sin(t + p.z * 10.0);
    let pulse_val = pulse * 0.5 + 0.5;
    let bolt = smoothstep(0.72, 0.98, pulse_val);
    let view_direction = normalize(transform.camera_position.xyz - input.world_pos);
    let facing = clamp(abs(dot(normalize(input.world_normal), view_direction)), 0.0, 1.0);
    let rim = pow(1.0 - facing, 1.7);
    let temporal_glow = 0.85 + 0.15 * sin(transform.time * 2.0);

    let base_color = vec3<f32>(0.06, 0.30, 0.95);
    let lightning_color = vec3<f32>(0.78, 0.94, 1.0);
    let shell_color = mix(base_color, lightning_color, bolt);

    // Transform the world-space view ray as a direction. Using w = 0 keeps
    // this correct for translated objects even though normal_matrix carries
    // only the inverse model's linear transform.
    let world_ray = input.world_pos - transform.camera_position.xyz;
    let ray_direction = normalize((transpose(transform.normal_matrix) * vec4<f32>(world_ray, 0.0)).xyz);
    let sphere_radius = 1.0;
    let entry = input.local_pos;
    let b = dot(entry, ray_direction);
    let c = dot(entry, entry) - sphere_radius * sphere_radius;
    let exit_distance = max(0.0, -b + sqrt(max(b * b - c, 0.0)));
    let step_length = exit_distance / f32(VOLUME_STEPS);

    var volume_radiance = vec3<f32>(0.0);
    for (var step = 0u; step < VOLUME_STEPS; step = step + 1u) {
        let distance_along_ray = (f32(step) + 0.5) * step_length;
        let sample_position = entry + ray_direction * distance_along_ray;
        let radial_fade = 1.0 - smoothstep(sphere_radius * 0.72, sphere_radius, length(sample_position));
        let density = lightning_density(sample_position / sphere_radius, t) * radial_fade;
        let flicker = 0.72 + 0.28 * sin(t * 1.9 + f32(step) * 0.63);
        let sample_color = mix(base_color, lightning_color, clamp(density * 1.8, 0.0, 1.0));
        volume_radiance = volume_radiance + sample_color * density * flicker * step_length;
    }

    let shell_radiance = shell_color * temporal_glow * (0.12 + rim * 0.35 + bolt * 0.45);
    let radiance = shell_radiance + volume_radiance * 3.2;

    // A faint glassy shell with brighter lightning filaments and rim, while
    // preserving visibility of geometry behind the sphere.
    let material_opacity = clamp(transform.material_albedo.a, 0.0, 1.0);
    let volume_energy = clamp(dot(volume_radiance, vec3<f32>(0.2126, 0.7152, 0.0722)), 0.0, 1.0);
    let alpha = clamp((0.004 + rim * 0.025 + bolt * 0.035 + volume_energy * 0.11) * material_opacity, 0.0, 0.16);
    return vec4<f32>(radiance, alpha);
}
