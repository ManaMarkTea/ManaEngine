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

float size = 6400.0;
float divisions = 64.0;
float thickness = 2.0;

float gridColor( out vec4 fragColor, in vec2 fragCoord )
{
    float halfThick = thickness * 0.5;

    vec2 uv = fragCoord / divisions;
    
    vec2 tiledUv = fract(uv*size);
    
    vec2 square = abs(tiledUv * thickness - halfThick);
    
    vec2 sharpSquare = step(0.9,square);
        
    float result = sharpSquare.x + sharpSquare.y;

    fragColor = vec4(1.0)*result;
    return result;
}


void main( void )
{
    gl_FragColor = vec4(0.0,0.0,0.0,1.0);
    vec4 color = vec4(1e20);
    float result = gridColor( color, TexCoords );

    color = mix(vec4(0, float(0x72)/255.0, float(0x8F)/255.0, 1.0), vec4(1), result);

    color.w = 1.0;
    if(gl_FragColor.w<0.0) color=vec4(1.0,0.0,0.0,1.0);
    if(gl_FragColor.x<0.0) color=vec4(1.0,0.0,0.0,1.0);
    if(gl_FragColor.y<0.0) color=vec4(0.0,1.0,0.0,1.0);
    if(gl_FragColor.z<0.0) color=vec4(0.0,0.0,1.0,1.0);
    if(gl_FragColor.w<0.0) color=vec4(1.0,1.0,0.0,1.0);
    gl_FragColor = vec4(color.xyz,1.0);
}

