// Prefix-only subsets of the canonical FrameUniform/ObjectUniform layout (see
// ManaWebGpuContext.Uniforms.cs) -- WGSL derives uniform-buffer offsets purely from declaration
// order, so a subset is only offset-correct if it's a genuine leading prefix of the real struct
// (no skipped fields). This shader previously declared its own hand-rolled field order that did NOT
// match the C# struct's actual byte layout (time/model were at the wrong offsets) -- fixed here by
// using real prefixes instead.
struct FrameUniform {
    view_projection : mat4x4<f32>,
    camera_position : vec4<f32>,
    irradiance_factors : vec4<f32>,
};

struct ObjectUniform {
    model : mat4x4<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) direction : vec3<f32>,
};

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;

@vertex
fn vs_main(
    @location(0) position : vec3<f32>,
    @location(1) normal : vec3<f32>,
    @location(2) color : vec4<f32>,
    @location(3) uv0 : vec2<f32>
) -> VertexOutput {
    var output : VertexOutput;
    output.clip_position = frame.view_projection * object.model * vec4<f32>(position, 1.0);
    output.direction = normalize(position);
    return output;
}

fn environment_radiance(direction : vec3<f32>) -> vec3<f32> {
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

fn irradiance(direction : vec3<f32>) -> vec3<f32> {
    let n = normalize(direction);
    let up = environment_radiance(vec3<f32>(0.0, 1.0, 0.0));
    let side = environment_radiance(normalize(vec3<f32>(n.x, 0.25, n.z)));
    let down = environment_radiance(vec3<f32>(0.0, -1.0, 0.0));
    let upper_weight = smoothstep(-0.15, 0.75, n.y);
    let lower_weight = smoothstep(0.20, -0.85, n.y);
    let horizon_weight = max(0.0, 1.0 - upper_weight - lower_weight);
    return (up * upper_weight + side * horizon_weight + down * lower_weight) * 1.15;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let color = irradiance(input.direction);
    let mappedColor = color * frame.irradiance_factors.x * frame.irradiance_factors.y;
    let mapped = mappedColor / (mappedColor + vec3<f32>(1.0));
    return vec4<f32>(mapped, 1.0);
}
