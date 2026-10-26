Shader "Cashg/GraphExplorer/GraphEdge"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Filter ("Filter", Float) = 0.8
    }
    SubShader
    {
        // Drawn with the opaques, after nodes, so the coverage blend lands on the
        // sky and nodes before the fog banks. Depth stays with the scene: a link
        // behind a node is drawn again only while the camera is against that node.
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+1"
        }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        ColorMask RGB

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 _BaseColor;
        float _Filter;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
            float4 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            noperspective float4 uv : TEXCOORD1;
            float4 color : COLOR;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = input.uv;
            output.color = input.color;
            return output;
        }

        // uv.x is signed pixels from the centerline, uv.y the core half-width in
        // pixels, uv.z pixels from the real start (negative in the start cap),
        // uv.w the segment length in pixels. The mesh is padded past the filter
        // so this coverage reaches zero inside the quad instead of at a hard edge.
        float Coverage(float4 uv)
        {
            float filter = max(_Filter, 0.02);
            float coreHalf = max(uv.y, 0.0);
            float side = 1.0 - smoothstep(coreHalf - filter, coreHalf + filter, abs(uv.x));
            float outside = max(-uv.z, uv.z - max(uv.w, 0.0));
            float ends = 1.0 - smoothstep(0.0, filter, max(outside, 0.0));
            return side * ends;
        }

        half4 Shade(Varyings input, float reveal)
        {
            float coverage = saturate(Coverage(input.uv) * reveal);
            half3 rgb = _BaseColor.rgb * input.color.rgb;
            float fog = smoothstep(55.0, 260.0, distance(input.positionWS, _WorldSpaceCameraPos));
            rgb = lerp(rgb, half3(0.055, 0.095, 0.15), fog * 0.58);
            return half4(rgb * coverage, coverage);
        }
        ENDHLSL

        Pass
        {
            Name "Visible"
            Tags
            {
                "LightMode"="SRPDefaultUnlit"
            }
            ZTest LEqual
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return Shade(input, 1.0); }
            ENDHLSL
        }
        Pass
        {
            Name "Occluded"
            Tags
            {
                "LightMode"="SRPDefaultUnlit"
            }
            ZTest Greater
            Offset 1, 1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOccluded
            half4 FragOccluded(Varyings input) : SV_Target
            {
                half4 color = Shade(input, 1.0);
                clip(color.a - 0.004);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}