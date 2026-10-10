// The catalogue's stars and constellation lines, shared by skybox.wgsl and skybox_mobile.wgsl. Both declare `sky`
// (SkyboxUniform) and `constellations` (binding 4, ConstellationTable from constellations.wgsl).

// The catalogue's real stars, and the lines joining them when asked, for a direction in the sky's own frame
// (east, up, north). Turned into the equatorial frame the catalogue is written in, so they rise and set with the date.
fn to_equatorial(direction : vec3<f32>) -> vec3<f32> {
    return sky.celestial_east.xyz * direction.x + sky.celestial_up.xyz * direction.y + sky.celestial_north.xyz * direction.z;
}

fn has_celestial_frame() -> bool {
    return dot(sky.celestial_up.xyz, sky.celestial_up.xyz) > 0.5;
}

fn catalogue_stars(direction : vec3<f32>) -> vec3<f32> {
    if (!has_celestial_frame()) {
        return vec3<f32>(0.0);
    }
    let equatorial = to_equatorial(direction);

    var total = vec3<f32>(0.0);
    for (var i = 0u; i < STAR_COUNT; i = i + 1u) {
        let star = constellations.stars[i];
        // Squared angle between them; stars further than about 0.6 degrees add nothing.
        let angle_squared = 2.0 * (1.0 - dot(equatorial, star.xyz));
        if (angle_squared > 0.0002) {
            continue;
        }
        let magnitude = star.w;
        let sigma = 0.0013 * (1.0 + 0.3 * clamp(2.5 - magnitude, 0.0, 3.0));
        let brightness = 2.4 * pow(0.55, magnitude);
        let warmth = constellations.warmth[i / 4u][i % 4u];
        let colour = mix(mix(vec3<f32>(0.7, 0.8, 1.0), vec3<f32>(1.0, 0.95, 0.88), clamp(warmth * 2.0, 0.0, 1.0)), vec3<f32>(1.0, 0.55, 0.35), clamp(warmth * 2.0 - 1.0, 0.0, 1.0));
        total = total + colour * brightness * exp(-angle_squared / (2.0 * sigma * sigma));
    }

    let lines = sky.celestial_east.w;
    if (lines > 0.0) {
        // Each figure's lines come up as the view turns toward it: the centre of the view, in the stars' frame.
        let view_ray = sky.inverse_direction_view_projection * vec4<f32>(0.0, 0.0, 1.0, 1.0);
        let view_centre = to_equatorial(normalize(view_ray.xyz / view_ray.w));
        var line = 0.0;
        for (var i = 0u; i < SEGMENT_COUNT; i = i + 1u) {
            let pair = constellations.segments[i];
            let gaze = dot(view_centre, constellations.centres[pair.z].xyz);
            // Full within about 20 degrees of the figure's middle, gone past about 45.
            let shown = select(smoothstep(0.707, 0.94, gaze), 1.0, lines > 1.5);
            if (shown <= 0.0) {
                continue;
            }
            let a = constellations.stars[pair.x].xyz;
            let b = constellations.stars[pair.y].xyz;
            let normal = normalize(cross(a, b));
            // Between the two ends, and within a pixel or so of the great circle joining them.
            let side_a = dot(cross(a, equatorial), normal);
            let side_b = dot(cross(equatorial, b), normal);
            if (side_a > 0.0 && side_b > 0.0) {
                line = max(line, shown * (1.0 - smoothstep(0.0, 0.0011, abs(dot(equatorial, normal)))));
            }
        }
        total = total + vec3<f32>(0.35, 0.5, 0.8) * line * 0.22 * min(lines, 1.0);
    }
    return total;
}
