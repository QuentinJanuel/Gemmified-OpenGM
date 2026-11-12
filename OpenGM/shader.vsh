// 460 has new stuff for gl_fragcolor etc. dont feel like changing it
#version 330 core

#define MATRIX_VIEW 0
#define MATRIX_PROJECTION 1
#define MATRIX_WORLD 2
#define MATRIX_WORLD_VIEW 3
#define MATRIX_WORLD_VIEW_PROJECTION 4
#define MATRICES_MAX 5
#define FOG_SETTINGS 0
#define FOG_COLOUR 1

layout (location = 0) in vec3 a_pos;
layout (location = 1) in vec4 a_color;
layout (location = 2) in vec2 a_uv;

out vec4 fcolor;
out vec2 texc;
out float fogFactor;

uniform mat4 gm_Matrices[MATRICES_MAX];
uniform float gm_FogStart;
uniform float gm_RcpFogRange;
uniform bool gm_VS_FogEnabled;

float CalcFogFactor(vec4 pos)
{
	if (gm_VS_FogEnabled)
	{
		vec4 viewpos = gm_Matrices[MATRIX_WORLD_VIEW] * pos;
		// -viewpos.z, not viewpos.z: gm_FogStart/gm_RcpFogRange are distances IN FRONT OF the
		// camera, and this view matrix is a GL one (BuildViewMatrices' LookAt puts the eye at
		// z = +16000 looking down -z), so everything visible has negative view-space z. GM's own
		// D3D view matrix is left-handed and gets the same distance as a positive number.
		// Unnegated, ordinary 2D geometry sits at view z = -16000 and every fog factor came out
		// hugely negative, i.e. clamped to zero: gpu_set_fog did nothing at all, whatever a game
		// asked for. tijit engine 2 tints its whole tile mesh by setting fog to (-1, 1) so that
		// everything is fully fogged, which is only a trick that works because of that 16000.
		float fogfactor = ((-viewpos.z - gm_FogStart) * gm_RcpFogRange);
		return fogfactor;
	}
	else
	{
		return 0.0;
	}
}

void main() {
    fcolor = a_color;
    texc = a_uv;
    vec4 pos = vec4(a_pos.xyz, 1);
    fogFactor = CalcFogFactor(pos);
    gl_Position = gm_Matrices[MATRIX_WORLD_VIEW_PROJECTION] * pos;
    gl_PointSize = 1.0;
}