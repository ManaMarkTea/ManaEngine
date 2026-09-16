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

// Must match VoxelEditorGameMode.cs's gridSize -- see grid.wgsl's matching RoomSize constant.
uniform vec2 chunkSize = vec2(512.0, 512.0);
uniform vec2 tileSize = vec2(8.0, 8.0);

float chunkThickness = 4.0;
float tileThickness = 2.0;
float cellThickness = 1.0;

// Themed rather than fixed: ManaGridShader.BindShader pushes the active theme's GridFloor/GridLine here
// per material, the same colours the WebGPU grid.wgsl reads from the object uniform. Defaults match the
// original fixed light-mode palette, for the rare case nothing sets them. gridLine.a doubles as a
// line-thickness multiplier (see UiTheme.GridLineThickness).
uniform vec4 gridFloor = vec4(0xC6, 0xC4, 0xC7, 255.0) / 255.0;
uniform vec4 gridLine = vec4(0x54, 0x54, 0x55, 255.0) / 255.0;

// Lets a trimmed plane (the floor/wall quads cut back for VoxelEditorGameMode's rounded-corner coves)
// remap its own 0..1 UV back onto the coordinate the untrimmed room-sized grid expects, matching grid.wgsl's
// use of object.uv_transform, so tiles keep their true size across the seam with each cove fillet mesh.
uniform vec4 uvTransform = vec4(1.0, 1.0, 0.0, 0.0);

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
    vec2 uv = (fragCoord.xy * uvTransform.xy + uvTransform.zw) * chunkSize;
    vec2 derivative = fwidth(uv);

    // How many world units one screen pixel covers here -- not straight-line camera distance (`d`, now
    // unused), which misses why a floor gets dense toward the horizon: a floor viewed at a grazing angle
    // has a huge derivative long before it is actually far away, while a wall viewed face-on keeps a
    // small, roughly constant derivative regardless of distance. See grid.wgsl's matching comment.
    float pixelFootprint = max(derivative.x, derivative.y);

    // Exponential rather than smoothstep -- see grid.wgsl's matching comment for why smoothstep's
    // steepest-in-the-middle slope read as the fade hitting hardest at mid-distance, and this doesn't.
    float fadeTau = 1.33;
    float fadeT = 1.0 - exp(-pixelFootprint / fadeTau);

    // Lines get thinner with distance, not just dimmer (below) -- every tier's antialiasing width is
    // scaled by this alongside lineThickness (see UiTheme.GridLineThickness), so a line that would
    // otherwise stay a constant N pixels wide forever instead narrows as it recedes.
    float thicknessFade = mix(1.0, 0.35, fadeT);
    float lineThickness = gridLine.a * thicknessFade;
    float aScale = chunkThickness * lineThickness;
    float bScale = tileThickness * lineThickness;
    float cScale = cellThickness * lineThickness;

    vec2 halfChunkSize = chunkSize / 2.0;
    vec2 halfTileSize =  tileSize / 2.0;
    vec2 halfCellSize = vec2(0.5, 0.5);

    vec2 aGrid = abs(mod(uv - halfChunkSize, chunkSize) - halfChunkSize) / derivative;
    vec2 cGrid = abs(mod(uv - halfCellSize, 1.0) - halfCellSize) / derivative;

    vec2 fillMod = floor(uv / tileSize);

    float oddness = fract((fillMod.x + fillMod.y) / 2.0) * 2.0;

    vec4 fillEven = gridFloor;

    // 0.9645, not 0.92 -- see grid.wgsl's matching comment for where this ratio actually comes from.
    vec4 fillOdd = vec4(gridFloor.rgb * 0.9645, 1.0);

    // Fades toward the FLOOR colour, not toward black -- see grid.wgsl's matching comment for why
    // multiplying toward black worked only by coincidence for LCARS's near-black floor and made Light's
    // pale-floor lines pull toward black (more contrast, not less) as they receded.
    float lineDim = mix(1.0, 0.12, fadeT);
    vec4 dimmedLine = vec4(mix(gridFloor.rgb, gridLine.rgb, lineDim), 1.0);

    // A dim tint of the line colour, not the line colour itself -- the 1-unit sub-grid is meant to read as
    // fine graph-paper texture, not another tier of the same bright line. Darken the floor FIRST, then
    // tint toward the line colour -- see grid.wgsl's matching comment for why blending straight from
    // floor to line only darkens meaningfully when the two are far apart in the first place.
    vec3 minorBase = gridFloor.rgb * 0.9;
    vec4 minorLine = vec4(mix(minorBase, dimmedLine.rgb, 0.15), 1.0);

    // The sub-grid fades outright (a plain opacity fade -- there is no coarser sub-grid tier to hand off
    // to). The main tile lines instead double their spacing (8, 16, 32, ...) as the footprint grows, the
    // way a mipmap halves resolution instead of fading a texture to grey -- see grid.wgsl's matching
    // comment for why that reads as "every 2nd line fades out" rather than the whole tier dimming evenly.
    float minorLineFade = 1.0 - smoothstep(0.15, 0.6, pixelFootprint);

    float mainBaseSize = tileSize.x;

    // The footprint at which the first doubling begins -- NOT mainBaseSize itself; see grid.wgsl's
    // matching comment for why that was stuck permanently at LOD 0 with no visible effect.
    float mainLodOnset = 1.2;
    float mainLod = log2(max(pixelFootprint / mainLodOnset, 1.0));
    float mainLodFloor = floor(mainLod);
    float mainLodFrac = mainLod - mainLodFloor;
    float mainSpacing0 = mainBaseSize * exp2(mainLodFloor);
    float mainSpacing1 = mainSpacing0 * 2.0;
    vec2 halfSpacing0 = vec2(mainSpacing0 * 0.5);
    vec2 halfSpacing1 = vec2(mainSpacing1 * 0.5);
    vec2 bGrid0 = abs(mod(uv - halfSpacing0, mainSpacing0) - halfSpacing0) / derivative;
    vec2 bGrid1 = abs(mod(uv - halfSpacing1, mainSpacing1) - halfSpacing1) / derivative;

    vec4 colorVal = mix(
        fillEven,
        fillOdd,
        smoothstep(0.0, 1.0, (oddness))
    );

    // See grid.wgsl's matching comment: every tier draws both axes on a cove same as a flat plane,
    // despite the cross-axis arc-length UV not being phase-aligned with both walls it joins at once.
    float aLineDist = min(aGrid.x, aGrid.y);
    float bLineDist0 = min(bGrid0.x, bGrid0.y);
    float bLineDist1 = min(bGrid1.x, bGrid1.y);
    float cLineDist = min(cGrid.x, cGrid.y);

    // Finest tier first so the coarser, brighter tiers draw over it at their shared boundaries.
    colorVal = mix(
        colorVal,
        minorLine,
        smoothstep(cScale, .0, cLineDist) * minorLineFade
    );

    colorVal = mix(
        colorVal,
        dimmedLine,
        smoothstep(aScale, .0, aLineDist)
    );

    float mainLineMix0 = smoothstep(bScale, .0, bLineDist0);
    float mainLineMix1 = smoothstep(bScale, .0, bLineDist1);
    colorVal = mix(
        colorVal,
        dimmedLine,
        mix(mainLineMix0, mainLineMix1, mainLodFrac)
    );

    return colorVal;
}

void mainImage( out vec4 fragColor, in vec2 fragCoord )
{
    // Transparent from outside the room, not just past the wall it draws on -- see grid.wgsl's matching
    // comment for why this tests the CAMERA against the room's bounding box rather than each face's own
    // (inconsistent) normal direction. chunkSize.x doubles as the room's edge length on every axis, since
    // the room is a cube.
    if (iCameraPosition.x < 0.0 || iCameraPosition.x > chunkSize.x ||
        iCameraPosition.y < 0.0 || iCameraPosition.y > chunkSize.x ||
        iCameraPosition.z < 0.0 || iCameraPosition.z > chunkSize.x)
    {
        discard;
    }

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
