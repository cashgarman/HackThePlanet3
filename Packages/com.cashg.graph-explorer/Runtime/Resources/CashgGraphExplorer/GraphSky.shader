Shader "Cashg/GraphExplorer/GraphSky"
{
    Properties
    {
        [HideInInspector] _GraphAtmosphereTime ("Animation time", Float) = 0
        _Zenith ("Zenith", Color) = (0.010, 0.018, 0.034, 1)
        _Haze ("Haze", Color) = (0.055, 0.090, 0.145, 1)
        _Cloud ("Cloud", Color) = (0.24, 0.36, 0.52, 1)
        _Warm ("Warm", Color) = (0.34, 0.18, 0.08, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background"
        }
        Cull Off
        ZWrite On
        ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GraphAtmosphereNoise.hlsl"

            float4 _Zenith, _Haze, _Cloud, _Warm;
            float _GraphAtmosphereTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.positionWS - _WorldSpaceCameraPos);
                float time = _GraphAtmosphereTime;
                float3 samplePos = dir * 2.45 + _WorldSpaceCameraPos * 0.00115;
                samplePos += float3(time * 0.02, time * 0.0065, -time * 0.014);
                float3 warp = float3(
                    GraphNoise(samplePos + float3(0.0, 2.1, 4.3)),
                    GraphNoise(samplePos + float3(5.2, 1.3, 0.7)),
                    GraphNoise(samplePos + float3(1.7, 8.2, 3.5)));
                float low = GraphFbm(samplePos + warp * 0.85);
                float high = GraphFbm(samplePos * 2.15 + float3(-time * 0.013, 3.4, time * 0.009));
                float clouds = smoothstep(0.42, 0.72, low);
                clouds = saturate(clouds * 0.82 + smoothstep(0.55, 0.86, high) * 0.30);

                float horizon = pow(saturate(1.0 - abs(dir.y)), 1.25);
                float abyss = saturate(-dir.y * 0.85 + 0.15);
                clouds = saturate(clouds * lerp(0.42, 1.12, horizon) + abyss * 0.28);

                half3 color = lerp(_Zenith.rgb, _Haze.rgb, saturate(horizon * 0.9 + abyss * 0.7));
                color = lerp(color, _Cloud.rgb, clouds);
                color += _Warm.rgb * clouds * abyss * 0.30;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}