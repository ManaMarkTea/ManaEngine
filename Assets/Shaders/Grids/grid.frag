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


float size = 5.0;

void mainImage( out vec4 fragColor, in vec2 fragCoord )
{
    vec2 uv = fragCoord/iResolution.xy;
    vec2 tiledUV = fract(uv*size);
    vec2 square = abs(tiledUV*2.-1.);
    vec2 sharpSquare = step(0.8,square);
    float result = sharpSquare.x+sharpSquare.y;

    fragColor = vec4(1.0)*result;
}


void main( void )
{
    gl_FragColor = vec4(0.0,0.0,0.0,1.0);
    vec4 color = vec4(1e20);
    mainImage( color, gl_FragCoord.xy );
    color.w = 1.0;
    if(gl_FragColor.w<0.0) color=vec4(1.0,0.0,0.0,1.0);
    if(gl_FragColor.x<0.0) color=vec4(1.0,0.0,0.0,1.0);
    if(gl_FragColor.y<0.0) color=vec4(0.0,1.0,0.0,1.0);
    if(gl_FragColor.z<0.0) color=vec4(0.0,0.0,1.0,1.0);
    if(gl_FragColor.w<0.0) color=vec4(1.0,1.0,0.0,1.0);
    gl_FragColor = vec4(color.xyz,1.0);
}

