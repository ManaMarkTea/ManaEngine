// ManaUI's entire draw pipeline: one instanced, analytically-antialiased rounded box.
//
// Panels, borders, glyphs, images, focus rings and drop shadows are all the same primitive with different
// flags, which is what lets a whole UI frame go out as a single draw call. Clipping is per-instance
// (discarded in the fragment shader) rather than via scissor, because a scissor change would force a
// draw-call split and cannot express a rounded clip at all.
//
// Instance layout must stay in lockstep with ManaUI.ManaUiInstance (92 bytes).

struct Uniforms {
    // Logical UI size. Positions arrive in logical pixels; the surface is physical, and the ratio between
    // them is exactly what makes the UI resolution-independent.
    viewport_size : vec2<f32>,
    atlas_size : vec2<f32>,
};

@group(0) @binding(0) var<uniform> uniforms : Uniforms;
@group(0) @binding(1) var atlas_sampler : sampler;
@group(0) @binding(2) var glyph_atlas : texture_2d<f32>;
@group(0) @binding(3) var image_atlas : texture_2d<f32>;

// A blurred snapshot of everything drawn under the UI. Bound to a 1x1 placeholder when nothing asked for
// it, so the layout never changes and the pipeline is built once.
@group(0) @binding(4) var backdrop : texture_2d<f32>;

// Its own sampler: atlas_sampler clamps LOD to 1 so a glyph can never land on a mip, and the backdrop is
// nothing but mips. Shared, a level-4 blur samples level 1 and looks like no blur at all.
@group(0) @binding(5) var backdrop_sampler : sampler;

const FLAG_GLYPH  : u32 = 1u;
const FLAG_TEXTURE: u32 = 2u;
const FLAG_SHADOW : u32 = 4u;
const FLAG_GRADIENT : u32 = 8u;
const FLAG_BACKDROP : u32 = 16u;

struct InstanceInput {
    @location(0) rect : vec4<f32>,          // minX, minY, maxX, maxY
    @location(1) uv : vec4<f32>,            // u0, v0, u1, v1  (shadows reuse this for the source box)
    @location(2) corners : vec4<f32>,       // tl, tr, br, bl
    @location(3) clip : vec4<f32>,          // minX, minY, maxX, maxY
    @location(4) fill_color : u32,
    @location(5) border_color : u32,
    @location(6) border_width : f32,        // doubles as shadow blur radius
    @location(7) flags : u32,
    @location(8) gradient_color : u32,
    @location(9) rotation : f32,
    @location(10) backdrop_blur : f32,
};

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) local_position : vec2<f32>,    // pixel position, for the SDF
    @location(1) uv : vec2<f32>,
    @location(2) @interpolate(flat) rect : vec4<f32>,
    @location(3) @interpolate(flat) corners : vec4<f32>,
    @location(4) @interpolate(flat) clip : vec4<f32>,
    @location(5) @interpolate(flat) fill_color : vec4<f32>,
    @location(6) @interpolate(flat) border_color : vec4<f32>,
    @location(7) @interpolate(flat) border_width : f32,
    @location(8) @interpolate(flat) flags : u32,
    @location(10) @interpolate(flat) gradient_color : vec4<f32>,
    // The unmodified instance uv. Distinct from the interpolated `uv` above because the shadow path needs
    // the source box as a whole rather than this fragment's position within it.
    @location(9) @interpolate(flat) source_rect : vec4<f32>,
    @location(11) @interpolate(flat) rotation : f32,
    @location(12) @interpolate(flat) backdrop_blur : f32,
};

fn unpack_rgba(packed : u32) -> vec4<f32> {
    return vec4<f32>(
        f32(packed & 0xFFu),
        f32((packed >> 8u) & 0xFFu),
        f32((packed >> 16u) & 0xFFu),
        f32((packed >> 24u) & 0xFFu)
    ) / 255.0;
}

