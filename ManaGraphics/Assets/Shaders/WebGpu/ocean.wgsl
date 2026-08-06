struct FrameUniform {
    view_projection : mat4x4<f32>,
    camera_position : vec4<f32>,
    irradiance_factors : vec4<f32>,
    light_positions : array<vec4<f32>, 4>,
    light_colors : array<vec4<f32>, 4>,
    time : f32,
    webgpu_debug_mode : f32,
    _padding1 : f32,
    _padding2 : f32,
};

struct ObjectUniform {
    model : mat4x4<f32>,
    normal_matrix : mat4x4<f32>,
    material_albedo : vec4<f32>,
    material_factors : vec4<f32>,
    material_emission : vec4<f32>,
    outline_color : vec4<f32>,
    outline_parameters : vec4<f32>,
    debug_mode_override : f32,
    _padding0 : f32,
    _padding1 : f32,
    _padding2 : f32,
};

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_position : vec3<f32>,
    @location(3) world_normal : vec3<f32>,
};

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;

// Custom per-material uniforms (group 2), parsed by declaration order from this struct - see
// ManaWebGpuCustomUniformSchema.cs. No rippleDataN slots here unlike water.wgsl -- impact ripples
// are a local-splash effect that doesn't read as meaningful at whole-planet scale.
struct CustomMaterialUniform {
    //PROPERTY(default=0.4, min=0.0, max=1.0)
    waveStrength : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    waveSpeed : f32,

    // Unlike water.wgsl's flat-plane version (frequency against literal world-unit X/Z), this is
    // sampled against a point on the *unit* sphere -- the whole globe only spans a coordinate range
    // of roughly [-1,1], so getting more than a fraction of one wave across the entire planet needs a
    // higher frequency than a flat plane would.
    //
    // This is capped much lower than you'd naively want (a bare "more waves" slider would suggest
    // pushing well past 40) because of how wave_noise3's FBM compounds it: each of its 3 octaves
    // multiplies frequency by ~2.03 on top of the 0.55 factor ambient_wave3 applies before calling it,
    // so the finest octave's *effective* spatial frequency is roughly waveFrequency * 2.3, not
    // waveFrequency itself. PlanetOceanMesh's shell mesh has a fixed vertex density -- once that
    // effective frequency's wavelength gets close to the mesh's own vertex spacing, the wave stops
    // being geometry the mesh can represent and starts being effectively-random per-vertex noise,
    // which is what read as the blotchy/jaggy "camouflage" pattern. This max keeps the finest octave's
    // wavelength comfortably above that spacing at the mesh's current resolution; raising it further
    // needs a proportionally higher PlanetOceanMesh resolution to match.
    //PROPERTY(default=6.0, min=0.05, max=20.0)
    waveFrequency : f32,

    //PROPERTY(default=0.85, min=0.0, max=1.0)
    opacity : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    fresnelStrength : f32,

    //PROPERTY(default=[0.01,0.08,0.14], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    deepColor : vec3<f32>,

    //PROPERTY(default=[0.08,0.28,0.34], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    shallowColor : vec3<f32>,
};

@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

// 3D extension of water.wgsl's IQ-style hash/value noise -- keyed directly off a point's position on
// the unit sphere rather than a flat local X/Z, so it has no periodic coordinate and no seam anywhere
// (in particular, no jump in the wave pattern across any of the 6 cube faces this mesh is built from).
fn hash31(p : vec3<f32>) -> f32 {
    var p3 = fract(p * 0.1031);
    p3 = p3 + dot(p3, p3.zyx + 31.32);
    return fract((p3.x + p3.y) * p3.z);
}

fn value_noise3(p : vec3<f32>) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let u = f * f * (3.0 - 2.0 * f);

    let c000 = hash31(i + vec3<f32>(0.0, 0.0, 0.0));
    let c100 = hash31(i + vec3<f32>(1.0, 0.0, 0.0));
    let c010 = hash31(i + vec3<f32>(0.0, 1.0, 0.0));
    let c110 = hash31(i + vec3<f32>(1.0, 1.0, 0.0));
    let c001 = hash31(i + vec3<f32>(0.0, 0.0, 1.0));
    let c101 = hash31(i + vec3<f32>(1.0, 0.0, 1.0));
    let c011 = hash31(i + vec3<f32>(0.0, 1.0, 1.0));
    let c111 = hash31(i + vec3<f32>(1.0, 1.0, 1.0));

    let x00 = mix(c000, c100, u.x);
    let x10 = mix(c010, c110, u.x);
    let x01 = mix(c001, c101, u.x);
    let x11 = mix(c011, c111, u.x);

    let y0 = mix(x00, x10, u.y);
    let y1 = mix(x01, x11, u.y);

    return mix(y0, y1, u.z) * 2.0 - 1.0;
}

