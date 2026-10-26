Shader "Cashg/GraphExplorer/GraphGuideArrow"
{
    Properties { _ArrowColor ("Arrow", Color) = (.6,1,.85,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _ArrowColor;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.p = input.positionOS.xy;
                return output;
            }
            // Signed distance to a broad, downward arrow with a short square stem.
            float Arrow(float2 p)
            {
                const float2 points[7] = {
                    float2(-.105,.32), float2(.105,.32), float2(.105,.015),
                    float2(.29,.015), float2(0,-.31), float2(-.29,.015), float2(-.105,.015)
                };
                float distanceSquared = 1;
                float sign = 1;
                [unroll] for (int i = 0; i < 7; i++)
                {
                    float2 a = points[i], b = points[(i + 1) % 7];
                    float2 edge = b - a, offset = p - a;
                    float2 nearest = offset - edge * saturate(dot(offset,edge) / dot(edge,edge));
                    distanceSquared = min(distanceSquared, dot(nearest,nearest));
                    if ((a.y > p.y) != (b.y > p.y))
                        if (p.x < a.x + (p.y - a.y) * edge.x / edge.y) sign = -sign;
                }
                return sqrt(distanceSquared) * sign;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float d = Arrow(input.p);
                float aa = max(fwidth(d), .001);
                float fill = 1 - smoothstep(-aa, aa, d);
                float outline = 1 - smoothstep(.043 - aa, .043 + aa, d);
                float glow = exp(-max(d, 0) * 28) * .22;
                float3 tint = lerp(_ArrowColor.rgb, float3(1,1,1), .24 + .16 * saturate(input.p.y + .5));
                float3 rgb = lerp(_ArrowColor.rgb * .6, float3(.018,.035,.04), outline);
                rgb = lerp(rgb, tint, fill);
                return half4(rgb, max(outline, glow) * _ArrowColor.a);
            }
            ENDHLSL
        }
    }
}
