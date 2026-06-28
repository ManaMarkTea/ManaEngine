#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D hdrColor;

void main()
{
    vec3 raw_color = texture(hdrColor, TexCoords).rgb;
    
    // Clamp maximum bloom input brightness to prevent fireflies
    vec3 color = clamp(raw_color, vec3(0.0), vec3(8.0));
    
    // Calculate relative luminance
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    
    // Smooth threshold extraction
    float threshold = 1.0;
    float knee = 0.1;
    float soft = luma - threshold + knee;
    float soft_clamp = clamp(soft, 0.0, 2.0 * knee);
    float rq = soft_clamp * soft_clamp / (4.0 * knee + 0.0001);
    float br = max(rq, luma - threshold) / max(luma, 0.0001);
    
    FragColor = vec4(color * br, 1.0);
}
