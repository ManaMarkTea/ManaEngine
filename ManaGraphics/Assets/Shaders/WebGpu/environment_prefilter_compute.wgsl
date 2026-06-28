struct PrefilterParams {
    roughness : f32,
    source_resolution : f32,
    sample_count : u32,
    _pad0 : u32,
};

@group(0) @binding(0) var source_sampler : sampler;
@group(0) @binding(1) var source_cube : texture_cube<f32>;
@group(0) @binding(2) var destination_cube : texture_storage_2d_array<rgba8unorm, write>;
@group(0) @binding(3) var<uniform> params : PrefilterParams;

const PI : f32 = 3.14159265359;

fn saturate(value : f32) -> f32 {
    return clamp(value, 0.0, 1.0);
}

fn radical_inverse_vdc(bits_in : u32) -> f32 {
    var bits = bits_in;
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return f32(bits) * 2.3283064365386963e-10;
}

fn hammersley(i : u32, n : u32) -> vec2<f32> {
    return vec2<f32>(f32(i) / f32(n), radical_inverse_vdc(i));
}

fn importance_sample_ggx(xi : vec2<f32>, n : vec3<f32>, roughness : f32) -> vec3<f32> {
    let a = roughness * roughness;
    let phi = 2.0 * PI * xi.x;
    let cos_theta = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
    let sin_theta = sqrt(max(1.0 - cos_theta * cos_theta, 0.0));

    let h = vec3<f32>(
        cos(phi) * sin_theta,
        sin(phi) * sin_theta,
        cos_theta);

    let up = select(vec3<f32>(0.0, 1.0, 0.0), vec3<f32>(1.0, 0.0, 0.0), abs(n.y) > 0.96);
    let tangent = normalize(cross(up, n));
    let bitangent = cross(n, tangent);
    return normalize(tangent * h.x + bitangent * h.y + n * h.z);
}

fn distribution_ggx(n : vec3<f32>, h : vec3<f32>, roughness : f32) -> f32 {
    let a = roughness * roughness;
    let a2 = a * a;
    let ndoth = max(dot(n, h), 0.0);
    let ndoth2 = ndoth * ndoth;
    let denom = ndoth2 * (a2 - 1.0) + 1.0;
    return a2 / max(PI * denom * denom, 0.000001);
}

fn cube_direction(face : u32, uv : vec2<f32>) -> vec3<f32> {
    let u = uv.x;
    let v = uv.y;
    switch face {
        case 0u: { return normalize(vec3<f32>(1.0, -v, -u)); }
        case 1u: { return normalize(vec3<f32>(-1.0, -v, u)); }
        case 2u: { return normalize(vec3<f32>(u, 1.0, v)); }
        case 3u: { return normalize(vec3<f32>(u, -1.0, -v)); }
        case 4u: { return normalize(vec3<f32>(u, -v, 1.0)); }
        default: { return normalize(vec3<f32>(-u, -v, -1.0)); }
    }
}

fn prefilter(direction : vec3<f32>) -> vec3<f32> {
    let roughness = saturate(params.roughness);
    if (roughness <= 0.0001) {
        return textureSampleLevel(source_cube, source_sampler, direction, 0.0).rgb;
    }

    let n = normalize(direction);
    let v = n;
    var color = vec3<f32>(0.0);
    var total_weight = 0.0;
    let sample_count = max(params.sample_count, 1u);

    for (var i = 0u; i < sample_count; i = i + 1u) {
        let xi = hammersley(i, sample_count);
        let h = importance_sample_ggx(xi, n, roughness);
        let l = normalize(2.0 * dot(v, h) * h - v);
        let ndotl = max(dot(n, l), 0.0);
        if (ndotl > 0.0) {
            let d = distribution_ggx(n, h, roughness);
            let ndoth = max(dot(n, h), 0.0);
            let hdotv = max(dot(h, v), 0.0);
            let pdf = d * ndoth / max(4.0 * hdotv, 0.0001) + 0.0001;
            let sa_texel = 4.0 * PI / (6.0 * params.source_resolution * params.source_resolution);
            let sa_sample = 1.0 / (f32(sample_count) * pdf + 0.0001);
            let mip_level = max(0.0, 0.5 * log2(sa_sample / sa_texel));

            color = color + textureSampleLevel(source_cube, source_sampler, l, mip_level).rgb * ndotl;
            total_weight = total_weight + ndotl;
        }
    }

    if (total_weight <= 0.0001) {
        return textureSampleLevel(source_cube, source_sampler, direction, 0.0).rgb;
    }

    return color / total_weight;
}

@compute @workgroup_size(8, 8, 1)
fn cs_main(@builtin(global_invocation_id) id : vec3<u32>) {
    let destination_size = textureDimensions(destination_cube);
    if (id.x >= destination_size.x || id.y >= destination_size.y || id.z >= 6u) {
        return;
    }

    let pixel = vec2<f32>(f32(id.x) + 0.5, f32(id.y) + 0.5);
    let uv = pixel / vec2<f32>(destination_size) * 2.0 - vec2<f32>(1.0);
    let direction = cube_direction(id.z, uv);
    let color = prefilter(direction);
    textureStore(destination_cube, vec2<i32>(id.xy), i32(id.z), vec4<f32>(color, 1.0));
}
