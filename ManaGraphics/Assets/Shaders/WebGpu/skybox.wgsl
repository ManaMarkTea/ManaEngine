// The environment drawn as a background, behind every mesh: the environment image (EnvironmentMapResource,
// the cube the reflections use), or a sky computed per pixel -- a scattering atmosphere with clouds, sun, moon
// and stars, or deep space. See ManaSky / ManaIrradianceSettings.Sky; the image is only 512 pixels a face and
// shows as a smear behind the scene, where a computed sky stays sharp at any resolution.
//
// The atmosphere is single scattering marched through a thin shell of air around a small planet, after
// Sebastian Lague's Solar System (Atmosphere.shader):
//
//   Copyright (c) 2020 Sebastian Lague. MIT License -- https://github.com/SebLague/Solar-System
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
//   associated documentation files (the "Software"), to deal in the Software without restriction, including
//   without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//   copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
//   following conditions: The above copyright notice and this permission notice shall be included in all
//   copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
//   KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
//   PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
//   ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Differences from the original: a thin, Earth-like shell seen from the ground; Earth's Rayleigh colour
// balance, ozone absorption, and aerosol (Mie) scattering with phase functions, which is what makes a sunset
// orange; the view ray's optical depth accumulates as it marches rather than being read from a baked
// texture; points the planet shadows from the sun scatter nothing (so night is dark), softened at the shadow's
// edge; and the sunlight reaching each point also tints the sun, the clouds and the ground.
//
// No vertex buffer: a full-screen triangle. Depth write is off and the compare is Always in the pipeline that
// uses this, so the sky draws first and never blocks or is blocked by anything drawn after it.

struct SkyboxUniform {
    // View with its translation zeroed, then projected and inverted -- so unprojecting a screen position
    // through this yields a direction, not a point that would still need the camera's position subtracted.
    inverse_direction_view_projection : mat4x4<f32>,
    // xyz = direction towards the sun; w = 0 the environment image, 1 a day sky, 2 space.
    sun : vec4<f32>,
    // xyz = direction towards the moon; w = 1 when the dated sky clock has a moon.
    moon : vec4<f32>,
    // x = seconds, for the stars' twinkle; y = daylight, 0..1; z = how far the clouds have drifted (ManaSky).
    parameters : vec4<f32>,
    // Rows taking an equatorial direction to east, up, north (xyz); celestial_east.w is how strongly the
    // constellation lines show. All zero when the sky has no date to turn the stars by.
    celestial_east : vec4<f32>,
    celestial_up : vec4<f32>,
    celestial_north : vec4<f32>,
};

#include "include/constellations.wgsl"
#include "include/star_catalogue.wgsl"


@group(0) @binding(0) var<uniform> sky : SkyboxUniform;
@group(0) @binding(1) var sky_sampler : sampler;
@group(0) @binding(2) var sky_texture : texture_cube<f32>;
// The moon's map, equirectangular with the near side at the centre: NASA's CGI Moon Kit (LRO colour, public domain).
@group(0) @binding(3) var moon_texture : texture_2d<f32>;
@group(0) @binding(4) var<storage, read> constellations : ConstellationTable;

// The moon's angular radius, as the cosine of it -- about 1.5 degrees, larger than life so it reads on screen.
const MoonCosRadius : f32 = 0.99965;
const HorizonMoonCosRadius : f32 = 0.99939;

