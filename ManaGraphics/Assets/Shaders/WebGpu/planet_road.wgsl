// Roads on a planet (PlanetRoadMesh, OpenStreetMap's from the vector tiles): far off a line a pixel or two wide in its map
// colour, close up the road at its true width -- asphalt lit by the sun, edge lines and dashed lanes.
//
// Vertex channels (PlanetRoadMesh owns both sides):
//   position - the road's centre line at that point, laid on the ground.
//   color    - xyz: the unit way across the road toward this vertex's side (the shader widens along it).
//   uv0      - x: -1 or +1, the side; y: metres along the road from its start (for the lane dashes).

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) uv0 : vec2<f32>,
    @location(1) world_position : vec3<f32>,
    @location(2) world_normal : vec3<f32>,
    // How much of the drawn width is the road itself rather than its minimum on screen: 1 close up, toward 0 far off.
    @location(3) real : f32,
    @location(4) half_width_metres : f32,
};

#include "include/uniforms.wgsl"

@group(0) @binding(0) var<uniform> frame : FrameUniform;
#include "include/haze.wgsl"
@group(1) @binding(0) var<uniform> object : ObjectUniform;

struct CustomMaterialUniform {
    // World units of half-width per unit of camera distance: the narrowest the road is drawn on screen.
    //PROPERTY(default=0.002, min=0.0, max=0.05)
    widthPerDistance : f32,

    // The road's own half width, in world units.
    //PROPERTY(default=5.0, min=0.0, max=100.0)
    halfWidth : f32,

    // Metres a world unit stands for (the planet's scale), for the markings.
    //PROPERTY(default=1.0, min=0.0001, max=100000.0)
    metresPerUnit : f32,

    // Share of the camera distance the road is pulled toward the camera, to win the depth test against the ground.
    //PROPERTY(default=0.004, min=0.0, max=0.05)
    towardCamera : f32,

    // Lanes each way the markings divide the road into (1 draws no lane lines).
    //PROPERTY(default=2.0, min=1.0, max=6.0)
    lanes : f32,

    // 1 draws the far road in its map colour, 0 in the grey of a road seen from the air.
    //PROPERTY(default=0.0, min=0.0, max=1.0)
    mapStyle : f32,

    //PROPERTY(default=[1.0,0.55,0.15], min=[0.0,0.0,0.0], max=[1.0,1.0,1.0])
    mapColor : vec3<f32>,
};

@group(2) @binding(0) var<uniform> custom : CustomMaterialUniform;

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

@vertex
fn vs_main(@location(0) position : vec3<f32>, @location(1) normal : vec3<f32>, @location(2) color : vec4<f32>, @location(3) uv0 : vec2<f32>) -> VertexOutput {
    let center = (object.model * vec4<f32>(position, 1.0)).xyz;
    let up = normalize((object.normal_matrix * vec4<f32>(normal, 0.0)).xyz);
    var side = (object.model * vec4<f32>(color.xyz, 0.0)).xyz;
    let to_camera = frame.camera_position.xyz - center;
    let distance_to_camera = length(to_camera);

    let screen_half = distance_to_camera * custom.widthPerDistance;
    let half_width = max(custom.halfWidth, screen_half);
    let real = custom.halfWidth / max(half_width, 1e-6);

    // Held to its minimum on screen, a ribbon lying flat thins to dots seen edge-on: turned toward the camera instead.
    // At its true width it lies on the ground, as a road does.
    if (real < 0.999) {
        let along = cross(side, up);
        let facing = cross(along, to_camera);
        let facing_length = length(facing);
        if (facing_length > 1e-6 * distance_to_camera) {
            var turned = facing / facing_length;
            if (dot(turned, side) < 0.0) {
                turned = -turned;
            }
            side = normalize(mix(turned, side, real));
        }
    }

    let lifted = center + to_camera * custom.towardCamera;
    var output : VertexOutput;
    output.position = frame.projection * (frame.view * vec4<f32>(lifted + side * half_width * uv0.x, 1.0));
    output.uv0 = uv0;
    output.world_position = center + side * half_width * uv0.x;
    output.world_normal = up;
    output.real = real;
    output.half_width_metres = half_width * custom.metresPerUnit;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    // Metres a pixel spans along and across, before any branch (uniformity): markings narrower than a pixel or so fade
    // rather than shimmer.
    let along = input.uv0.y;
    let across = input.uv0.x * input.half_width_metres;
    let pixel_along = max(fwidth(along), 1e-3);
    let pixel_across = max(fwidth(across), 1e-3);

    let up = normalize(input.world_normal);
    var to_sun = frame.sun_direction.xyz;
    if (dot(to_sun, to_sun) < 1e-6) {
        to_sun = vec3<f32>(0.0, -1.0, 0.0);
    }
    to_sun = normalize(-to_sun);
    let sun_light = frame.sun_color.rgb * frame.sun_color.a;
    let sun_height = dot(up, to_sun);
    let day = smoothstep(-0.1, 0.25, sun_height);
    let light = vec3<f32>(0.03) + sun_light * max(sun_height, 0.0) * 0.55 + vec3<f32>(0.16, 0.19, 0.24) * day;

    // Asphalt (dark, as the satellite ground round it is), a touch lighter where the tyres have worn it, then the paint.
    let half = input.half_width_metres;
    var surface = vec3<f32>(0.04, 0.04, 0.043);
    let lane_width = 2.0 * half / custom.lanes;
    let in_lane = fract((across + half) / lane_width);
    let tyres = smoothstep(0.12, 0.3, abs(in_lane - 0.5)) * (1.0 - smoothstep(0.32, 0.45, abs(in_lane - 0.5)));
    surface = surface + vec3<f32>(0.012) * tyres;

    // Paint fades out as it gets thinner than a pixel, so far off the road is plain asphalt, not shimmering lines.
    let line = 0.15;
    let paint_seen = 1.0 - smoothstep(line * 0.6, line * 3.0, pixel_across);
    let edge = 1.0 - smoothstep(line * 0.5, line * 0.5 + pixel_across, abs(abs(across) - (half - 0.6)));
    var lane_line = 0.0;
    if (custom.lanes > 1.5) {
        let to_line = abs(fract((across + half) / lane_width + 0.5) - 0.5) * lane_width;
        let dash = step(fract(along / 12.0), 0.25) * (1.0 - smoothstep(4.0, 12.0, pixel_along));
        let near_edge = step(lane_width * 0.5, half - abs(across));
        lane_line = (1.0 - smoothstep(line * 0.5, line * 0.5 + pixel_across, to_line)) * dash * near_edge;
    }
    let paint = max(edge, lane_line) * paint_seen;
    surface = mix(surface, vec3<f32>(0.75, 0.75, 0.72), paint);
    var colour = surface * light;

    // Far off, a road seen from the air: lighter than its asphalt (the concrete, the verges), or its map colour.
    let aerial = vec3<f32>(0.3, 0.3, 0.29) * light;
    var far = aerial;
    if (custom.mapStyle > 0.5) {
        far = pow(custom.mapColor, vec3<f32>(2.2));
    }
    colour = mix(far, colour, smoothstep(0.5, 1.0, input.real));

    colour = mana_haze(colour, input.world_position);
    return vec4<f32>(colour * frame.irradiance_factors.x, 1.0);
}
