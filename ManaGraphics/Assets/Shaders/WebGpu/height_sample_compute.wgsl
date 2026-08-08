// Samples elevation for a batch of (lon, lat) points against a small set of pre-uploaded height tiles,
// on the GPU -- replaces PlanetPatchMesh.GenerateData()'s per-vertex CPU loop
// (GeoTiffLibrary.FindCurrentTile + IHeightTile.GetHeightAtLatLong) for the Terrarium up-res/split path.
//
// Mirrors GeoTiffLibrary.FindCurrentTile's own "first bounding-box match wins" semantics: tiles is
// expected newest-first, and the first tile whose bounds contain a point wins for that point -- no
// blending/averaging across overlapping tiles, matching the CPU behavior exactly.
//
// Reproduces TerrariumHeightTile.GetHeightAtLatLong's Web Mercator reprojection (see
// ManaGeoTools/Code/GeoTiff/Terrarium/SlippyMapMath.cs's LatLongToTileCoords) rather than a naive
// linear lon/lat interpolation -- Terrarium tiles are Web Mercator projected, so a linear lerp would
// introduce a real sampling error/seam versus the CPU path's output. Nearest-pixel sampling (matching
// the CPU code's own nearest-pixel indexing, not bilinear) -- same fidelity, not a regression.

// Tile bounds arrive PRE-PROJECTED into the same normalized Web Mercator space as the points (see
// ManaWebGpuHeightSampleCompute's MercatorXy helper, called once per tile on the CPU before upload) --
// NOT raw lon/lat degrees. Originally this shader called the lon/lat->Mercator projection (asinh+tan,
// both real transcendental-function cost) once per (point, tile) PAIR inside the loop below -- for a
// large patch (up to ~1M points) against a large tile list (up to ~1024), that's up to ~2 BILLION
// transcendental calls in a single dispatch, which hung the GPU/driver in testing. Projecting each
// point once (O(points)) and each tile once on the CPU (O(tiles), negligible) instead reduces the
// per-(point,tile) cost inside the loop to plain comparisons/arithmetic.
struct TileDescriptor {
    merc_x_min: f32,
    merc_x_max: f32,
    merc_y_min: f32,
    merc_y_max: f32,
    pixel_offset: u32,
    width: u32,
    height: u32,
    _pad: u32,
}

@group(0) @binding(0) var<storage, read> points: array<vec2<f32>>;          // (lon, lat) degrees
@group(0) @binding(1) var<storage, read> tiles: array<TileDescriptor>;      // newest-first
@group(0) @binding(2) var<storage, read> height_data: array<f32>;          // concatenated tile pixels
@group(0) @binding(3) var<storage, read_write> out_heights: array<f32>;
@group(0) @binding(4) var<uniform> tile_count: u32;

const PI: f32 = 3.14159265358979;
const MAX_LATITUDE_DEGREES: f32 = 85.0511287798;

// Web Mercator normalized [0,1] coordinates at zoom 0 -- same formula as SlippyMapMath.LatLongToTileCoords
// divided by n=2^zoom, so it's independent of any particular tile's zoom level. Called once per POINT
// only -- tile bounds are already pre-projected (see TileDescriptor's own comment above).
fn mercator_xy(lon: f32, lat: f32) -> vec2<f32> {
    let lat_clamped = clamp(lat, -MAX_LATITUDE_DEGREES, MAX_LATITUDE_DEGREES);
    let lat_rad = lat_clamped * PI / 180.0;
    let x = (lon + 180.0) / 360.0;
    let y = (1.0 - asinh(tan(lat_rad)) / PI) / 2.0;
    return vec2<f32>(x, y);
}

@compute @workgroup_size(64)
fn cs_main(@builtin(global_invocation_id) id: vec3<u32>) {
    let i = id.x;
    if (i >= arrayLength(&points)) {
        return;
    }

    let point = points[i];
    let pointMerc = mercator_xy(point.x, point.y);

    var height: f32 = 0.0;
    for (var t: u32 = 0u; t < tile_count; t = t + 1u) {
        let tile = tiles[t];

        if (pointMerc.x < tile.merc_x_min || pointMerc.x > tile.merc_x_max ||
            pointMerc.y < tile.merc_y_min || pointMerc.y > tile.merc_y_max) {
            continue;
        }

        let u = (pointMerc.x - tile.merc_x_min) / max(tile.merc_x_max - tile.merc_x_min, 1e-12);
        let v = (pointMerc.y - tile.merc_y_min) / max(tile.merc_y_max - tile.merc_y_min, 1e-12);

        let px = min(u32(u * f32(tile.width)), tile.width - 1u);
        let py = min(u32(v * f32(tile.height)), tile.height - 1u);

        height = height_data[tile.pixel_offset + py * tile.width + px];
        break;
    }

    out_heights[i] = height;
}
