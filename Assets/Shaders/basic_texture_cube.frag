#version 330

layout(location = 0) out vec4 FragColor;

in vec3 ObjectPos;
in vec3 WorldPos;
in vec2 TexCoords;

uniform samplerCube mainTexture;
 
void main()
{
    vec4 color = texture(mainTexture, ObjectPos);
    //color = textureLod(mainTexture, ObjectPos, 1.0);
    color.a = 1;
    FragColor = color;
}