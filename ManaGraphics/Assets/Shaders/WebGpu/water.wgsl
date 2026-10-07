struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_position : vec3<f32>,
    @location(3) world_normal : vec3<f32>,
    // Undisplaced local XZ, interpolated across the triangle - used to evaluate ripple rings
    // per-pixel in fs_main so ring sharpness isn't limited by the (much coarser) vertex grid.
    @location(4) local_xz : vec2<f32>,
};

#include "include/uniforms.wgsl"
#include "include/point_lights.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;

// Custom per-material uniforms (group 2), parsed by declaration order from this struct - see
// ManaWebGpuCustomUniformSchema.cs. rippleDataN slots are impact-triggered ripples spawned via
// ManaWaterNode.SpawnRipple; they are plain vec4 fields (not a WGSL array) because the schema
// parser doesn't understand array<...> field types.
struct CustomMaterialUniform {
    //PROPERTY(default=0.12, min=0.0, max=1.0)
    waveStrength : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    waveSpeed : f32,

    //PROPERTY(default=0.55, min=0.05, max=4.0)
    waveFrequency : f32,

    //PROPERTY(default=0.85, min=0.0, max=1.0)
    opacity : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    fresnelStrength : f32,

    //PROPERTY(default=2.2, min=0.1, max=8.0)
    rippleSpeed : f32,

    //PROPERTY(default=0.5, min=0.05, max=20.0)
    rippleFrequency : f32,

    //PROPERTY(default=0.8, min=0.0, max=8.0)
    rippleDecay : f32,

    //PROPERTY(default=[0.01,0.08,0.14], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    deepColor : vec3<f32>,

    //PROPERTY(default=[0.08,0.28,0.34], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    shallowColor : vec3<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData0 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData1 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData2 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData3 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData4 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData5 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData6 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData7 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData8 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData9 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData10 : vec4<f32>,

    //PROPERTY(default=[0.0,0.0,0.0,0.0], min=[-1000.0,-1000.0,0.0,0.0], max=[1000.0,1000.0,100000.0,4.0])
    rippleData11 : vec4<f32>,
};

@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

// Cheap hash-based value noise (IQ-style) - no textures needed, good enough at vertex-shader scale.
fn hash21(p : vec2<f32>) -> f32 {
    var p3 = fract(vec3<f32>(p.x, p.y, p.x) * 0.1031);
    p3 = p3 + dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

fn value_noise(p : vec2<f32>) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let a = hash21(i);
    let b = hash21(i + vec2<f32>(1.0, 0.0));
    let c = hash21(i + vec2<f32>(0.0, 1.0));
    let d = hash21(i + vec2<f32>(1.0, 1.0));
    let u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y) * 2.0 - 1.0;
}

// 4-octave fractal Brownian motion; callers move it (with the wind, mostly) by offsetting p.
fn wave_noise(p : vec2<f32>) -> f32 {
    let drift = p;
    var value = 0.0;
    var amplitude = 0.5;
    var freq = 1.0;
    for (var i = 0u; i < 4u; i = i + 1u) {
        value = value + value_noise(drift * freq) * amplitude;
        freq = freq * 2.03;
        amplitude = amplitude * 0.5;
    }
    return value;
}

// The world wind (ManaWind) against the breeze waveStrength is tuned for, ManaWind.ReferenceSpeed (4 m/s).
fn wind_factor() -> f32 {
    return min(frame.wind.z / 4.0, 3.0);
}

fn wind_direction() -> vec2<f32> {
    let d = frame.wind.xy;
    if (dot(d, d) < 1e-6) {
        return vec2<f32>(0.0, -1.0);
    }
    return normalize(d);
}

// Where a gust is touching the water now: patches carried downwind that roughen and darken it -- cat's paws.
fn gust_field(p : vec2<f32>) -> f32 {
    let carried = (p - frame.wind_motion.xy) * 0.07 + vec2<f32>(0.0, frame.time * 0.02);
    let patches = smoothstep(0.0, 0.45, wave_noise(carried));
    return patches * frame.wind.w * (0.35 + frame.wind_motion.w);
}

// Two large swells (rather than purely axis-aligned sines, which line up into an obvious diamond lattice)
// running downwind, plus layered FBM noise on top to break up the periodicity stacked sines always show.
// Phases follow the distance the wind has travelled, not the clock, so a gust speeds them without a jump.
fn ambient_wave(p : vec2<f32>) -> f32 {
    let dir = wind_direction();
    let across = normalize(dir * 0.82 + vec2<f32>(-dir.y, dir.x) * 0.57);
    let travel = frame.wind_motion.z * 0.25 * custom.waveSpeed;
    var h = sin(dot(p, dir) * custom.waveFrequency - travel) * 0.4;
    h = h + sin(dot(p, across) * custom.waveFrequency * 0.58 - travel * 1.15) * 0.26;
    h = h + wave_noise((p - frame.wind_motion.xy * 0.3) * custom.waveFrequency * 0.55) * 0.5;
    return h * custom.waveStrength * wind_factor() * (1.0 + gust_field(p) * 0.6);
}

