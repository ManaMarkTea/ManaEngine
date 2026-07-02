#version 330 core

layout(location = 0) out vec4 FragColor;

in vec3 ObjectPos;
in vec3 ObjectCameraPos;

uniform float iTime;
uniform float sphereRadius = 1.0;
uniform float intensity = 1.0;
uniform vec3 coreColorTint = vec3(2.9, 3.0, 3.15);
uniform vec3 haloColorTint = vec3(0.12, 0.62, 2.25);

const int StepCount = 60;
const int BoltCount = 7;
const int SegmentCount = 6;

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
        p = p * 2.07 + vec3(11.7, 4.2, 8.3);
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

void basisFromDirection(vec3 dir, out vec3 right, out vec3 up)
{
    vec3 seed = abs(dir.y) < 0.92 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    right = normalize(cross(seed, dir));
    up = cross(dir, right);
}

vec3 boltDirection(float fi, float t)
{
    float spin = t * (0.23 + fi * 0.017) + fi * 2.39;
    float yaw = spin + 1.35 * sin(t * 0.19 + fi * 1.71);
    float pitch = 0.48 * sin(t * (0.31 + fi * 0.021) + fi * 2.07);
    float cp = cos(pitch);

    return normalize(vec3(cos(yaw) * cp, sin(pitch), sin(yaw) * cp));
}

vec3 boltPoint(vec3 dir, vec3 right, vec3 up, float s, float fi, float t)
{
    float bend = sin(s * 3.14159265);
    float crawl = t * (3.2 + fi * 0.29) + fi * 5.17;
    float crackle = sin(s * 21.0 + crawl) + 0.45 * sin(s * 43.0 - crawl * 1.37);
    float fork = cos(s * 17.0 - crawl * 0.83) + 0.35 * sin(s * 31.0 + fi);
    float amp = bend * (0.055 + 0.035 * smoothstep(0.15, 0.9, s));
    vec3 wander = (right * crackle + up * fork) * amp;

    return dir * (s * 0.94 * sphereRadius) + wander;
}

vec3 branchPoint(vec3 start, vec3 dir, vec3 right, vec3 up, float s, float fi, float t)
{
    float bend = sin(s * 3.14159265);
    float wiggle = t * (4.8 + fi * 0.2) + fi * 9.1;
    vec3 wander = (right * sin(s * 19.0 + wiggle) + up * cos(s * 23.0 - wiggle)) * bend * 0.045;
    return mix(start, dir * 0.86 * sphereRadius, s) + wander;
}

vec3 boltField(vec3 p, float fi, float t)
{
    vec3 dir = boltDirection(fi, t);
    vec3 right;
    vec3 up;
    basisFromDirection(dir, right, up);

    float dMain = 10.0;
    vec3 prev = vec3(0.0);
    for (int i = 1; i <= SegmentCount; i++)
    {
        float s = float(i) / float(SegmentCount);
        vec3 nextPoint = boltPoint(dir, right, up, s, fi, t);
        dMain = min(dMain, segmentDistance(p, prev, nextPoint));
        prev = nextPoint;
    }

    float forkSide = fi * 1.618 + 3.0;
    vec3 forkDir = normalize(mix(dir, boltDirection(forkSide, t * 0.73), 0.52));
    vec3 forkRight;
    vec3 forkUp;
    basisFromDirection(forkDir, forkRight, forkUp);

    float dBranch = 10.0;
    vec3 branchStart = boltPoint(dir, right, up, 0.47, fi, t);
    prev = branchStart;
    for (int i = 1; i <= 3; i++)
    {
        float s = float(i) / 3.0;
        vec3 nextPoint = branchPoint(branchStart, forkDir, forkRight, forkUp, s, fi, t);
        dBranch = min(dBranch, segmentDistance(p, prev, nextPoint));
        prev = nextPoint;
    }

    float flicker = 0.5 + 0.5 * sin(t * (8.5 + fi * 0.53) + fi * 4.7);
    float active = mix(0.28, 1.0, smoothstep(0.16, 0.92, flicker));
    float chase = 0.72 + 0.28 * sin(t * 28.0 + fi * 13.0 - length(p) * 29.0);

    float mainCore = exp(-pow(dMain / 0.012, 2.0));
    float mainGlow = exp(-pow(dMain / 0.075, 2.0));
    float branchCore = exp(-pow(dBranch / 0.009, 2.0));
    float branchGlow = exp(-pow(dBranch / 0.052, 2.0));

    vec3 tip = boltPoint(dir, right, up, 1.0, fi, t);
    float contact = exp(-length(p - tip) * 17.0) * smoothstep(0.72, 1.0, length(p) / sphereRadius);

    return vec3(
        (mainCore + branchCore * 0.7) * active * chase,
        (mainGlow + branchGlow * 0.55) * active,
        contact * active
    );
}

