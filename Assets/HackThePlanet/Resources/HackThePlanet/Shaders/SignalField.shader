Shader "HackThePlanet/SignalField"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Resolution;
            float4 _SignalState; // trace, tool activity, research, animation enabled
            float _SignalTime;

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float inkLine(float distance, float width, float aa) { return 1 - smoothstep(width, width + aa, abs(distance)); }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 pixels = uv * _Resolution.xy;
                float2 p = (uv - .5) * float2(_Resolution.x / _Resolution.y, 1);
                float time = _SignalTime;
                float danger = smoothstep(.55, .95, _SignalState.x);
                float3 mint = lerp(float3(.29, .83, .67), float3(1, .32, .25), danger);
                float3 blue = float3(.22, .55, .67);
                float radius = length(p * float2(.83, 1));
                float aa = 1.5 / _Resolution.y;

                // Instrument graticule: faint crosses at regular physical-pixel intervals.
                float2 cell = abs(frac(pixels / 44) - .5) * 44;
                float cross = max(inkLine(cell.x, .55, .6) * (1 - step(3.5, cell.y)),
                                  inkLine(cell.y, .55, .6) * (1 - step(3.5, cell.x)));
                float2 major = abs(frac(pixels / 220) - .5) * 220;
                float grid = max(inkLine(major.x, .4, .7), inkLine(major.y, .4, .7));

                // Offset orbital arcs suggest a network being measured, rather than a star field.
                float2 center = p - float2(-.14, .01);
                float r = length(center) * lerp(1, .82, _SignalState.z);
                float angle = atan2(center.y, center.x);
                float rings = inkLine(frac(r * 5.5) - .5, .002, aa * 5.5);
                rings *= .28 + .72 * step(.22, frac(angle * 2.4 + r * 3));
                float radialTicks = pow(saturate(cos(angle * 72)), 38) * inkLine(r - .33, .009, aa);
                float sweepPhase = frac(angle / 6.2831853 - time * .021);
                float sweep = pow(1 - sweepPhase, 48) * (1 - smoothstep(.15, .65, r));
                float band = exp(-abs(p.x - sin(time * .11) * .6) * 55);

                // Sparse rectangular bit cells and an oscilloscope lane stay below node contrast.
                float2 bitCell = floor(pixels / float2(18, 7));
                float bit = step(.78, hash(bitCell + floor(time * .6))) * step(3, frac(pixels.y / 7) * 7);
                bit *= step(.89, uv.y) * step(uv.x, .29) * .035;
                float waveY = .075 + sin(uv.x * 43 + time * .65) * .009 + sin(uv.x * 97 - time * .3) * .003;
                float waveform = inkLine(uv.y - waveY, .0008, 1.2 / _Resolution.y);
                waveform *= smoothstep(0, .06, uv.x) * (1 - smoothstep(.4, .62, uv.x));

                float3 color = float3(.025, .050, .065);
                color += blue * (.026 * grid + .095 * cross + .070 * rings + .10 * radialTicks);
                color += mint * ((.035 + _SignalState.y * .035) * sweep + .018 * band + bit + waveform * .12);
                color += mint * exp(-r * 4) * .027;
                color += float3(.8, .18, .08) * danger * .024 * pow(abs(uv.x - .5) * 2, 4);
                color *= 1 - .32 * smoothstep(.25, .9, radius);
                #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
                #endif
                return fixed4(color, 1);
            }
            ENDCG
        }
    }
}
