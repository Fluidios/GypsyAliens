Shader "GypsyAliens/VisionCone"
{
    Properties
    {
        _ColorNear ("Near Color", Color) = (0.25, 0.95, 0.35, 0.45)
        _ColorFar ("Far Color", Color) = (0.15, 0.75, 0.25, 0.32)
        _AlertColorNear ("Alert Near Color", Color) = (1.0, 0.92, 0.15, 0.55)
        _AlertColorFar ("Alert Far Color", Color) = (0.95, 0.75, 0.05, 0.4)
        _NearRadius ("Near Radius", Float) = 2.5
        _FarRadius ("Far Radius", Float) = 8
        _StripeWidth ("Stripe Width", Float) = 0.35
        _StripeGap ("Stripe Gap", Float) = 0.25
        _AlertFill ("Alert Fill", Float) = 0
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
            Name "VisionCone"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // Depth-tested against walls so the cone never draws through occluders.
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorNear;
                float4 _ColorFar;
                float4 _AlertColorNear;
                float4 _AlertColorFar;
                float _NearRadius;
                float _FarRadius;
                float _StripeWidth;
                float _StripeGap;
                float _AlertFill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0; // x = normalized angle, y = distance from origin
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float distanceFromOrigin : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.distanceFromOrigin = input.uv.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float dist = input.distanceFromOrigin;
                float nearR = max(_NearRadius, 0.01);
                float farR = max(_FarRadius, nearR + 0.01);
                float fill = saturate(_AlertFill);
                float fillRadius = farR * fill;

                half4 nearCol = (half4)_ColorNear;
                half4 farCol = (half4)_ColorFar;
                if (fill > 0.001 && dist <= fillRadius)
                {
                    nearCol = (half4)_AlertColorNear;
                    farCol = (half4)_AlertColorFar;
                }

                if (dist <= nearR)
                {
                    return nearCol;
                }

                // Concentric stripes in the outer zone.
                float band = dist - nearR;
                float period = max(_StripeWidth + _StripeGap, 0.01);
                float inStripe = step(frac(band / period) * period, _StripeWidth);
                farCol.a *= inStripe;
                return farCol;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
