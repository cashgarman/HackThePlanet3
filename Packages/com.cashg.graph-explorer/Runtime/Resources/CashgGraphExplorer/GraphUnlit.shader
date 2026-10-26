Shader "Cashg/GraphExplorer/GraphUnlit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1) _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"
        }
        Cull [_Cull]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                half light = .55 + .45 * saturate(dot(normalize(input.normalWS + .0001),
                                                      normalize(float3(-.4, .8, -.5))));
                half3 rgb = tint.rgb * input.color.rgb * light;
                // Alpha is the running-task pulse. Zero leaves every other node unchanged.
                float pulse = saturate(tint.a);
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float facing = saturate(dot(normalize(input.normalWS + .0001), viewDir));
                float rim = pow(1.0 - facing, 1.65);
                half3 glow = half3(.42, .9, 1);
                rgb += (glow * rim * (0.7 + 1.15 * pulse) + glow * 0.22 * pulse) * step(0.001, pulse);
                float fog = smoothstep(55.0, 260.0, distance(input.positionWS, _WorldSpaceCameraPos));
                rgb = lerp(rgb, half3(0.055, 0.095, 0.15), fog * 0.58);
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
}