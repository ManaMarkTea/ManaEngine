#version 330 core

layout(location = 0) out vec4 FragColor;

uniform vec3      iResolution;
uniform float     iTime;
uniform float     iChannelTime[4];
uniform vec4      iMouse;
uniform vec4      iDate;
uniform float     iSampleRate;
uniform vec3      iChannelResolution[4];
uniform int       iFrame;
uniform float     iTimeDelta;
uniform float     iFrameRate;
uniform sampler2D iChannel0;
uniform sampler2D iChannel1;
uniform sampler2D iChannel2;

vec3 palette(float t)
{
    vec3 a = vec3(0.5, 0.5, 0.5);
    vec3 b = vec3(0.5, 0.5, 0.5);
    vec3 c = vec3(1.0, 1.0, 1.0);
    vec3 d = vec3(0.263,0.416,0.557);

    return a + b * cos( 6.28318 * (c * t + d) );
}

void mainImage(out vec4 fragColor, vec2 fragCoord)
{
    vec2 uv = (fragCoord * 2.0 - 1.0);
    vec2 uv0 = uv;

    vec3 finalColor = vec3(0.0);

    for (float i = 0.0; i < 3.0f; i++)
    {
        uv = fract(uv * 1.5) - 0.5;
        float d = length(uv);
        vec3 col = palette(length(uv0) + iTime * 0.4);

        d = sin(d*8.0 + iTime) / 8.0;
        d = abs(d);
        d = 0.02 / d;

        finalColor += col * d;

    }

    fragColor = vec4(finalColor, 1.0);
    

}

in vec2 TexCoords;

void main(void)
{
    FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 color = vec4(1e20);
    mainImage(color, TexCoords);
    color.w = 1.0;
    if (FragColor.w < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (FragColor.x < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (FragColor.y < 0.0) color = vec4(0.0, 1.0, 0.0, 1.0);
    if (FragColor.z < 0.0) color = vec4(0.0, 0.0, 1.0, 1.0);
    if (FragColor.w < 0.0) color = vec4(1.0, 1.0, 0.0, 1.0);
    FragColor = vec4(color.xyz, 1.0);
}


