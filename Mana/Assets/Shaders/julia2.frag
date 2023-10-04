#version 330

uniform sampler1D tex;
uniform vec2 center;
uniform float scale;
uniform int iter = 500;

out vec4 FragColor;

in vec2 coords;


void main()
{		
    int i = 0;
    highp float zx, zy;
    zx = 3 * (coords.x - 0.5) * scale - center.x;
    zy = 2 * (coords.y - 0.5) * scale - center.y;
    float n= 2;

    while (zx * zx + zy * zy < 4  &&  i < iter) 
    {
        float distance = (zx * zx + zy * zy);			

        highp float xtmp =  pow(distance,  (n / 2)) * cos(n * atan(zy, zx)) + center.x;
        zy = pow(distance, (n / 2)) * sin(n * atan(zy, zx)) + center.y;
        zx = xtmp;

        i++;
    } 

    if (i == iter)
    {
        FragColor = vec4(0,0,0,1.0f);
        return;
    }

    float value = ((i == iter ? 0.0 : float(i)) / 10.0);
    float value2 = 0;
    float value3 = 0;

    if ( i > 0 && i < 100 )
    {
        value = (float(i) / 100.0);
    }
    else if ( i > 100 && i < 200 )
    {
        value = (float(i) / 200.0);
    }
    else if ( i > 200 && i < 300 )
    {
        //value = (float(i) / 300.0);
        value = 0;
        value2 = (i - 200.0f) / 100f;
    }
    else if ( i > 300 && i < 400 )
    {
        //value = (float(i) / 400.0);
        value = 0;
        value2 = (i - 300.0f) / 200f;
    }
    else if ( i > 400)
    {
        value = 0;
        value2 = 0;//(i - 400.0f) / 300f;
        value3 = (i - 400.0f) / 300f;
    }

    value = clamp(value, 0, 0.9f);
    value2 = clamp(value2, 0, 0.9f);
    value3 = clamp(value3, 0, 0.9f);

    FragColor = vec4(0.1 + value2 ,value2 * 0.5f + value * 0.25f + value3, value, 1.0f);
}