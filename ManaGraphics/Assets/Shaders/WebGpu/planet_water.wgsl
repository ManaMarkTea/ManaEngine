// Inland water on a planet (PlanetWaterMesh, lakes and rivers from the vector tiles): ripples blown by the world wind,
// the sun's glint, the sky in the surface at grazing angles, and shallows and foam by the distance to the shore.
// water.wgsl assumes a level plane with world +Y up; here up is the way out from the planet's centre.
//
// Vertex channels (PlanetWaterMesh owns both sides):
//   uv0     - (east, north) within the Mercator tile. The buffer outside [0, 1) helps sample the shore but isn't drawn.
//   color.r - metres to the nearest shore (interpolated between corners, so only as sharp as the triangles).

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) shore : f32,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_position : vec3<f32>,
    @location(3) world_normal : vec3<f32>,
    @location(4) relative_position : vec3<f32>,
};

#include "include/uniforms.wgsl"
#include "include/camera_relative.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
#include "include/haze.wgsl"
@group(1) @binding(0) var<uniform> object : ObjectUniform;

struct CustomMaterialUniform {
    // Share of the camera distance the water is pulled toward the camera (as draped_line.wgsl's), so it wins the depth
    // test against the ground it lies on.
    //PROPERTY(default=0.0028, min=0.0, max=0.05)
    towardCamera : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    waveStrength : f32,

    // Metres from the shore over which the water goes from shallow to deep.
    //PROPERTY(default=35.0, min=1.0, max=5000.0)
    shoreWidth : f32,

    // Metres of foam along the shore.
    //PROPERTY(default=5.0, min=0.0, max=500.0)
    foamWidth : f32,

    //PROPERTY(default=[0.02,0.07,0.14], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    deepColor : vec3<f32>,

    //PROPERTY(default=[0.04,0.12,0.17], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    shallowColor : vec3<f32>,

    //PROPERTY(default=[0.0,0.0])
    tileIndex : vec2<f32>,
};

@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

fn hash21(p : vec2<f32>) -> f32 {
    var p3 = fract(vec3<f32>(p.x, p.y, p.x) * 0.1031);
    p3 = p3 + dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Web Mercator metres per zoom-9 tile. An integer lattice per tile keeps opposite edges in phase.
const TileMetres : f32 = 78271.516;

fn lattice_hash(p : vec2<f32>) -> f32 {
    return hash21(fract(p / 4096.0) * 4096.0);
}

fn value_noise(p : vec2<f32>, cells : f32) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let wrapped_cells = fract(cells / 4096.0) * 4096.0;
    let origin = fract(custom.tileIndex * wrapped_cells / 4096.0) * 4096.0;
    let a = lattice_hash(i + origin);
    let b = lattice_hash(i + origin + vec2<f32>(1.0, 0.0));
    let c = lattice_hash(i + origin + vec2<f32>(0.0, 1.0));
    let d = lattice_hash(i + origin + vec2<f32>(1.0, 1.0));
    let u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y) * 2.0 - 1.0;
}

fn wave_noise(p : vec2<f32>, cells : f32) -> f32 {
    var value = 0.0;
    var amplitude = 0.5;
    var freq = 1.0;
    for (var i = 0u; i < 3u; i = i + 1u) {
        value = value + value_noise(p * freq, cells * freq) * amplitude;
        freq = freq * 2.0;
        amplitude = amplitude * 0.5;
    }
    return value;
}