// ripple : (originX, originZ, startTime, strength). strength <= 0 means the slot is inactive.
// Gaussian cross-section (smooth rounded bump) rather than exp(-abs(d)) (a sharp V-shaped ridge) -
// the latter, once vertex-displaced onto a triangle grid, faceted into an unrealistic scalloped edge.
fn ripple_contribution(x : f32, z : f32, ripple : vec4<f32>) -> f32 {
    if (ripple.w <= 0.0) {
        return 0.0;
    }

    let age = frame.time - ripple.z;
    if (age < 0.0) {
        return 0.0;
    }

    let dist = length(vec2<f32>(x, z) - ripple.xy);
    let ring_radius = age * custom.rippleSpeed;
    let d = dist - ring_radius;
    let ring_shape = exp(-d * d * custom.rippleFrequency);
    let fade = exp(-age * custom.rippleDecay);
    let reach = smoothstep(0.0, 0.3, ring_radius - dist + 0.5);
    return ring_shape * fade * ripple.w * reach;
}

fn ripple_height(x : f32, z : f32) -> f32 {
    var h = ripple_contribution(x, z, custom.rippleData0);
    h = h + ripple_contribution(x, z, custom.rippleData1);
    h = h + ripple_contribution(x, z, custom.rippleData2);
    h = h + ripple_contribution(x, z, custom.rippleData3);
    h = h + ripple_contribution(x, z, custom.rippleData4);
    h = h + ripple_contribution(x, z, custom.rippleData5);
    h = h + ripple_contribution(x, z, custom.rippleData6);
    h = h + ripple_contribution(x, z, custom.rippleData7);
    h = h + ripple_contribution(x, z, custom.rippleData8);
    h = h + ripple_contribution(x, z, custom.rippleData9);
    h = h + ripple_contribution(x, z, custom.rippleData10);
    h = h + ripple_contribution(x, z, custom.rippleData11);
    return h;
}

// The vertex-displaced bump stays gentle (proportionate to the ambient chop) so a fresh, full-
// strength ripple doesn't spike into a steep ridge that facets on the triangle grid; the fragment
// shader's per-pixel highlight (see fs_main) uses the undamped ripple_height for a crisp, visible
// ring so the effect still reads clearly even though the geometry itself barely bulges.
// The wind is in world space, so the swell is evaluated there; ripples stay in the node's own XZ.
fn water_height(local : vec3<f32>, calm : f32) -> f32 {
    let p = (object.model * vec4<f32>(local.x, 0.0, local.z, 1.0)).xz;
    return (ambient_wave(p) + ripple_height(local.x, local.z) * 0.25) * calm;
}

// Flat at the plane's rim, so where the water meets a bank it lies still instead of slicing a moving line through it.
fn shore_calm(uv : vec2<f32>) -> f32 {
    let edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
    return smoothstep(0.0, 0.12, edge);
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;

    let epsilon = 0.05;
    let calm = shore_calm(uv0);
    let height = water_height(position, calm);
    let height_x = water_height(position + vec3<f32>(epsilon, 0.0, 0.0), calm);
    let height_z = water_height(position + vec3<f32>(0.0, 0.0, epsilon), calm);

    var displaced = position;
    displaced.y = displaced.y + height;

    let tangent_x = vec3<f32>(epsilon, height_x - height, 0.0);
    let tangent_z = vec3<f32>(0.0, height_z - height, epsilon);
    let local_normal = normalize(cross(tangent_z, tangent_x));

    let world = object.model * vec4<f32>(displaced, 1.0);
    output.position = frame.view_projection * object.model * vec4<f32>(displaced, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.world_position = world.xyz;
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(local_normal, 0.0)).xyz);
    output.local_xz = vec2<f32>(position.x, position.z);
    return output;
}

// Wind ripples in the lighting only: far too small for the vertex grid, but they break the moon's and the
// lamps' reflections into a path of glints. One layer runs downwind, a finer one shimmers against it; they
// pick up where a gust is blowing, and still out toward the banks like the swell.
fn ripple_detail(p : vec2<f32>, downwind : vec2<f32>, shimmer : vec2<f32>) -> f32 {
    return wave_noise(p * 2.3 - downwind) + wave_noise(p * 6.2 + shimmer) * 0.5;
}

