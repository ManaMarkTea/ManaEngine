// Probe volume, step 1 of a bounce: the light each voxel sends back -- its albedo times the sun (shadowed by marching
// the voxels toward it), the placed lights, and, after the first bounce, the probes' own light -- plus what it emits.
#include "include/probe_volume_common.wgsl"

struct ProbeLight {
    position_range : vec4<f32>,
    color : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : ProbeParams;
@group(0) @binding(1) var voxel_albedo : texture_3d<f32>;
@group(0) @binding(2) var voxel_normal : texture_3d<f32>;
@group(0) @binding(3) var voxel_emission : texture_3d<f32>;
@group(0) @binding(4) var<storage, read> lights : array<ProbeLight>;
@group(0) @binding(5) var probe_sh_r : texture_3d<f32>;
@group(0) @binding(6) var probe_sh_g : texture_3d<f32>;
@group(0) @binding(7) var probe_sh_b : texture_3d<f32>;
@group(0) @binding(8) var probe_sampler : sampler;
@group(0) @binding(9) var voxel_radiance : texture_storage_3d<rgba16float, write>;

fn attenuation(distance_to_light : f32, light_range : f32) -> f32 {
    if (light_range <= 0.0) {
        return 0.0;
    }
    let distance = max(distance_to_light, 0.05);
    let fade = clamp(1.0 - pow(distance / light_range, 4.0), 0.0, 1.0);
    return fade * fade / (distance * distance);
}

@compute @workgroup_size(4, 4, 4)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let counts = params.voxel_counts.xyz;
    if (any(id >= counts)) {
        return;
    }
    let cell = vec3<i32>(id);
    let albedo = textureLoad(voxel_albedo, cell, 0);
    if (albedo.a < 0.5) {
        textureStore(voxel_radiance, cell, vec4<f32>(0.0));
        return;
    }

    let size = params.volume_min.w;
    let centre = params.volume_min.xyz + (vec3<f32>(cell) + vec3<f32>(0.5)) * size;
    let raw_normal = probe_decode_normal(textureLoad(voxel_normal, cell, 0));
    // A short normal is a wall seen from both sides: lit from whichever side the light is on.
    let two_sided = length(raw_normal) < 0.3;
    var n = vec3<f32>(0.0, 1.0, 0.0);
    if (!two_sided) {
        n = normalize(raw_normal);
    }

    var irradiance = vec3<f32>(0.0);
    if (params.sun_direction.w > 0.5) {
        let to_sun = -normalize(params.sun_direction.xyz);
        var ndotl = max(dot(n, to_sun), 0.0);
        if (two_sided) {
            ndotl = 0.7;
        }
        if (ndotl > 0.0) {
            // Out of the voxel toward the sun; anything in the way shadows it.
            // From a voxel out of the surface: half a voxel toward the sun stayed inside a thick wall and shadowed itself.
            var out_of_surface = n;
            if (two_sided) {
                out_of_surface = to_sun;
            }
            let hit = probe_march(params, voxel_albedo, centre + out_of_surface * size * 1.01 + to_sun * size * 0.5, to_sun, false);
            if (hit.w == 0) {
                irradiance = irradiance + params.sun_color.rgb * ndotl;
            }
        }
    }

    for (var i = 0u; i < params.voxel_counts.w; i = i + 1u) {
        let light = lights[i];
        let to_light = light.position_range.xyz - centre;
        let distance = length(to_light);
        let fall = attenuation(distance, light.position_range.w);
        if (fall <= 0.0) {
            continue;
        }
        var ndotl = max(dot(n, to_light / max(distance, 0.001)), 0.0);
        if (two_sided) {
            ndotl = 0.7;
        }
        irradiance = irradiance + light.color.rgb * fall * ndotl;
    }

    // E / pi from the sun and lamps, plus the previous bounce's probes (already E / pi).
    var reflected = irradiance / PROBE_PI;
    if (params.probe_counts.w > 0u) {
        let uvw = probe_uvw(params, centre + n * params.volume_size.w * 0.5);
        let r = textureSampleLevel(probe_sh_r, probe_sampler, uvw, 0.0);
        let g = textureSampleLevel(probe_sh_g, probe_sampler, uvw, 0.0);
        let b = textureSampleLevel(probe_sh_b, probe_sampler, uvw, 0.0);
        reflected = reflected + probe_eval(r, g, b, n);
    }

    let emission = textureLoad(voxel_emission, cell, 0).rgb;
    textureStore(voxel_radiance, cell, vec4<f32>(probe_clean(albedo.rgb * reflected + emission), 1.0));
}
