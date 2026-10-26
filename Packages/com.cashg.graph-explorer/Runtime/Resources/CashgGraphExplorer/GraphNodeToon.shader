Shader "Cashg/GraphExplorer/GraphNodeToon"
{
    Properties
    {
        _BaseColor ("Instance color", Color) = (1,1,1,1)
        _Cull ("Cull", Float) = 2
        [Toggle] _CelShading ("Cel shading", Float) = 1
        _LightBands ("Lighting bands", Range(2,5)) = 3
        _ShadowBrightness ("Shadow brightness", Range(0,1)) = .38
        _BandSoftness ("Band softness", Range(0,.15)) = .015
        _LightDirection ("Direction toward light", Vector) = (-.4,.8,-.5,0)
        _SurfaceEmission ("Surface emission", Range(0,1)) = .08
        [HDR] _SelectionColor ("Selected rim color", Color) = (.4,.85,1,1)
        _SelectionRimIntensity ("Selected rim intensity", Range(0,4)) = .65
        [HideInInspector] _NodeEffects ("Selection and pulse", Vector) = (0,1,0,0)
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
            Name "Cel shading"
            Tags
            {
                "LightMode"="UniversalForwardOnly"
            }
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

            CBUFFER_START(UnityPerMaterial)
                float _CelShading, _LightBands, _ShadowBrightness, _BandSoftness;
                float4 _LightDirection, _SelectionColor;
                float _SurfaceEmission, _SelectionRimIntensity;
            CBUFFER_END

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
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float4 effects = UNITY_ACCESS_INSTANCED_PROP(Props, _NodeEffects);
                float3 normal = normalize(input.normalWS + .0001);
                float diffuse = saturate(dot(normal, normalize(_LightDirection.xyz + .0001)));
                float bands = clamp(round(_LightBands), 2, 5);
                float softness = max(_BandSoftness, fwidth(diffuse) * .5 + .0001);
                float quantized = 0;
                [unroll] for (int band = 1; band < 5; band++)
                {
                    float threshold = band / bands;
                    quantized += step(band, bands - 1) *
                        smoothstep(threshold - softness, threshold + softness, diffuse);
                }
                quantized /= bands - 1;
                float light = lerp(_ShadowBrightness, 1, lerp(diffuse, quantized, saturate(_CelShading)));
                float3 rgb = tint.rgb * input.color.rgb * (light + _SurfaceEmission);
                float3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                float rim = pow(1 - saturate(dot(normal, view)), 2);
                rgb += _SelectionColor.rgb * effects.x * effects.y * _SelectionRimIntensity * rim;
                // Running-task glow remains independent of selection.
                float pulse = saturate(tint.a);
                rgb += float3(.42, .9, 1) * (rim * (.7 + 1.15 * pulse) + .22 * pulse) * step(.001, pulse);
                float fog = smoothstep(55, 260, distance(input.positionWS, _WorldSpaceCameraPos));
                rgb = lerp(rgb, float3(.055, .095, .15), fog * .58);
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
}