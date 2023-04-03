#version 330

layout(location = 0) out vec4 FragColor;

in vec2 coords;
in vec3 norms;

uniform float time = 0.2f;

uniform sampler2D mainTexture;


uniform vec3 ViewParams = vec3(1,1,1);
uniform ivec2 ScreenParams = ivec2(1920,1080);
uniform vec4 GroundColor = vec4(0.25, 0.25,0.25, 1.0);
uniform vec4 SkyColorHorizon = vec4(0.84f, 0.55f, 0.14f, 1.0);
uniform vec4 SkyColorZenith = vec4(0.14f, 0.25f, 0.84f, 1.0);
uniform vec3 SunLightDirection = vec3(0.3, 0.33, 0.33);
uniform float SunFocus = 2.0f;
uniform float SunIntensity = 4.0f;
uniform int FrameNumber = 0;

uniform mat4 gWVP;
uniform mat4 gWorld;
uniform mat4 modelViewMatrix;
uniform vec3 gEyeWorldPos;
uniform mat4 gCameraWorldMatrix;
uniform mat4 gCameraWorldMatrixProjection;
uniform mat4 gProjectionMatrix;

struct Ray 
{
    vec3 origin;
    vec3 direction;
};

struct RayTracingMaterial
{
    vec4 color;
    vec4 emission;
    float emissionStrength;
};

struct HitInfo
{
    bool didHit;
    float dist;
    vec3 hitPoint;
    vec3 normal;
    RayTracingMaterial material;
};

struct Sphere
{
    vec3 position;
    float radius;
    RayTracingMaterial material;
};

uniform Sphere spheres[32];
uniform int numSpheres;


HitInfo RaySphere(Ray ray, vec3 sphereCenter, float sphereRadius)
{
    HitInfo hitInfo;
    hitInfo.didHit = false;
    hitInfo.dist = 0;
    hitInfo.normal = vec3(0);
    hitInfo.hitPoint = vec3(0);
    hitInfo.material.color = vec4(0,0,0,1);

    vec3 offsetRayOrigin = ray.origin - sphereCenter;

    float a = dot(ray.direction, ray.direction);
    float b = 2 * dot(offsetRayOrigin, ray.direction);
    float c = dot(offsetRayOrigin, offsetRayOrigin) - sphereRadius * sphereRadius;

    float discriminant = b*b - 4 * a * c;

    if (discriminant >= 0)
    {
        float dist = (-b - sqrt(discriminant)) / (2 * a);
        if (dist >= 0)
        {
            hitInfo.didHit = true;
            hitInfo.dist = dist;
            hitInfo.hitPoint = ray.origin + ray.direction * dist;
            hitInfo.normal = normalize(hitInfo.hitPoint - sphereCenter);
            hitInfo.material.color = vec4(1);
        }
    }

    return hitInfo;
};

float RandomValue(inout uint state)
{
    state = state * 74796405u + 2891336453u;
    uint result = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    result = (result >> 22) ^ result;
    return result / 4294967295.0;

}

float RandomValueNormalDistribution(inout uint state)
{
    float theta = 2 * 3.1415926 * RandomValue(state);
    float rho = sqrt(-2 * log(RandomValue(state)));
    return rho * cos(theta);
}

vec3 RandomDirection(inout uint state)
{    
    float x = RandomValueNormalDistribution(state);
    float y = RandomValueNormalDistribution(state);
    float z = RandomValueNormalDistribution(state);
    return normalize(vec3(x,y,z));
}

vec3 RandomHemisphereDirection(vec3 normal, inout uint rngState)
{
    vec3 direction = RandomDirection(rngState);
    return direction * sign(dot(normal, direction));    
}

vec4 RandomColor(vec2 uv)
{
    ivec2 numPixels = ScreenParams.xy;
    ivec2 pixelCoord = ivec2(uv.xy * numPixels);
    int pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;

    uint rngState = uint(pixelIndex);

    float r = RandomValue(rngState);
    float g = RandomValue(rngState);
    float b = RandomValue(rngState);

    return vec4(r,g,b, 1);
}

vec4 GetEnvironmentLight(Ray ray)
{
    float skyGradientT = pow(smoothstep(0, 0.4, ray.direction.y), 0.35);
    vec4 skyGradient = mix(SkyColorHorizon, SkyColorZenith, skyGradientT);
    float sun = pow(max(0, dot(ray.direction, -SunLightDirection)), SunFocus) * SunIntensity;

    float groundToSkyT = smoothstep(-0.01f, 0, ray.direction.y);
    float sunMask = float(groundToSkyT >= 1);
    return mix(GroundColor, skyGradient, groundToSkyT) + sun * sunMask;
}

