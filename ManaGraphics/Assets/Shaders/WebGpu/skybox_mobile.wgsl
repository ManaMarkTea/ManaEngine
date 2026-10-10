// Mobile sky: a few gradients and sparse procedural stars, plus the real catalogue and its constellation lines when
// asked for (?sky=stars; celestial_north.w). Same bindings as the desktop sky; used for both the visible background
// and the environment cubes that light the scene.
#include "include/constellations.wgsl"
#include "include/star_catalogue.wgsl"

struct SkyboxUniform {
    inverse_direction_view_projection : mat4x4<f32>,
    sun : vec4<f32>,
    moon : vec4<f32>,
    parameters : vec4<f32>,
    celestial_east : vec4<f32>,
    celestial_up : vec4<f32>,
    celestial_north : vec4<f32>,
};
@group(0) @binding(0) var<uniform> sky : SkyboxUniform;
@group(0) @binding(1) var sky_sampler : sampler;
@group(0) @binding(2) var sky_texture : texture_cube<f32>;
@group(0) @binding(3) var moon_texture : texture_2d<f32>;
@group(0) @binding(4) var<storage, read> constellations : ConstellationTable;

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) ndc : vec2<f32>,
};
@vertex
fn vs_main(@builtin(vertex_index) index : u32) -> VertexOutput {
    let ndc = vec2<f32>(f32((index << 1u) & 2u), f32(index & 2u)) * 2.0 - 1.0;
    return VertexOutput(vec4<f32>(ndc, 1.0, 1.0), ndc);
}

fn hash3(value : vec3<f32>) -> f32 {
    var p = fract(value * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

fn stars(direction : vec3<f32>) -> vec3<f32> {
    let cells = direction * 220.0;
    let cell = floor(cells);
    let pick = hash3(cell);
    if (pick < 0.98) { return vec3<f32>(0.0); }
    let centre = cell + vec3<f32>(hash3(cell + 17.0), hash3(cell + 43.0), hash3(cell + 71.0));
    let offset = cells - centre;
    let shape = 1.0 - smoothstep(0.0, 0.12, dot(offset, offset));
    let twinkle = 0.85 + 0.15 * sin(sky.parameters.x * 1.5 + pick * 60.0);
    return mix(vec3<f32>(0.7, 0.8, 1.0), vec3<f32>(1.0, 0.9, 0.75), fract(pick * 91.0))
        * shape * twinkle * (0.5 + fract(pick * 37.0) * 2.0);
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let far = sky.inverse_direction_view_projection * vec4<f32>(input.ndc, 1.0, 1.0);
    let direction = normalize(far.xyz / far.w);
    if (sky.sun.w < 0.5) {
        return textureSampleLevel(sky_texture, sky_sampler, direction, 0.0);
    }
    let space = sky.sun.w > 1.5;
    let daylight = select(clamp(sky.parameters.y, 0.0, 1.0), 0.0, space);
    let height = smoothstep(-0.12, 0.85, direction.y);
    let day = mix(vec3<f32>(0.6, 0.75, 0.95), vec3<f32>(0.04, 0.2, 0.48), height);
    let night = mix(vec3<f32>(0.025, 0.03, 0.07), vec3<f32>(0.002, 0.004, 0.018), height);
    var colour = mix(night, day, daylight);
    let sunset = (1.0 - smoothstep(0.0, 0.25, abs(sky.sun.y))) * daylight;
    colour += vec3<f32>(0.45, 0.12, 0.025) * sunset * (1.0 - height);
    let sun_dot = dot(direction, sky.sun.xyz);
    colour += vec3<f32>(1.0, 0.85, 0.55) * smoothstep(0.9996, 0.9998, sun_dot) * 3.0 * daylight;
    let night_amount = (1.0 - daylight) * select(smoothstep(0.0, 0.2, direction.y), 1.0, space);
    if (night_amount > 0.01) {
        // Rotate dated skies with the shared sky clock.
        var star_direction = direction;
        if (dot(sky.celestial_up.xyz, sky.celestial_up.xyz) > 0.5) {
            star_direction = direction.x * sky.celestial_east.xyz + direction.y * sky.celestial_up.xyz
                + direction.z * sky.celestial_north.xyz;
        }
        colour += stars(star_direction) * night_amount;
        if (sky.celestial_north.w > 0.5 && !space) {
            colour += catalogue_stars(direction) * night_amount;
        }
        colour += vec3<f32>(0.7, 0.75, 0.85) * smoothstep(0.9995, 0.9998, dot(direction, sky.moon.xyz))
            * sky.moon.w * night_amount;
    }
    return vec4<f32>(colour, 1.0);
}
