#version 330 core

layout(location = 0) out vec4 FragColor;

in vec3 ObjectPos;
in vec3 ObjectCameraPos;

uniform float iTime;

const float SphereRadius = 0.5;

float hash13(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 19.19);
    return fract((p.x + p.y) * p.z);
}

float noise3d(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float n000 = hash13(i + vec3(0.0, 0.0, 0.0));
    float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash13(i + vec3(1.0, 1.0, 1.0));

    float nx00 = mix(n000, n100, u.x);
    float nx10 = mix(n010, n110, u.x);
    float nx01 = mix(n001, n101, u.x);
    float nx11 = mix(n011, n111, u.x);

    float nxy0 = mix(nx00, nx10, u.y);
    float nxy1 = mix(nx01, nx11, u.y);

    return mix(nxy0, nxy1, u.z);
}

float fbm(vec3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += amp * noise3d(p);
        p = p * 2.03 + vec3(11.7, 4.2, 8.3);
        amp *= 0.5;
    }
    return sum;
}

float segmentDistance(vec3 p, vec3 a, vec3 b)
{
    vec3 pa = p - a;
    vec3 ba = b - a;
    float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-5), 0.0, 1.0);
    return length(pa - ba * h);
}

float teslaBolts(vec3 p, float t)
{
    float field = 0.0;

    // Keep bolt count modest for performance; thicken lines via falloff below.
    for (int i = 0; i < 4; i++)
    {
        float fi = float(i);
        float phase = t * (1.8 + fi * 0.17);

        vec3 dir = normalize(vec3(
            sin(phase + fi * 1.37),
            cos(phase * 1.23 + fi * 2.11),
            sin(phase * 1.57 - fi * 0.79)
        ));

        vec3 a = dir * 0.42 * SphereRadius;
        vec3 b = -dir * 0.96 * SphereRadius;

        vec3 wobbleA = vec3(
            sin(t * 5.0 + fi * 3.1),
            cos(t * 4.2 + fi * 1.7),
            sin(t * 3.4 + fi * 2.4)
        ) * 0.055 * SphereRadius;

        vec3 wobbleB = vec3(
            cos(t * 4.6 + fi * 1.1),
            sin(t * 3.8 + fi * 2.2),
            cos(t * 5.3 + fi * 0.7)
        ) * 0.075 * SphereRadius;

        float dMain = segmentDistance(p, a + wobbleA, b + wobbleB);
        float mainBolt = exp(-dMain * 92.0);

        vec3 branchEnd = mix(a, b, 0.58) + normalize(vec3(
            sin(fi * 7.1 + t * 2.3),
            cos(fi * 5.3 - t * 1.8),
            sin(fi * 4.7 + t * 2.9)
        )) * 0.42 * SphereRadius;

        float dBranch = segmentDistance(p, a + wobbleA * 0.5, branchEnd);
        float branchBolt = exp(-dBranch * 76.0) * 0.7;

        // Cheaper gating than high-frequency fbm while retaining temporal breakup.
        float gateNoise = noise3d(p * 5.0 + vec3(fi * 7.1, t * 0.55, -t * 0.4));
        float gate = smoothstep(0.42, 0.82, gateNoise);
        float bolt = max(mainBolt, branchBolt) * gate;
        field = max(field, bolt);
    }

    return clamp(field, 0.0, 1.0);
}

void main(void)
{
    vec3 normal = normalize(ObjectPos);
    vec3 viewDir = normalize(ObjectCameraPos - ObjectPos);
    vec3 marchDir = normalize(-viewDir);
    vec3 startPos = ObjectPos - normal * 0.01;

    const int stepCount = 56;
    float maxDistance = SphereRadius * 2.0;
    float stepSize = maxDistance / float(stepCount);

    vec3 accum = vec3(0.0);
    float transmittance = 1.0;

    for (int i = 0; i < stepCount; i++)
    {
        float t = (float(i) + 0.5) * stepSize;
        vec3 p = startPos + marchDir * t;

        if (length(p) > SphereRadius)
        {
            break;
        }

        float radius01 = length(p) / SphereRadius;
        float edgeFade = 1.0 - smoothstep(0.83, 1.00, radius01);
        float centerFade = smoothstep(1.00, 0.15, radius01);

        float bolts = teslaBolts(p, iTime);
        float sparseShell = 1.0 - smoothstep(0.06, 0.95, radius01);
        float density = bolts * edgeFade * centerFade * sparseShell;

        float core = pow(bolts, 2.2);
        float halo = bolts;
        vec3 arcColor = vec3(0.13, 0.50, 1.55) * halo + vec3(1.9, 1.9, 2.0) * core;

        float hazeNoise = 0.7 + 0.3 * noise3d(p * 4.0 + vec3(iTime * 0.7, -iTime * 0.45, iTime * 0.35));
        float hazeMask = pow(1.0 - clamp(radius01, 0.0, 1.0), 1.6);
        vec3 hazeColor = vec3(0.03, 0.09, 0.22) * hazeMask * hazeNoise;

        float pulse = 0.72 + 0.28 * sin(iTime * 18.0 + float(i) * 0.13 + radius01 * 40.0);
        pulse = clamp(pulse, 0.35, 1.0);
        arcColor *= pulse;

        float attenuation = density * 0.18;

        accum += transmittance * (arcColor * attenuation * 10.5 + hazeColor * 0.03);
        transmittance *= exp(-attenuation * 1.8);

        if (transmittance < 0.02)
        {
            break;
        }
    }

    float fresnel = pow(1.0 - max(dot(normal, viewDir), 0.0), 4.0);
    float rimMask = smoothstep(0.55, 1.00, length(ObjectPos) / SphereRadius);
    vec3 rim = vec3(0.55, 0.82, 1.5) * fresnel * rimMask * 6.0;
    vec3 coreGlow = vec3(0.06, 0.14, 0.35);

    vec3 color = (accum + rim + coreGlow) * 1.45;
    color = pow(color, vec3(0.86));
    float alpha = 1.0;

    FragColor = vec4(color, alpha);
}
