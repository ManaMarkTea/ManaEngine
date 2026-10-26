// Probe volume, step 3 of a bounce: writes the probes into the textures the scene samples. A probe buried in a wall
// (low validity) takes the average of its valid neighbours instead, so the dark inside of the wall does not bleed
// onto the surfaces beside it when the probes are blended.
#include "include/probe_volume_common.wgsl"

@group(0) @binding(0) var<uniform> params : ProbeParams;
@group(0) @binding(1) var<storage, read> probes : array<vec4<f32>>;
@group(0) @binding(2) var probe_sh_r : texture_storage_3d<rgba16float, write>;
@group(0) @binding(3) var probe_sh_g : texture_storage_3d<rgba16float, write>;
@group(0) @binding(4) var probe_sh_b : texture_storage_3d<rgba16float, write>;

const VALID : f32 = 0.8;

fn probe_index(cell : vec3<i32>) -> u32 {
    let counts = vec3<i32>(params.probe_counts.xyz);
    return u32(cell.x + counts.x * (cell.y + counts.y * cell.z)) * 4u;
}

@compute @workgroup_size(4, 4, 4)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let counts = vec3<i32>(params.probe_counts.xyz);
    let cell = vec3<i32>(id);
    if (any(cell >= counts)) {
        return;
    }

    let own = probe_index(cell);
    var r = probes[own];
    var g = probes[own + 1u];
    var b = probes[own + 2u];
    if (probes[own + 3u].x < VALID) {
        var sum_r = vec4<f32>(0.0);
        var sum_g = vec4<f32>(0.0);
        var sum_b = vec4<f32>(0.0);
        var found = 0.0;
        for (var z = -1; z <= 1; z = z + 1) {
            for (var y = -1; y <= 1; y = y + 1) {
                for (var x = -1; x <= 1; x = x + 1) {
                    let near = cell + vec3<i32>(x, y, z);
                    if (any(near < vec3<i32>(0)) || any(near >= counts)) {
                        continue;
                    }
                    let index = probe_index(near);
                    if (probes[index + 3u].x >= VALID) {
                        sum_r = sum_r + probes[index];
                        sum_g = sum_g + probes[index + 1u];
                        sum_b = sum_b + probes[index + 2u];
                        found = found + 1.0;
                    }
                }
            }
        }
        if (found > 0.0) {
            r = sum_r / found;
            g = sum_g / found;
            b = sum_b / found;
        }
    }

    textureStore(probe_sh_r, cell, r);
    textureStore(probe_sh_g, cell, g);
    textureStore(probe_sh_b, cell, b);
}
