#version 330 core

layout(location = 0) out vec4 FragColor;

in vec3 ObjectPos;
in vec3 ObjectCameraPos;

uniform float iTime;
uniform float liquidFillLevel = -0.14;
uniform float liquidWaveStrength = 1.0;
uniform float liquidWaterOpacity = 1.0;
uniform float liquidSurfaceStrength = 1.0;
uniform float liquidPoolContourStrength = 1.0;
uniform float glassEdgeStrength = 1.0;
uniform float glassAirAlpha = 1.0;

const float SphereRadius = 1.0;
const int StepCount = 64;

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
        p = p * 2.08 + vec3(9.7, 3.1, 6.4);
        amp *= 0.5;
    }
    return sum;
}

float liquidHeight(vec3 p)
{
    float sway = sin(iTime * 1.55) * 0.045 * liquidWaveStrength;
    float waveA = sin(p.x * 4.3 + iTime * 2.4) * 0.045 * liquidWaveStrength;
    float waveB = sin(p.z * 3.7 - iTime * 1.8 + p.x * 1.4) * 0.03 * liquidWaveStrength;
    float ripple = (fbm(vec3(p.xz * 2.3, iTime * 0.3)) - 0.5) * 0.035 * liquidWaveStrength;

    return liquidFillLevel + sway + waveA + waveB + ripple;
}

vec3 liquidSurfaceNormal(vec3 p)
{
    const float e = 0.025;
    float dx = liquidHeight(p + vec3(e, 0.0, 0.0)) - liquidHeight(p - vec3(e, 0.0, 0.0));
    float dz = liquidHeight(p + vec3(0.0, 0.0, e)) - liquidHeight(p - vec3(0.0, 0.0, e));
    return normalize(vec3(-dx / (2.0 * e), 1.0, -dz / (2.0 * e)));
}

float bubbleField(vec3 p)
{
    vec3 cell = floor(p * 8.0 + vec3(0.0, iTime * 0.65, 0.0));
    vec3 local = fract(p * 8.0 + vec3(0.0, iTime * 0.65, 0.0)) - 0.5;
    float h = hash13(cell);
    vec3 bubbleCenter = vec3(
        h - 0.5,
        fract(h * 7.31) - 0.5,
        fract(h * 13.17) - 0.5
    ) * 0.42;

    float d = length(local - bubbleCenter);
    float size = mix(0.045, 0.13, fract(h * 19.7));
    float bubble = 1.0 - smoothstep(size * 0.35, size, d);
    float sparse = smoothstep(0.78, 0.97, h);

    return bubble * sparse;
}

