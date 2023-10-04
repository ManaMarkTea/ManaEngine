#version 330

layout(location = 0) in vec3 pos;
layout(location = 1) in vec2 coord;

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