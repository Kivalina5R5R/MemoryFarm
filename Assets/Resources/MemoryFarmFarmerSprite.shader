Shader "Deepspace/MemoryFarmFarmerSprite"
{
    Properties
    {
        [NoScaleOffset] _MainTex("Art",2D)="white"{}
        [NoScaleOffset] _ShirtMask("Shirt mask",2D)="black"{}
        _ShirtColor("Shirt colour",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off
            // Separate alpha so the render texture keeps the sprite's real alpha for the silhouette pass.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShirtMask); SAMPLER(sampler_ShirtMask);
            CBUFFER_START(UnityPerMaterial)
            half4 _ShirtColor;
            CBUFFER_END
            struct A { float4 position:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A v) { V o; o.position=TransformObjectToHClip(v.position.xyz); o.uv=v.uv; return o; }
            half4 frag(V i):SV_Target
            {
                half4 art=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                half mask=SAMPLE_TEXTURE2D(_ShirtMask,sampler_ShirtMask,i.uv).r;
                // The shirt is drawn white, so multiplying gives the exact colour and keeps the ink lines black.
                art.rgb*=lerp(half3(1,1,1),_ShirtColor.rgb,mask);
                return art;
            }
            ENDHLSL
        }
    }
}
