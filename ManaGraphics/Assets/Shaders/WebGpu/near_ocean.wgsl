// Near tier of the two-tier planet ocean: the camera-following high-density patch built by
// PlanetNearOceanMesh, which only exists when the camera is close enough to the sea surface for its
// vertex spacing to actually resolve waves. Everything here is expressed in METERS and converted to
// scene units via the engine-driven unitsPerMeter, because a planet-scale scene has ~160km per scene
// unit -- authoring wave size in scene units is what produced the 60km-tall "waves" this replaces.
//
// Vertex channel layout is specific to PlanetNearOceanMesh (it owns both sides):
//   uv0     - (east, north) position on the wave plane, in METERS, origin at the patch's own tracked
//             wave origin. Precision matters here: doing this subtraction in the shader from
//             world-space positions would catastrophically cancel (two ~240-unit numbers differing by
//             ~1e-3), so the mesh bakes the small local offsets directly.
//   color.r - edge fade weight (1 in the patch interior, 0 at the outer rim), the complement of
//             ocean.wgsl's own nearFade* falloff so the two tiers cross-fade to ~1 total coverage.
//   color.a - depth factor (0 = shoreline, 1 = deep), same OceanSurface.ComputeDepthFactor the far
//             shell uses so the color matches across the blend.

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) world_position : vec3<f32>,
    @location(3) world_normal : vec3<f32>,
};

#include "include/uniforms.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;

struct CustomMaterialUniform {
    // Wave height as a fraction of wavelength, not an absolute size. Real ocean runs about 1/20 to
    // 1/7; past that a wave breaks. Steepness is the parameter that stays meaningful while the
    // wavelength itself is being rescaled by altitude below, which an absolute amplitude would not.
    //PROPERTY(default=0.05, min=0.0, max=0.15)
    waveSteepness : f32,

    // The wavelength the patch settles to once the camera is low enough to resolve it. It is a FLOOR,
    // not a target: waveVerticesPerWavelength below raises the effective wavelength whenever the patch
    // is too coarse for this one, so descending walks the waves down to this real, physical size and
    // then stops shrinking.
    //PROPERTY(default=70.0, min=5.0, max=600.0)
    waveLengthMeters : f32,

    // How many vertices one wavelength must span. This is what keeps waves simultaneously VISIBLE and
    // ALIAS-FREE across a ~1000:1 altitude range: at any altitude the wave is the same size relative
    // to the patch, so it subtends roughly the same angle on screen. Below ~4 the mesh cannot describe
    // the wave and it degenerates into per-vertex noise -- which is exactly the failure the old
    // planet-scale ocean shader had, and no amount of frequency tuning fixed.
    //PROPERTY(default=10.0, min=4.0, max=40.0)
    waveVerticesPerWavelength : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    waveSpeed : f32,

    //PROPERTY(default=0.9, min=0.0, max=1.0)
    opacity : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    fresnelStrength : f32,

    //PROPERTY(default=[0.01,0.08,0.14], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    deepColor : vec3<f32>,

    //PROPERTY(default=[0.08,0.28,0.34], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    shallowColor : vec3<f32>,

    // Engine-driven (PlanetNode.UpdateNearOcean), not user-editable. The patch's tangent basis in
    // planet-LOCAL space, matching uv0's (east, north) axes and built so cross(east, north) is the
    // outward radial -- needed to turn the 2D wave gradient back into a 3D surface normal. Constant
    // across the patch, which is accurate to O(span / planetRadius).
    waveEast : vec3<f32>,
    waveNorth : vec3<f32>,

    // Scene units per real-world meter, and the period at which the wave lattice repeats (also in
    // meters). PlanetNearOceanMesh reduces its tracked wave origin modulo this same tile so the field
    // is bit-identical before and after a re-center -- see its RecenterTo for why that matters.
    unitsPerMeter : f32,
    waveTileMeters : f32,

    // Distance between adjacent patch vertices, in meters. The patch keeps a fixed vertex count and
    // varies its span, so this grows with altitude, and it is what effective_wavelength_meters sizes
    // the waves against.
    vertexSpacingMeters : f32,

