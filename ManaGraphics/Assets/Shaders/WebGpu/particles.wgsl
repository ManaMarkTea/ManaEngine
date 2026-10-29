// Particle trajectories are reconstructed in the shader, with no per-particle buffers or CPU mesh uploads.
// Fixed lifetimes and staggered phases keep exactly Count slots active. Additive blending needs no sort.
struct Uniforms {
    view_projection: mat4x4<f32>,
    model: mat4x4<f32>,
    right: vec4<f32>,
    up: vec4<f32>,
    motion: vec4<f32>, // seconds, lifetime, speed, cos(spread)
    gravity_drag: vec4<f32>,
    sizes: vec4<f32>, // start, end, radius, brightness
    start_color: vec4<f32>,
    end_color: vec4<f32>,
    flags: vec4<u32>, // count, seed, hard edge, newest spawn-transform index
    history: vec4<f32>, // newest tick within lifetime, seconds per sample, reserved
    bounce_plane1: vec4<f32>, // world normal, signed offset
    bounce_plane2: vec4<f32>,
    bounce_settings: vec4<f32>, // enabled 1, enabled 2, restitution, reserved
    wind: vec4<f32>, // global world-space air velocity, emitter influence
    swirl: vec4<f32>, // radians a second round the birth up axis, falloff per metre, pull per second, radial launch speed
    turbulence: vec4<f32>, // metres, 1 / feature size, field speed, field clock
    variation: vec4<f32>, // hue scatter, hue turns over life, twinkle, size scatter
    shape: vec4<f32>, // rim births, arm count, streak seconds, arm width (share of the gap between arms)
    burst: vec4<f32>, // bursts a lifetime, lifetimes elapsed (wrapped), speed scatter, reserved
}
@group(0) @binding(0) var<uniform> u: Uniforms;
@group(0) @binding(1) var<storage, read> spawn_transforms: array<mat4x4<f32>, 256>;

