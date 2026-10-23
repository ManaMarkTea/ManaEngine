// Shared by mesh and grid lighting. A smooth finite range over inverse-square falloff.
fn mana_point_light_attenuation(distance_to_light : f32, light_range : f32) -> f32 {
    if (light_range <= 0.0) {
        return 0.0;
    }
    let distance = max(distance_to_light, 0.05);
    let fade = clamp(1.0 - pow(distance / light_range, 4.0), 0.0, 1.0);
    return fade * fade / (distance * distance);
}

// Placed point lights, clustered (ManaLightClusters, ManaWebGpuLightClusters): every light once, the view's cells, and
// each cell's lights as indices -- so a pixel loops over only the lights whose range reaches it.
struct ManaPointLight {
    // xyz position, w range.
    position_range : vec4<f32>,
    // rgb colour times intensity.
    color : vec4<f32>,
};

@group(0) @binding(3) var<storage, read> mana_point_lights : array<ManaPointLight>;
@group(0) @binding(4) var<storage, read> mana_light_cells : array<vec2<u32>>;
@group(0) @binding(5) var<storage, read> mana_light_indices : array<u32>;

// The lights that can reach a pixel: where they start in mana_light_indices, and how many. frag_position is the
// fragment's @builtin(position) (framebuffer pixels from the top left); the cell is found as ManaLightClusters.CellOf does.
fn mana_light_cell(frag_position : vec4<f32>, world_position : vec3<f32>) -> vec2<u32> {
    let grid = frame.cluster_grid;
    // w is the light count: with none, the binning did not run and the cells are stale.
    if (grid.x < 1.0 || grid.w < 0.5) {
        return vec2<u32>(0u, 0u);
    }
    let tile = clamp(floor(frag_position.xy / frame.cluster_depth.zw * grid.xy), vec2<f32>(0.0), grid.xy - vec2<f32>(1.0));
    let slice = mana_light_slice(world_position);
    return mana_light_cells[u32(tile.x + tile.y * grid.x + slice * grid.x * grid.y)];
}

// The depth slice a point falls in (0 = nearest), as ManaLightClusters slices the view.
fn mana_light_slice(world_position : vec3<f32>) -> f32 {
    let depth = dot(world_position - frame.camera_position.xyz, frame.camera_forward.xyz);
    let near = max(frame.cluster_depth.x, 0.0001);
    return clamp(floor(log(max(depth, near) / near) * frame.cluster_depth.y), 0.0, max(frame.cluster_grid.z - 1.0, 0.0));
}

// The light-count debug view's colours: dark blue for none, then blue, green, yellow and red by 32 lights.
fn mana_light_count_heat(count : u32) -> vec3<f32> {
    if (count == 0u) {
        return vec3<f32>(0.02, 0.02, 0.08);
    }
    let t = clamp(f32(count) / 32.0, 0.0, 1.0);
    if (t < 0.33) {
        return mix(vec3<f32>(0.0, 0.1, 0.9), vec3<f32>(0.0, 0.85, 0.3), t / 0.33);
    }
    if (t < 0.66) {
        return mix(vec3<f32>(0.0, 0.85, 0.3), vec3<f32>(1.0, 0.9, 0.0), (t - 0.33) / 0.33);
    }
    return mix(vec3<f32>(1.0, 0.9, 0.0), vec3<f32>(1.0, 0.05, 0.0), (t - 0.66) / 0.34);
}

// 1 on a light tile's border, for the debug views to draw the grid over the picture.
fn mana_light_tile_edge(frag_position : vec4<f32>) -> f32 {
    let grid = max(frame.cluster_grid.xy, vec2<f32>(1.0));
    let tile_size = frame.cluster_depth.zw / grid;
    let inside = frag_position.xy - floor(frag_position.xy / tile_size) * tile_size;
    return select(0.0, 1.0, inside.x < 1.0 || inside.y < 1.0);
}

// The n-th light of a cell (from mana_light_cell).
fn mana_cell_light(cell : vec2<u32>, n : u32) -> ManaPointLight {
    return mana_point_lights[mana_light_indices[cell.x + n]];
}