// The ripples' slope at a tile coordinate, carried downwind.
fn ripple_slope(p : vec2<f32>, scale : f32, drift : vec2<f32>) -> vec2<f32> {
    let cells = round(TileMetres / scale);
    let q = (p - drift / TileMetres) * cells;
    let e = 0.15;
    let h = wave_noise(q, cells);
    let hx = wave_noise(q + vec2<f32>(e, 0.0), cells);
    let hy = wave_noise(q + vec2<f32>(0.0, e), cells);
    return vec2<f32>(hx - h, hy - h) / e;
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    let world = (object.model * vec4<f32>(position, 1.0)).xyz;
    let relative = mana_globe_relative(object.model, position);
    let lifted = relative * (1.0 - custom.towardCamera);
    var output : VertexOutput;
    output.position = mana_relative_clip_position(lifted);
    output.shore = color.r;
    output.uv0 = uv0;
    output.world_position = world;
    output.relative_position = relative;
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // How many metres a pixel spans, before any branch (uniformity): ripples smaller than a few pixels fade out
    // rather than shimmer.
    let pixel_metres = max(length(fwidth(input.uv0)) * TileMetres, 1e-3);
    if (any(input.uv0 < vec2<f32>(0.0)) || any(input.uv0 >= vec2<f32>(1.0))) {
        discard;
    }

    let up = normalize(input.world_normal);
    var east = cross(vec3<f32>(0.0, 1.0, 0.0), up);
    if (dot(east, east) < 1e-6) {
        east = vec3<f32>(1.0, 0.0, 0.0);
    }
    east = normalize(east);
    let north = cross(up, east);

    // The world wind (ManaWind, world XZ) turned into this spot's east and north.
    let wind_world = vec3<f32>(frame.wind.x, 0.0, frame.wind.y);
    var wind = vec2<f32>(dot(wind_world, east), dot(wind_world, north));
    if (dot(wind, wind) < 1e-6) {
        wind = vec2<f32>(0.0, 1.0);
    }
    wind = normalize(wind);
    let breeze = clamp(frame.wind.z / 4.0, 0.2, 3.0);
    let travel = wind * (frame.wind_motion.z * 0.6 + frame.time * 0.4);

    // Three scales of ripple, each fading out once a pixel covers a good part of it.
    var slope = vec2<f32>(0.0);
    let scales = vec3<f32>(3.0, 14.0, 60.0);
    for (var i = 0u; i < 3u; i = i + 1u) {
        let scale = scales[i];
        let shown = 1.0 - smoothstep(scale * 0.25, scale * 1.5, pixel_metres);
        slope = slope + ripple_slope(input.uv0, scale, travel * (1.0 + f32(i) * 0.5)) * shown * (0.05 + 0.03 * f32(i));
    }
    // Calm in the shallows, as a lake is against its bank.
    let shore = max(input.shore, 0.0);
    let calm = smoothstep(0.0, custom.foamWidth * 2.0 + 1.0, shore);
    slope = slope * custom.waveStrength * breeze * (0.3 + 0.7 * calm);
    let n = normalize(up - east * slope.x - north * slope.y);

    let v = normalize(-input.relative_position);
    let ndotv = saturate(dot(n, v));

    var to_sun = frame.sun_direction.xyz;
    if (dot(to_sun, to_sun) < 1e-6) {
        to_sun = vec3<f32>(0.0, -1.0, 0.0);
    }
    to_sun = normalize(-to_sun);
    let sun_light = frame.sun_color.rgb * frame.sun_color.a;
    // Daylight here, from the sun's height over this spot (the sky's own daylight is for the observer's place).
    let sun_height = dot(up, to_sun);
    let day = smoothstep(-0.1, 0.25, sun_height);

    // Shallow to deep with the distance from the shore.
    let depth = smoothstep(0.0, custom.shoreWidth, shore);
    let base = mix(custom.shallowColor, custom.deepColor, depth);
    let lit = base * (vec3<f32>(0.04) + sun_light * max(sun_height, 0.0) * 0.55 + vec3<f32>(0.18, 0.24, 0.32) * day);

    // The sky in the surface: pale toward the horizon, deeper overhead, dark at night.
    let reflected = reflect(-v, n);
    let elevation = saturate(dot(reflected, up));
    let sky = mix(vec3<f32>(0.55, 0.66, 0.82), vec3<f32>(0.18, 0.34, 0.62), sqrt(elevation)) * (0.02 + 0.6 * day);
    let fresnel = 0.02 + 0.98 * pow(1.0 - ndotv, 5.0);
    var colour = mix(lit, sky, fresnel);

    // The sun's glint, sharper on the small ripples.
    let half_vector = normalize(to_sun + v);
    colour = colour + min(sun_light, vec3<f32>(3.0)) * pow(max(dot(n, half_vector), 0.0), 600.0) * 0.6 * day;

    // Foam along the shore, broken up so it is not a ruled line.
    let foam_cells = round(TileMetres / 9.0);
    let broken = value_noise((input.uv0 + travel * 0.05 / TileMetres) * foam_cells, foam_cells) * 0.5 + 0.5;
    let foam_width = max(custom.foamWidth, 0.001);
    let foam = (1.0 - smoothstep(foam_width * 0.3, foam_width, shore + broken * foam_width * 0.6)) * 0.23;
    colour = mix(colour, vec3<f32>(0.85, 0.9, 0.92) * (0.05 + 0.75 * day), foam * (1.0 - smoothstep(foam_width * 4.0, foam_width * 40.0, pixel_metres)));

    colour = mana_haze(colour, input.world_position);
    return vec4<f32>(colour * frame.irradiance_factors.x, 1.0);
}
