#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform sampler2D hdrColor;
uniform sampler2D bloomColor;

void main()
{
    vec4 hdr_sample = texture(hdrColor, TexCoords);
    
    vec3 bloom = max(texture(bloomColor, TexCoords).rgb, vec3(0.0));
    float bloom_strength = 0.22;
    
    // Check if we should bypass tonemapping (e.g. for debug rendering modes)
    if (hdr_sample.a < 0.0)
    {
        vec3 final_color = hdr_sample.rgb + bloom * bloom_strength;
        FragColor = vec4(final_color, 1.0);
        return;
    }
    
    vec3 hdr = max(hdr_sample.rgb, vec3(0.0));
    
    // Super premium soft bloom combination
    vec3 color = hdr + bloom * bloom_strength;
    
    // Aces Filmic Tonemapping for cinema-grade HDR colors
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    vec3 mapped = clamp((color * (a * color + b)) / (color * (c * color + d) + e), vec3(0.0), vec3(1.0));
    
    // Gamma correction
    mapped = pow(mapped, vec3(1.0 / 2.2));
    
    FragColor = vec4(mapped, 1.0);
}