/// The moon's colour in this direction, or nothing off its disc. The disc is a sphere facing the viewer: each
/// point is mapped to the latitude and longitude it shows, so the maria sit where they should.
fn moon_disc(direction : vec3<f32>, toward_moon : vec3<f32>, toward_sun : vec3<f32>, cos_radius : f32) -> vec4<f32> {
    let cos_angle = dot(direction, toward_moon);
    if (cos_angle < cos_radius - 0.0002) {
        return vec4<f32>(0.0);
    }

    // A frame on the disc with its north kept up, then the point's place on the unit circle across it.
    var north = vec3<f32>(0.0, 1.0, 0.0);
    if (abs(toward_moon.y) > 0.99) {
        north = vec3<f32>(0.0, 0.0, 1.0);
    }
    let right = normalize(cross(north, toward_moon));
    let up = cross(toward_moon, right);
    let sin_radius = sqrt(1.0 - cos_radius * cos_radius);
    let p = vec2<f32>(dot(direction, right), dot(direction, up)) / sin_radius;
    let r2 = dot(p, p);
    if (r2 >= 1.0) {
        return vec4<f32>(0.0);
    }

    // The visible hemisphere's point: x across, y up, z towards the viewer.
    let n = vec3<f32>(p.x, p.y, sqrt(1.0 - r2));
    let longitude = atan2(n.x, n.z);
    let latitude = asin(clamp(n.y, -1.0, 1.0));
    let uv = vec2<f32>(0.5 + longitude / 6.2831853, 0.5 - latitude / 3.14159265);
    let albedo = textureSampleLevel(moon_texture, sky_sampler, uv, 0.0).rgb;
    let air_length = ray_sphere(vec3<f32>(0.0), AtmosphereRadius, Eye, direction).y;
    let air = transmittance(optical_depth(Eye, direction, air_length));
    let surface_normal = right * n.x + up * n.y - toward_moon * n.z;
    let sunlit = dot(surface_normal, toward_sun);

    // Keep the unlit hemisphere transparent so stars and sky remain visible through a crescent.
    let limb = mix(0.8, 1.0, pow(n.z, 0.4));
    let edge = 1.0 - smoothstep(0.96, 1.0, sqrt(r2));
    return vec4<f32>(albedo * limb * air, edge * smoothstep(-0.015, 0.015, sunlit));
}

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) ndc : vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) vertex_index : u32) -> VertexOutput {
    let uv = vec2<f32>(f32((vertex_index << 1u) & 2u), f32(vertex_index & 2u));
    let ndc = uv * 2.0 - 1.0;

    var output : VertexOutput;
    output.ndc = ndc;
    // z = 1.0 (the far plane in WebGPU's [0,1] depth range) and w = 1.0: no real projection, so no divide.
    output.clip_position = vec4<f32>(ndc, 1.0, 1.0);
    return output;
}

// ---- The atmosphere --------------------------------------------------------------------------------------

// A planet of radius 1 under air a fiftieth as deep: thin, as Earth's is seen from the ground (Lague's
// half-a-planet shell is for looking at a planet from space, and from the surface reads cyan and murky).
// Thin is also what reddens a sunset: sunlight grazing the horizon crosses many times more air than it does
// from overhead, and at a twentieth the horizon only ever went yellow.
const PlanetRadius : f32 = 1.0;
const AtmosphereRadius : f32 = 1.02;
const DensityFalloff : f32 = 5.0;
// Aerosols -- haze and dust -- sit lower than the air and fall off faster.
const MieFalloff : f32 = 14.0;
const InScatterPoints : i32 = 16;
const OpticalDepthPoints : i32 = 8;
// Rayleigh: strength goes as (400 / wavelength)^4, here for 680, 550 and 440 nm -- the ratio of Earth's own
// coefficients (Lague's 700/530/460 weights green heavily, fine from space, greenish from below) -- times 100,
// so the air overhead holds as much as it would if deeper and thinner.
const Scattering : vec3<f32> = vec3<f32>(11.98, 27.97, 68.30);
// Ozone absorbs without scattering -- mostly orange and green. Bruneton's ozone coefficients in the same
// units, somewhat strengthened; it only changes light that crosses a lot of air, so noon is untouched.
const Ozone : vec3<f32> = vec3<f32>(0.54, 1.55, 0.07) * 7.0;
// Mie (aerosol) scattering is grey: it scatters sunlight in whatever colour the sunlight has become, and
// mostly forwards. So it is what paints the sky around a low sun orange rather than the yellow Rayleigh
// alone gives, and puts the bright haze round the sun at noon. Absorbs a little too (the 1.1).
const MieScattering : f32 = 1.25;
const MieAnisotropy : f32 = 0.76;
// The camera stands just above the surface.
const Eye : vec3<f32> = vec3<f32>(0.0, PlanetRadius + 0.0002, 0.0);

