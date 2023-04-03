
#version 330

layout(location = 0) out vec4 FragColor;

in vec2 coords;
in vec3 norms;
uniform sampler2D mainTexture;
 
void main()
{
    vec4 color = texture2D(mainTexture, coords.st);
    color.a = 1;
    FragColor = color;
}