// 3-octave fractal Brownian motion, drifting slowly over time so the noise itself isn't static.
// Was 4 octaves; dropping the finest one halves the peak effective frequency reaching the mesh (see
// waveFrequency's PROPERTY comment above) for a similar amount of visible layering, buying headroom
// against geometric aliasing without needing to gut waveFrequency's usable range as much.
fn wave_noise3(p : vec3<f32>, t : f32) -> f32 {
    let drift = p + vec3<f32>(t * 0.17, -t * 0.11, t * 0.07);
    var value = 0.0;
    var amplitude = 0.5;
    var freq = 1.0;
    for (var i = 0u; i < 3u; i = i + 1u) {
        value = value + value_noise3(drift * freq) * amplitude;
        freq = freq * 2.03;
        amplitude = amplitude * 0.5;
    }
    return value;
}

// Two large directional swells plus layered FBM on top, same composition water.wgsl's ambient_wave
// uses, just keyed by a 3D point on the unit sphere instead of flat 2D X/Z.
fn ambient_wave3(p : vec3<f32>) -> f32 {
    let t = frame.time * custom.waveSpeed;
    var h = sin(dot(p, vec3<f32>(0.80, 0.60, 0.35)) * custom.waveFrequency + t) * 0.4;
    h = h + sin(dot(p, vec3<f32>(-0.53, 0.85, -0.24)) * custom.waveFrequency * 0.58 - t * 1.15) * 0.26;
    h = h + wave_noise3(p * custom.waveFrequency * 0.55, t) * 0.5;
    return h * custom.waveStrength;
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;

    // This mesh is a sphere centered at the local origin, so `normal` here already IS the point's
    // direction on the unit sphere (== position / radius) -- used directly as the noise input.
    // There's no single global tangent-plane axis pair the way a flat plane has X/Z, so build one
    // per-vertex (same axisA/axisB-style construction PlanetPatchMesh/CubeSphere use elsewhere).
    //
    // epsilon (the finite-difference step used below to estimate the surface normal from
    // ambient_wave3) has to stay a small fraction of wave_noise3's finest actual oscillation, or it
    // starts sampling across more than one full cycle -- at that point the "derivative" it estimates
    // is essentially noise instead of a real slope, reading as flickering/spiky normals. The finest
    // octave's effective frequency is ~waveFrequency * 2.3 (see waveFrequency's PROPERTY comment), so
    // epsilon needs to shrink faster than a plain 1/waveFrequency to keep pace -- this used to be
    // 0.5/waveFrequency, which was still roughly 10x too loose to safely resolve that finest octave.
    let epsilon = 0.04 / max(custom.waveFrequency, 1.0);
    let helper = select(vec3<f32>(1.0, 0.0, 0.0), vec3<f32>(0.0, 1.0, 0.0), abs(normal.x) > 0.9);
    let tangentA = normalize(cross(normal, helper));
    let tangentB = cross(normal, tangentA);

    let height = ambient_wave3(normal);
    let height_a = ambient_wave3(normalize(normal + tangentA * epsilon));
    let height_b = ambient_wave3(normalize(normal + tangentB * epsilon));

    // Displace along the outward normal (radial direction), not a fixed world axis -- the flat-plane
    // water.wgsl displaces along local Y, which only makes sense because a plane's "up" is constant
    // everywhere; a sphere's isn't.
    let displaced = position + normal * height;

    let d_a = tangentA * epsilon + normal * (height_a - height);
    let d_b = tangentB * epsilon + normal * (height_b - height);
    let local_normal = normalize(cross(d_a, d_b));

    let world = object.model * vec4<f32>(displaced, 1.0);
    output.position = frame.view_projection * object.model * vec4<f32>(displaced, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.world_position = world.xyz;
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(local_normal, 0.0)).xyz);
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let n = normalize(input.world_normal);
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

    // input.color.a carries a per-vertex depth factor baked in at mesh-build time (0 = shoreline/no
    // data, 1 = deep water -- see PlanetOceanMesh.BuildFace), used here to pick the deep/shallow color
    // instead of `diffuse`. Real bathymetry is smooth, continuous data; `diffuse` is driven by the
    // wave-displaced normal, which is inherently noisy per-vertex -- mixing color choice off of it (the
    // old behavior) baked that per-vertex noise directly into which color showed up where, which read
    // as blotchy "camouflage" patches. Keeping diffuse as a separate brightness multiplier still lets
    // waves catch/lose light without tying color identity to it.
    let depth_factor = input.color.a;
    let base_color = mix(custom.shallowColor, custom.deepColor, depth_factor) * diffuse;
    let color = base_color + vec3<f32>(fresnel * 0.3);
    let alpha = saturate(custom.opacity + fresnel * 0.25);

    return vec4<f32>(color, alpha);
}
