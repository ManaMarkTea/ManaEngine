struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) color : vec4<f32>,
    @location(1) uv0 : vec2<f32>,
    @location(2) local_pos : vec3<f32>,
    @location(3) world_position : vec3<f32>,
    @location(4) world_normal : vec3<f32>,
}

#include "include/uniforms.wgsl"
#include "include/point_lights.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
#include "include/shadows.wgsl"
@group(1) @binding(0) var<uniform> object : ObjectUniform;
@group(1) @binding(1) var color_sampler : sampler;
@group(1) @binding(2) var color_texture : texture_2d<f32>;

fn srgb_to_linear_channel(value : f32) -> f32 {
    if (value <= 0.04045) {
        return value / 12.92;
    }

    return pow((value + 0.055) / 1.055, 2.4);
}

fn srgb_to_linear(value : vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        srgb_to_linear_channel(value.r),
        srgb_to_linear_channel(value.g),
        srgb_to_linear_channel(value.b)
    );
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    var output : VertexOutput;
    output.position = frame.view_projection * object.model * vec4<f32>(position, 1.0);
    output.color = color;
    output.uv0 = uv0;
    output.local_pos = position;
    output.world_position = (object.model * vec4<f32>(position, 1.0)).xyz;
    output.world_normal = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    return output;
}

// Must match VoxelEditorGameMode.cs's gridSize -- the whole-plane boundary tier and the UV
// reconstruction below both assume a plane's 0..1 UV spans exactly this many world units.
const RoomSize : f32 = 256.0;

// The world axes, in the transform gizmo's colours (UiTheme.AxisX/AxisY/AxisZ).
const AxisXColor : vec3<f32> = vec3<f32>(0.780, 0.329, 0.314);
const AxisYColor : vec3<f32> = vec3<f32>(0.427, 0.627, 0.353);
const AxisZColor : vec3<f32> = vec3<f32>(0.353, 0.529, 0.780);