    // Radial lift applied to beat depth-buffer precision, as coefficient * viewDistance^2 (capped).
    // A 24-bit depth buffer with a 0.1 near plane resolves only ~240m at 50 units of view distance and
    // ~5.9km at 250 -- both COARSER than the few-kilometre gap between sea level and the seabed under
    // it, so without this the ocean and the floor beneath it land in the same depth bucket and fight.
    // Scaling with the square of view distance matches how that precision actually falls off, so one
    // coefficient holds from sea level to orbit; the cap stops the lift from growing large enough to
    // stand visibly proud of the shore at extreme distance. See PlanetNode.ComputeDepthLift.
    depthLiftCoefficient : f32,
    depthLiftMaxUnits : f32,
};

@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

// Wrap a lattice cell into [0, period) so the noise repeats exactly every `period` cells. This is what
// makes the mesh's modulo-reduced wave origin a no-op rather than a visible jump.
fn wrap_cell(cell : vec2<f32>, period : f32) -> vec2<f32> {
    return cell - floor(cell / period) * period;
}

fn hash21(p : vec2<f32>) -> f32 {
    var p3 = fract(vec3<f32>(p.xyx) * 0.1031);
    p3 = p3 + dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

fn value_noise2(p : vec2<f32>, period : f32) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let u = f * f * (3.0 - 2.0 * f);

    let c00 = hash21(wrap_cell(i + vec2<f32>(0.0, 0.0), period));
    let c10 = hash21(wrap_cell(i + vec2<f32>(1.0, 0.0), period));
    let c01 = hash21(wrap_cell(i + vec2<f32>(0.0, 1.0), period));
    let c11 = hash21(wrap_cell(i + vec2<f32>(1.0, 1.0), period));

    return mix(mix(c00, c10, u.x), mix(c01, c11, u.x), u.y) * 2.0 - 1.0;
}

// 4-octave FBM. The lacunarity is exactly 2.0 (not the 2.03 the old planet-scale shader used) because
// each octave has to keep wrapping on the same tile: shifting the input by one tile shifts octave i by
// 2^i tiles, which is only an exact whole number of periods when the ratio is a power of two. The
// per-octave offset breaks up the axis alignment that a plain doubling would otherwise show, and being
// a constant it preserves that wrapping.
fn wave_fbm(p : vec2<f32>, cells : f32, cell_size_meters : f32, t : f32) -> f32 {
    let drift = vec2<f32>(t * 0.021, -t * 0.013);
    let spacing = max(custom.vertexSpacingMeters, 0.001);
    var value = 0.0;
    var amplitude = 0.5;
    var frequency = 1.0;
    var offset = vec2<f32>(0.0, 0.0);
    for (var i = 0u; i < 4u; i = i + 1u) {
        // Nyquist guard, per octave: fully present once this octave's own wavelength spans 5+ vertices,
        // gone by the time it spans 2. The base wavelength is already kept above this limit, but the
        // finer octaves are 2/4/8x shorter and would still alias -- doing the guard per octave is what
        // lets the coarse swell survive at an altitude where the fine chop has had to drop out.
        let octave_wavelength = cell_size_meters / frequency;
        let resolvable = smoothstep(2.0, 5.0, octave_wavelength / spacing);
        if (resolvable > 0.0) {
            value = value + value_noise2((p + drift) * frequency + offset, cells * frequency) * amplitude * resolvable;
        }
        frequency = frequency * 2.0;
        amplitude = amplitude * 0.5;
        offset = offset + vec2<f32>(17.0, 23.0);
    }
    return value;
}

// The wavelength actually used, in meters: the authored physical wavelength, but never allowed to drop
// below what this patch's vertex density can describe. From orbit the spacing term dominates and waves
// scale with the patch (so they stay visible instead of vanishing to a sub-pixel ripple); descending
// shrinks the spacing until the authored wavelength takes over and the waves settle at real size.
fn effective_wavelength_meters() -> f32 {
    let spacing_floor = max(custom.vertexSpacingMeters, 0.001) * max(custom.waveVerticesPerWavelength, 4.0);
    return max(custom.waveLengthMeters, spacing_floor);
}

