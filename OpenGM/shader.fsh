// 460 has new stuff for gl_fragcolor etc. dont feel like changing it
#version 330 core

in vec4 fcolor;
in vec2 texc;
in float fogFactor;

uniform sampler2D gm_BaseTexture;
uniform bool gm_PS_FogEnabled;
uniform vec4 gm_FogColour;
uniform bool gm_AlphaTestEnabled;
uniform float gm_AlphaRefValue;

void DoAlphaTest(vec4 SrcColour)
{
    if (gm_AlphaTestEnabled)
    {
        if (SrcColour.a <= gm_AlphaRefValue)
        {
            discard;
        }
    }
}

void DoFog(inout vec4 SrcColour, float fogval)
{
	if (gm_PS_FogEnabled)
	{
		// RGB only -- fog recolours a pixel, it does not make transparent parts of a sprite
		// opaque. Mixing all four channels meant a fully fogged sprite came out as a solid
		// rectangle the size of its quad: tijit engine 2 draws its drop shadows by rendering
		// objects under white fog and tinting the result, so the player's shadow was a box
		// instead of its silhouette. Fixed-function D3D fog blends colour and leaves alpha
		// alone, and the real runner matches that.
		SrcColour.rgb = mix(SrcColour.rgb, gm_FogColour.rgb, clamp(fogval, 0.0, 1.0));
	}
}

void main() {
    vec4 color = texture2D(gm_BaseTexture, texc).rgba * fcolor.rgba;
    DoAlphaTest(color);
    DoFog(color, fogFactor);
    gl_FragColor = color;
}