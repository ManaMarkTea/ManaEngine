#version 330

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aColor;
layout(location = 4) in vec2 aTexCoord2;

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
    gl_Position = gWVP * vec4(aPosition, 1.0);                                        
    outPosition = (gWVP * vec4(aPosition, 1.0)).xyz;    
    outNormal = (gWorld * vec4(aNormal, 0.0)).xyz;                                            
    outColor = aColor;
}