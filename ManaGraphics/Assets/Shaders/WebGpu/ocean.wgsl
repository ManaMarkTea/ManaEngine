// Far tier of the two-tier planet ocean: a flat, wave-free sea-level shell (see PlanetOceanMesh).
// Waves live in near_ocean.wgsl, which only renders close to the surface where the vertex spacing can
// actually resolve them -- this file used to displace vertices too, but at ~100km vertex spacing that
// could only ever produce noise, not waves.

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

// Custom per-material uniforms (group 2), parsed by declaration order from this struct - see
// ManaWebGpuCustomUniformSchema.cs.
struct CustomMaterialUniform {
    //PROPERTY(default=0.85, min=0.0, max=1.0)
    opacity : f32,

    //PROPERTY(default=1.0, min=0.0, max=4.0)
    fresnelStrength : f32,

    //PROPERTY(default=[0.01,0.08,0.14], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    deepColor : vec3<f32>,

    //PROPERTY(default=[0.08,0.28,0.34], min=[0.0,0.0,0.0], max=[2.0,2.0,2.0])
    shallowColor : vec3<f32>,

    // Engine-driven (PlanetNode.UpdateNearOcean), not user-editable -- world-space center of the
    // camera-following near-ocean patch, and the band over which this shell fades out beneath it.
    // Without this the two tiers would both blend over the same footprint, reading as a darker,
    // double-covered disc wherever the near patch sits. Inner/outer are world-space distances chosen
    // to be the complement of near_ocean.wgsl's own inward edge fade, so total coverage stays ~1.
    // A non-positive outer radius means "no near patch active": the shell renders everywhere.
    nearFadeCenter : vec3<f32>,
    nearFadeInnerRadius : f32,
    nearFadeOuterRadius : f32,

    // Radial lift applied to beat depth-buffer precision, as coefficient * viewDistance^2 (capped).
    // A 24-bit depth buffer with a 0.1 near plane resolves only ~240m at 50 units of view distance and
    // ~5.9km at 250 -- both COARSER than the few-kilometre gap between sea level and the seabed under
    // it, so without this the shell and the floor beneath it land in the same depth bucket and fight.
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

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;

    // Lift along the outward radial before projecting -- see depthLiftCoefficient's comment. The
    // undisplaced world position is what the lift is measured against, so this shell and the near
    // patch, sharing one coefficient, lift by the same amount at the same place and stay coincident
    // through their cross-fade.
    let flat_world = (object.model * vec4<f32>(position, 1.0)).xyz;
    let view_distance = distance(flat_world, frame.camera_position.xyz);
    let depth_lift = min(custom.depthLiftCoefficient * view_distance * view_distance, custom.depthLiftMaxUnits);

    let world = object.model * vec4<f32>(position + normal * depth_lift, 1.0);
    output.position = frame.view_projection * world;
    output.color = color;
    output.uv0 = uv0;
    output.world_position = world.xyz;
    // This mesh is a sphere centered at the local origin and is never displaced, so the vertex normal
    // is exactly the surface normal -- no finite-difference estimate needed.
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
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
    // data, 1 = deep water -- see OceanSurface.ComputeDepthFactor). diffuse stays a separate
    // brightness multiplier so lighting never decides which color a stretch of water IS.
    let depth_factor = input.color.a;
    let base_color = mix(custom.shallowColor, custom.deepColor, depth_factor) * diffuse;
    let color = base_color + vec3<f32>(fresnel * 0.3);
    var alpha = saturate(custom.opacity + fresnel * 0.25);

    // Cross-fade with the near patch. Chord distance rather than arc: over the near patch's footprint
    // (kilometres on a planet-sized sphere) the two agree to well under a pixel.
    if (custom.nearFadeOuterRadius > 0.0) {
        let distance_to_near = distance(input.world_position, custom.nearFadeCenter);
        alpha = alpha * smoothstep(custom.nearFadeInnerRadius, custom.nearFadeOuterRadius, distance_to_near);
    }

    return vec4<f32>(color, alpha);
}
