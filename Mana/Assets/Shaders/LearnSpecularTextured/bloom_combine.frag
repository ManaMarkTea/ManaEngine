#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform sampler2D hdrColor;
uniform sampler2D bloomColor;
uniform sampler2D outlineMask;

void main()
{
    vec4 hdr_sample = texture(hdrColor, TexCoords);
    
    vec3 bloom = max(texture(bloomColor, TexCoords).rgb, vec3(0.0));
    float bloom_strength = 0.22;
    
    vec3 mapped;

    // Check if we should bypass tonemapping (e.g. for debug rendering modes)
    if (hdr_sample.a < 0.0)
    {
        mapped = hdr_sample.rgb + bloom * bloom_strength;
    }
    else
    {
        vec3 hdr = max(hdr_sample.rgb, vec3(0.0));

        // Super premium soft bloom combination
        vec3 color = hdr + bloom * bloom_strength;

        // Aces Filmic Tonemapping for cinema-grade HDR colors
        float a = 2.51;
        float b = 0.03;
        float c = 2.43;
        float d = 0.59;
        float e = 0.14;
        mapped = clamp((color * (a * color + b)) / (color * (c * color + d) + e), vec3(0.0), vec3(1.0));

        // Gamma correction
        mapped = pow(mapped, vec3(1.0 / 2.2));
    }
    
    ivec2 dimensions = textureSize(outlineMask, 0);
    ivec2 pixel = clamp(ivec2(TexCoords * vec2(dimensions)), ivec2(0), dimensions - ivec2(1));
    vec4 centerMask = texelFetch(outlineMask, pixel, 0);
    vec3 selectedOutlineColor = vec3(0.0);
    float bestCoverage = 0.0;

    if (centerMask.a <= 0.001)
    {
        for (int y = -4; y <= 4; ++y)
        {
            for (int x = -4; x <= 4; ++x)
            {
                float distancePixels = length(vec2(x, y));
                if (distancePixels <= 0.0 || distancePixels > 4.0)
                {
                    continue;
                }

                ivec2 samplePixel = clamp(pixel + ivec2(x, y), ivec2(0), dimensions - ivec2(1));
                vec4 mask = texelFetch(outlineMask, samplePixel, 0);
                float packedOutline = round(mask.a * 255.0);
                float widthCode = floor(packedOutline / 64.0);
                float opacity = (packedOutline - widthCode * 64.0) / 63.0;
                float widthPixels = widthCode + 1.0;
                float coverage = clamp(widthPixels - distancePixels + 1.0, 0.0, 1.0) * opacity;
                if (coverage > bestCoverage)
                {
                    bestCoverage = coverage;
                    selectedOutlineColor = mask.rgb;
                }
            }
        }
    }

    mapped = mix(mapped, selectedOutlineColor, bestCoverage);
    FragColor = vec4(mapped, 1.0);
}