// Distance to the sphere and distance through it, or (big, 0) for a miss.
fn ray_sphere(centre : vec3<f32>, radius : f32, origin : vec3<f32>, direction : vec3<f32>) -> vec2<f32> {
    let offset = origin - centre;
    let b = 2.0 * dot(offset, direction);
    let c = dot(offset, offset) - radius * radius;
    let d = b * b - 4.0 * c;
    if (d > 0.0) {
        let s = sqrt(d);
        let near = max(0.0, (-b - s) * 0.5);
        let far = (-b + s) * 0.5;
        if (far >= 0.0) {
            return vec2<f32>(near, far - near);
        }
    }
    return vec2<f32>(1e9, 0.0);
}

// Air (x) and aerosol (y) density at a point.
fn density_at(point : vec3<f32>) -> vec2<f32> {
    let height01 = (length(point) - PlanetRadius) / (AtmosphereRadius - PlanetRadius);
    return vec2<f32>(exp(-height01 * DensityFalloff), exp(-height01 * MieFalloff)) * (1.0 - height01);
}

fn optical_depth(origin : vec3<f32>, direction : vec3<f32>, ray_length : f32) -> vec2<f32> {
    let step = ray_length / f32(OpticalDepthPoints - 1);
    var point = origin;
    var depth = vec2<f32>(0.0);
    for (var i = 0; i < OpticalDepthPoints; i = i + 1) {
        depth = depth + density_at(point) * step;
        point = point + direction * step;
    }
    return depth;
}

// What an optical depth of air and aerosol leaves of each colour.
fn transmittance(depth : vec2<f32>) -> vec3<f32> {
    return exp(-(depth.x * (Scattering + Ozone) + vec3<f32>(depth.y * MieScattering * 1.1)));
}

// How much of the sun a point sees, 0..1: whole unless the planet is in the way, fading over a thin band
// either side of the edge of its shadow. A hard in-or-out test here flips whole march samples at once as
// the sun sets, and each flip drew a hard edge across the dusk sky.
fn sun_visibility(point : vec3<f32>, toward_sun : vec3<f32>) -> f32 {
    let along = dot(point, toward_sun);
    if (along >= 0.0) {
        return 1.0;
    }
    let closest = length(point - toward_sun * along);
    return smoothstep(PlanetRadius - 0.0025, PlanetRadius + 0.0025, closest);
}

// The light scattered towards the eye along a view ray. `jitter`, 0..1 per pixel, shifts where the samples
// fall so what is left of their steps dissolves into noise rather than lining up into bands.
fn in_scattered(direction : vec3<f32>, toward_sun : vec3<f32>, jitter : f32) -> vec3<f32> {
    let through = ray_sphere(vec3<f32>(0.0), AtmosphereRadius, Eye, direction).y;
    let ground = ray_sphere(vec3<f32>(0.0), PlanetRadius, Eye, direction);
    let ray_length = select(through, min(through, ground.x), ground.y > 0.0);
    let step = ray_length / f32(InScatterPoints - 1);

    var point = Eye + direction * step * jitter;
    var view_depth = vec2<f32>(0.0);
    var rayleigh = vec3<f32>(0.0);
    var mie = vec3<f32>(0.0);
    for (var i = 0; i < InScatterPoints; i = i + 1) {
        let local = density_at(point);
        view_depth = view_depth + local * step;
        // A point the planet hides from the sun scatters nothing: that is what makes night.
        let visible = sun_visibility(point, toward_sun);
        if (visible > 0.0) {
            let sun_length = ray_sphere(vec3<f32>(0.0), AtmosphereRadius, point, toward_sun).y;
            let reaching = transmittance(optical_depth(point, toward_sun, sun_length) + view_depth) * visible;
            rayleigh = rayleigh + reaching * local.x;
            mie = mie + reaching * local.y;
        }
        point = point + direction * step;
    }

    // Rayleigh scatters nearly evenly, Mie mostly forwards (Cornette-Shanks), both normalised to average 1.
    let mu = dot(direction, toward_sun);
    let g = MieAnisotropy;
    let rayleigh_phase = 0.75 * (1.0 + mu * mu);
    let mie_phase = 1.5 * ((1.0 - g * g) / (2.0 + g * g)) * (1.0 + mu * mu) / pow(1.0 + g * g - 2.0 * g * mu, 1.5);
    return (rayleigh * Scattering * rayleigh_phase + mie * MieScattering * mie_phase) * step / PlanetRadius;
}