HitInfo CalculateRayCollision(Ray ray)
{
    HitInfo closest;
    closest.didHit = false;
    closest.dist = 1000000;
    closest.normal = vec3(0);
    closest.hitPoint = vec3(0);

    for (int i = 0; i < numSpheres; i++)
    {
        Sphere sphere = spheres[i];

        HitInfo hitInfo = RaySphere(ray, sphere.position, sphere.radius);

        if (hitInfo.didHit && hitInfo.dist < closest.dist)
        {
            closest = hitInfo;
            closest.material = sphere.material;
        }
    }

    return closest;
}


vec4 Trace(Ray ray, inout uint rngState)
{
    vec4 incomingLight = vec4(0,0,0,1);
    vec4 rayColor = vec4(1);

    for (int i = 0; i < 9; i++)
    {
        HitInfo hitInfo = CalculateRayCollision(ray);
        if (hitInfo.didHit)
        {
            ray.origin = hitInfo.hitPoint;
           // ray.direction = RandomHemisphereDirection(hitInfo.normal, rngState);
            ray.direction = normalize(hitInfo.normal + RandomDirection(rngState));

            RayTracingMaterial material = hitInfo.material;

            vec4 emittedLight = material.emission * material.emissionStrength;
            incomingLight += emittedLight * rayColor;
            rayColor *= material.color;
        }
        else 
        {
            incomingLight += GetEnvironmentLight(ray) * rayColor;
            break;
        }
    }

    return incomingLight;
}


mat3 getRotation()
{
  // Extract the 3x3 rotation matrix from the 4x4 view matrix
  return mat3(
    modelViewMatrix[0].xyz,
    modelViewMatrix[1].xyz,
    modelViewMatrix[2].xyz
  );
}

vec2 getImagePlane()
{
  // http://www.terathon.com/gdc07_lengyel.pdf
  // Extracting aspect and focal from projection matrix:
  // e = focal, h = height, w = width, P = projection matrix
  // P = | e   0       0   0 |
  //     | 0   e/(h/w) 0   0 |
  //     | 0   0       .   . |
  //     | 0   0       -1  0 |
  float focal = gProjectionMatrix[0].x;
  float focalY = gProjectionMatrix[1].y;

  // Fix coordinate aspect and scale
  return vec2( ( coords.x - 0.5 ) * focalY, ( coords.y - 0.5 ) * focal );
}

vec3 getCameraPos( in mat3 rotation )
{
  // Xc = R * Xw + t
  // c = - R.t() * t <=> c = - t.t() * R
  return modelViewMatrix[3].xyz * rotation;
}

vec3 getCamRay( in mat3 rotation, in vec2 screenUV )
{
  // Compute camera ray then rotate it in order to get it in world coordinate
  return vec3( screenUV.x, screenUV.y, gProjectionMatrix[0].x ) * rotation;
}

void main()
{
    //vec4 color = texture2D(mainTexture, coords.st);

    ivec2 numPixels = ScreenParams.xy;
    ivec2 pixelCoord = ivec2(coords.xy * numPixels);
    int pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;
    uint rngState = uint(pixelIndex) + uint(FrameNumber*FrameNumber*FrameNumber*pixelIndex);


    mat3 cameraRotation = getRotation();
    vec2 screenUV = getImagePlane();
    vec3 eyePos = getCameraPos(cameraRotation);
    vec3 rayDir = getCamRay(cameraRotation, screenUV);    

    vec3 viewPointLocal = vec3(coords - 0.5, 1) * ViewParams;    
    vec3 viewPoint = ( modelViewMatrix * vec4(viewPointLocal, 1)).xyz;
    //vec3 viewPoint = ( gCameraWorldMatrixProjection * vec4(viewPointLocal, 1)).xyz;

    Ray ray;
    ray.origin = eyePos;
    ray.direction = rayDir;
        
    vec4 incomingLight = vec4(0,0,0,1);
    for (int i = 0; i < 55; i++)
    {    
        vec4 color = Trace(ray, rngState);
        incomingLight += color;
    }

    incomingLight = incomingLight / 50;
    
    FragColor = incomingLight;
    //FragColor = RandomColor(coords);
    //FragColor = vec4(ray.Direction,0);
    //FragColor = vec4(coords.yyy,1);
    //FragColor = vec4(screenUV.xy, 0, 1);
    
}