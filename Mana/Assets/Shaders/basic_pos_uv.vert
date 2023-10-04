#version 330

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoords;
layout (location = 3) in vec4 aColor;
layout (location = 4) in vec2 aTexCoord2;


uniform mat4 projection;
uniform mat4 view;
uniform mat4 model;
uniform mat3 normalMatrix;

out vec3 ObjectPos;
out vec3 WorldPos;
out vec2 TexCoords;

void main()
{
    TexCoords = aTexCoords;
    ObjectPos = aPos;
    WorldPos = vec3(model * vec4(aPos, 1.0));    
    gl_Position =  projection * view * vec4(WorldPos, 1.0);
}