fn detail_normal(xz : vec2<f32>, calm : f32, gust : f32) -> vec3<f32> {
    let t = frame.time * custom.waveSpeed;
    let downwind = frame.wind_motion.xy * 1.4;
    let shimmer = vec2<f32>(5.2 + t * 0.9, 1.3 - t * 1.3);
    let e = 0.02;
    let h = ripple_detail(xz, downwind, shimmer);
    let hx = ripple_detail(xz + vec2<f32>(e, 0.0), downwind, shimmer);
    let hz = ripple_detail(xz + vec2<f32>(0.0, e), downwind, shimmer);
    let strength = 0.014 * calm * wind_factor() * (0.6 + gust * 1.6);
    let slope = vec2<f32>(hx - h, hz - h) / e * strength;
    return vec3<f32>(-slope.x, 0.0, -slope.y);
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let calm = shore_calm(input.uv0);
    let paw = gust_field(input.world_position.xz) * calm;
    let n = normalize(normalize(input.world_normal) + detail_normal(input.world_position.xz, calm, paw));
    let v = normalize(frame.camera_position.xyz - input.world_position);
    let ndotv = saturate(dot(n, v));

    var diffuse = 0.35;
    for (var i = 0u; i < 4u; i = i + 1u) {
        let light_pos = frame.light_positions[i].xyz;
        let light_color = frame.light_colors[i].xyz;
        let l = normalize(light_pos - input.world_position);
        diffuse = diffuse + max(dot(n, l), 0.0) * saturate(light_color.r + light_color.g + light_color.b) * 0.05;
    }
    diffuse = saturate(diffuse);

    let fresnel = pow(1.0 - ndotv, 3.0) * custom.fresnelStrength;

    // Evaluated per-pixel from the interpolated (undisplaced) local XZ rather than reading back the
    // vertex-interpolated height, so ripple rings stay crisp regardless of the mesh's grid density.
    // Ambient waves are intentionally excluded here so calm water doesn't glint all over - only
    // actual ripples produce this highlight. smoothstep (not a hard saturate ramp) keeps the ring's
    // glint soft-edged instead of a flat-topped painted disc.
    let ripple = ripple_height(input.local_xz.x, input.local_xz.y);
    let ripple_highlight = smoothstep(0.0, 1.0, ripple) * 0.5;

    let base_color = mix(custom.deepColor, custom.shallowColor, diffuse) * input.color.rgb;

    // Lit like everything else: ambient that falls with ManaSky's daylight, the sun (or moon), and placed
    // lamps -- before, the water glowed the same at midnight as at noon.
    let daylight = frame.irradiance_factors.w;
    var light = vec3<f32>(mix(0.07, 0.75, daylight));
    // Glints: the moon or sun, and each lamp, mirrored in the ripples -- the path of light across a pond.
    var glint = vec3<f32>(0.0);
    if (frame.sun_direction.w > 0.5) {
        let to_sun = normalize(-frame.sun_direction.xyz);
        let sun = max(dot(n, to_sun), 0.0);
        let sun_light = frame.sun_color.rgb * min(frame.sun_color.a, 4.0);
        light = light + sun_light * sun * 0.3;
        glint = glint + sun_light * pow(max(dot(n, normalize(to_sun + v)), 0.0), 180.0) * 2.5;
    }
    var lamps = vec3<f32>(0.0);
    for (var i = 0u; i < 16u; i = i + 1u) {
        let range = frame.local_light_positions[i].w;
        if (range <= 0.0) {
            continue;
        }
        let to_light = frame.local_light_positions[i].xyz - input.world_position;
        let distance = length(to_light);
        let ndotl = max(dot(n, to_light / max(distance, 0.001)), 0.0);
        let to_lamp = to_light / max(distance, 0.001);
        let reach = frame.local_light_colors[i].rgb * mana_point_light_attenuation(distance, range);
        lamps = lamps + reach * ndotl;
        glint = glint + reach * pow(max(dot(n, normalize(to_lamp + v)), 0.0), 400.0) * 1.2;
    }
    // Soft: the water's own colour only takes a little of a lamp; the rest shows in the lamp's colour below.
    light = light + lamps * 0.3;

    // Mixed toward a brighter shade of the water's own color (not raw white), and mix() rather than
    // additive keeps overlapping rings from stacking up into a blown-out white patch.
    let ripple_tint = custom.shallowColor * 1.5 + vec3<f32>(0.05, 0.08, 0.08);
    // A gust's roughened patch reflects less sky, so it reads darker as it runs across.
    let shaded_color = mix(base_color, ripple_tint, ripple_highlight) * light * (1.0 - paw * 0.2);
    // The sheen is the sky's reflection, so it fades with the daylight; lamps still catch the ripples.
    let color = shaded_color + vec3<f32>(fresnel * 0.3 * mix(0.12, 1.0, daylight) * (1.0 - paw * 0.5)) + lamps * (0.04 + ripple_highlight * 0.3) + glint;
    let alpha = saturate(custom.opacity + fresnel * 0.25 + ripple_highlight * 0.1);

    return vec4<f32>(color, alpha);
}
