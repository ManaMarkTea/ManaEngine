#version 330 core

// Input vertex attributes
in vec3 outPosition;
in vec3 outNormal;
in vec4 outColor;

// Output fragment color
out vec4 outPixelColor;

// Uniform variables
uniform vec3 lightPosition;
uniform vec3 viewPosition;
uniform vec3 albedo = vec3(1,1,1);
uniform float metallic;
uniform float roughness;

// Calculate the lighting
void main()
{
    // Calculate the normal vector
    vec3 N = normalize(outNormal);

    // Calculate the view vector
    vec3 V = normalize(viewPosition - outPosition);

    // Calculate the half vector
    vec3 H = normalize(V + lightPosition);

    // Calculate the diffuse component
    float diffuse = max(dot(N, H), 0.0) + 0.25f;

    // Calculate the specular component
    float specular = pow(max(dot(H, V), 0.0), 64.0);

    // Calculate the final color
    vec3 tintedAlbedo = (vec4(albedo,1) * outColor).xyz;

    outPixelColor = vec4(tintedAlbedo * diffuse + specular * metallic, 1.0);
        
    //outPixelColor = vec4(N,1);
}