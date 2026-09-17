Shader "Debug/OverdrawInt/Opaque"
{
    Properties
    {
        [Header(Hardware settings)]
        [Enum(UnityEngine.Rendering.CullMode)] HARDWARE_CullMode ("Cull faces", Float) = 0
        [Enum(Off, 0, On, 1)] HARDWARE_ZWrite ("Depth write", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] HARDWARE_ZTest ("Depth test", Float) = 8
    }
    SubShader
    {
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue" = "Geometry+50"
        }
        LOD 100

        Pass
        {
            Name "OverdrawInt"
            Tags { "LightMode" = "UniversalForward" }

            Cull [HARDWARE_CullMode]
            ZWrite [HARDWARE_ZWrite]
            ZTest [HARDWARE_ZTest]
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            float OverdrawFragmentWeight;

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                // Add a small exactly representable value. The compute reduction
                // converts it back to one integer fragment per covered pixel.
                return float4(OverdrawFragmentWeight, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}
