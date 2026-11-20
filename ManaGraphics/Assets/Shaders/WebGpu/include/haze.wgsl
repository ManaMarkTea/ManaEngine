// Haze over a planet seen from inside or above its air (ManaWebGpuFrameUniform's haze fields; off when haze_color.w is
// 0): the air between the camera and a point, thinning with height, blends the point toward the sunlit air's colour.

// Points along the part of the ray inside the air.
const HazeSamples : i32 = 8;

fn mana_haze(colour : vec3<f32>, world_position : vec3<f32>) -> vec3<f32> {
    let density = frame.haze_color.w;
    if (density <= 0.0) {
        return colour;
    }
    let centre = frame.haze_planet.xyz;
    let radius = frame.haze_planet.w;
    let scale_height = frame.haze_sun.w;
    let top = radius + scale_height * 8.0;

    // Only the stretch inside the air counts, so from orbit the samples are not spent on empty space.
    let eye = frame.camera_position.xyz - centre;
    let surface = world_position - centre;
    let span = surface - eye;
    let span_length = length(span);
    if (span_length <= 0.0) {
        return colour;
    }
    let direction = span / span_length;
    let b = dot(eye, direction);
    let c = dot(eye, eye) - top * top;
    let d = b * b - c;
    if (d <= 0.0) {
        return colour;
    }
    let enter = clamp(-b - sqrt(d), 0.0, span_length);
    let step = (span_length - enter) / f32(HazeSamples);

    var depth = 0.0;
    for (var i = 0; i < HazeSamples; i = i + 1) {
        let point = eye + direction * (enter + (f32(i) + 0.5) * step);
        depth = depth + exp(-max(length(point) - radius, 0.0) / scale_height);
    }
    // Squared, as a game's distance fog: the near ground stays clear and the haze closes in toward the horizon.
    let air = density * depth * step;
    let amount = 1.0 - exp(-air * air);

    // Lit where the sun is up over the point, dark air over the night side.
    let lit = smoothstep(-0.12, 0.3, dot(normalize(surface), frame.haze_sun.xyz));
    return mix(colour, frame.haze_color.rgb * lit, amount);
}
