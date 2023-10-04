

void main(void)
{
    FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 color = vec4(1e20);
    mainImage(color, FragColor.xy);
    color.w = 1.0;
    if (FragColor.w < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (FragColor.x < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (FragColor.y < 0.0) color = vec4(0.0, 1.0, 0.0, 1.0);
    if (FragColor.z < 0.0) color = vec4(0.0, 0.0, 1.0, 1.0);
    if (FragColor.w < 0.0) color = vec4(1.0, 1.0, 0.0, 1.0);
    FragColor = vec4(color.xyz, 1.0);
}

