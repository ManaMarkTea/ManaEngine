// Under MSAA the scene pass draws into a four-sample depth buffer, which WebGPU cannot resolve; this copies its first
// sample into the ordinary one that the outline pass, the always-on-top pass and the target preview read afterwards.

@group(0) @binding(0) var scene_depth : texture_depth_multisampled_2d;

@vertex
fn vs_main(@builtin(vertex_index) vertex_index : u32) -> @builtin(position) vec4<f32> {
    var positions = array<vec2<f32>, 3>(vec2<f32>(-1.0, -1.0), vec2<f32>(3.0, -1.0), vec2<f32>(-1.0, 3.0));
    return vec4<f32>(positions[vertex_index], 0.0, 1.0);
}

@fragment
fn fs_main(@builtin(position) position : vec4<f32>) -> @builtin(frag_depth) f32 {
    return textureLoad(scene_depth, vec2<i32>(position.xy), 0);
}
