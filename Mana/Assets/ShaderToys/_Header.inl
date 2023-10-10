#version 330 core

layout(location = 0) out vec4 FragColor;

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
uniform sampler2D iChannel0;
uniform sampler2D iChannel1;
uniform sampler2D iChannel2;


mat3 LookAt(vec3 eye, vec3 at) 
{
	vec3 localUp = vec3(0, 1, 0); // temp sop space up vector
	vec3 fwd = normalize(at - eye); // direction to look at position
	vec3 right = normalize(cross(localUp, fwd)); // right vector of direction
	vec3 up = normalize(cross(fwd, right)); // up vector of direction
	return mat3(right, up, fwd);
}

mat4 LookAt4(vec3 eye, vec3 at) {

	vec3 localUp = vec3(0, 1, 0); // temp sop space up vector
	vec3 fwd = normalize(at - eye); // direction to look at position
	vec3 right = normalize(cross(localUp, fwd)); // right vector of direction
	vec3 up = normalize(cross(fwd, right)); // up vector of direction

	return mat4(vec4(right, 0), vec4(up, 0), vec4(fwd, 0), vec4(0, 0, 0, 1));
}

