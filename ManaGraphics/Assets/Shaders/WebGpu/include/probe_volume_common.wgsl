// Shared by the probe volume's compute passes (ManaWebGpuProbeVolume): the box, its voxels and probes, and the
// march through the voxels that both lighting the voxels and gathering the probes use.

struct ProbeParams {
    // xyz the box's low corner, w the voxel size in metres.
    volume_min : vec4<f32>,
    // xyz the box's size, w the probe spacing in metres.
    volume_size : vec4<f32>,
    // xyz voxels along each axis, w the placed lights.
    voxel_counts : vec4<u32>,
    // xyz probes along each axis, w which bounce this is (0 = the first).
    probe_counts : vec4<u32>,
    // xyz the direction sunlight travels, w 1 when there is a sun.
    sun_direction : vec4<f32>,
    // rgb the sun's colour times its intensity.
    sun_color : vec4<f32>,
    // x the sky's (IBL) intensity, y rays per probe.
    sky : vec4<f32>,
};

const PROBE_PI : f32 = 3.14159265;
const PROBE_Y00 : f32 = 0.282095;
const PROBE_Y1 : f32 = 0.488603;

fn probe_voxel_counts(params : ProbeParams) -> vec3<i32> {
    return vec3<i32>(params.voxel_counts.xyz);
}

// The voxel a world point lies in (may be outside the grid).
fn probe_voxel_of(params : ProbeParams, world : vec3<f32>) -> vec3<i32> {
    return vec3<i32>(floor((world - params.volume_min.xyz) / params.volume_min.w));
}

fn probe_inside(params : ProbeParams, cell : vec3<i32>) -> bool {
    let counts = probe_voxel_counts(params);
    return all(cell >= vec3<i32>(0)) && all(cell < counts);
}

// Steps voxel by voxel from origin along dir (Amanatides-Woo) until a voxel with surface in it, or out of the box.
// Returns the voxel hit and 1 in w, or w 0 for a miss. `skip_first` ignores the starting voxel (the surface itself).
fn probe_march(params : ProbeParams, occupancy : texture_3d<f32>, origin : vec3<f32>, dir : vec3<f32>, skip_first : bool) -> vec4<i32> {
    let size = params.volume_min.w;
    let local = (origin - params.volume_min.xyz) / size;
    var cell = vec3<i32>(floor(local));
    let step = vec3<i32>(sign(dir));
    let safe = select(dir, vec3<f32>(1e-6), abs(dir) < vec3<f32>(1e-6));
    let delta = abs(1.0 / safe);
    let next_boundary = vec3<f32>(cell) + max(vec3<f32>(step), vec3<f32>(0.0));
    var t_max = (next_boundary - local) / safe;
    let counts = probe_voxel_counts(params);
    let limit = counts.x + counts.y + counts.z;
    var first = skip_first;
    for (var i = 0; i < limit; i = i + 1) {
        if (!probe_inside(params, cell)) {
            return vec4<i32>(cell, 0);
        }
        if (!first && textureLoad(occupancy, cell, 0).a > 0.5) {
            return vec4<i32>(cell, 1);
        }
        first = false;
        if (t_max.x < t_max.y && t_max.x < t_max.z) {
            cell.x = cell.x + step.x;
            t_max.x = t_max.x + delta.x;
        } else if (t_max.y < t_max.z) {
            cell.y = cell.y + step.y;
            t_max.y = t_max.y + delta.y;
        } else {
            cell.z = cell.z + step.z;
            t_max.z = t_max.z + delta.z;
        }
    }
    return vec4<i32>(cell, 0);
}

// The probes' light (E / pi, as the irradiance cube holds it) arriving at a surface facing n, from L1 harmonics kept
// per channel as (constant, x, y, z).
fn probe_eval(r : vec4<f32>, g : vec4<f32>, b : vec4<f32>, n : vec3<f32>) -> vec3<f32> {
    let basis = vec4<f32>(PROBE_Y00, (2.0 / 3.0) * PROBE_Y1 * n.x, (2.0 / 3.0) * PROBE_Y1 * n.y, (2.0 / 3.0) * PROBE_Y1 * n.z);
    return max(vec3<f32>(dot(r, basis), dot(g, basis), dot(b, basis)), vec3<f32>(0.0));
}

// Where a world point falls in the probe textures (probe i at the middle of cell i).
fn probe_uvw(params : ProbeParams, world : vec3<f32>) -> vec3<f32> {
    let extent = vec3<f32>(params.probe_counts.xyz) * params.volume_size.w;
    return (world - params.volume_min.xyz) / extent;
}

// Light as the probes may carry it: finite, not negative, and short of what a half float holds once summed.
fn probe_clean(light : vec3<f32>) -> vec3<f32> {
    let finite = select(light, vec3<f32>(0.0), light != light || abs(light) > vec3<f32>(1e4));
    return clamp(finite, vec3<f32>(0.0), vec3<f32>(64.0));
}

fn probe_decode_normal(raw : vec4<f32>) -> vec3<f32> {
    return raw.xyz * 2.0 - vec3<f32>(1.0);
}
