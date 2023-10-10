#version 330 core

layout(location = 0) out vec4 FragColor;

uniform vec3      iResolution;
uniform float     iTime;
uniform float     iChannelTime[4];
uniform vec4      iMouse;
uniform vec4      iDate;
uniform float     iSampleRate;
uniform vec3      iChannelResolution[4];
uniform int       iFrame;
uniform float     iTimeDelta;
uniform float     iFrameRate;
uniform sampler2D iChannel0;
uniform sampler2D iChannel1;
uniform sampler2D iChannel2;



// 2D liquid inside 'sphere' shader.

// Author: Mirza Beig

// https://twitter.com/TheMirzaBeig
// https://www.youtube.com/@MirzaBeig

// Feel free to use this however you want.
// Modify, learn from it, copy-paste, etc...

// This is the compact version of:
// > https://www.shadertoy.com/view/Ds3fRN

/*

INFO:

sdf = gradient for circle.
vB, vbA = vertical band, +animated.

fW, bW = front waves, back waves.

fA = fill animation, just a simple, constant (small) wave.
fP = fill progress, where 0.0 = center.

-0.2 is the default constant 'fill progress'.

im = (iMouse.y / iResolution.y, 

the current normalized mouse Y, centered with -0.5 * 2.0.

fF, bF = front fill, back fill.

*/

#define PI 3.14
//#define IS_INTERACTIVE // Uncomment to control fill with mouse.

// Optional anti-aliasing, as per suggestion by u/fishy:

#define step(b, a) smoothstep(a, a - fwidth(b) * 2.0, b)

void mainImage(out vec4 col, in vec2 uv)
{       
    vec3 r = iResolution;
    //uv=((2.*uv)-r.xy)/r.y;    

    float 
		sdf=length(uv),c=step(sdf,.85),
		
        vB=smoothstep(.1,.9,sin(uv.x+(PI*.5))-.3),
		vBA=vB*sin(iTime*4.)*.1,
		
        fW=(sin(((iTime*2.)+uv.x)*2.)*.05)+vBA,
		bW=(sin(((iTime*-2.)+uv.x)*2.+PI)*.05)-vBA,
		
        fA=(sin(iTime*4.)*.05)*vB,
        
        #ifdef IS_INTERACTIVE
            im=((iMouse.y/r.y)-.5)*2.,        
            fP=im+(sin((iTime)*PI)*.1),
        #else
            fP=-.2+(sin((iTime)*PI)*.1),
        #endif
		
        fF=step(uv.y,(fA+fW)+fP)*c,
		bF=step(uv.y,(-fA+bW)+fP)*c;

    col =
		vec4((step(sdf,1.)-step(sdf,.9))+
		(fF+(clamp(bF-fF,0.,1.)*.8))+
		clamp(pow((sdf+.01)*
		((1.-(fF+bF))*c),5.),0.,1.));
}



in vec2 TexCoords;

void main(void)
{
    FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 color = vec4(1e20);
    mainImage(color, TexCoords);
    color.w = 1.0;
    if (FragColor.w < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (FragColor.x < 0.0) color = vec4(1.0, 0.0, 0.0, 1.0);
    if (FragColor.y < 0.0) color = vec4(0.0, 1.0, 0.0, 1.0);
    if (FragColor.z < 0.0) color = vec4(0.0, 0.0, 1.0, 1.0);
    if (FragColor.w < 0.0) color = vec4(1.0, 1.0, 0.0, 1.0);
    FragColor = vec4(color.xyz, 1.0);
}


