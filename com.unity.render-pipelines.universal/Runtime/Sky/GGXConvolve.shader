Shader "Hidden/Universal Render Pipeline/GGXConvolve"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull   Off
            ZTest  Always
            ZWrite Off
            Blend  Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

            SAMPLER(s_trilinear_clamp_sampler);

            TEXTURECUBE(_MainTex);

            TEXTURE2D(_GgxIblSamples);

            float _Level;
            float _InvOmegaP;
            float4x4 _PixelCoordToViewDirWS; // Actually just 3x3, but Unity can only set 4x4

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Points towards the camera
                float3 viewDirWS = normalize(mul(float3(input.positionCS.xy, 1.0), (float3x3)_PixelCoordToViewDirWS));
                // Reverse it to point into the scene
                float3 N = -viewDirWS;
                // Remove view-dependency from GGX, effectively making the BSDF isotropic.
                float3 V = N;

                float perceptualRoughness = MipmapLevelToPerceptualRoughness(_Level);
                float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
                uint  sampleCount = GetIBLRuntimeFilterSampleCount(_Level);
                sampleCount = min(sampleCount, 34); // Don't have more than 34 samples in URP yet

                float4 val = IntegrateLD(TEXTURECUBE_ARGS(_MainTex, s_trilinear_clamp_sampler),
                                         _GgxIblSamples,
                                         V, N,
                                         roughness,
                                         _Level - 1,
                                         _InvOmegaP,
                                         sampleCount, // Must be a Fibonacci number
                                         true,
                                         true);

                return val;
            }
            ENDHLSL
        }
    }
}
