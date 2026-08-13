// ManaUI's entire draw pipeline: one instanced, analytically-antialiased rounded box.
//
// Panels, borders, glyphs, images, focus rings and drop shadows are all the same primitive with different
// flags, which is what lets a whole UI frame go out as a single draw call. Clipping is per-instance
// (discarded in the fragment shader) rather than via scissor, because a scissor change would force a
// draw-call split and cannot express a rounded clip at all.
//
// Instance layout must stay in lockstep with ManaUI.ManaUiInstance (88 bytes).

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

const FLAG_GLYPH  : u32 = 1u;
const FLAG_TEXTURE: u32 = 2u;
const FLAG_SHADOW : u32 = 4u;
const FLAG_GRADIENT : u32 = 8u;

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
    // Per-instance clipping. Shadows are exempt on their own quad but still respect the clip rect.
    let p = input.local_position;
    if (p.x < input.clip.x || p.y < input.clip.y || p.x > input.clip.z || p.y > input.clip.w) {
        discard;
    }

    // Text: the atlas red channel is coverage, modulating the fill colour.
    if ((input.flags & FLAG_GLYPH) != 0u) {
        let glyph_coverage = textureSample(glyph_atlas, atlas_sampler, input.uv).r;
        let alpha = input.fill_color.a * glyph_coverage;
        if (alpha <= 0.0) {
            discard;
        }

        // Premultiplied: the blend state is (One, OneMinusSrcAlpha).
        return vec4<f32>(input.fill_color.rgb * alpha, alpha);
    }

    let centre = (input.rect.xy + input.rect.zw) * 0.5;
    let half_size = (input.rect.zw - input.rect.xy) * 0.5;

    // Rotating the sample point the other way is the same as rotating the box, and costs nothing when the
    // rotation is zero.
    var local = p - centre;
    if (input.rotation != 0.0) {
        let c = cos(-input.rotation);
        let s = sin(-input.rotation);
        local = vec2<f32>(local.x * c - local.y * s, local.x * s + local.y * c);
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
    if ((input.flags & FLAG_GRADIENT) != 0u) {
        let height = max(input.rect.w - input.rect.y, 0.0001);
        let t = clamp((local.y + half_size.y) / height, 0.0, 1.0);
        base_fill = mix(input.fill_color, input.gradient_color, t);
    }

    let distance = sd_rounded_box(local, half_size, input.corners);
    let outer = coverage(distance);
    if (outer <= 0.0) {
        discard;
    }

    var rgb = vec3<f32>(0.0);
    var alpha = 0.0;

    if (input.border_width > 0.0) {
        // Fill occupies everything inside the border; the ring is the difference between the two coverages,
        // which keeps the seam between them exactly antialiased rather than double-blended.
        let inner = coverage(distance + input.border_width);
        let ring = max(outer - inner, 0.0);

        var fill = base_fill;
        if ((input.flags & FLAG_TEXTURE) != 0u) {
            fill = fill * textureSample(image_atlas, atlas_sampler, input.uv);
        }

        rgb = fill.rgb * fill.a * inner + input.border_color.rgb * input.border_color.a * ring;
        alpha = fill.a * inner + input.border_color.a * ring;
    } else {
        var fill = base_fill;
        if ((input.flags & FLAG_TEXTURE) != 0u) {
            fill = fill * textureSample(image_atlas, atlas_sampler, input.uv);
        }

        alpha = fill.a * outer;
        rgb = fill.rgb * alpha;
    }

    if (alpha <= 0.0) {
        discard;
    }

    return vec4<f32>(rgb, alpha);
}
