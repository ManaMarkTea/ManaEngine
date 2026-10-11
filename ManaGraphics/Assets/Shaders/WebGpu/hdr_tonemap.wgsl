struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) uv : vec2<f32>,
};

@group(0) @binding(0) var color_sampler : sampler;
@group(0) @binding(1) var hdr_color : texture_2d<f32>;
@group(0) @binding(2) var bloom_color : texture_2d<f32>;
@group(0) @binding(3) var outline_mask : texture_2d<f32>;

@vertex
fn vs_main(@builtin(vertex_index) vertex_index : u32) -> VertexOutput {
    var positions = array<vec2<f32>, 3>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>(3.0, -1.0),
        vec2<f32>(-1.0, 3.0)
    );

    var output : VertexOutput;
    let position = positions[vertex_index];
    output.position = vec4<f32>(position, 0.0, 1.0);
    output.uv = position * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5, 0.5);
    return output;
}

// 1. Extract bright colors
@fragment
fn fs_bright(input : VertexOutput) -> @location(0) vec4<f32> {
    let raw_color = textureSample(hdr_color, color_sampler, input.uv).rgb;
    
    // Clamp maximum bloom input brightness to prevent fireflies and explosive blooming
    let color = clamp(raw_color, vec3<f32>(0.0), vec3<f32>(8.0));
    
    // Calculate relative luminance
    let luma = dot(color, vec3<f32>(0.2126, 0.7152, 0.0722));
    
    // Smooth threshold extraction to prevent hard-edges
    let threshold = 1.0;
    let knee = 0.1;
    let soft = luma - threshold + knee;
    let soft_clamp = clamp(soft, 0.0, 2.0 * knee);
    let rq = soft_clamp * soft_clamp / (4.0 * knee + 0.0001);
    let br = max(rq, luma - threshold) / max(luma, 0.0001);
    
    return vec4<f32>(color * br, 1.0);
}

// 2. Horizontal Gaussian Blur (9-tap)
@fragment
fn fs_blur_h(input : VertexOutput) -> @location(0) vec4<f32> {
    var tex_offset = 1.0 / vec2<f32>(textureDimensions(hdr_color, 0u));
    var weights = array<f32, 5>(0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);
    
    var result = textureSample(hdr_color, color_sampler, input.uv).rgb * weights[0];
    
    for (var i = 1u; i < 5u; i = i + 1u) {
        let offset = vec2<f32>(f32(i) * tex_offset.x, 0.0);
        result = result + textureSample(hdr_color, color_sampler, input.uv + offset).rgb * weights[i];
        result = result + textureSample(hdr_color, color_sampler, input.uv - offset).rgb * weights[i];
    }
    
    return vec4<f32>(result, 1.0);
}

// 3. Vertical Gaussian Blur (9-tap)
@fragment
fn fs_blur_v(input : VertexOutput) -> @location(0) vec4<f32> {
    var tex_offset = 1.0 / vec2<f32>(textureDimensions(hdr_color, 0u));
    var weights = array<f32, 5>(0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);
    
    var result = textureSample(hdr_color, color_sampler, input.uv).rgb * weights[0];
    
    for (var i = 1u; i < 5u; i = i + 1u) {
        let offset = vec2<f32>(0.0, f32(i) * tex_offset.y);
        result = result + textureSample(hdr_color, color_sampler, input.uv + offset).rgb * weights[i];
        result = result + textureSample(hdr_color, color_sampler, input.uv - offset).rgb * weights[i];
    }
    
    return vec4<f32>(result, 1.0);
}

fn scene_color(uv : vec2<f32>) -> vec3<f32> {
    // Explicit LOD keeps edge-dependent FXAA branches valid under WebGPU uniformity rules.
    let hdr = max(textureSampleLevel(hdr_color, color_sampler, uv, 0.0).rgb, vec3<f32>(0.0));
    let bloom = max(textureSampleLevel(bloom_color, color_sampler, uv, 0.0).rgb, vec3<f32>(0.0));
    
    // Super premium soft bloom combination
    let bloom_strength = 0.22;
    let color = hdr + bloom * bloom_strength;
    
    // Aces Filmic Tonemapping for cinema-grade HDR colors
    let a = 2.51;
    let b = 0.03;
    let c = 2.43;
    let d = 0.59;
    let e = 0.14;
    return clamp((color * (a * color + b)) / (color * (c * color + d) + e), vec3<f32>(0.0), vec3<f32>(1.0));
}

fn scene_luma(color : vec3<f32>) -> f32 {
    return dot(sqrt(color), vec3<f32>(0.299, 0.587, 0.114));
}

fn fxaa(uv : vec2<f32>) -> vec3<f32> {
    let pixel_size = 1.0 / vec2<f32>(textureDimensions(hdr_color, 0u));
    let center = scene_color(uv);
    let luma_center = scene_luma(center);
    let nw = scene_luma(scene_color(uv + vec2<f32>(-1.0, -1.0) * pixel_size));
    let ne = scene_luma(scene_color(uv + vec2<f32>(1.0, -1.0) * pixel_size));
    let sw = scene_luma(scene_color(uv + vec2<f32>(-1.0, 1.0) * pixel_size));
    let se = scene_luma(scene_color(uv + vec2<f32>(1.0, 1.0) * pixel_size));
    let luma_min = min(luma_center, min(min(nw, ne), min(sw, se)));
    let luma_max = max(luma_center, max(max(nw, ne), max(sw, se)));

    // Leave low-contrast regions alone so the filter does not soften the whole scene.
    if (luma_max - luma_min < max(0.0312, luma_max * 0.125)) {
        return center;
    }

    var direction = vec2<f32>(-((nw + ne) - (sw + se)), (nw + sw) - (ne + se));
    let reduction = max((nw + ne + sw + se) * (0.25 * 0.125), 1.0 / 128.0);
    let inverse_min = 1.0 / (min(abs(direction.x), abs(direction.y)) + reduction);
    direction = clamp(direction * inverse_min, vec2<f32>(-8.0), vec2<f32>(8.0)) * pixel_size;
    let narrow = 0.5 * (scene_color(uv - direction / 6.0) + scene_color(uv + direction / 6.0));
    let wide = narrow * 0.5 + 0.25 * (scene_color(uv - direction * 0.5) + scene_color(uv + direction * 0.5));
    let luma_wide = scene_luma(wide);
    return select(wide, narrow, luma_wide < luma_min || luma_wide > luma_max);
}

// 4. FXAA on the tone-mapped scene; selection outlines and UI stay crisp above it.
@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    var mapped = fxaa(input.uv);

    let dimensions = vec2<i32>(textureDimensions(outline_mask, 0u));
    let maximum_pixel = dimensions - vec2<i32>(1);
    let pixel = clamp(vec2<i32>(input.uv * vec2<f32>(dimensions)), vec2<i32>(0), maximum_pixel);
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
