Shader "GypsyAliens/XRayFade"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.72, 0, 0.35)
        _Grid ("Grid", 2D) = "white" {}
        _GridScale ("Grid Scale", Float) = 1
        _Falloff ("Falloff", Float) = 50
        _OverlayAmount ("Overlay Amount", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayFade"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Grid);
            SAMPLER(sampler_Grid);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Grid_ST;
                float _GridScale;
                float _Falloff;
                float _OverlayAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 worldNormal = normalize(input.normalWS);
                float3 blend = pow(abs(worldNormal), _Falloff);
                blend /= max(blend.x + blend.y + blend.z, 1e-5);

                float3 worldPos = input.positionWS * max(_GridScale, 0.0001);
                half4 xSample = SAMPLE_TEXTURE2D(_Grid, sampler_Grid, worldPos.zy);
                half4 ySample = SAMPLE_TEXTURE2D(_Grid, sampler_Grid, worldPos.xz);
                half4 zSample = SAMPLE_TEXTURE2D(_Grid, sampler_Grid, worldPos.xy);
                half4 grid = xSample * blend.x + ySample * blend.y + zSample * blend.z;

                half3 albedo = _BaseColor.rgb * lerp(half3(1, 1, 1), grid.rgb, _OverlayAmount);

                // Cheap Lambert so the mesh does not look self-emissive.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(worldNormal, mainLight.direction));
                half3 lighting = mainLight.color * (ndotl * 0.65h + 0.35h);
                half3 color = albedo * lighting;

                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
