Shader "Cashg/GraphExplorer/GraphNodeMask"
{
    Properties
    {
        _BaseColor ("Instance color", Color) = (1,1,1,0)
        _NodeEffects ("Selection and pulse", Vector) = (0,1,0,0)
        _BloomIntensity ("Node bloom seed", Float) = .16
        _SelectionEnabled ("Selected glow", Float) = 1
        [HDR] _SelectionColor ("Selected glow color", Color) = (.4,.85,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
        }
        Pass
        {
            Name "Node normals depth and glow seeds"
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID };

            struct Targets
            {
                half4 normalsDepth : SV_Target0;
                half4 glow : SV_Target1;
            };

            float _BloomIntensity, _SelectionEnabled;
            float4 _SelectionColor;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _NodeEffects)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            Targets frag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float4 effects = UNITY_ACCESS_INSTANCED_PROP(Props, _NodeEffects);
                float selection = effects.x * effects.y * _SelectionEnabled;
                float fog = 1 - smoothstep(55, 260, distance(input.positionWS, _WorldSpaceCameraPos)) * .58;
                Targets output;
                output.normalsDepth = half4(normalize(input.normalWS) * .5 + .5,
                                            -TransformWorldToView(input.positionWS).z);
                output.glow = half4((tint.rgb * _BloomIntensity + _SelectionColor.rgb * selection * .2) * fog,
                                    selection * fog);
                return output;
            }
            ENDHLSL
        }
    }
}