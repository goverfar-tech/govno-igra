Shader "Sky/SkyUnlit" {
    Properties { _MainTex ("Tex", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "ForceNoShadowCasting"="True" }
        ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 frag (v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color; }
            ENDCG
        }
    }
    Fallback Off
}
