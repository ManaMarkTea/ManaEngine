

void main(void)
{
    gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 color = vec4(1e20);
    mainImage(color, gl_FragCoord.xy);
    color.w = 1.0;
    if (gl_FragColor.w < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (gl_FragColor.x < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (gl_FragColor.y < 0.0) color = vec4(0.0, 1.0, 0.0, 1.0);
    if (gl_FragColor.z < 0.0) color = vec4(0.0, 0.0, 1.0, 1.0);
    if (gl_FragColor.w < 0.0) color = vec4(1.0, 1.0, 0.0, 1.0);
    gl_FragColor = vec4(color.xyz, 1.0);
}

