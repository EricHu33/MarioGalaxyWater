Shader "Unlit/WaterSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SurfaceTex ("Texture", 2D) = "white" {}
        _SurfaceColor ("Surface color", COLOR) = ( 1, 1, 1, 1)
        _LowCut("Low Cut", Range(0, 1)) = 0.13
        _HighCut("High Cut", Range(0, 1)) = 0.92
        _BaseScrollSpeed("Base Scroll Speed", Float) = 0
        _ReplacementScrollSpeed("Replacement Scroll Speed", Float) = 0
        _DisplaceStrength("Displacement Strength", Range(0.01, 1)) = 0
        _DisplaceTex ("Displacement Map", 2D) = "" {}
        _RefractionTex ("Internal Refraction", 2D) = "" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
// Upgrade NOTE: excluded shader from DX11; has structs without semantics (struct v2f members displaceUv)
#pragma exclude_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3: TEXCOORD2;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3 : TEXCOORD2;
                float4 scrPos : TEXCOORD3;
                float3 worldPos :TEXCOORD4;
                float3 worldNormal : NORMAL;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _RefractionTex;
            sampler2D _DisplaceTex;
            sampler2D _SurfaceTex;
            float4 _MainTex_ST;
            float4 _DisplaceTex_ST;
            float4 _SurfaceTex_ST;
            float _BaseScrollSpeed;
            float _ReplacementScrollSpeed;
            float _DisplaceStrength;
            float4 _SurfaceColor;
            float _LowCut;
            float _HighCut;
            float _TestRefrac;
            float _TestWave;
            float _TestSurface;
            float _TestAll;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv2, _DisplaceTex);
                o.uv3 = TRANSFORM_TEX(v.uv3, _SurfaceTex);
                o.scrPos = ComputeScreenPos(o.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 displaceUv = float2((i.uv2.x + _Time.y * _ReplacementScrollSpeed), (i.uv2.y + _Time.y * _ReplacementScrollSpeed));
                float displacemment = tex2D(_DisplaceTex, displaceUv).r * 2 - 1;

                float4 refUv = UNITY_PROJ_COORD(i.scrPos);
                refUv.xy += displacemment * _DisplaceStrength;
                fixed4 col = tex2Dproj(_RefractionTex, refUv);

                float2 waterUv = (i.uv.xy ) + displacemment * _DisplaceStrength;
                waterUv += _Time.y * _BaseScrollSpeed;
                fixed4 colWater = tex2D(_MainTex, waterUv);
                fixed4 surfaceColor1 = tex2D(_SurfaceTex, i.uv3 + _Time.y * _BaseScrollSpeed);
                fixed4 surfaceColor2 = tex2D(_SurfaceTex, i.uv3 - _Time.y * _BaseScrollSpeed);
                fixed4 surfaceColorSum = surfaceColor1 + surfaceColor2;
                
                float3 worldNormal = normalize(i.worldNormal);
                float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
                float NdotV = 1-saturate (pow(dot (viewDir, worldNormal) * 2, 1));
                float surfaceCutFactor = 1;
                surfaceCutFactor = step(_LowCut * 2 , surfaceColorSum.r) * step(surfaceColorSum.r , _HighCut * 2);
                surfaceCutFactor *= NdotV;
                col = col * 0.6 + colWater * (0.4 - 0.3 * surfaceCutFactor)  + 0.3 * (surfaceCutFactor * surfaceColorSum * _SurfaceColor);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
