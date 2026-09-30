// Shared by mesh and grid lighting. A smooth finite range over inverse-square falloff.
fn mana_point_light_attenuation(distance_to_light : f32, light_range : f32) -> f32 {
    if (light_range <= 0.0) {
        return 0.0;
    }
    let distance = max(distance_to_light, 0.05);
    let fade = clamp(1.0 - pow(distance / light_range, 4.0), 0.0, 1.0);
    return fade * fade / (distance * distance);
}