vec3 lightningField(vec3 p, float t)
{
    vec3 field = vec3(0.0);

    for (int i = 0; i < BoltCount; i++)
    {
        vec3 bolt = boltField(p, float(i) + 1.0, t);
        field.x = max(field.x, bolt.x);
        field.y += bolt.y * 0.42;
        field.z = max(field.z, bolt.z);
    }

    field.y = min(field.y, 1.7);
    return field;
}

void main(void)
{
    vec3 normal = normalize(ObjectPos);
    vec3 viewDir = normalize(ObjectCameraPos - ObjectPos);
    vec3 marchDir = normalize(-viewDir);
    vec3 startPos = ObjectPos - normal * 0.002;

    float maxDistance = sphereRadius * 2.05;
    float stepSize = maxDistance / float(StepCount);

    vec3 accum = vec3(0.0);
    float transmittance = 1.0;

    for (int i = 0; i < StepCount; i++)
    {
        float travel = (float(i) + 0.5) * stepSize;
        vec3 p = startPos + marchDir * travel;
        float radius = length(p);

        if (radius > sphereRadius)
        {
            break;
        }

        float radius01 = radius / sphereRadius;
        float insideFade = 1.0 - smoothstep(0.97, 1.0, radius01);
        float centerGlow = exp(-radius * 6.0) * (0.75 + 0.25 * sin(iTime * 16.0));
        float haze = fbm(p * 3.2 + vec3(iTime * 0.23, -iTime * 0.31, iTime * 0.17));
        haze = smoothstep(0.25, 0.95, haze) * pow(1.0 - radius01, 0.55);

        vec3 field = lightningField(p, iTime) * intensity;

        vec3 coreColor = coreColorTint * field.x;
        vec3 haloColor = haloColorTint * field.y;
        vec3 tipColor = vec3(1.0, 0.38, 1.85) * field.z;
        vec3 hazeColor = vec3(0.025, 0.09, 0.26) * haze;
        vec3 centerColor = vec3(0.25, 0.62, 1.4) * centerGlow;

        vec3 sampleColor = (coreColor + haloColor + tipColor + hazeColor + centerColor) * insideFade;
        float density = clamp(field.x * 0.42 + field.y * 0.075 + field.z * 0.18 + haze * 0.025 + centerGlow * 0.04, 0.0, 1.0);

        accum += transmittance * sampleColor * stepSize * 4.8;
        transmittance *= exp(-density * 0.08);

        if (transmittance < 0.015)
        {
            break;
        }
    }

    float facing = max(dot(normal, viewDir), 0.0);
    float fresnel = pow(1.0 - facing, 3.1);
    float rimMask = smoothstep(0.48, 1.0, length(ObjectPos) / sphereRadius);
    vec3 rim = vec3(0.22, 0.72, 1.85) * fresnel * rimMask * 2.8;
    vec3 glass = vec3(0.018, 0.055, 0.13) + vec3(0.045, 0.13, 0.32) * fresnel;

    vec3 color = accum + rim + glass;
    color = vec3(1.0) - exp(-color * 1.2);
    color = pow(color, vec3(0.78));

    float glowEnergy = clamp(max(max(accum.r, accum.g), accum.b) * 0.28, 0.0, 1.0);
    float alpha = clamp(0.08 + fresnel * 0.34 + glowEnergy * 0.58, 0.08, 0.78);

    float boost = 1.0 + glowEnergy * 5.0;
    FragColor = vec4(color * boost, alpha);
}
