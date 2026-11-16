// Ảnh thảm nguyên lông (bake sẵn) bị khoét theo mask ô: alpha của _MaskTex = 1 ô chưa cạo, 0 ô đã cạo.
// _MaskTex là texture cols x rows (lọc bilinear), smoothstep làm mép ô gọn và bo tròn ở góc thay vì nhoè cả ô.
Shader "RugMaster/UI Pile Mask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Uncut pile", 2D) = "white" {}
        _MaskTex ("Cell mask (alpha)", 2D) = "white" {}
        _MaskST ("Mask UV scale/offset", Vector) = (1,1,0,0)
        _Edge ("Mask edge (low, high)", Vector) = (0.38,0.62,0,0)
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float2 mask:TEXCOORD1;float4 world:TEXCOORD2;};
            sampler2D _MainTex;sampler2D _MaskTex;float4 _MaskST;float4 _Edge;fixed4 _Color;float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;
                o.mask=v.uv*_MaskST.xy+_MaskST.zw;o.color=v.color*_Color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 c=tex2D(_MainTex,i.uv)*i.color;
                c.a*=smoothstep(_Edge.x,_Edge.y,tex2D(_MaskTex,i.mask).a);
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
