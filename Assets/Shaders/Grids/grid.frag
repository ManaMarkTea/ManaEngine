#version 330

#extension GL_OES_standard_derivatives : enable


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

in vec2 TexCoords;
layout(location = 0) out vec4 FragColor;

float size = 128.0;
float thickness = 2.0;

float gridColor( out vec4 fragColor, in vec2 fragCoord )
{
    vec2 coord = fragCoord * size;

    // Compute anti-aliased world-space grid lines
    //vec2 grid = abs(fract(coord - 0.5) - 0.5) / fwidth(coord*thickness);
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / fwidth(coord*thickness);
    float line = min(grid.x, grid.y);

    // Just visualize the grid lines directly
    float color = 1.0 - min(line, 1.0);
    color = pow(color, 1.0 / 2.2);
    return color;
}

void main( void )
{
    FragColor = vec4(0.0,0.0,0.0,1.0);
    vec4 color = vec4(1e20);
    float result = gridColor( color, TexCoords );

    color = mix(vec4(0, float(0x72)/255.0, float(0x8F)/255.0, 0.0), vec4(1), result);
    color = mix(vec4(0.25,0,0,0), vec4(1), result);
        
        
    if (color.w < 0.5)
    {
        discard;
    }

    FragColor = vec4(color.xyz, 0.25);
}

