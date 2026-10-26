Shader "Cashg/GraphExplorer/GraphFog"
{
    Properties
    {
        [HideInInspector] _GraphAtmosphereTime ("Animation time", Float) = 0
        _FogColor ("Fog", Color) = (0.22, 0.34, 0.50, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.35
        _Seed ("Seed", Float) = 0
        _Frequency ("Frequency", Float) = 0.01
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
            #include "GraphAtmosphereNoise.hlsl"

            float4 _FogColor;
            float _Opacity, _Seed, _Frequency, _GraphAtmosphereTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
                float scale : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float scale = distance(TransformObjectToWorld(float3(1, 0, 0)), centerWS);
                float3 positionVS = TransformWorldToView(centerWS);
                positionVS.xy += input.positionOS.xy * scale;
                output.positionCS = TransformWViewToHClip(positionVS);
                output.positionWS = TransformViewToWorld(positionVS);
                output.uv = input.uv;
                output.centerWS = centerWS;
                output.scale = scale;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 plane = input.uv * 2.0 - 1.0;
                float disc = saturate(1.0 - dot(plane, plane));
                disc = smoothstep(0.0, 0.28, disc) * disc;

                float3 samplePos = input.positionWS * float3(_Frequency, _Frequency * 0.62, _Frequency);
                samplePos += float3(_Seed, _Seed * 0.37, _Seed * 1.71);
                samplePos += float3(0.031, 0.011, -0.018) * _GraphAtmosphereTime;
                float body = GraphFbm(samplePos);
                float detail = GraphFbm(samplePos * 2.05 + float3(4.2, 1.1, 7.4));
                float cloud = smoothstep(0.40, 0.74, body);
                cloud *= lerp(0.42, 1.0, smoothstep(0.36, 0.82, detail));

                float approach = saturate(distance(input.centerWS, _WorldSpaceCameraPos) / max(input.scale * 0.5, 1.0));
                float alpha = disc * max(cloud, 0.08) * _Opacity * approach;
                return half4(_FogColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}