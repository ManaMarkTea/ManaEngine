// Model space to clip space through the camera's own frame. The model's place less the camera's is exact in floats (two
// nearby numbers), so a mesh millions of units from the origin -- an aircraft over a true-size planet -- keeps its
// shape; through world space each of its corners rounded to half a metre on its own, and it shook as it moved.
// Needs `frame : FrameUniform` declared by the including shader.
fn mana_camera_relative(model : mat4x4<f32>, position : vec3<f32>) -> vec3<f32> {
    let offset = model[3].xyz - frame.camera_position.xyz;
    return (model * vec4<f32>(position, 0.0)).xyz + offset;
}

fn mana_clip_position(model : mat4x4<f32>, position : vec3<f32>) -> vec4<f32> {
    let eye = (frame.view * vec4<f32>(mana_camera_relative(model, position), 0.0)).xyz;
    return frame.projection * vec4<f32>(eye, 1.0);
}
