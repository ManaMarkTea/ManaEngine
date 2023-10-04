#version 330 core

layout(points) in;
layout(triangle_strip, max_vertices = 4) out;

out vec2 coords;

void main() 
{
    vec2 res = vec2(1920,1080);

    gl_Position = vec4( 1.0, 1.0, 0.5, 1.0 );
    coords = vec2( 1.0, 1.0 ) * res;
    EmitVertex();

    gl_Position = vec4(-1.0, 1.0, 0.5, 1.0 );
    coords = vec2( 0.0, 1.0 ) * res; 
    EmitVertex();

    gl_Position = vec4( 1.0,-1.0, 0.5, 1.0 );
    coords = vec2( 1.0, 0.0 ) * res; 
    EmitVertex();

    gl_Position = vec4(-1.0,-1.0, 0.5, 1.0 );
    coords = vec2( 0.0, 0.0 ) * res; 
    EmitVertex();

    EndPrimitive(); 
}