// How far, in pixels, a point is from the world axis running along `along` (0 = X, 1 = Y, 2 = Z): the
// larger of its distances from the two planes that meet in that axis.
fn axis_pixels(position : vec3<f32>, footprint : vec3<f32>, along : u32) -> f32 {
    let off = abs(position) / footprint;
    if (along == 0u) {
        return max(off.y, off.z);
    }
    if (along == 1u) {
        return max(off.x, off.z);
    }
    return max(off.x, off.y);
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // See-through from behind: a surface is dropped only when the camera is on its outer side, so flying
    // outside the room removes the wall between you and the room but leaves the other faces drawn. The
    // mesh normals' signs aren't consistent (a mirrored far wall reuses its near wall's winding), so the
    // normal is flipped to point at the room's centre first. The room is convex, so a camera inside it is
    // always on the inner side of every surface and nothing is ever dropped from within.
    // The room is centred on the origin in X and Z (VoxelEditorGameMode.BuildGridRoom), and stands on Y = 0.
    let toCenter = vec3<f32>(0.0, RoomSize * 0.5, 0.0) - input.world_position;
    var inward = normalize(input.world_normal);
    if (dot(inward, toCenter) < 0.0) {
        inward = -inward;
    }
    if (dot(inward, frame.camera_position.xyz - input.world_position) < 0.0) {
        discard;
    }

    // object.uv_transform lets a trimmed plane (the floor/wall quads cut back for VoxelEditorGameMode's
    // rounded-corner coves) remap its own 0..1 UV back onto the coordinate the untrimmed room-sized grid
    // expects, so tiles keep their true size and stay aligned across the seam with each cove fillet mesh.
    let uv = (input.uv0 * object.uv_transform.xy + object.uv_transform.zw) * RoomSize;

    // Compute derivative for antialiasing
    let derivative = fwidth(uv);

    let aGrid = abs(fract(uv / RoomSize - 0.5) - 0.5) / (derivative / RoomSize);
    let cGrid = abs(fract(uv / 0.1 - 0.5) - 0.5) / (derivative / 0.1);

    // How many world units one screen pixel covers here -- not straight-line camera distance, which
    // misses why a floor gets dense toward the horizon: a floor viewed at a grazing angle has a huge
    // derivative (a one-pixel move crosses many world units) long before it is actually far away, while a
    // wall viewed face-on keeps a small, roughly constant derivative regardless of distance. Fading on
    // this instead is what makes a wall you're looking straight at stay untouched -- exactly the "fine at
    // this distance once the wall is facing us" case a distance-only fade got wrong -- while a receding
    // floor still fades. The same idea a mip/anisotropic filter uses to pick its detail level.
    let pixelFootprint = max(derivative.x, derivative.y);

    // Themed rather than fixed: object.material_albedo is the floor tint, object.material_emission.rgb is
    // the line tint, both set per-theme in C# (see UiTheme.GridFloor/GridLine and VoxelEditorGameMode's
    // grid quads) so the same shader draws a light-mode graph-paper floor and an LCARS holodeck grid
    // alike. material_emission.a doubles as a line-thickness multiplier (see UiTheme.GridLineThickness) --
    // the alpha channel is otherwise unused here, the same way PBR shaders reuse it for emissive strength.
    let fillEven = object.material_albedo;

    // 0.9645, not a rounder-looking 0.92 -- this is the actual ratio between the original shader's two
    // hardcoded checkerboard colours (0.7490.../0.7765... etc averaged across channels), from back when
    // fillOdd was its own constant rather than computed from fillEven. 0.92 was a guess made when this
    // became theme-driven, and it darkened the checkerboard's alternating squares distinctly more than
    // the original ever did.
    let fillOdd = vec4<f32>(object.material_albedo.rgb * 0.9645, 1.0);

    // Exponential rather than smoothstep: smoothstep(a,b,x) has ZERO slope at both ends of its range but
    // its STEEPEST slope right in the middle, which is exactly what read as the fade hitting hardest at
    // mid-distance. An exponential approach curve front-loads the change instead -- fastest right as the
    // footprint starts growing, tapering off more and more the further out it goes -- so there is no
    // concentrated "hit" anywhere, just a gradual, ever-slowing approach toward the floor value. tau is
    // the characteristic distance: the curve is ~95% of the way to its floor by 3*tau, ~98% by 4*tau.
    let fadeTau = 0.33;
    let fadeT = 1.0 - exp(-pixelFootprint / fadeTau);

    // Fades every line tier -- sub-grid, main tile, and whole-plane boundary alike -- toward the FLOOR
    // colour (not toward black) as the footprint grows, on top of (not instead of) the sub-grid's fade and
    // the main tier's LOD doubling: a line that recedes into its own floor reads as fading away, in either
    // a dark or a light theme. Multiplying toward black instead (an earlier version of this) worked only
    // by coincidence for LCARS's near-black floor -- against Light's pale floor it made the line pull
    // toward black, i.e. MORE contrast with distance, the opposite of fading. Never fully to the floor
    // colour -- a perfectly invisible line at extreme distance would just be an odd-shaped hole in the
    // checkerboard rather than a faded line, but close to it.
    let lineDim = mix(1.0, 0.12, fadeT);
    let dimmedLineRgb = mix(object.material_albedo.rgb, object.material_emission.rgb, lineDim);
    let lineColor = vec4<f32>(dimmedLineRgb, 1.0);

    // Lines also get thinner with distance, not just dimmer -- the antialiasing width every tier's
    // smoothstep uses below is scaled by this alongside lineThickness (see UiTheme.GridLineThickness),
    // so a line that would otherwise stay a constant N pixels wide forever instead narrows as it recedes.
    let thicknessFade = mix(1.0, 0.35, fadeT);
    let lineThickness = object.material_emission.a * thicknessFade;

    // A dim tint of the line colour rather than the line colour itself -- the 1-unit sub-grid is meant to
    // read as fine graph-paper texture, not another tier of the same bright line, which is what made
    // removing it entirely feel necessary before this dimmer version existed.
    //
    // Darken the floor FIRST, then tint toward the line colour -- blending straight from floor to line
    // only darkens meaningfully when the two are far apart (LCARS: near-black floor, bright gold line).
    // Light's floor and line are both light greys close together, so 18% of that tiny gap barely moved
    // the colour at all, reading as "still basically the floor" rather than a visible sub-grid line. A
    // flat darken guarantees real contrast against the floor regardless of how close floor and line
    // start out; the tint afterward is what still gives LCARS's sub-grid its warm gold cast.
    let minorBase = object.material_albedo.rgb * 0.9;
    let minorLineColor = vec4<f32>(mix(minorBase, dimmedLineRgb, 0.15), 1.0);

    // The sub-grid's 1-unit cells alias into visual noise at a much smaller footprint than the main tile
    // lines do, so it fades out first (a plain opacity fade -- there is no coarser sub-grid tier to hand
    // off to, so there is nothing to double). The whole-plane boundary line is left unfaded entirely --
    // one line at the room's edge is never dense.
    let minorLineFade = 1.0 - smoothstep(0.015, 0.06, pixelFootprint);

    // The main tile lines don't just fade -- they double their spacing (8, 16, 32, ...) as the footprint
    // grows, the way a mipmap halves resolution instead of just fading a texture to grey. mainLod is
    // fractional: at mainLod=0.3, say, spacing is already 8*2^0=8 but 30% of the way toward needing 16,
    // so mainLineMix0 (spacing 8) and mainLineMix1 (spacing 16, its immediate coarser neighbour) blend by
    // that fraction. Because 16's lines are a subset of 8's (every other one, same phase), this reads as
    // the in-between lines fading out first while the ones that survive to the next tier stay full
    // strength -- exactly "fade out every 2nd one" -- rather than the whole tier dimming uniformly.
    let mainBaseSize = 1.0;

    // The footprint at which the first doubling begins -- NOT mainBaseSize itself. That would only start
    // doubling once a pixel already spanned an entire 8-unit tile, which in practice never happens at any
    // distance this editor's camera reaches (measured against the same pixelFootprint the sub-grid fades
    // over, which tops out around 0.6), so the LOD was permanently stuck at 0 with no visible effect.
    let mainLodOnset = 0.15;
    let mainLod = log2(max(pixelFootprint / mainLodOnset, 1.0));
    let mainLodFloor = floor(mainLod);
    let mainLodFrac = mainLod - mainLodFloor;
    let mainSpacing0 = mainBaseSize * exp2(mainLodFloor);
    let mainSpacing1 = mainSpacing0 * 2.0;
    let bGrid0 = abs(fract(uv / mainSpacing0 - 0.5) - 0.5) / (derivative / mainSpacing0);
    let bGrid1 = abs(fract(uv / mainSpacing1 - 0.5) - 0.5) / (derivative / mainSpacing1);

    let fillMod = floor(uv / mainBaseSize);
    let oddness = fract((fillMod.x + fillMod.y) / 2.0) * 2.0;

    var colorVal = mix(fillEven, fillOdd, smoothstep(0.0, 1.0, oddness));

    // A cove's cross-axis UV is an arc length that starts at 0 at both ends, so it can't be
    // phase-aligned with the flat planes' tile grid at both the wall and floor seam simultaneously --
    // every tier draws both axes anyway (same as a flat plane), since a visibly missing set of lines
    // reads worse than a tier being slightly out of phase with one of the two walls it sits between.
    let cLineDist = min(cGrid.x, cGrid.y);
    let bLineDist0 = min(bGrid0.x, bGrid0.y);
    let bLineDist1 = min(bGrid1.x, bGrid1.y);
    let aLineDist = min(aGrid.x, aGrid.y);

    // Finest tier first so the coarser, brighter tiers draw over it at their shared boundaries. The
    // whole-plane boundary (aLineDist) is not faded -- see the pixelFootprint comment above.
    let minorLineMix = (1.0 - smoothstep(0.0, 1.0 * lineThickness, cLineDist)) * minorLineFade;
    let mainLineMix0 = 1.0 - smoothstep(0.0, 2.0 * lineThickness, bLineDist0);
    let mainLineMix1 = 1.0 - smoothstep(0.0, 2.0 * lineThickness, bLineDist1);
    let mainLineMix = mix(mainLineMix0, mainLineMix1, mainLodFrac);
    colorVal = mix(colorVal, minorLineColor, minorLineMix);
    colorVal = mix(colorVal, lineColor, mainLineMix);
    colorVal = mix(colorVal, lineColor, 1.0 - smoothstep(0.0, 4.0 * lineThickness, aLineDist));

    // The world axes drawn over the grid wherever a surface holds one -- X and Z cross at the floor's
    // centre -- so which way is +X is readable at a glance. Kept wider and stronger than a tile line and
    // faded only a little with distance, since there are just the three of them.
    let axisFootprint = max(fwidth(input.world_position), vec3<f32>(1e-5));
    let axisWidth = 3.0 * max(lineThickness, 0.5);
    // Mixed in after the lighting below rather than here: they glow, so they read at night too.
    let axisStrength = mix(1.0, 0.6, fadeT);
    let axisX = (1.0 - smoothstep(0.0, axisWidth, axis_pixels(input.world_position, axisFootprint, 0u))) * axisStrength;
    let axisZ = (1.0 - smoothstep(0.0, axisWidth, axis_pixels(input.world_position, axisFootprint, 2u))) * axisStrength;
    let axisY = (1.0 - smoothstep(0.0, axisWidth, axis_pixels(input.world_position, axisFootprint, 1u))) * axisStrength;

    // Darkened, not blacked out, where the sun's shadow falls -- this room is a decorative,
    // otherwise-unlit backdrop, not a PBR surface, so a soft multiply reads as "something is between the
    // room and the light" without pretending this floor is really being lit at all. Gated on
    // sun_direction.w (see mana_sun_shadow_factor's caller convention in pbr_irradiance.wgsl): without an
    // active shadow-casting sun this frame, frame.cascade_view_projections can hold last frame's stale
    // matrices, so skipping the sample entirely is what keeps a disabled/removed sun from leaving a
    // ghost shadow behind.
    // Preserve the themed surface colour as diffuse albedo before sun shadows and night dimming.
    let local_albedo = srgb_to_linear(colorVal.rgb);
    var shadow = 1.0;
    if (frame.sun_direction.w > 0.5) {
        let sun_ndotl = max(dot(normalize(input.world_normal), normalize(-frame.sun_direction.xyz)), 0.0);
        shadow = mana_sun_shadow_factor(input.world_position, input.world_normal, sun_ndotl);
    }
    colorVal = vec4<f32>(colorVal.rgb * mix(0.35, 1.0, shadow), colorVal.a);

    // Night falls on the floor too (frame.irradiance_factors.w is ManaSky's daylight), to a dim moonlit grey.
    colorVal = vec4<f32>(colorVal.rgb * mix(0.05, 1.0, frame.irradiance_factors.w), colorVal.a);

    // The axes are emissive: full colour by day, and brighter than the dimmed floor by night, so they glow.
    let axisGlow = mix(1.6, 1.0, frame.irradiance_factors.w);
    colorVal = mix(colorVal, vec4<f32>(AxisXColor * axisGlow, 1.0), axisX);
    colorVal = mix(colorVal, vec4<f32>(AxisZColor * axisGlow, 1.0), axisZ);
    colorVal = mix(colorVal, vec4<f32>(AxisYColor * axisGlow, 1.0), axisY);

    // Lamps illuminate the room independently of daylight. Use its inward-facing normal so
    // mirrored walls and the floor all receive light from inside the room.
    var local_diffuse = vec3<f32>(0.0);
    for (var i = 0u; i < 16u; i = i + 1u) {
        let to_light = frame.local_light_positions[i].xyz - input.world_position;
        let distance = max(length(to_light), 0.05);
        let attenuation = mana_point_light_attenuation(distance, frame.local_light_positions[i].w);
        let ndotl = max(dot(inward, to_light / distance), 0.0);
        local_diffuse += local_albedo / 3.14159265 * frame.local_light_colors[i].rgb * attenuation * ndotl;
    }

    return vec4<f32>(srgb_to_linear(colorVal.rgb) + local_diffuse, -1.0);
}
