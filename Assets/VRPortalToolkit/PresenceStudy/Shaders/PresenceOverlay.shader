Shader "VRPortalToolkit/Presence/Overlay"
{
    Properties
    {
        _MainTex ("Secondary world", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZTest Always
        ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"

        UNITY_DECLARE_TEX2DARRAY(_MainTex);
        UNITY_DECLARE_TEX2DARRAY(_PortalDepthNormalsTexture);
        float4 _MainTex_ST;
        float4 _MainTex_ST_2;
        float4 _MainTex_TexelSize;
        float4x4 _Aperture;
        float _Technique;
        float _Reveal;

        struct Attributes
        {
            float4 vertex : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 position : SV_POSITION;
            float4 screen : TEXCOORD0;
            float4 edge01 : TEXCOORD1;
            float4 edge23 : TEXCOORD2;
            float3 center : TEXCOORD3;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float4 ProjectPortal(float2 corner)
        {
            return ComputeScreenPos(UnityWorldToClipPos(mul(_Aperture, float4(corner, 0, 1)).xyz));
        }

        Varyings vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_OUTPUT(Varyings, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.position = UnityObjectToClipPos(input.vertex);
            output.screen = ComputeScreenPos(output.position);

            // Include the narrow frame in the uncomposited aperture.
            const float s = 0.527;
            float4 a = ProjectPortal(float2(-s, -s));
            float4 b = ProjectPortal(float2(s, -s));
            float4 c = ProjectPortal(float2(s, s));
            float4 d = ProjectPortal(float2(-s, s));
            float4 center = ProjectPortal(float2(0, 0));
            output.edge01 = float4(a.xy / max(a.w, 0.001), b.xy / max(b.w, 0.001));
            output.edge23 = float4(c.xy / max(c.w, 0.001), d.xy / max(d.w, 0.001));
            output.center = float3(center.xy / max(center.w, 0.001), min(min(a.w, b.w), min(c.w, d.w)));
            return output;
        }

        float Wedge(float2 p, float2 a, float2 b)
        {
            float2 edge = b - a, delta = p - a;
            return edge.x * delta.y - edge.y * delta.x;
        }

        float2 RevealUV(Varyings input)
        {
            float2 uv = input.screen.xy / input.screen.w;
            float2 aspect = float2(_ScreenParams.x / _ScreenParams.y, 1);
            float2 center = input.center.z > 0 ? saturate(input.center.xy) : float2(0.5, 0.5);
            float radius = length(max(center, 1 - center) * aspect) * _Reveal;
            clip(radius - length((uv - center) * aspect));

            float e0 = Wedge(uv, input.edge01.xy, input.edge01.zw);
            float e1 = Wedge(uv, input.edge01.zw, input.edge23.xy);
            float e2 = Wedge(uv, input.edge23.xy, input.edge23.zw);
            float e3 = Wedge(uv, input.edge23.zw, input.edge01.xy);
            bool inside = min(min(e0, e1), min(e2, e3)) >= 0 ||
                          max(max(e0, e1), max(e2, e3)) <= 0;
            if (input.center.z > 0 && inside)
                discard;

            return uv;
        }

        float3 SecondaryUV(float2 uv)
        {
            float4 st = lerp(_MainTex_ST, _MainTex_ST_2, unity_StereoEyeIndex);
            return float3(uv * st.xy + st.zw, unity_StereoEyeIndex);
        }

        float EdgeStrength(float3 uv, float3 firstColor)
        {
            float depth;
            float3 normal;
            DecodeDepthNormal(UNITY_SAMPLE_TEX2DARRAY(_PortalDepthNormalsTexture, uv), depth, normal);
            float edge = 0;
            float2 px = abs(_MainTex_TexelSize.xy) * 1.35;
            const float2 offsets[4] = {float2(1,0), float2(0,1), float2(-1,0), float2(0,-1)};
            [unroll] for (int i = 0; i < 4; ++i)
            {
                float3 sampleUV = float3(uv.xy + px * offsets[i], uv.z);
                float nextDepth;
                float3 nextNormal;
                DecodeDepthNormal(UNITY_SAMPLE_TEX2DARRAY(_PortalDepthNormalsTexture, sampleUV), nextDepth, nextNormal);
                float3 nextColor = UNITY_SAMPLE_TEX2DARRAY(_MainTex, sampleUV).rgb;
                edge = max(edge, step(0.009, abs(depth - nextDepth)));
                edge = max(edge, step(0.30, length(normal - nextNormal)));
                edge = max(edge, step(0.13, length(nextColor - firstColor)));
            }
            return edge;
        }

        fixed4 composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 uv = SecondaryUV(RevealUV(input));
            float3 color = UNITY_SAMPLE_TEX2DARRAY(_MainTex, uv).rgb;
            if (_Technique < 1.5)
                return fixed4(1, 1, 1, 0.5 * EdgeStrength(uv, color));
            return fixed4(color, _Technique < 2.5 ? 1.0 / 3.0 : 1.0);
        }

        fixed4 markStencil(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            RevealUV(input);
            float2 pixel = floor(input.position.xy);
            clip(0.5 - fmod(pixel.x + 2 * pixel.y, 3));
            return 0;
        }

        fixed4 fromStencil(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return fixed4(UNITY_SAMPLE_TEX2DARRAY(_MainTex,
                SecondaryUV(input.screen.xy / input.screen.w)).rgb, 1);
        }
        ENDCG

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment composite
            #pragma multi_compile_instancing
            ENDCG
        }
        Pass
        {
            ColorMask 0
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp Always Pass Replace }
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment markStencil
            #pragma multi_compile_instancing
            ENDCG
        }
        Pass
        {
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp Equal Pass Zero }
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment fromStencil
            #pragma multi_compile_instancing
            ENDCG
        }
    }
}
