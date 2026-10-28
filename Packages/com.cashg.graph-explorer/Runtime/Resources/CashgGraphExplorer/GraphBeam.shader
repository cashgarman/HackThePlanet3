Shader "Cashg/GraphExplorer/GraphBeam"
{
    Properties
    {
        [HideInInspector] _GraphAtmosphereTime ("Animation time", Float) = 0
        _BeamColor ("Beam", Color) = (0.22, 0.78, 0.74, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.12
        _Seed ("Seed", Float) = 0
        _Speed ("Speed", Float) = 1
        _Interval ("Interval", Float) = 7
        _Duration ("Duration", Float) = 0.18
        _Phase ("Phase", Float) = 0
        _DirectionAngle ("Direction angle", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BeamColor;
            float _Opacity, _Seed, _Speed, _Interval, _Duration, _Phase, _DirectionAngle, _GraphAtmosphereTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float width = distance(TransformObjectToWorld(float3(1, 0, 0)), centerWS);
                float height = distance(TransformObjectToWorld(float3(0, 1, 0)), centerWS);
                float3 positionVS = TransformWorldToView(centerWS);
                positionVS.xy += float2(input.positionOS.x * width, input.positionOS.y * height);
                output.positionCS = TransformWViewToHClip(positionVS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float sine = sin(_DirectionAngle), cosine = cos(_DirectionAngle);
                p = float2(p.x * cosine - p.y * sine, p.x * sine + p.y * cosine);
                float x = p.x * .5 + .5;
                float cycle = frac((_GraphAtmosphereTime * _Speed + _Phase) / max(_Interval, 0.1));
                float active = 1.0 - smoothstep(_Duration, _Duration + 0.035, cycle);
                float head = saturate(cycle / max(_Duration, 0.01));
                float tail = saturate((head - x) / 0.72) * smoothstep(0.0, 0.08, head - x);
                float headGlow = exp(-abs(x - head) * 135.0);
                float trail = saturate(tail * .62 + headGlow);
                float core = exp(-abs(p.y) * 13.0);
                float haze = exp(-abs(p.y) * 4.0) * .22;
                float segments = step(.32, frac(x * 19.0 + _Seed * 1.7));
                float alpha = active * (core + haze) * trail * _Opacity;
                half3 color = _BeamColor.rgb * (.72 + segments * .28);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
