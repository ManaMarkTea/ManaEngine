#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
in vec3 WorldPos;
in vec3 Normal;
in vec4 Color;

// material parameters
uniform sampler2D albedoMap;
uniform sampler2D normalMap;
uniform sampler2D metallicMap;
uniform sampler2D roughnessMap;
uniform sampler2D aoMap;
uniform sampler2D emissionMap;
uniform vec3 emissionColor;
uniform float emissionStrength;
uniform float normalMapSelection = 0.0;

// Brought up to what the WebGPU path already reads. These were missing entirely: metallic, roughness and
// occlusion came from their maps alone, so a material with no map got whatever the fallback texture held
// and the sliders did nothing at all.
uniform vec4 albedoColor = vec4(1.0);
// x metallic, y roughness, z occlusion, w alpha cutoff.
uniform vec4 materialFactors = vec4(0.0, 1.0, 1.0, 0.5);
uniform sampler2D opacityMap;
uniform float opacityFactor = 1.0;
// Matches ManaOpacityMode: 1 opaque, 2 cutout, 3 blend.
uniform float opacityMode = 1.0;
// Texture coordinate scale in xy, offset in zw. Applies to every map.
uniform vec4 uvTransform = vec4(1.0, 1.0, 0.0, 0.0);

// IBL
uniform samplerCube irradianceMap;
uniform samplerCube prefilterMap;
uniform sampler2D brdfLUT;
uniform float exposure = 1.0;
uniform float iblIntensity = 1.0;
uniform float pbrDebugMode = 0.0;

// lights
uniform vec3 lightPositions[4];
uniform vec3 lightColors[4];

uniform vec3 camPos;

