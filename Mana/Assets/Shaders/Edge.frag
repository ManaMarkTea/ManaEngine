#version 330

layout(location = 0) out vec4 FragColor;

in vec2 coords;
in vec3 norms;

uniform float time = 0.2f;
uniform sampler2D mainTexture;

vec2 iResolution = vec2(1920,1080);
vec4 iMouse = vec4(1,1,1,1);

float d;

float lookup(vec2 p, float dx, float dy)
{

    vec2 uv = (p.xy + vec2(dx * d, dy * d)) / iResolution.xy;
    vec4 c = texture2D(mainTexture, uv.xy);
	
	// return as luma
    return 0.2126*c.r + 0.7152*c.g + 0.0722*c.b;
}

void mainImage( out vec4 fragColor, vec2 fragCoord )
{
    d = sin(time * 5.0)*0.5 + 1.5; // kernel offset
    vec2 p = fragCoord.xy;
    
	// simple sobel edge detection
    float gx = 0.0;
    gx += -1.0 * lookup(p, -1.0, -1.0);
    gx += -2.0 * lookup(p, -1.0,  0.0);
    gx += -1.0 * lookup(p, -1.0,  1.0);
    gx +=  1.0 * lookup(p,  1.0, -1.0);
    gx +=  2.0 * lookup(p,  1.0,  0.0);
    gx +=  1.0 * lookup(p,  1.0,  1.0);
    
    float gy = 0.0;
    gy += -1.0 * lookup(p, -1.0, -1.0);
    gy += -2.0 * lookup(p,  0.0, -1.0);
    gy += -1.0 * lookup(p,  1.0, -1.0);
    gy +=  1.0 * lookup(p, -1.0,  1.0);
    gy +=  2.0 * lookup(p,  0.0,  1.0);
    gy +=  1.0 * lookup(p,  1.0,  1.0);
    
	// hack: use g^2 to conceal noise in the video
    float g = gx*gx + gy*gy;
    float g2 = g * (sin(time) / 2.0 + 0.5);
    
    vec4 col = texture2D(mainTexture, p / iResolution.xy);
    col += vec4(0.0, g, g2, 1.0);
    
    fragColor = col;
}

void main()
{
    vec4 color = texture2D(mainTexture, coords.st);
    mainImage(color, coords.st);
    //color.a = 1;
    FragColor = color;
}




