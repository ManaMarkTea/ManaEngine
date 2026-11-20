// The CPU subtracts the camera from the model origin before rounding to f32.
fn mana_camera_relative(object_data : ObjectUniform, position : vec3<f32>) -> vec3<f32> {
    let origin = select(object_data.model[3].xyz - frame.camera_position.xyz,
        object_data.relative_model_origin.xyz, object_data.relative_model_origin.w != 0.0);
    return (object_data.model * vec4<f32>(position, 0.0)).xyz + origin;
}

fn mana_relative_clip_position(position : vec3<f32>) -> vec4<f32> {
    let eye = (frame.view * vec4<f32>(position, 0.0)).xyz;
    return frame.projection * vec4<f32>(eye, 1.0);
}

fn mana_clip_position(object_data : ObjectUniform, position : vec3<f32>) -> vec4<f32> {
    return mana_relative_clip_position(mana_camera_relative(object_data, position));
}

// Globe-wide meshes already have planet-sized vertices. Cancel the large terms before adding the
// small camera remainder or a procedural displacement, so neither is lost on the float grid.
fn mana_globe_relative(model : mat4x4<f32>, position : vec3<f32>) -> vec3<f32> {
    let offset = model[3].xyz - frame.camera_position.xyz;
    return ((model * vec4<f32>(position, 0.0)).xyz + offset) - frame.camera_position_remainder.xyz;
}
