Shader "Cashg/GraphExplorer/GraphNodeProgress"
{
    Properties
    {
        _ProgressColor ("Progress", Color) = (.25,.95,.48,1)
        _TrackColor ("Track", Color) = (.16,.23,.20,1)
        [HideInInspector] _ProgressData ("Fraction, completion, width, badge size", Vector) = (0,0,.03,.16)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10"
        }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags
            {
                "LightMode"="SRPDefaultUnlit"
            }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ProgressColor, _TrackColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ProgressData)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS:POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID };

            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float2 coordinates:TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.coordinates = input.positionOS.xy;
                return output;
            }

            float Segment(float2 p, float2 a, float2 b)
            {
                float2 direction = b - a;
                return length(
                    p - a - direction * saturate(dot(p - a, direction) / max(dot(direction, direction), 1e-6)));
            }

            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 data = UNITY_ACCESS_INSTANCED_PROP(Props, _ProgressData);
                float2 p = input.coordinates;
                float width = max(data.z, .002);
                float aa = max(fwidth(length(p)), .0005);
                float ring = 1 - smoothstep(width - aa, width + aa, abs(length(p) - 1));
                float angle = frac(atan2(p.x, p.y) / 6.28318530718 + 1);
                float filled = data.x >= 1
                                   ? 1
                                   : data.x <= 0
                                   ? 0
                                   : saturate((data.x - angle) / max(fwidth(angle), .0001) + .5);
                half4 color = lerp(_TrackColor, _ProgressColor, filled);
                color.a *= ring;
                if (data.y > .5)
                {
                    float size = max(data.w, .02);
                    float2 badge = p - float2(.70710678, .70710678);
                    float square = max(abs(badge.x), abs(badge.y));
                    float background = 1 - smoothstep(size + width, size + width + aa, square);
                    float outline = 1 - smoothstep(width - aa, width + aa, abs(square - size));
                    float check = 1 - smoothstep(width - aa, width + aa, min(
                                                     Segment(badge, float2(-.65, 0) * size,
                                                             float2(-.12, -.45) * size),
                                                     Segment(badge, float2(-.12, -.45) * size,
                                                             float2(.65, .6) * size)));
                    float ink = max(outline, check);
                    color.rgb = lerp(color.rgb, lerp(half3(.015, .035, .025), _ProgressColor.rgb, ink), background);
                    color.a = max(color.a, background);
                }
                clip(color.a - .003);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}