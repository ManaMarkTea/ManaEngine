#version 430

// Samples elevation for a batch of (lon, lat) points against a small set of pre-uploaded height tiles,
// on the GPU -- OpenGL counterpart to ManaGraphics/Assets/Shaders/WebGpu/height_sample_compute.wgsl. Keep
// the two in exact algorithmic sync (see that file's own header comment for the full rationale) -- any
// divergence here would show up as a backend-dependent difference in generated terrain.

layout(local_size_x = 64) in;

const float PI = 3.14159265358979;
const float MAX_LATITUDE_DEGREES = 85.0511287798;

// Tile bounds arrive PRE-PROJECTED into the same normalized Web Mercator space as the points (see
// ManaOpenGlHeightSampleCompute's MercatorXy helper, called once per tile on the CPU before upload) --
// NOT raw lon/lat degrees. Originally this shader called the lon/lat->Mercator projection (asinh+tan,
// both real transcendental-function cost) once per (point, tile) PAIR inside the loop below -- for a
// large patch (up to ~1M points) against a large tile list (up to ~1024), that's up to ~2 BILLION
// transcendental calls in a single dispatch, which hung the GPU/driver in testing. Projecting each
// point once (O(points)) and each tile once on the CPU (O(tiles), negligible) instead reduces the
// per-(point,tile) cost inside the loop to plain comparisons/arithmetic.
struct TileDescriptor {
    float mercXMin;
    float mercXMax;
    float mercYMin;
    float mercYMax;
    uint pixelOffset;
    uint width;
    uint height;
    uint _pad;
};

layout(std430, binding = 0) readonly buffer PointsBuffer {
    vec2 points[];
};

layout(std430, binding = 1) readonly buffer TilesBuffer {
    TileDescriptor tiles[];
};

layout(std430, binding = 2) readonly buffer HeightDataBuffer {
    float heightData[];
};

layout(std430, binding = 3) writeonly buffer OutHeightsBuffer {
    float outHeights[];
};

layout(location = 0) uniform uint tileCount;

// Web Mercator normalized [0,1] coordinates at zoom 0 -- see the WGSL shader's own comment for why this
// is independent of any particular tile's zoom level. Called once per POINT only -- tile bounds are
// already pre-projected (see TileDescriptor's own comment above).
vec2 mercatorXy(float lon, float lat) {
    float latClamped = clamp(lat, -MAX_LATITUDE_DEGREES, MAX_LATITUDE_DEGREES);
    float latRad = latClamped * PI / 180.0;
    float x = (lon + 180.0) / 360.0;
    float y = (1.0 - asinh(tan(latRad)) / PI) / 2.0;
    return vec2(x, y);
}

void main() {
    uint i = gl_GlobalInvocationID.x;
    if (i >= points.length()) {
        return;
    }

    vec2 point = points[i];
    vec2 pointMerc = mercatorXy(point.x, point.y);

    float height = 0.0;
    for (uint t = 0u; t < tileCount; t++) {
        TileDescriptor tile = tiles[t];

        if (pointMerc.x < tile.mercXMin || pointMerc.x > tile.mercXMax ||
            pointMerc.y < tile.mercYMin || pointMerc.y > tile.mercYMax) {
            continue;
        }

        float u = (pointMerc.x - tile.mercXMin) / max(tile.mercXMax - tile.mercXMin, 1e-12);
        float v = (pointMerc.y - tile.mercYMin) / max(tile.mercYMax - tile.mercYMin, 1e-12);

        uint px = min(uint(u * float(tile.width)), tile.width - 1u);
        uint py = min(uint(v * float(tile.height)), tile.height - 1u);

        height = heightData[tile.pixelOffset + py * tile.width + px];
        break;
    }

    outHeights[i] = height;
}
