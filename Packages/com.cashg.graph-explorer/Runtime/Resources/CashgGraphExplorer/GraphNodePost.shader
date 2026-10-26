Shader "Cashg/GraphExplorer/GraphNodePost"
{
    Properties
    {
        _OutlineColor ("Outline color", Color) = (.012,.018,.028,1)
        _OutlineParams ("Width opacity normal cosine depth threshold", Vector) = (1.25,.95,.669,.025)
        [HDR] _SelectionColor ("Selection glow color", Color) = (.4,.85,1,1)
        _SelectionGlowIntensity ("Selection glow intensity", Float) = 1.7
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
        }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D_X(_GraphNormalsDepth);
        TEXTURE2D_X(_GraphSeed);
        TEXTURE2D_X(_GraphBloom);
        TEXTURE2D_X(_GraphSelectionGlow);
        float4 _GraphBlurRadius;
        float4 _OutlineColor, _OutlineParams, _SelectionColor;
        float _SelectionGlowIntensity;

        half4 Downsample(Varyings input) : SV_Target
        {
            float2 d = _BlitTexture_TexelSize.xy * .5;
            return .25 * (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord+float2(-d.x,-d.y))
                + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord+float2(d.x,-d.y))
                + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord+float2(-d.x,d.y))
                + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord+d));
        }

        half4 Blur(Varyings input) : SV_Target
        {
            float2 offset = _GraphBlurRadius.xy / _ScaledScreenParams.xy;
            float4 result = 0;
            float total = 0;
            [unroll] for (int tap = -4; tap <= 4; tap++)
            {
                float x = tap * .6;
                float weight = exp(-.5 * x * x);
                result += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord+x*offset) * weight;
                total += weight;
            }
            return result / total;
        }

        float DetectEdge(float4 center, float4 neighbor)
        {
            float a = step(.0001, center.a), b = step(.0001, neighbor.a);
            float silhouette = abs(a - b);
            float normalEdge = 1 - step(_OutlineParams.z, dot(center.xyz * 2 - 1, neighbor.xyz * 2 - 1));
            float depthEdge = step(_OutlineParams.w, abs(center.a - neighbor.a) / max(.001, min(center.a, neighbor.a)));
            return max(silhouette, a * b * max(normalEdge, depthEdge));
        }

        half4 Composite(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float4 center = SAMPLE_TEXTURE2D_X(_GraphNormalsDepth, sampler_PointClamp, uv);
            float edge = 0;
            if (_OutlineParams.x > .001)
            {
                float2 px = _OutlineParams.x / _ScaledScreenParams.xy;
                [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                        if (x != 0 || y != 0)
                            edge = max(edge, DetectEdge(
                                           center,SAMPLE_TEXTURE2D_X(_GraphNormalsDepth, sampler_PointClamp,
                                                                     uv+float2(x,y)*px)));
            }
            float3 rgb = lerp(scene.rgb, _OutlineColor.rgb, edge * _OutlineParams.y);
            float4 seed = SAMPLE_TEXTURE2D_X(_GraphSeed, sampler_LinearClamp, uv);
            float3 bloom = SAMPLE_TEXTURE2D_X(_GraphBloom, sampler_LinearClamp, uv).rgb;
            float glow = SAMPLE_TEXTURE2D_X(_GraphSelectionGlow, sampler_LinearClamp, uv).a;
            rgb += bloom * (1 - step(.0001, center.a) * .85);
            rgb += _SelectionColor.rgb * glow * _SelectionGlowIntensity * (1 - saturate(seed.a));
            return half4(rgb, scene.a);
        }
        ENDHLSL
        Pass
        {
            Name "Separable Gaussian blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Blur
            ENDHLSL
        }
        Pass
        {
            Name "Normal-depth edges and bloom"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
        Pass
        {
            Name "Downsample prefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Downsample
            ENDHLSL
        }
    }
}