#version 330 core

layout(location = 0) in vec3 a_position;

uniform mat4 gWVP;

void main()
{
    gl_Position = gWVP * vec4(a_position, 1.0);
}
