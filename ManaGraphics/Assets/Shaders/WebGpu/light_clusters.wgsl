// Bins the placed lights into the view's cells (ManaLightClusters; the CPU's Build is this, step for step). One
// workgroup a cell: its 64 threads test the lights between them against the cell's box, gather the hits in workgroup
// memory, then reserve room in the index list with one atomic and copy them there.

const TILES_X : u32 = 16u;
const TILES_Y : u32 = 9u;
const SLICES : u32 = 24u;
const MAX_INDICES : u32 = 524288u;
const MAX_PER_CELL : u32 = 256u;

struct Params {
    view : mat4x4<f32>,
    inverse_projection : mat4x4<f32>,
    // x first slice's start, y slices per log-unit of depth, z light count.
    depth : vec4<f32>,
};

struct PointLight {
    position_range : vec4<f32>,
    color : vec4<f32>,
};

@group(0) @binding(0) var<uniform> params : Params;
@group(0) @binding(1) var<storage, read> lights : array<PointLight>;
@group(0) @binding(2) var<storage, read_write> cells : array<vec2<u32>>;
@group(0) @binding(3) var<storage, read_write> indices : array<u32>;
@group(0) @binding(4) var<storage, read_write> used : atomic<u32>;

var<workgroup> found : array<u32, MAX_PER_CELL>;
var<workgroup> found_count : atomic<u32>;
var<workgroup> start : u32;
var<workgroup> kept : u32;

// The view-space point at a depth along a pixel's ray: the line through two points the ray passes, cut at z = -depth.
fn view_point(ndc : vec2<f32>, depth : f32) -> vec3<f32> {
    let a4 = params.inverse_projection * vec4<f32>(ndc, 0.25, 1.0);
    let b4 = params.inverse_projection * vec4<f32>(ndc, 0.75, 1.0);
    let a = a4.xyz / a4.w;
    let b = b4.xyz / b4.w;
    let t = (-depth - a.z) / (b.z - a.z);
    return a + (b - a) * t;
}

@compute @workgroup_size(64)
fn cs_main(@builtin(workgroup_id) group : vec3<u32>, @builtin(local_invocation_index) local : u32) {
    if (local == 0u) {
        atomicStore(&found_count, 0u);
    }

    // The cell's box: its tile's corner rays cut at the slice's near and far depths; the last slice runs on for ever.
    let near = params.depth.x;
    let scale = params.depth.y;
    var depth0 = 0.0;
    if (group.z > 0u) {
        depth0 = near * exp(f32(group.z) / scale);
    }
    var depth1 = 1e7;
    if (group.z < SLICES - 1u) {
        depth1 = near * exp(f32(group.z + 1u) / scale);
    }
    let left = -1.0 + 2.0 * f32(group.x) / f32(TILES_X);
    let right = -1.0 + 2.0 * f32(group.x + 1u) / f32(TILES_X);
    let top = 1.0 - 2.0 * f32(group.y) / f32(TILES_Y);
    let bottom = 1.0 - 2.0 * f32(group.y + 1u) / f32(TILES_Y);
    var box_min = vec3<f32>(3.0e38);
    var box_max = vec3<f32>(-3.0e38);
    for (var corner = 0u; corner < 8u; corner = corner + 1u) {
        var ndc = vec2<f32>(left, top);
        if ((corner & 1u) != 0u) {
            ndc.x = right;
        }
        if ((corner & 2u) != 0u) {
            ndc.y = bottom;
        }
        var depth = depth0;
        if ((corner & 4u) != 0u) {
            depth = depth1;
        }
        let point = view_point(ndc, depth);
        box_min = min(box_min, point);
        box_max = max(box_max, point);
    }
    workgroupBarrier();

    let light_count = u32(params.depth.z);
    for (var i = local; i < light_count; i = i + 64u) {
        let light = lights[i];
        let radius = light.position_range.w;
        let centre = (params.view * vec4<f32>(light.position_range.xyz, 1.0)).xyz;
        let gap = clamp(centre, box_min, box_max) - centre;
        if (radius > 0.0 && dot(gap, gap) <= radius * radius) {
            let slot = atomicAdd(&found_count, 1u);
            if (slot < MAX_PER_CELL) {
                found[slot] = i;
            }
        }
    }
    workgroupBarrier();

    let cell = group.x + group.y * TILES_X + group.z * TILES_X * TILES_Y;
    if (local == 0u) {
        let count = min(atomicLoad(&found_count), MAX_PER_CELL);
        var first = 0u;
        var room = 0u;
        if (count > 0u) {
            first = atomicAdd(&used, count);
            if (first < MAX_INDICES) {
                room = min(count, MAX_INDICES - first);
            }
        }
        start = first;
        kept = room;
        cells[cell] = vec2<u32>(first, room);
    }
    workgroupBarrier();

    // Threads gathered hits in no set order: sorted here, so a cell's lights add up the same way every frame.
    let n = kept;
    for (var k = local; k < n; k = k + 64u) {
        let value = found[k];
        var rank = 0u;
        for (var j = 0u; j < n; j = j + 1u) {
            let other = found[j];
            if (other < value) {
                rank = rank + 1u;
            }
        }
        indices[start + rank] = value;
    }
}