fn srgb_to_linear_channel(value : f32) -> f32 {
    if (value <= 0.04045) {
        return value / 12.92;
    }

    return pow((value + 0.055) / 1.055, 2.4);
}

// Colours are authored as sRGB hex. The surface expects linear, matching what the ImGui pass already does,
// so the same hex value renders identically in both.
fn srgb_to_linear(color : vec4<f32>) -> vec4<f32> {
    return vec4<f32>(
        srgb_to_linear_channel(color.r),
        srgb_to_linear_channel(color.g),
        srgb_to_linear_channel(color.b),
        color.a
    );
}

@vertex
fn vs_main(
    @builtin(vertex_index) vertex_index : u32,
    instance : InstanceInput
) -> VertexOutput {
    // A unit quad as a triangle strip: (0,0) (1,0) (0,1) (1,1). No vertex buffer needed.
    let corner = vec2<f32>(
        f32(vertex_index & 1u),
        f32((vertex_index >> 1u) & 1u)
    );

    // The quad has to cover the box after it turns, so it is sized from the rotated bounding extent rather
    // than from the rect. At zero rotation this is exactly mix(rect.xy, rect.zw, corner), so the ordinary
    // path is unchanged.
    let centre = (instance.rect.xy + instance.rect.zw) * 0.5;
    let half_extent = (instance.rect.zw - instance.rect.xy) * 0.5;
    let cos_r = cos(instance.rotation);
    let sin_r = sin(instance.rotation);
    let bounds = vec2<f32>(
        abs(cos_r) * half_extent.x + abs(sin_r) * half_extent.y,
        abs(sin_r) * half_extent.x + abs(cos_r) * half_extent.y
    );

    let position = centre + (corner * 2.0 - 1.0) * bounds;

    var output : VertexOutput;

    // Logical pixels -> clip space, with Y flipped because UI space grows downward.
    let normalized = position / uniforms.viewport_size;
    output.clip_position = vec4<f32>(
        normalized.x * 2.0 - 1.0,
        1.0 - normalized.y * 2.0,
        0.0,
        1.0
    );

    output.local_position = position;
    output.uv = mix(instance.uv.xy, instance.uv.zw, corner);
    output.rect = instance.rect;
    output.corners = instance.corners;
    output.clip = instance.clip;
    output.fill_color = srgb_to_linear(unpack_rgba(instance.fill_color));
    output.border_color = srgb_to_linear(unpack_rgba(instance.border_color));
    output.border_width = instance.border_width;
    output.flags = instance.flags;
    output.gradient_color = srgb_to_linear(unpack_rgba(instance.gradient_color));
    output.source_rect = instance.uv;
    output.rotation = instance.rotation;
    output.backdrop_blur = instance.backdrop_blur;
    return output;
}

/// Radius for the quadrant a point falls in. `corners` is (top-left, top-right, bottom-right, bottom-left)
/// and `p` is relative to the box centre with Y growing downward.
fn corner_radius(p : vec2<f32>, corners : vec4<f32>) -> f32 {
    if (p.y < 0.0) {
        return select(corners.x, corners.y, p.x > 0.0);
    }

    return select(corners.w, corners.z, p.x > 0.0);
}

/// Signed distance to a rounded box: negative inside, zero on the edge, positive outside.
fn sd_rounded_box(p : vec2<f32>, half_size : vec2<f32>, corners : vec4<f32>) -> f32 {
    // Clamped so an over-large radius degrades to a capsule instead of inverting the field.
    let radius = min(corner_radius(p, corners), min(half_size.x, half_size.y));
    let q = abs(p) - half_size + vec2<f32>(radius);
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2<f32>(0.0))) - radius;
}

