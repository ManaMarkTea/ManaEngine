#version 330
#extension GL_OES_standard_derivatives : enable

#define GAME
#ifdef GAME
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
uniform vec3      iCameraPosition;

uniform mat4 projection; 
uniform mat4 view;
uniform mat4 model;
uniform mat3 normalMatrix;

in vec3 WorldPos;
in vec2 TexCoords;

layout(location = 0) out vec4 FragColor;
#endif

//uniform vec2 chunkSize = vec2(64.0, 64.0);
uniform vec2 chunkSize = vec2(256.0, 256.0);
uniform vec2 tileSize = vec2(8.0, 8.0);
uniform vec2 cellSize = vec2(1.0, 1.0);

float chunkThickness = 4.0;
float tileThickness = 2.0;
float cellThickness = 1.0;

vec4 fillEven = vec4(0xC6,0xC4,0xC7, 255.0) / 255.0;
vec4 fillOdd = vec4(0xBF,0xBD,0xC0, 255.0) / 255.0;

vec4 cellStroke = vec4(0xAB, 0xBD, 0xD8, 255.0) / 255.0;
vec4 stroke = vec4(0xF2, 0xD3, 0x98, 255.0) / 255.0;

uniform float near = 0.01;
uniform float far = 100.0;
uniform vec3 nearPoint = vec3(0,0,0);
uniform vec3 farPoint = vec3(100,100,100);

float computeDepth(vec3 pos) 
{
    vec4 clip_space_pos = projection * view * vec4(pos.xyz, 1.0);
    return (clip_space_pos.z / clip_space_pos.w);
}

float computeLinearDepth(vec3 pos) 
{
    vec4 clip_space_pos = projection * view * vec4(pos.xyz, 1.0);
    float clip_space_depth = (clip_space_pos.z / clip_space_pos.w) * 2.0 - 1.0; // put back between -1 and 1
    float linearDepth = (2.0 * near * far) / (far + near - clip_space_depth * (far - near)); // get linear value between 0.01 and 100
    return linearDepth / far; // normalize
}

vec4 gridColor( out vec4 fragColor, in vec2 fragCoord, in float d)
{
    vec2 uv = fragCoord.xy * chunkSize;
    vec2 derivative = fwidth(uv);

    float aa = length(fwidth(uv));
    float aScale = chunkThickness;
    float bScale = tileThickness;
    float cScale = cellThickness;

    vec2 halfChunkSize = chunkSize / 2.0;
    vec2 halfTileSize =  tileSize / 2.0;
    vec2 halfCellSize =  cellSize / 2.0;

    vec2 aGrid = abs(mod(uv - halfChunkSize, chunkSize) - halfChunkSize) / derivative;
    vec2 bGrid = abs(mod(uv - halfTileSize, tileSize) - halfTileSize) / derivative;
    vec2 cGrid = abs(mod(uv - halfCellSize, cellSize) - halfCellSize) / derivative;
    
    vec2 fillMod = floor(uv / tileSize);    
    
    float oddness = fract((fillMod.x + fillMod.y) / 2.0) * 2.0;
    
    vec4 cellStrokeDynamic = (fillEven + fillOdd) / 2.2;
    vec4 strokeDynamic = (fillEven + fillOdd) / 2.4;

    vec4 colorVal = mix(
        fillEven, 
        fillOdd,  
        smoothstep(0.0, 1.0, (oddness))
    );       

    colorVal = mix(
        colorVal, 
        cellStrokeDynamic, 
        smoothstep(cScale, .0, min(cGrid.x, cGrid.y))
    );

    colorVal = mix(
        colorVal, 
        strokeDynamic, 
        smoothstep(aScale, .0, min(aGrid.x, aGrid.y))
    );

    colorVal = mix(
        colorVal, 
        strokeDynamic, 
        smoothstep(bScale, .0, min(bGrid.x, bGrid.y))
    );
     
    return colorVal;
}

void mainImage( out vec4 fragColor, in vec2 fragCoord )
{
    fragColor = vec4(0.0,0.0,0.0,1.0);
    vec4 color = vec4(1e20);
    float d = distance(WorldPos,iCameraPosition);
    color = vec4(gridColor(color, fragCoord, d));

    if (color.w < 0.5)
    {
        discard;
    }

    fragColor = vec4(color.xyz, -1.0);
}

#ifdef GAME
void main( void )
{
    mainImage(FragColor, TexCoords);
}
#endif
