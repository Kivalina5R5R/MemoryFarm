Shader "Deepspace/MemoryFarmSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex("Models",2D)="black"{}
        _Color("Tint",Color)=(1,1,1,1)
        _Width("Outline pixels",Range(1,6))=2
        _StencilComp("Stencil comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil operation",Float)=0
        _StencilWriteMask("Stencil write mask",Float)=255
        _StencilReadMask("Stencil read mask",Float)=255
        _ColorMask("Color mask",Float)=15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize,_Color,_ClipRect;
            float _Width;
            struct A { float4 position:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float4 local:TEXCOORD1; };
            V vert(A v){V o;o.local=v.position;o.position=UnityObjectToClipPos(v.position);o.uv=v.uv;o.color=v.color*_Color;return o;}
            float4 frag(V i):SV_Target
            {
                float4 source=tex2D(_MainTex,i.uv);
                float2 d=abs(_MainTex_TexelSize.xy)*_Width;
                float mask=0;
                mask=max(mask,tex2D(_MainTex,saturate(i.uv+float2(d.x,0))).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv-float2(d.x,0))).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv+float2(0,d.y))).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv-float2(0,d.y))).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv+d*.707)).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv-d*.707)).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv+float2(d.x,-d.y)*.707)).a);
                mask=max(mask,tex2D(_MainTex,saturate(i.uv+float2(-d.x,d.y)*.707)).a);
                float edge=saturate(mask-source.a);
                // Transparent outside the models, so the parade walks in front of the wall's farm backdrop (was opaque black).
                float alpha=saturate(max(source.a,edge));
                float4 col=float4((source.rgb*source.a+edge*.88)/max(alpha,.0001),alpha)*i.color;
                // Soft left/right edges: farmers fade in and out of the window instead of being cut off.
                col.a*=smoothstep(0,.07,i.uv.x)*smoothstep(1,.93,i.uv.x);
                #ifdef UNITY_UI_CLIP_RECT
                col.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
