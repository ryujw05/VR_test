Shader "Custom/FloorMatMasked"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.0, 0.5, 1.0, 0.3)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

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
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
            };

            float4 _BaseColor;

            // 최대 4개의 마스크 평면 (nx, ny, nz, d)  :  n·x + d = 0
            float4 _MaskPlane0;
            float4 _MaskPlane1;
            float4 _MaskPlane2;
            float4 _MaskPlane3;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldPos   = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            // 유효 평면 여부 검사 (스칼라)
            inline bool IsValidPlane(float4 p)
            {
                // 절댓값 합으로 0 판정(부동소수 오차 방지)
                return (abs(p.x) + abs(p.y) + abs(p.z) + abs(p.w)) > 1e-6;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float4 col = _BaseColor;

                // 최대 4개의 평면으로 바닥(Quad)을 마스킹
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float4 plane =
                        (i == 0 ? _MaskPlane0 :
                        (i == 1 ? _MaskPlane1 :
                        (i == 2 ? _MaskPlane2 : _MaskPlane3)));

                    if (!IsValidPlane(plane)) continue;

                    float side = dot(plane.xyz, IN.worldPos) + plane.w; // >0 이면 평면의 한쪽
                    if (side > 0) discard;
                }

                return col;
            }
            ENDHLSL
        }
    }
}
