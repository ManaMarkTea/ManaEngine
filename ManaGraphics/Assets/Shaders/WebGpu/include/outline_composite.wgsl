// The selection outline over the finished picture: the outline pass leaves a mask (colour, and in alpha a packed width
// and opacity) at the selected meshes' pixels; this draws a ring of that width round them. Shared by the tone map and
// SMAA's last pass, so outlines stay crisp over every kind of anti-aliasing.
fn mana_outline_composite(scene : vec3<f32>, uv : vec2<f32>, outline_mask : texture_2d<f32>) -> vec4<f32> {
    var mapped = scene;
    let dimensions = vec2<i32>(textureDimensions(outline_mask, 0u));
    let maximum_pixel = dimensions - vec2<i32>(1);
    let pixel = clamp(vec2<i32>(uv * vec2<f32>(dimensions)), vec2<i32>(0), maximum_pixel);
    let center_mask = textureLoad(outline_mask, pixel, 0);
    var outline_color = vec3<f32>(0.0);
    var best_coverage = 0.0;

    if (center_mask.a <= 0.001) {
        for (var y = -4; y <= 4; y = y + 1) {
            for (var x = -4; x <= 4; x = x + 1) {
                let distance = length(vec2<f32>(f32(x), f32(y)));
                if (distance <= 0.0 || distance > 4.0) {
                    continue;
                }

                let sample_pixel = clamp(pixel + vec2<i32>(x, y), vec2<i32>(0), maximum_pixel);
                let mask = textureLoad(outline_mask, sample_pixel, 0);
                let packed_outline = round(mask.a * 255.0);
                let width_code = floor(packed_outline / 64.0);
                let opacity = (packed_outline - width_code * 64.0) / 63.0;
                let width_pixels = width_code + 1.0;
                let coverage = clamp(width_pixels - distance + 1.0, 0.0, 1.0) * opacity;
                if (coverage > best_coverage) {
                    best_coverage = coverage;
                    outline_color = mask.rgb;
                }
            }
        }
    }

    mapped = mix(mapped, outline_color, best_coverage);

    return vec4<f32>(mapped, 1.0);
}
