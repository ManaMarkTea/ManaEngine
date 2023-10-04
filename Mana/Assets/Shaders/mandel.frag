#version 330

uniform sampler1D tex;
uniform highp vec2 center;
uniform highp float scale = 1;
uniform int iter = 500;

out vec4 FragColor;

in vec2 coords;

void main() 
{
	highp vec2 z, c;

	c.x = 1.3333 * (coords.x - 0.5) * scale - center.x;
	c.y = (coords.y - 0.5) * scale - center.y;

	int i;
	z = c;
	
    for (i = 0; i < iter; i++) 
    {
		highp float x = (z.x * z.x - z.y * z.y) + c.x;
		highp float y = (z.y * z.x + z.x * z.y) + c.y;

		if ((x * x + y * y) > 4.0) break;
		z.x = x;
		z.y = y;
	}

	//FragColor = texture1D(tex, (i == iter ? 0.0 : float(i)) / 100.0);
	float value = ((i == iter ? 0.0 : float(i)) / 100.0);
	FragColor = vec4(0.1, value * 0.25f, value, 1.0f);
}