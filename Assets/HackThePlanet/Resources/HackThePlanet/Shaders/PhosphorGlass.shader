Shader "HackThePlanet/PhosphorGlass"
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
            float4 _SignalState;
            float _SignalTime;

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 pixel = uv * _Resolution.xy;
                float animation = _SignalState.w;
                float danger = smoothstep(.55, .95, _SignalState.x);
                float2 edge = abs(uv - .5) * 2;
                float vignette = pow(max(edge.x, edge.y), 5) * .105;
                float scanline = step(.66, frac(pixel.y / 3)) * .022;
                float grain = hash(floor(pixel) + floor(_SignalTime * 24)) * .018;
                // Slow, low-contrast refresh drift. No flicker, screen shake, or text displacement.
                float refresh = pow(saturate(1 - abs(uv.y - frac(_SignalTime * .035))), 70) * .022 * animation;
                float interference = pow(saturate(sin(uv.y * 740 + _SignalTime * .9)), 24);
                interference *= danger * .025 * animation;
                float alpha = (scanline + grain + vignette + refresh + interference) * lerp(.40, 1, animation);
                return fixed4(lerp(float3(.025, .06, .065), float3(.23, .045, .025), danger * .55), alpha);
            }
            ENDCG
        }
    }
}