// Wave height at a point on the wave plane, in METERS. `cells` is how many lattice cells fit in one
// wave tile; forcing it to a whole number keeps the tile an exact multiple of the base wavelength.
fn wave_height_meters(plane_meters : vec2<f32>, cells : f32) -> f32 {
    let cell_size_meters = custom.waveTileMeters / cells;
    let t = frame.time * custom.waveSpeed;
    let amplitude_meters = cell_size_meters * custom.waveSteepness;
    return wave_fbm(plane_meters / cell_size_meters, cells, cell_size_meters, t) * amplitude_meters;
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;

    let wavelength = effective_wavelength_meters();
    let cells = max(round(custom.waveTileMeters / max(wavelength, 1.0)), 1.0);

    // Finite-difference step, in meters, along the patch's own (east, north) axes. A fraction of the
    // FINEST octave's wavelength, not the base one -- the finest octave is 8x the base frequency, and
    // stepping across more than a fraction of its cycle turns the estimated slope into noise, which is
    // what reads as flickering specular sparkle.
    let epsilon_meters = max(wavelength, 1.0) * 0.02;

    let height = wave_height_meters(uv0, cells);
    let height_east = wave_height_meters(uv0 + vec2<f32>(epsilon_meters, 0.0), cells);
    let height_north = wave_height_meters(uv0 + vec2<f32>(0.0, epsilon_meters), cells);

    // Displace along the outward radial (the undisplaced sphere normal), converting meters to scene
    // units only at the point of use. The depth lift rides on the same radial, so it changes where the
    // surface sits without touching the wave shape or the normal derived from it below.
    let approx_world = (object.model * vec4<f32>(position, 1.0)).xyz;
    let view_distance = distance(approx_world, frame.camera_position.xyz);
    let depth_lift = min(custom.depthLiftCoefficient * view_distance * view_distance, custom.depthLiftMaxUnits);
    let displaced = position + normal * ((height * custom.unitsPerMeter) + depth_lift);

    let step_units = epsilon_meters * custom.unitsPerMeter;
    let d_east = custom.waveEast * step_units + normal * ((height_east - height) * custom.unitsPerMeter);
    let d_north = custom.waveNorth * step_units + normal * ((height_north - height) * custom.unitsPerMeter);
    // waveEast/waveNorth are built so cross(east, north) is the outward radial, so this cross product
    // needs no sign correction.
    let local_normal = normalize(cross(d_east, d_north));

    let world = object.model * vec4<f32>(displaced, 1.0);
    output.position = frame.view_projection * world;
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
    var specular = 0.0;
    for (var i = 0u; i < 4u; i = i + 1u) {
        let light_pos = frame.light_positions[i].xyz;
        let light_color = frame.light_colors[i].xyz;
        let l = normalize(light_pos - input.world_position);
        let light_strength = saturate(light_color.r + light_color.g + light_color.b);
        diffuse = diffuse + max(dot(n, l), 0.0) * light_strength * 0.05;
        // Water's defining highlight. Cheap Blinn-Phong rather than full PBR: this surface is a single
        // known material, so there is nothing for a metallic/roughness workflow to vary.
        let h = normalize(l + v);
        specular = specular + pow(max(dot(n, h), 0.0), 220.0) * light_strength * 0.5;
    }
    diffuse = saturate(diffuse);

    let fresnel = pow(1.0 - ndotv, 3.0) * custom.fresnelStrength;

    let depth_factor = input.color.a;
    let base_color = mix(custom.shallowColor, custom.deepColor, depth_factor) * diffuse;
    let color = base_color + vec3<f32>(fresnel * 0.3) + vec3<f32>(specular);

    // Baked per-vertex (see this file's header): 1 in the interior, falling to 0 at the rim, so the
    // far shell fading in underneath adds back to roughly full coverage.
    let edge_fade = input.color.r;
    let alpha = saturate(custom.opacity + fresnel * 0.25) * edge_fade;

    return vec4<f32>(color, alpha);
}
