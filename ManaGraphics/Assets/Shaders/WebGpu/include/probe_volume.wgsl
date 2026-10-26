// The level's baked light probes (ManaWebGpuProbeVolume) as the scene shaders read them: L1 harmonics per channel
// in three 3D textures over the box frame.probe_volume_min / probe_volume_extent describe. Needs `frame`.

@group(0) @binding(6) var probe_sh_r : texture_3d<f32>;
@group(0) @binding(7) var probe_sh_g : texture_3d<f32>;
@group(0) @binding(8) var probe_sh_b : texture_3d<f32>;
@group(0) @binding(9) var probe_sampler : sampler;

// The light reaching a surface at world_position facing n, as E / pi (the irradiance cube's units), in rgb; w how
// much of it to use: 1 well inside the box, fading to 0 over its last probe spacing, 0 with no baked volume.
fn mana_probe_irradiance(world_position : vec3<f32>, n : vec3<f32>) -> vec4<f32> {
    if (frame.probe_volume_min.w < 0.5) {
        return vec4<f32>(0.0);
    }
    let spacing = frame.probe_volume_extent.w;
    // Half a spacing out along the normal: the probes on the surface's own side, not those buried in its wall.
    let at = world_position + n * spacing * 0.5;
    let extent = frame.probe_volume_extent.xyz;
    let local = at - frame.probe_volume_min.xyz;
    let inside = min(local, extent - local);
    let weight = saturate(min(inside.x, min(inside.y, inside.z)) / spacing);
    if (weight <= 0.0) {
        return vec4<f32>(0.0);
    }
    let uvw = local / extent;
    let r = textureSampleLevel(probe_sh_r, probe_sampler, uvw, 0.0);
    let g = textureSampleLevel(probe_sh_g, probe_sampler, uvw, 0.0);
    let b = textureSampleLevel(probe_sh_b, probe_sampler, uvw, 0.0);
    let basis = vec4<f32>(0.282095, (2.0 / 3.0) * 0.488603 * n.x, (2.0 / 3.0) * 0.488603 * n.y, (2.0 / 3.0) * 0.488603 * n.z);
    let light = max(vec3<f32>(dot(r, basis), dot(g, basis), dot(b, basis)), vec3<f32>(0.0));
    return vec4<f32>(light, weight);
}