const float PI = 3.14159265359;
// ----------------------------------------------------------------------------
// Easy trick to get tangent-normals to world-space to keep PBR code simplified.
// Don't worry if you don't get what's going on; you generally want to do normal 
// mapping the usual way for performance anyways; I do plan make a note of this 
// technique somewhere later in the normal mapping tutorial.
vec3 getNormalFromMap(vec2 uv)
{
    vec3 tangentNormal = texture(normalMap, uv).xyz * 2.0 - 1.0;

    vec3 Q1  = dFdx(WorldPos);
    vec3 Q2  = dFdy(WorldPos);
    // Derivatives of the transformed coordinate, so a tiled material's tangent frame tiles with it.
    vec2 st1 = dFdx(uv);
    vec2 st2 = dFdy(uv);

    vec3 N   = normalize(Normal);
    vec3 T  = normalize(Q1*st2.t - Q2*st1.t);
    vec3 B  = -normalize(cross(N, T));
    mat3 TBN = mat3(T, B, N);

    return normalize(TBN * tangentNormal);
}
// ----------------------------------------------------------------------------
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness*roughness;
    float a2 = a*a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH*NdotH;

    float nom   = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return nom / denom;
}
// ----------------------------------------------------------------------------
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r*r) / 8.0;

    float nom   = NdotV;
    float denom = NdotV * (1.0 - k) + k;

    return nom / denom;
}
// ----------------------------------------------------------------------------
float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}
// ----------------------------------------------------------------------------
vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}
// ----------------------------------------------------------------------------
vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}   
// ----------------------------------------------------------------------------
void main()
{		
    vec2 uv = TexCoords * uvTransform.xy + uvTransform.zw;

    vec3 tint = pow(Color.rgb, vec3(2.2));

    vec4 albedoSample = texture(albedoMap, uv);
    vec3 albedo = pow(albedoSample.rgb, vec3(2.2)) * pow(max(albedoColor.rgb, vec3(0.0)), vec3(2.2));

    albedo *= tint;

    // Channels follow the glTF packing: occlusion in R, roughness in G, metallic in B, all one texture.
    // A standalone greyscale map holds the same value in every channel, so this is right for both --
    // whereas reading R everywhere gave the packed map's unused channel and made every loaded model
    // fully metallic and fully rough.
    float metallic = clamp(materialFactors.x * texture(metallicMap, uv).b, 0.0, 1.0);
    float roughness = clamp(materialFactors.y * texture(roughnessMap, uv).g, 0.0, 1.0);
    float ao = clamp(materialFactors.z * texture(aoMap, uv).r, 0.0, 1.0);

    float alpha = Color.a * albedoSample.a * albedoColor.a * opacityFactor * texture(opacityMap, uv).r;
       
    // input lighting data
    vec3 N = normalize(mix(getNormalFromMap(uv), normalize(Normal), normalMapSelection));

    // There seems to be a bug in get from normal map, that results in noise instead on flat normals
    if (normalMapSelection >= 0.999)
    {
        N = normalize(Normal);
    }

    //vec3 N = normalize(Normal);
    vec3 V = normalize(camPos - WorldPos);
    vec3 R = reflect(-V, N); 

    // calculate reflectance at normal incidence; if dia-electric (like plastic) use F0 
    // of 0.04 and if it's a metal, use the albedo color as F0 (metallic workflow)    
    vec3 F0 = vec3(0.04); 
    F0 = mix(F0, albedo, metallic);
        
    // reflectance equation
    vec3 Lo = vec3(0.0);
    for(int i = 0; i < 4; ++i) 
    {
        vec3 lightPos = lightPositions[i].xyz;
        vec3 lightColor = lightColors[i].xyz;

        
        // calculate per-light radiance
        vec3 L = normalize(lightPos - WorldPos);
        vec3 H = normalize(V + L);
        float distance = length(lightPos - WorldPos);
        float attenuation = 1.0 / (distance * distance);
        vec3 radiance = lightColor * attenuation;
        
        // Cook-Torrance BRDF
        float NDF = DistributionGGX(N, H, roughness);   
        float G   = GeometrySmith(N, V, L, roughness);    
        vec3 F    = fresnelSchlick(max(dot(H, V), 0.0), F0);        
        
        vec3 numerator    = NDF * G * F;
        float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.0001; // + 0.0001 to prevent divide by zero
        vec3 specular = numerator / denominator;
        
         // kS is equal to Fresnel
        vec3 kS = F;
        // for energy conservation, the diffuse and specular light can't
        // be above 1.0 (unless the surface emits light); to preserve this
        // relationship the diffuse component (kD) should equal 1.0 - kS.
        vec3 kD = vec3(1.0) - kS;
        // multiply kD by the inverse metalness such that only non-metals 
        // have diffuse lighting, or a linear blend if partly metal (pure metals
        // have no diffuse light).
        kD *= 1.0 - metallic;	                
            
        // scale light by NdotL
        float NdotL = max(dot(N, L), 0.0);        

        // add to outgoing radiance Lo
        //Lo += (kD * albedo / PI + specular) * radiance * NdotL * lightColor; // note that we already multiplied the BRDF by the Fresnel (kS) so we won't multiply by kS again
        Lo += (kD * albedo / PI + specular) * radiance * NdotL; // note that we already multiplied the BRDF by the Fresnel (kS) so we won't multiply by kS again
    }   
    
    // ambient lighting (we now use IBL as the ambient term)
    vec3 F = fresnelSchlickRoughness(max(dot(N, V), 0.0), F0, roughness);
    
    vec3 kS = F;
    vec3 kD = 1.0 - kS;
    kD *= 1.0 - metallic;	  
    
    vec3 irradiance = texture(irradianceMap, N).rgb * iblIntensity;
    vec3 diffuse      = irradiance * albedo;
    
    // sample both the pre-filter map and the BRDF lut and combine them together as per the Split-Sum approximation to get the IBL specular part.
    const float MAX_REFLECTION_LOD = 4.0;
    vec3 prefilteredColor = textureLod(prefilterMap, R,  roughness * MAX_REFLECTION_LOD).rgb * iblIntensity;    
    vec2 brdf  = texture(brdfLUT, vec2(max(dot(N, V), 0.0), roughness)).rg;
    vec3 specular = prefilteredColor * (F * brdf.x + brdf.y);

    vec3 ambient = (kD * diffuse + specular) * ao;
    
    vec3 linear_emission_color = pow(max(emissionColor, vec3(0.0)), vec3(2.2));
    vec3 emissive = linear_emission_color * emissionStrength;

    vec3 color = (ambient + Lo + emissive) * exposure;

    int debugMode = int(pbrDebugMode + 0.5);
    if (debugMode == 1)
    {
        FragColor = vec4(V * 0.5 + vec3(0.5), -1.0);
        return;
    }
    else if (debugMode == 2)
    {
        FragColor = vec4(N * 0.5 + vec3(0.5), -1.0);
        return;
    }
    else if (debugMode == 3)
    {
        FragColor = vec4(R * 0.5 + vec3(0.5), -1.0);
        return;
    }
    else if (debugMode == 4)
    {
        FragColor = vec4(normalize(R) * 0.5 + vec3(0.5), -1.0);
        return;
    }
    else if (debugMode == 5)
    {
        FragColor = vec4(prefilteredColor, -1.0);
        return;
    }
    else if (debugMode == 6)
    {
        FragColor = vec4((F * brdf.x + brdf.y) * iblIntensity, -1.0);
        return;
    }
    else if (debugMode == 7)
    {
        FragColor = vec4(albedo, -1.0);
        return;
    }
    else if (debugMode == 8)
    {
        FragColor = vec4(F0, -1.0);
        return;
    }
    else if (debugMode == 9)
    {
        FragColor = vec4(F, -1.0);
        return;
    }
    else if (debugMode == 10)
    {
        FragColor = vec4(brdf, 0.0, -1.0);
        return;
    }
    else if (debugMode == 11)
    {
        FragColor = vec4(kD * diffuse, -1.0);
        return;
    }
    else if (debugMode == 12)
    {
        FragColor = vec4(specular, -1.0);
        return;
    }
    else if (debugMode == 13)
    {
        FragColor = vec4(Lo, -1.0);
        return;
    }
    else if (debugMode == 14)
    {
        FragColor = vec4(color, -1.0);
        return;
    }

    // Cutout discards below the threshold and is otherwise solid; blend keeps the alpha for the blend
    // state; opaque throws it away. The discard sits last so every derivative above it has been taken.
    int mode = int(opacityMode + 0.5);
    if (mode == 2 && alpha < materialFactors.w)
    {
        discard;
    }

    FragColor = vec4(color, mode == 3 ? alpha : 1.0);
}
