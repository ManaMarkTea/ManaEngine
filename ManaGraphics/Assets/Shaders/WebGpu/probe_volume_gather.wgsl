// Probe volume, step 2 of a bounce: each probe casts rays through the voxels; a ray that hits takes that voxel's
// light, one that leaves the box takes the sky's. What arrives is kept as L1 harmonics per channel. Rays that strike
// the back of a surface mean the probe sits inside a wall: its validity drops, and the pack step fills it in.
#include "include/probe_volume_common.wgsl"

@group(0) @binding(0) var<uniform> params : ProbeParams;
@group(0) @binding(1) var voxel_albedo : texture_3d<f32>;
@group(0) @binding(2) var voxel_normal : texture_3d<f32>;
@group(0) @binding(3) var voxel_radiance : texture_3d<f32>;
@group(0) @binding(4) var environment_map : texture_cube<f32>;
@group(0) @binding(5) var environment_sampler : sampler;
// Four vec4 per probe: red, green and blue harmonics, then validity in x.
@group(0) @binding(6) var<storage, read_write> probes : array<vec4<f32>>;

// Evenly spread directions (a Fibonacci sphere).
fn ray_direction(i : u32, count : u32) -> vec3<f32> {
    let golden = 2.39996323;
    let y = 1.0 - (f32(i) + 0.5) / f32(count) * 2.0;
    let radius = sqrt(max(1.0 - y * y, 0.0));
    let phi = f32(i) * golden;
    return vec3<f32>(cos(phi) * radius, y, sin(phi) * radius);
}

@compute @workgroup_size(4, 4, 4)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let counts = params.probe_counts.xyz;
    if (any(id >= counts)) {
        return;
    }
    let spacing = params.volume_size.w;
    let probe = params.volume_min.xyz + (vec3<f32>(id) + vec3<f32>(0.5)) * spacing;
    let ray_count = max(u32(params.sky.y), 1u);

    var r = vec4<f32>(0.0);
    var g = vec4<f32>(0.0);
    var b = vec4<f32>(0.0);
    var backfaces = 0u;
    for (var i = 0u; i < ray_count; i = i + 1u) {
        let dir = ray_direction(i, ray_count);
        let hit = probe_march(params, voxel_albedo, probe, dir, false);
        var radiance = vec3<f32>(0.0);
        if (hit.w == 1) {
            let cell = hit.xyz;
            let raw_normal = probe_decode_normal(textureLoad(voxel_normal, cell, 0));
            if (length(raw_normal) > 0.3 && dot(normalize(raw_normal), dir) > 0.25) {
                backfaces = backfaces + 1u;
            }
            radiance = textureLoad(voxel_radiance, cell, 0).rgb;
        } else {
            radiance = textureSampleLevel(environment_map, environment_sampler, dir, 0.0).rgb * params.sky.x;
        }
        radiance = probe_clean(radiance);
        let basis = vec4<f32>(PROBE_Y00, PROBE_Y1 * dir.x, PROBE_Y1 * dir.y, PROBE_Y1 * dir.z);
        r = r + radiance.r * basis;
        g = g + radiance.g * basis;
        b = b + radiance.b * basis;
    }

    let weight = 4.0 * PROBE_PI / f32(ray_count);
    let index = (id.x + counts.x * (id.y + counts.y * id.z)) * 4u;
    probes[index] = r * weight;
    probes[index + 1u] = g * weight;
    probes[index + 2u] = b * weight;
    probes[index + 3u] = vec4<f32>(1.0 - f32(backfaces) / f32(ray_count), 0.0, 0.0, 0.0);
}