// The sunlight that makes it down to the eye, reddened by the air it came through.
fn sunlight(toward_sun : vec3<f32>) -> vec3<f32> {
    let sun_length = ray_sphere(vec3<f32>(0.0), AtmosphereRadius, Eye, toward_sun).y;
    return transmittance(optical_depth(Eye, toward_sun, sun_length));
}

// ---- Noise, for clouds, stars and nebulae ---------------------------------------------------------------

// Dave Hoskins' hash12 and hash13: sine-free, so they stay random at any coordinate instead of banding.
fn hash(p : vec2<f32>) -> f32 {
    var p3 = fract(vec3<f32>(p.x, p.y, p.x) * 0.1031);
    p3 = p3 + dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

fn hash3(p : vec3<f32>) -> f32 {
    var p3 = fract(p * 0.1031);
    p3 = p3 + dot(p3, p3.zyx + 31.32);
    return fract((p3.x + p3.y) * p3.z);
}

fn value_noise(p : vec2<f32>) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    let a = hash(i);
    let b = hash(i + vec2<f32>(1.0, 0.0));
    let c = hash(i + vec2<f32>(0.0, 1.0));
    let d = hash(i + vec2<f32>(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Five octaves, each rotated a little off the last so the layers never line up into a visible grid.
fn fbm(start : vec2<f32>) -> f32 {
    var p = start;
    var total = 0.0;
    var amplitude = 0.5;
    let turn = mat2x2<f32>(0.8, 0.6, -0.6, 0.8);
    for (var i = 0; i < 5; i = i + 1) {
        total = total + amplitude * value_noise(p);
        p = turn * p * 2.03;
        amplitude = amplitude * 0.5;
    }
    return total;
}

// A star field: one candidate star per cell of a fine grid over the sphere of directions, most cells empty.
fn stars(direction : vec3<f32>, density : f32, seconds : f32) -> vec3<f32> {
    let cells = direction * 220.0;
    let cell = floor(cells);
    let pick = hash3(cell);
    if (pick < 1.0 - density) {
        return vec3<f32>(0.0);
    }
    // Where in its cell the star sits, and how far this pixel is from it.
    let centre = cell + vec3<f32>(hash3(cell + 17.0), hash3(cell + 43.0), hash3(cell + 71.0));
    let distance = length(cells - centre);
    let brightness = pow(fract(pick * 37.0), 3.0) * 3.0 + 0.3;
    let twinkle = 0.75 + 0.25 * sin(seconds * (1.0 + pick * 4.0) + pick * 60.0);
    let warmth = fract(pick * 91.0);
    let colour = mix(vec3<f32>(0.75, 0.82, 1.0), vec3<f32>(1.0, 0.86, 0.7), warmth);
    return colour * brightness * twinkle * (1.0 - smoothstep(0.0, 0.35, distance));
}


// ---- The skies -------------------------------------------------------------------------------------------

const Ground : vec3<f32> = vec3<f32>(0.30, 0.33, 0.37);

// Interleaved gradient noise (Jimenez): a cheap per-pixel value that tiles without visible pattern.
fn pixel_noise(pixel : vec2<f32>) -> f32 {
    return fract(52.9829189 * fract(dot(pixel, vec2<f32>(0.06711056, 0.00583715))));
}

// Rolls bright air off towards 1 by scaling all three channels together, so the hue survives. Left for the
// tone mapper, a bright orange horizon clips red first and the green left over turns it yellow.
fn keep_hue(colour : vec3<f32>) -> vec3<f32> {
    let peak = max(colour.r, max(colour.g, colour.b));
    if (peak <= 0.7) {
        return colour;
    }
    let rolled = 0.7 + (1.0 - exp(-(peak - 0.7) * 2.0)) * 0.3;
    return colour * (rolled / peak);
}

fn day_sky(direction : vec3<f32>, jitter : f32) -> vec3<f32> {
    let up = direction.y;
    let toward_sun = normalize(sky.sun.xyz);
    let daylight = sky.parameters.y;
    let sun_dot = max(dot(direction, toward_sun), 0.0);
    let reaching = sunlight(toward_sun);

    // Well below the horizon the sky is the flat ground colour and nothing else (the mix at the end is complete from
    // -0.25), so skip the air, clouds and stars: a camera looking down at a board is nearly all such pixels.
    if (up < -0.25) {
        let floor_colour = Ground * mix(0.05, 1.0, daylight) * mix(vec3<f32>(1.0), reaching, 0.5);
        return floor_colour * (1.0 + (jitter - 0.5) * 0.03) + vec3<f32>((jitter - 0.5) * 0.0015);
    }

    // The air, lit by the sun where it can see it. Looking below the horizon the air is sampled at a
    // grazing angle instead, so the haze runs right down to the floor's edge rather than stopping at a line.
    let air_direction = normalize(vec3<f32>(direction.x, max(up, 0.015), direction.z));
    var colour = keep_hue(in_scattered(air_direction, toward_sun, jitter) * 1.5);

    // Night: a deep blue floor for the sky, the moon opposite the sun, and stars once the light has gone.
    let night = 1.0 - smoothstep(0.08, 0.5, daylight);
    colour = colour + vec3<f32>(0.004, 0.008, 0.02) * night;
    if (up > 0.0) {
        // The fill stars turn with the catalogue's, so the whole sky wheels together.
        let fill_direction = select(direction, to_equatorial(direction), has_celestial_frame());
        colour = colour + stars(fill_direction, 0.012, sky.parameters.x) * night * smoothstep(0.0, 0.2, up);
        if (night > 0.0) {
            colour = colour + catalogue_stars(direction) * night * smoothstep(0.0, 0.15, up);
        }
    }
    if (up > 0.0 && sky.moon.w > 0.5) {
        let toward_moon = normalize(sky.moon.xyz);
        let moon_dot = max(dot(direction, toward_moon), 0.0);
        // A restrained horizon-size cue stands in for the Moon illusion, not a physical change in angular size.
        let horizon = 1.0 - smoothstep(0.0, 0.2, toward_moon.y);
        let moon_cos_radius = mix(MoonCosRadius, HorizonMoonCosRadius, horizon);
        let moon = moon_disc(direction, toward_moon, toward_sun, moon_cos_radius);
        let moon_visibility = mix(0.42, 1.0, night);
        colour = mix(colour, moon.rgb * vec3<f32>(1.0, 1.0, 1.05) * 1.8, moon.a * moon_visibility);
        let moon_phase_angle = sky.parameters.w * 6.2831853;
        let moon_illumination = (1.0 - cos(moon_phase_angle)) * 0.5;
        colour = colour + vec3<f32>(0.8, 0.85, 1.0) * night * moon_illumination * pow(moon_dot, 200.0) * 0.08;
    }

    // Clouds on a flat layer overhead, drifting slowly, thinning towards the horizon where they would crowd
    // into noise, and lit by the same sunlight -- white at noon, pink at sunset, grey at night.
    if (up > 0.0) {
        // A light breeze: a cloud crosses its own width in about half a minute, faster when the day runs fast.
        let layer = direction.xz / (up + 0.12) * 1.6 + vec2<f32>(0.02, 0.008) * sky.parameters.z;
        let density = smoothstep(0.53, 0.8, fbm(layer));
        let shade = 1.05 + 0.25 * pow(sun_dot, 4.0) - 0.25 * smoothstep(0.6, 0.9, fbm(layer * 1.7 + 3.1));
        // Sunlight on their tops, plus the sky's own light from overhead so they are never flat brown.
        let skylight = in_scattered(vec3<f32>(0.0, 1.0, 0.0), toward_sun, 0.5) * 2.5;
        let lit = reaching * max(toward_sun.y + 0.15, 0.0) * 0.9 + skylight + vec3<f32>(0.03, 0.035, 0.05);
        let cloud = vec3<f32>(shade) * lit;
        colour = mix(colour, cloud, density * smoothstep(0.02, 0.30, up) * 0.95);
    }

    // The sun, over the clouds so they never hide it, and the colour the air leaves it.
    if (toward_sun.y > -0.05) {
        // Rolled off by hue too, or a low sun clips to pale yellow however orange its light is.
        colour = colour + reaching * pow(sun_dot, 64.0) * 0.35 + keep_hue(reaching * 6.0) * 1.6 * smoothstep(0.9994, 0.9997, sun_dot);
    }

    // Below the horizon: a soft fade to a muted ground, lit as the rest of the scene is.
    let ground = Ground * mix(0.05, 1.0, daylight) * mix(vec3<f32>(1.0), reaching, 0.5);
    colour = mix(colour, ground, smoothstep(0.0, 0.25, -up));

    // A grain finer than a display step, so a dark gradient cannot quantize into visible rings once tone
    // mapped to eight bits.
    return colour * (1.0 + (jitter - 0.5) * 0.03) + vec3<f32>((jitter - 0.5) * 0.0015);
}

fn space_sky(direction : vec3<f32>) -> vec3<f32> {
    let seconds = sky.parameters.x;

    // The galaxy's band: a great circle tipped off the horizon, brightest along its middle.
    let band_normal = normalize(vec3<f32>(0.3, 0.55, -0.78));
    let band = exp(-pow(dot(direction, band_normal), 2.0) * 18.0);

    // Nebulae: noise read on two planes and multiplied, so the clouds are patchy rather than a wash.
    let n1 = fbm(direction.xy * 2.2 + 4.0);
    let n2 = fbm(direction.zy * 2.6 - 7.0);
    let n3 = fbm(direction.xz * 1.8 + 11.0);
    let cloud = smoothstep(0.35, 0.85, n1 * n2 * 2.2);
    var colour = vec3<f32>(0.002, 0.003, 0.008);
    colour = colour + mix(vec3<f32>(0.12, 0.04, 0.22), vec3<f32>(0.03, 0.12, 0.25), n3) * cloud * 0.35;
    colour = colour + vec3<f32>(0.20, 0.18, 0.24) * band * (0.25 + 0.75 * fbm(direction.xz * 9.0 + direction.yy * 5.0)) * 0.25;

    // Far more stars than a night sky, thicker along the band.
    colour = colour + stars(direction, 0.02 + 0.05 * band, seconds);
    colour = colour + stars(direction.zxy, 0.01, seconds) * 0.6;

    // The system's star, where the key light comes from: a white-hot point with a wide glow.
    let toward_sun = normalize(sky.sun.xyz);
    let sun_dot = max(dot(direction, toward_sun), 0.0);
    colour = colour + vec3<f32>(1.0, 0.95, 0.88) * (smoothstep(0.9996, 0.9998, sun_dot) * 8.0 + pow(sun_dot, 400.0) * 0.8 + pow(sun_dot, 12.0) * 0.04);
    return colour;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // Unprojected at the far plane, then normalized: only the direction matters to a background.
    let far = sky.inverse_direction_view_projection * vec4<f32>(input.ndc, 1.0, 1.0);
    let direction = normalize(far.xyz / far.w);

    if (sky.sun.w > 1.5) {
        return vec4<f32>(space_sky(direction), 1.0);
    }
    if (sky.sun.w > 0.5) {
        return vec4<f32>(day_sky(direction, pixel_noise(input.clip_position.xy)), 1.0);
    }
    return textureSample(sky_texture, sky_sampler, direction);
}
