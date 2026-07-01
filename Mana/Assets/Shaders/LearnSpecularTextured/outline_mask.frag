#version 330 core

out vec4 FragColor;

uniform vec4 outlineColor;
uniform float outlineWidth;

void main()
{
    float widthCode = round(clamp(outlineWidth, 1.0, 4.0)) - 1.0;
    float opacityCode = round(clamp(outlineColor.a, 0.0, 1.0) * 63.0);
    float packedOutline = (widthCode * 64.0 + opacityCode) / 255.0;
    FragColor = vec4(clamp(outlineColor.rgb, 0.0, 1.0), packedOutline);
}