/// Distance to coverage, antialiased over one screen pixel. `fwidth` gives the footprint automatically, so
/// this stays correct under any DPI scale without the shader knowing what the scale is.
fn coverage(distance : f32) -> f32 {
    let width = fwidth(distance);
    if (width <= 0.0) {
        return select(0.0, 1.0, distance <= 0.0);
    }

    return clamp(0.5 - distance / width, 0.0, 1.0);
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // Everything in this block runs unconditionally, before any flag branch, and stays that way even
    // though most of it is only meaningful for one particular flag combination. WGSL requires both
    // textureSample (unlike textureSampleLevel, used for the backdrop further down) and fwidth (inside
    // coverage(), called for `outer`/`inner` below) to run in uniform control flow, since the implicit
    // derivative either needs comes from comparing this invocation against its neighbours in the same 2x2
    // quad -- and the taint isn't just "textually inside an `if`": Chrome's real WGSL validator (Naga)
    // treats ALL code textually after the first flag-dependent branch as non-uniform for the rest of the
    // function, not only code nested inside that branch, which is why hoisting `inner`'s own computation
    // out of `if (input.border_width > 0.0)` alone (a narrower first attempt) still failed with the same
    // error -- confirmed live, twice, via the headless Chrome probe used elsewhere in this file's own
    // porting work. Desktop's wgpu-native backend enforces none of this and let the original, narrower
    // code compile silently (wrong or merely lucky, not verified either way). A fragment that ends up not
    // needing one of these values just paid for computing it -- cheap next to a whole draw call being
    // invalid.
    let glyph_sample = textureSample(glyph_atlas, atlas_sampler, input.uv);
    let image_sample = textureSample(image_atlas, atlas_sampler, input.uv);

    let centre = (input.rect.xy + input.rect.zw) * 0.5;
    let half_size = (input.rect.zw - input.rect.xy) * 0.5;

    // Rotating the sample point the other way is the same as rotating the box, and costs nothing when the
    // rotation is zero.
    var local = input.local_position - centre;
    if (input.rotation != 0.0) {
        let c = cos(-input.rotation);
        let s = sin(-input.rotation);
        local = vec2<f32>(local.x * c - local.y * s, local.x * s + local.y * c);
    }

    let distance = sd_rounded_box(local, half_size, input.corners);
    // Square image quads already have rasterizer coverage and texture alpha. Applying
    // the box SDF again fades their corner pixels, exposing every nine-slice junction.
    let box_coverage = coverage(distance);
    let square_image = (input.flags & FLAG_TEXTURE) != 0u
        && input.rotation == 0.0 && all(input.corners == vec4<f32>(0.0));
    let outer = select(box_coverage, 1.0, square_image);
    let inner = coverage(distance + input.border_width);

    // Per-instance clipping. Shadows are exempt on their own quad but still respect the clip rect.
    let p = input.local_position;
    if (p.x < input.clip.x || p.y < input.clip.y || p.x > input.clip.z || p.y > input.clip.w) {
        discard;
    }

    // Text: the atlas red channel is coverage, modulating the fill colour.
    if ((input.flags & FLAG_GLYPH) != 0u) {
        // The atlas stores stb_truetype's raw geometric coverage, and fill_color was already converted to
        // linear above to match this renderer's linear-blend/sRGB-encode swapchain -- physically correct,
        // but not what text rasterizers conventionally calibrate for: blending a coverage alpha in linear
        // light under-represents partial-coverage edge pixels far more for dark text than light text,
        // which is what read as Light theme's text "not being crisp" (thinner strokes than Dark theme's
        // text, which happened to look fine since light-on-dark isn't affected the same way). Boosting
        // coverage with a gamma curve is the standard fix, applied only when the text itself is dark --
        // i.e. only in the case that actually looked wrong -- so light text on a dark background, already
        // correct, is left alone.
        let text_luma = dot(input.fill_color.rgb, vec3<f32>(0.2126, 0.7152, 0.0722));
        var glyph_coverage = glyph_sample.r;
        if (text_luma < 0.5) {
            glyph_coverage = pow(glyph_coverage, 1.0 / 1.8);
        }

        let alpha = input.fill_color.a * glyph_coverage;
        if (alpha <= 0.0) {
            discard;
        }

        // Premultiplied: the blend state is (One, OneMinusSrcAlpha).
        return vec4<f32>(input.fill_color.rgb * alpha, alpha);
    }

    if ((input.flags & FLAG_SHADOW) != 0u) {
        // The shadow's own quad is inflated past the box it belongs to; source_rect carries the original
        // box so the falloff is measured from the real edge.
        let source_centre = (input.source_rect.xy + input.source_rect.zw) * 0.5;
        let source_half = (input.source_rect.zw - input.source_rect.xy) * 0.5;
        let distance = sd_rounded_box(p - source_centre, source_half, input.corners);

        // smoothstep over the blur radius is a good enough stand-in for a real gaussian at UI scale, and
        // costs one instruction instead of a separable blur pass.
        let falloff = 1.0 - smoothstep(0.0, max(input.border_width, 0.0001), distance);
        let alpha = input.fill_color.a * falloff * falloff;
        if (alpha <= 0.0) {
            discard;
        }

        return vec4<f32>(input.fill_color.rgb * alpha, alpha);
    }

    // Interpolated down the box. Mixed in linear space, which is the whole reason the colours were
    // converted on the way in: a gradient blended in sRGB darkens through the middle.
    var base_fill = input.fill_color;

    // The blur is behind the fill, not mixed into it: the fill's own alpha is what decides how much of the
    // scene survives, which is the same thing a translucent panel already meant.
    var behind = vec4<f32>(0.0);
    if ((input.flags & FLAG_BACKDROP) != 0u) {
        // The snapshot covers the whole window, so this fragment's own position is its texture coordinate.
        // Levels of the blur chain are progressively blurrier, so the radius picks one.
        let level = clamp(log2(max(input.backdrop_blur, 1.0)), 0.0, f32(textureNumLevels(backdrop) - 1u));
        let sample_uv = p / uniforms.viewport_size;
        behind = textureSampleLevel(backdrop, backdrop_sampler, sample_uv, level);
    }

    if ((input.flags & FLAG_GRADIENT) != 0u) {
        let height = max(input.rect.w - input.rect.y, 0.0001);
        let t = clamp((local.y + half_size.y) / height, 0.0, 1.0);
        base_fill = mix(input.fill_color, input.gradient_color, t);
    }

    // distance/outer/inner: computed once, up top, alongside local/half_size -- see the comment there.
    if (outer <= 0.0) {
        discard;
    }

    var rgb = vec3<f32>(0.0);
    var alpha = 0.0;

    if (input.border_width > 0.0) {
        // Fill occupies everything inside the border; the ring is the difference between the two coverages,
        // which keeps the seam between them exactly antialiased rather than double-blended.
        let ring = max(outer - inner, 0.0);

        var fill = base_fill;
        if ((input.flags & FLAG_TEXTURE) != 0u) {
            fill = fill * image_sample;
        }

        rgb = fill.rgb * fill.a * inner + input.border_color.rgb * input.border_color.a * ring;
        alpha = fill.a * inner + input.border_color.a * ring;
    } else {
        var fill = base_fill;
        if ((input.flags & FLAG_TEXTURE) != 0u) {
            fill = fill * image_sample;
        }

        alpha = fill.a * outer;
        rgb = fill.rgb * alpha;
    }

    // Composited under whatever the fill produced, then the whole thing is made opaque: the backdrop is
    // already a picture of what is behind, so letting more of the real scene through as well would show it
    // twice, once sharp and once blurred.
    if ((input.flags & FLAG_BACKDROP) != 0u) {
        rgb = rgb + behind.rgb * (1.0 - alpha) * outer;
        alpha = outer;
    }

    if (alpha <= 0.0) {
        discard;
    }

    return vec4<f32>(rgb, alpha);
}