fn hash(value: u32) -> u32 {
    var x = value;
    x = (x ^ (x >> 16u)) * 0x7feb352du;
    x = (x ^ (x >> 15u)) * 0x846ca68bu;
    return x ^ (x >> 16u);
}
fn random(value: u32) -> f32 { return f32(hash(value) >> 8u) / 16777216.0; }
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) stretch: f32,
}
// Exact free flight, including linear drag. Collision work is skipped when both planes are disabled.
fn travel_time(t: f32) -> f32 {
    if (u.gravity_drag.w > 0.001) { return (1.0 - exp(-u.gravity_drag.w*t))/u.gravity_drag.w; }
    return t;
}
fn flight(p: vec3<f32>, v: vec3<f32>, g: vec3<f32>, t: f32) -> vec3<f32> {
    let travel = travel_time(t);
    if (u.gravity_drag.w > 0.001) { return p + v*travel + g*((t-travel)/u.gravity_drag.w); }
    return p + v*t + g*(0.5*t*t);
}
fn flight_velocity(v: vec3<f32>, g: vec3<f32>, t: f32) -> vec3<f32> {
    if (u.gravity_drag.w > 0.001) { return v*exp(-u.gravity_drag.w*t) + g*travel_time(t); }
    return v + g*t;
}
fn bounce_motion(origin: vec3<f32>, velocity: vec3<f32>, age: f32) -> vec3<f32> {
    let air = u.wind.xyz*u.wind.w;
    // Advect in the world's breeze; drag damps launch motion relative to moving air.
    let acceleration = u.gravity_drag.xyz + air*select(0.0,u.gravity_drag.w,u.gravity_drag.w > 0.001);
    let initial_velocity = velocity + air;
    if (u.bounce_settings.x + u.bounce_settings.y == 0.0) {
        return flight(origin, initial_velocity, acceleration, age);
    }
    var planes = array<vec4<f32>, 2>(u.bounce_plane1, u.bounce_plane2);
    var enabled = array<f32, 2>(u.bounce_settings.x, u.bounce_settings.y);
    var p = origin;
    var v = initial_velocity;
    var remaining = age;
    // Bounded work per particle; after eight impacts, constrain the remaining free flight.
    for (var impact = 0; impact < 8; impact++) {
        var g = acceleration;
        for (var i = 0; i < 2; i++) {
            if (enabled[i] == 0.0) { continue; }
            let n = planes[i].xyz;
            let d = dot(n,p) + planes[i].w;
            if (d < 0.0) { p -= n*d; }
            let vn = dot(n,v);
            if (d <= 0.0001 && vn < 0.0) { v -= n*((1.0+u.bounce_settings.z)*vn); }
            // Tiny bounces settle into contact, preserving tangential motion.
            if (d <= 0.0001 && abs(dot(n,v)) < 0.01 && dot(n,g) < 0.0) {
                v -= n*dot(n,v);
                g -= n*dot(n,g);
            }
        }
        var hit = remaining + 1.0;
        var hit_plane = 0;
        for (var i = 0; i < 2; i++) {
            if (enabled[i] == 0.0) { continue; }
            let plane = planes[i];
            let vn = dot(plane.xyz,v);
            let gn = dot(plane.xyz,g);
            var end = remaining;
            // Check the trajectory's minimum too, so a dip followed by a rise cannot tunnel.
            if (gn > 0.0 && vn < 0.0) {
                var turning = -vn/gn;
                if (u.gravity_drag.w > 0.001) {
                    turning = log(1.0-u.gravity_drag.w*vn/gn)/u.gravity_drag.w;
                }
                end = min(end, turning);
            }
            if (dot(plane.xyz,flight(p,v,g,end)) + plane.w >= 0.0) { continue; }
            if (u.gravity_drag.w <= 0.001) {
                let distance = max(dot(plane.xyz,p)+plane.w, 0.0);
                var crossing = 0.0;
                if (abs(gn) < 0.000001) { crossing = distance/max(-vn,0.000001); }
                else {
                    let root = sqrt(max(vn*vn-2.0*gn*distance,0.0));
                    // Stable downward-crossing root, including a launch from the surface.
                    crossing = select((-vn-root)/gn, 2.0*distance/max(-vn+root,0.000001), vn < 0.0);
                }
                if (crossing >= 0.0 && crossing < hit) { hit = crossing; hit_plane = i; }
                continue;
            }
            var low = 0.0;
            var high = end;
            // Find the first crossing using the exact drag trajectory rather than frame-sized steps.
            for (var iteration = 0; iteration < 16; iteration++) {
                let middle = (low+high)*0.5;
                if (dot(plane.xyz,flight(p,v,g,middle)) + plane.w >= 0.0) { low = middle; }
                else { high = middle; }
            }
            if (high < hit) { hit = high; hit_plane = i; }
        }
        if (hit > remaining) { return flight(p,v,g,remaining); }
        p = flight(p,v,g,hit);
        v = flight_velocity(v,g,hit);
        let plane = planes[hit_plane];
        p -= plane.xyz*(dot(plane.xyz,p)+plane.w);
        v -= plane.xyz*((1.0+u.bounce_settings.z)*min(dot(plane.xyz,v),0.0));
        remaining = max(remaining-hit,0.0);
    }
    p = flight(p,v,acceleration,remaining);
    for (var i = 0; i < 2; i++) {
        if (enabled[i] != 0.0) { p -= planes[i].xyz*min(dot(planes[i].xyz,p)+planes[i].w,0.0); }
    }
    return p;
}
struct Launch {
    origin: vec3<f32>,
    velocity: vec3<f32>,
    axis: vec3<f32>,
    center: vec3<f32>,
    age: f32,
    t: f32,
    seed: u32,
    group: u32, // the burst's seed when bursting, else the particle's
}
fn particle_launch(instance: u32) -> Launch {
    let seed = hash(instance ^ u.flags.y);
    var phase = f32(instance) / f32(max(u.flags.x, 1u));
    var group = seed;
    if (u.burst.x >= 1.0) {
        // Whole groups born together, one firework each; the lifetime count gives every burst a new place and colour.
        let burst = floor(phase * u.burst.x);
        phase = burst / u.burst.x;
        let cycle = u.burst.y + select(0.0, 1.0, u.motion.x / u.motion.y + phase >= 1.0);
        group = hash(u32(burst + cycle * u.burst.x) ^ (u.flags.y * 7919u));
    }
    let t = fract(u.motion.x / u.motion.y + phase);
    let age = t * u.motion.y;
    let c = mix(u.motion.w, 1.0, random(seed));
    let s = sqrt(max(0.0, 1.0 - c*c));
    let angle = random(seed + 1u) * 6.2831853;
    let local_direction = vec3<f32>(s*cos(angle), c, s*sin(angle));
    let radius = select(sqrt(random(seed + 2u)), 1.0, u.shape.x != 0.0) * u.sizes.z;
    var origin_angle = random(seed + 3u) * 6.2831853;
    if (u.shape.y >= 1.0) {
        // Spokes with a soft (two-sample) scatter; swirl falloff winds them into arms.
        let arm = floor(random(seed + 4u) * u.shape.y);
        let scatter = (random(seed + 5u) + random(seed + 6u) - 1.0) * u.shape.w;
        origin_angle = (arm + scatter) / u.shape.y * 6.2831853;
    }
    let outward = vec3<f32>(cos(origin_angle), 0.0, sin(origin_angle));
    var origin = outward * radius;
    if (u.burst.x >= 1.0) {
        // A burst goes off anywhere in the ball of Radius.
        let z = random(group) * 2.0 - 1.0;
        let around = random(group + 1u) * 6.2831853;
        origin = vec3<f32>(sqrt(1.0 - z*z)*cos(around), z, sqrt(1.0 - z*z)*sin(around)) * pow(random(group + 2u), 0.3333) * u.sizes.z;
    }
    let speed = u.motion.z * (1.0 - u.burst.z * random(seed + 11u));
    // Integer birth ticks remain fixed for a slot's entire life. Using floating-point
    // (current time - age) here can hop between samples and make moving streams jitter.
    let cycle_offset = select(0, 254, u.motion.x / u.motion.y + phase >= 1.0);
    let birth_tick = i32(floor(-phase * 254.0)) + cycle_offset;
    let index = u32((i32(u.flags.w) - i32(u.history.x) + birth_tick + 512) % 256);
    let spawn = spawn_transforms[index];
    let launch_direction = (spawn * vec4<f32>(local_direction, 0.0)).xyz;
    let axis = (spawn * vec4<f32>(0.0, 1.0, 0.0, 0.0)).xyz;
    var launch: Launch;
    launch.origin = (spawn * vec4<f32>(origin, 1.0)).xyz;
    launch.velocity = launch_direction / max(length(launch_direction), 0.000001) * speed
        + (spawn * vec4<f32>(outward, 0.0)).xyz * u.swirl.w;
    launch.axis = axis / max(length(axis), 0.000001);
    launch.center = spawn[3].xyz;
    launch.age = age;
    launch.t = t;
    launch.seed = seed;
    launch.group = group;
    return launch;
}
// Nested sines in two octaves: smooth in space, so neighbouring particles drift together like smoke.
fn flow(q: vec3<f32>) -> vec3<f32> {
    let a = vec3<f32>(sin(q.y + 1.7*sin(q.z*1.3)), sin(q.z + 1.7*sin(q.x*1.1)), sin(q.x + 1.7*sin(q.y*1.7)));
    let r = q*2.13 + vec3<f32>(3.1, 1.7, 5.3);
    let b = vec3<f32>(sin(r.z + 1.3*sin(r.y)), sin(r.x + 1.3*sin(r.z)), sin(r.y + 1.3*sin(r.x)));
    return a + b*0.5;
}
fn position_at(launch: Launch, age: f32) -> vec3<f32> {
    var p = bounce_motion(launch.origin, launch.velocity, age);
    if (u.swirl.x != 0.0 || u.swirl.z != 0.0) {
        // Turn (and pull) the free flight about the birth axis, closed form in age. The turn slows away
        // from the axis, which shears even streams into spirals.
        let offset = p - launch.center;
        let along = launch.axis*dot(offset, launch.axis);
        let pulled = (offset - along)*exp(-u.swirl.z*age);
        let turn = u.swirl.x*age/(1.0 + u.swirl.y*length(pulled));
        p = launch.center + along + pulled*cos(turn) + cross(launch.axis, pulled)*sin(turn);
    }
    if (u.turbulence.x > 0.0) {
        let grow = 1.0 - exp(-2.0*age);
        let drift = u.turbulence.w*u.turbulence.z;
        p += flow(p*u.turbulence.y + vec3<f32>(0.0, drift, drift*0.7))*(u.turbulence.x*grow);
    }
    return p;
}
fn hue_rotate(color: vec3<f32>, turns: f32) -> vec3<f32> {
    let k = vec3<f32>(0.57735);
    let a = turns*6.2831853;
    let c = cos(a);
    return max(color*c + cross(k, color)*sin(a) + k*dot(k, color)*(1.0 - c), vec3<f32>(0.0));
}
@vertex fn vs_main(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
    let launch = particle_launch(instance);
    let t = launch.t;
    // Billboard offsets and gravity are world-space; the spawn pose sets only origin and launch direction.
    var corners = array<vec2<f32>, 6>(vec2(-1.0,-1.0), vec2(1.0,-1.0), vec2(-1.0,1.0),
                                     vec2(-1.0,1.0), vec2(1.0,-1.0), vec2(1.0,1.0));
    let uv = corners[vertex];
    let size = mix(u.sizes.x, u.sizes.y, t)*0.5*max(1.0 + u.variation.w*(random(launch.seed + 7u)*2.0 - 1.0), 0.0);
    let head = position_at(launch, launch.age);
    var side = u.right.xyz;
    var along = u.up.xyz;
    var center = head;
    var stretch = 0.0;
    if (u.shape.z > 0.0) {
        // A capsule from where the particle was streak-seconds ago to where it is, facing the camera.
        let tail = position_at(launch, max(launch.age - u.shape.z, 0.0));
        let path = head - tail;
        let across = cross(path, cross(side, along));
        if (length(path) > 0.0001 && length(across) > 0.000001) {
            along = normalize(path);
            side = normalize(across);
            center = (head + tail)*0.5;
            stretch = length(path)*0.5/max(size, 0.000001);
        }
    }
    var output: VertexOutput;
    output.position = u.view_projection * vec4<f32>(center + side*(uv.x*size) + along*(uv.y*size*(1.0 + stretch)), 1.0);
    output.uv = uv;
    output.stretch = stretch;
    var color = mix(u.start_color, u.end_color, t);
    let hue = (random(launch.group + 8u) - 0.5)*u.variation.x + u.variation.y*t;
    if (hue != 0.0) { color = vec4<f32>(hue_rotate(color.rgb, hue), color.a); }
    var glow = u.sizes.w;
    if (u.variation.z > 0.0) {
        let rate = 6.0 + random(launch.seed + 9u)*14.0;
        glow *= 1.0 - u.variation.z*(0.5 + 0.5*sin(launch.age*rate + random(launch.seed + 10u)*6.2831853));
    }
    output.color = vec4<f32>(color.rgb*glow, color.a);
    return output;
}
@fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // A streak is a capsule: the stretched middle is all core, only the caps round off.
    let k = input.stretch;
    let distance = length(vec2<f32>(input.uv.x, max(abs(input.uv.y)*(k + 1.0) - k, 0.0)));
    if (distance > 1.0) { discard; }
    var fade = select(1.0 - smoothstep(0.0, 1.0, distance), 1.0, u.flags.z != 0u);
    // The tail of a streak fades into its head.
    fade *= mix(1.0, 0.15 + 0.85*(input.uv.y*0.5 + 0.5), min(k, 1.0));
    return vec4<f32>(input.color.rgb, input.color.a*fade);
}
