Shader "CRTFilter"
{
    HLSLINCLUDE
    
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // The Blit.hlsl file provides the vertex shader (Vert),
        // the input structure (Attributes), and the output structure (Varyings)
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"			

		// parameters
        uniform float p_time;
		uniform float p_screenBend;
		uniform float p_screenOverscan;
		uniform float p_blur;
		uniform float p_smidge;
		uniform float p_bleedr;
		uniform float p_bleedg;
		uniform float p_bleedb;
		uniform float p_resX;
		uniform float p_resY;
		uniform float p_scanlinesStrength;
		uniform float p_apertureStrength;
		uniform float p_shadowlines;			
		uniform float p_shadowlinesSpeed;
		uniform float p_shadowlinesAlpha;
		uniform float p_vignetteSize;
		uniform float p_vignetteSmooth;
		uniform float p_vignetteRound;

		uniform float p_glitchFrequency;
		uniform float p_glitchBandSize;
		uniform float p_glitchLineSize;
		uniform float p_glitchPositionFlicker;
		uniform float p_glitchSpeed;
		uniform float p_glitchActiveBand;
		uniform float p_glitchStrength;
		uniform float p_glitchDistortion;
		uniform float p_glitchNoiseSpeed;
		uniform float p_glitchNoise;

		uniform float p_noiseSize;
		uniform float p_noiseAlpha;
		uniform float p_noiseSpeed;
		uniform float p_brightness;
		uniform float p_contrast;
		uniform float p_gamma;
		uniform float p_red;
		uniform float p_green;
		uniform float p_blue;
		uniform float2 p_redOffset;
		uniform float2 p_greenOffset;
		uniform float2 p_blueOffset;

		// variables
		static const float pi = 3.141592653589793238462;
		float2 m_pixSize;

		float3 tex(float2 uv)
		{
			return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
		}
		
		float2 pixel_size()
		{
			return float2((_BlitTexture_TexelSize.z / p_resX) * _BlitTexture_TexelSize.x, (_BlitTexture_TexelSize.w / p_resY) * _BlitTexture_TexelSize.y);				
		}

		float2 pixel_part(float2 uv)
		{
			return float2(floor(fmod(uv.x, m_pixSize.x) * _BlitTexture_TexelSize.z), floor(fmod(uv.y, m_pixSize.y) * _BlitTexture_TexelSize.w));
		}
		float pixel_part_x(float uvx)
		{
			return floor(fmod(uvx, m_pixSize.x) * _BlitTexture_TexelSize.z);
		}
		float pixel_part_y(float uvy)
		{
			return floor(fmod(uvy, m_pixSize.y) * _BlitTexture_TexelSize.w);
		}

		float2 pixel_frac(float2 uv)
		{
			return float2(fmod(uv.x, m_pixSize.x) * p_resX, fmod(uv.y, m_pixSize.y) * p_resY);
		}
		float pixel_frac_x(float uvx)
		{
			return fmod(uvx, m_pixSize.x) * p_resX;
		}
		float pixel_frac_y(float uvy)
		{
			return fmod(uvy, m_pixSize.y) * p_resY;
		}

		float2 pixel_num(float2 uv)
		{
			return float2(floor(uv.x / m_pixSize.x), floor(uv.y / m_pixSize.y));
		}
		float pixel_num_x(float uvx)
		{
			return floor(uvx / m_pixSize.x);
		}
		float pixel_num_y(float uvy)
		{
			return floor(uvy / m_pixSize.y);
		}

        float random_for_noise(float2 uv)
        {
            return frac(sin(dot(uv, float2(12.4898,78.233))) * 43758.541987 * sin(p_time * p_noiseSpeed));
        }

		float noise(float2 uv)
		{
			float2 i = floor(uv);
			float2 f = frac(uv);

			float a = random_for_noise(i);
			float b = random_for_noise(i + float2(1.0, 0.0));
			float c = random_for_noise(i + float2(0.0, 1.0));
			float d = random_for_noise(i + float2(1.0, 1.0));

			float2 u = smoothstep(0.0, 1.0, f);

			return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
		}

		float vignette(float2 uv)
		{
			uv -= 0.5;
			uv *= p_vignetteSize;
			float amount = 1.0 - sqrt(pow(abs(uv.x), p_vignetteRound) + pow(abs(uv.y), p_vignetteRound));				

			return smoothstep(0.0, p_vignetteSmooth, amount);
		}

		float crt_line(float i, float lines, float speed)
		{
			return sin(i * lines * pi + speed * p_time);
		}

		float2 screen_bend(float2 uv)
		{
			uv -= 0.5;
			uv *= 2.0;
			uv.x *= 1.0 + pow(uv.y / p_screenBend, 2.0) - p_screenOverscan;
			uv.y *= 1.0 + pow(uv.x / p_screenBend, 2.0) - p_screenOverscan;
			uv /= 2.0;
			return uv + 0.5;
		}

		float3 blur (float3 col, float2 uv)
        {                
			col += tex(uv + float2(p_blur, p_blur));
            col += tex(uv + float2(p_blur, -p_blur));
            col += tex(uv + float2(-p_blur, p_blur));
            col += tex(uv + float2(-p_blur, -p_blur));
            col /= 5.0;
                
            return col;
        }

		float weight(float3 color)
		{
			return max(max(color.r * p_bleedr, color.g * p_bleedg), color.b * p_bleedb);
		}

		float3 bleed(float3 col, float2 uv)
        {
			float side2 = ceil((_BlitTexture_TexelSize.z / p_resX) / 2.0);
			float side1 = side2 - 1.0;

			float s = 2.0;
			col *= s;

			float3 bld = tex(uv + float2(_BlitTexture_TexelSize.x * side1, 0.0));
			float w = weight(bld);
			col += bld * w;
			s += min(w, 1.0);

			bld = tex(uv + float2(_BlitTexture_TexelSize.x * side2, 0.0));
			w = weight(bld);
			col += bld * w;
			s += min(w, 1.0);

			bld = tex(uv - float2(_BlitTexture_TexelSize.x * side1, 0.0));
			w = weight(bld);
			col += bld * w;
			s += min(w, 1.0);


			bld = tex(uv - float2(_BlitTexture_TexelSize.x * side2, 0.0));
			w = weight(bld);
			col += bld * w;
			s += min(w, 1.0);

			return col / s;
        }

		float3 aperture(float3 col, float2 uv)
		{
			float odd = fmod(floor(uv.x * _BlitTexture_TexelSize.z), 2.0);

			col *= 1.0 - odd * p_apertureStrength;
								
			return col;
		}

		float3 scanline(float3 col, float2 uv)
		{
			return col * lerp(1.0, max(0.4, sin(pixel_frac_y(uv.y - _BlitTexture_TexelSize.y / 2.0) * pi)), p_scanlinesStrength);
		}

		float3 pixelSmidge(float3 col, float2 uv)
		{
			float3 smgL = tex(uv + float2(-m_pixSize.x / 2.0, -m_pixSize.y));
			float3 smgR = tex(uv + float2(m_pixSize.x / 2.0, -m_pixSize.y));
				
			float sL = step(0.4, max(smgL.r, max(smgL.g, smgL.b)));
			float sR = step(0.4, max(smgR.r, max(smgR.g, smgR.b)));
			smgL *= sL;
			smgR *= sR;
				
			float s = abs(sL - sR) * p_smidge;
			s *= 1.0 - step(0.1, col.r + col.g + col.b);
			s *= 1.0 - pixel_frac_y(uv.y);

			return col + (smgL + smgR) * s;
		}

		float4 glitch_values(float2 uv)
		{
			float fixedRnd = frac(sin(floor(p_time * 20.0)) * 24537.58917);
			float rndPos = lerp(0.0, (-0.1 * p_glitchPositionFlicker + 0.2 * p_glitchPositionFlicker * fixedRnd) * step(0.6, fixedRnd), p_glitchPositionFlicker);
			float uvy = frac(uv.y + p_time * p_glitchSpeed + rndPos);
			float band = floor(uvy / p_glitchBandSize);
			float active = saturate(1.0 - abs(band - p_glitchActiveBand)) * step(1.0 - p_glitchFrequency, fixedRnd);
			float subBand = floor(uvy / p_glitchLineSize);

			return float4(uvy, active, band, subBand);
		}

		float2 crt_glitch(float2 uv)
		{
			float4 vals = glitch_values(uv);
			float sinLine = sin((vals.x / p_glitchBandSize) * pi);
			float rnd = frac(sin(floor(p_time * p_glitchNoiseSpeed) * vals.w) * 8536.57201);
			rnd *= 1.0 - 2.0 * step(0.5, rnd);
			rnd *= step(0.05, rnd);

			float finalShift = lerp(sinLine, rnd, p_glitchDistortion);
			uv.x += vals.y * p_glitchStrength * finalShift;

			return uv;            
		}

		float3 crt_glitch_noise(float3 col, float2 uv)
		{
			float4 vals = glitch_values(uv);

			float rnd = frac(sin(floor(p_time * p_glitchNoiseSpeed) * vals.w) * 7853.16284);
			rnd = frac(sin(vals.w + uv.x) * p_glitchNoise * 30.0 * rnd);
			return lerp(col, float3(p_glitchStrength, p_glitchStrength, p_glitchStrength), vals.y * step(p_glitchNoise, rnd) * rnd);
		}
		
		float4 ApplyCRT (Varyings input) : SV_Target
		{
			m_pixSize = pixel_size();

			float2 buv = screen_bend(input.texcoord);
			buv = crt_glitch(buv);

			float3 col = tex(buv);
			col = blur(col, buv);

			col.r += tex(buv + p_redOffset).r;
			col.g += tex(buv + p_greenOffset).g;
			col.b += tex(buv + p_blueOffset).b;
			col.rgb /= 2.0;


			col = bleed(col, buv);
			col = crt_glitch_noise(col, buv);
			col = scanline(col, buv);
			col = aperture(col, buv);
			col = pixelSmidge(col, buv);

			col = pow(col, (1.0 / p_gamma));
			col = p_contrast * (col - 0.5) + 0.5 + p_brightness;

			col.r *= p_red;
			col.g *= p_green;
			col.b *= p_blue;
				
			col = lerp(col, float(noise(buv * p_noiseSize)), p_noiseAlpha);
			col = lerp(col, crt_line(buv.y, p_shadowlines, p_shadowlinesSpeed), p_shadowlinesAlpha * saturate(p_shadowlines));
			col = lerp(col, crt_line(buv.x, p_shadowlines, p_shadowlinesSpeed), p_shadowlinesAlpha * saturate(-p_shadowlines));
			

			return float4(col * vignette(input.texcoord), 1.0);
		}
    
    ENDHLSL
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off
        Pass
        {
            Name "CRTFilterPass"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment ApplyCRT
            
            ENDHLSL
        }
    }
}