Shader "Hidden/ToyGarden/EquipmentIMGUI"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _GUIClipTexture;
            float4x4 unity_GUIClipTextureMatrix;

            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 clipUV : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.color = input.color;
                float3 viewPosition = UnityObjectToViewPos(input.position);
                output.clipUV = mul(unity_GUIClipTextureMatrix, float4(viewPosition.xy, 0, 1)).xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // sRGB import already linearizes texture samples. The player framebuffer
                // performs the output transfer, so there must be no second gamma conversion here.
                half4 color = tex2D(_MainTex, input.uv) * input.color;
                color.a *= tex2D(_GUIClipTexture, input.clipUV).a;
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
