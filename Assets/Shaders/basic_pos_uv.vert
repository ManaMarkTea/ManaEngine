#version 330

layout(location = 0) in vec3 pos;
layout(location = 1) in vec3 normal;
layout(location = 2) in vec2 coord;
layout(location = 3) in vec4 color;
layout(location = 4) in vec2 coord2;

uniform mat4 projectionMatrix = mat4( 1.0 );
uniform mat4 modelViewMatrix =  mat4( 1.0 );

out vec2 coords;
out vec3 norms;

void main() 
{
    coords=coord.st;
    vec4 pos4 = vec4(pos, 1.0);
	vec4 cleanPos = projectionMatrix * modelViewMatrix * pos4;
	//norms = vec3(cleanPos.xyz);
    gl_Position = cleanPos;
}