void main(void)
{
    vec3 rayOrigin = ObjectCameraPos;
    vec3 marchDir = normalize(ObjectPos - ObjectCameraPos);
    float rayB = dot(rayOrigin, marchDir);
    float rayC = dot(rayOrigin, rayOrigin) - SphereRadius * SphereRadius;
    float rayDiscriminant = rayB * rayB - rayC;
    if (rayDiscriminant <= 0.0)
    {
        FragColor = vec4(0.0);
        return;
    }

    float rayRoot = sqrt(rayDiscriminant);
    float rayEnter = max(-rayB - rayRoot, 0.0);
    float rayExit = -rayB + rayRoot;
    float maxDistance = rayExit - rayEnter;
    if (maxDistance <= 0.0)
    {
        FragColor = vec4(0.0);
        return;
    }

    vec3 frontPos = rayOrigin + marchDir * rayEnter;
    vec3 backPos = rayOrigin + marchDir * rayExit;
    vec3 normal = normalize(frontPos);
    vec3 viewDir = -marchDir;
    vec3 startPos = frontPos + marchDir * 0.002;

    float stepSize = maxDistance / float(StepCount);

    vec3 accum = vec3(0.0);
    float liquidAmount = 0.0;
    float surfaceAmount = 0.0;
    float bubbleAmount = 0.0;
    float transmittance = 1.0;

    for (int i = 0; i < StepCount; i++)
    {
        float travel = (float(i) + 0.5) * stepSize;
        vec3 p = startPos + marchDir * travel;
        float radius = length(p);

        if (radius > SphereRadius)
        {
            break;
        }

        float radius01 = radius / SphereRadius;
        float shellFade = 1.0 - smoothstep(0.985, 1.0, radius01);
        float height = liquidHeight(p);
        float signedLiquidDepth = height - p.y;
        float liquidMask = smoothstep(-0.008, 0.02, signedLiquidDepth) * shellFade;
        float depthTint = smoothstep(0.0, 0.75, signedLiquidDepth);

        float surfaceBand = exp(-abs(p.y - height) * 155.0) * smoothstep(0.03, 0.95, 1.0 - radius01);
        float foam = fbm(vec3(p.xz * 9.0, iTime * 0.8));
        surfaceBand *= 0.65 + 0.35 * smoothstep(0.35, 0.92, foam);

        float bubbles = bubbleField(p) * liquidMask * smoothstep(0.0, 0.32, signedLiquidDepth);
        float caustic = 0.55 + 0.45 * sin((p.x + p.z) * 16.0 + iTime * 5.0 + fbm(p * 5.0) * 5.0);

        vec3 shallowColor = vec3(0.05, 0.72, 0.95);
        vec3 deepColor = vec3(0.00, 0.22, 0.62);
        vec3 liquidColor = mix(shallowColor, deepColor, depthTint);
        vec3 surfaceColor = vec3(0.78, 1.0, 0.98);
        vec3 bubbleColor = vec3(0.88, 1.0, 1.0);

        vec3 sampleColor = liquidColor * liquidMask * (0.92 + caustic * 0.28) * liquidWaterOpacity;
        sampleColor += surfaceColor * surfaceBand * 0.68 * liquidSurfaceStrength;
        sampleColor += bubbleColor * bubbles * 0.24;

        float density = liquidMask * 0.065 + surfaceBand * 0.035 + bubbles * 0.035;
        accum += transmittance * sampleColor * stepSize * 3.4;
        transmittance *= exp(-density);

        liquidAmount += liquidMask * stepSize * liquidWaterOpacity;
        surfaceAmount += surfaceBand * stepSize * 2.15 * liquidSurfaceStrength;
        bubbleAmount += bubbles * stepSize;
    }

    vec3 surfaceSheet = vec3(0.0);
    float surfaceSheetAlpha = 0.0;
    if (abs(marchDir.y) > 0.001)
    {
        float surfaceTravel = (liquidFillLevel - startPos.y) / marchDir.y;
        for (int i = 0; i < 4; i++)
        {
            vec3 surfacePoint = startPos + marchDir * surfaceTravel;
            surfaceTravel += (liquidHeight(surfacePoint) - surfacePoint.y) / marchDir.y;
        }

        vec3 surfacePoint = startPos + marchDir * surfaceTravel;
        float surfaceRadius = length(surfacePoint);
        if (surfaceTravel > 0.0 && surfaceTravel < maxDistance && surfaceRadius < SphereRadius)
        {
            vec3 surfaceNormal = liquidSurfaceNormal(surfacePoint);
            float maxSurfaceRadius = sqrt(max(SphereRadius * SphereRadius - surfacePoint.y * surfacePoint.y, 0.0));
            float surfaceRadius01 = surfaceRadius / SphereRadius;
            float interiorSurfaceFade = 1.0 - smoothstep(0.975, 1.0, surfaceRadius01);
            float rimSurfaceFade = 1.0 - smoothstep(0.998, 1.006, surfaceRadius01);
            float surfaceDistanceToWall = maxSurfaceRadius - length(surfacePoint.xz);
            float discRadius01 = length(surfacePoint.xz) / max(maxSurfaceRadius, 0.001);
            float poolDisc = 1.0 - smoothstep(0.18, 0.98, discRadius01);
            float poolContour = (1.0 - smoothstep(0.018, 0.13, surfaceDistanceToWall)) * rimSurfaceFade * liquidPoolContourStrength;
            float meniscus = poolContour;
            float glint = pow(max(dot(reflect(-viewDir, surfaceNormal), normalize(vec3(-0.45, 0.75, 0.48))), 0.0), 72.0);
            float grazing = pow(1.0 - abs(dot(surfaceNormal, viewDir)), 1.6);
            float surfacePattern = 0.62 + 0.38 * smoothstep(0.42, 0.9, fbm(vec3(surfacePoint.xz * 12.0, iTime * 0.85)));

            float sheetInterior = 0.32 + grazing * 0.26;
            surfaceSheet = (
                vec3(0.01, 0.22, 0.38) * poolDisc * 0.62 * liquidPoolContourStrength +
                vec3(0.02, 0.34, 0.52) * sheetInterior * interiorSurfaceFade +
                vec3(0.75, 1.0, 0.96) * surfacePattern * 0.1 +
                vec3(1.0, 1.0, 0.92) * glint * 1.45 +
                vec3(0.36, 0.95, 1.0) * meniscus * 1.35
            ) * liquidSurfaceStrength;
            surfaceSheetAlpha = clamp(
                (poolDisc * 0.16 * liquidPoolContourStrength + grazing * 0.12 + meniscus * 0.62 + glint * 0.6) * liquidSurfaceStrength,
                0.0,
                0.78
            );
        }
    }

    float backLiquidDepth = liquidHeight(backPos) - backPos.y;
    float backLiquidMask = smoothstep(-0.015, 0.055, backLiquidDepth);
    float lowerBowl = smoothstep(0.05, 0.86, -backPos.y);
    float bowlRadius = length(backPos.xz);
    float bowlDisk = 1.0 - smoothstep(0.12, 0.88, bowlRadius);
    float bowlRing = exp(-pow((bowlRadius - 0.54) / 0.12, 2.0));
    float bowlFresnel = pow(1.0 - max(dot(normalize(backPos), viewDir), 0.0), 2.4);
    float centeredBowl = backLiquidMask * lowerBowl * liquidPoolContourStrength;
    vec3 bottomBowl = (
        vec3(0.01, 0.18, 0.34) * bowlDisk * 0.85 +
        vec3(0.08, 0.62, 0.78) * bowlRing * 0.46 +
        vec3(0.58, 1.0, 0.96) * bowlFresnel * 0.18
    ) * centeredBowl * liquidWaterOpacity;
    float bottomBowlAlpha = clamp(centeredBowl * (bowlDisk * 0.2 + bowlRing * 0.16 + bowlFresnel * 0.08), 0.0, 0.34);

    float facing = max(dot(normal, viewDir), 0.0);
    float fresnel = pow(1.0 - facing, 3.2);
    float rimMask = 1.0;
    vec3 glass = vec3(0.004, 0.014, 0.02) + vec3(0.065, 0.28, 0.44) * fresnel * rimMask;
    float shellOpticalDepth = clamp(0.035 / max(facing, 0.075), 0.0, 0.42) * rimMask;
    float outerGlassEdge = pow(1.0 - facing, 2.1) * rimMask;
    float brightGlassEdge = pow(1.0 - facing, 7.0) * rimMask;
    float silhouetteLine = (1.0 - smoothstep(0.015, 0.15, facing)) * rimMask;
    float innerGlassWall = exp(-pow((facing - 0.18) / 0.07, 2.0)) * rimMask;
    vec3 thickGlass =
        vec3(0.03, 0.18, 0.26) * shellOpticalDepth +
        vec3(0.12, 0.58, 0.78) * outerGlassEdge * 0.42 +
        vec3(0.86, 1.0, 0.94) * brightGlassEdge * 0.38 +
        vec3(0.92, 1.0, 0.96) * silhouetteLine * 0.92 +
        vec3(0.02, 0.32, 0.46) * innerGlassWall * 0.42;
    thickGlass *= glassEdgeStrength;
    vec3 highlight = vec3(0.75, 1.0, 0.95) * pow(max(dot(reflect(-viewDir, normal), normalize(vec3(-0.45, 0.75, 0.48))), 0.0), 48.0) * 0.42;

    vec3 color = accum + surfaceSheet + bottomBowl + glass * 0.45 + thickGlass + highlight;
    color = vec3(1.0) - exp(-color * 1.35);
    color = pow(color, vec3(0.82));

    float alpha = (0.035 + fresnel * 0.12) * glassAirAlpha + (shellOpticalDepth * 0.55 + brightGlassEdge * 0.2) * glassEdgeStrength;
    alpha += clamp(liquidAmount * 0.88 + surfaceAmount * 1.05 + bubbleAmount * 0.7 + surfaceSheetAlpha * 0.75 + bottomBowlAlpha, 0.0, 0.72);
    alpha = max(alpha, (silhouetteLine * 0.86 + innerGlassWall * 0.18) * glassEdgeStrength);
    alpha = clamp(alpha, 0.035, 0.82);

    FragColor = vec4(color, alpha);
}
