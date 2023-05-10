#version 330

layout(location = 0) out vec4 FragColor;

in vec2 TexCoords;

uniform sampler2D mainTexture;
 
void main()
{
    vec4 color = texture2D(mainTexture, TexCoords.st);
    color.a = 1;
    FragColor = color;
}