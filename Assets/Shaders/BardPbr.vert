#version 330

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoord;
layout(location = 3) in vec4 color;
layout(location = 4) in vec2 coord2;

//out vec2 TexCoord0;
//out vec3 Normal0;
//out vec3 WorldPos0;

out vec3 outPosition;
out vec3 outNormal;
out vec4 outColor;


uniform mat4 gWVP;
uniform mat4 gWorld;

void main()
{                                        
    gl_Position = gWVP * vec4(Position, 1.0);                                        
    outPosition = (gWVP * vec4(Position, 1.0)).xyz;    
    outNormal = (gWorld * vec4(Normal, 0.0)).xyz;                                            
    outColor = color;
}