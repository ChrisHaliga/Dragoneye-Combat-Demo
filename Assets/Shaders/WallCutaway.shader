// Walls that go see-through where a creature is behind them.
//
// Every frame WallCutaway hands the shader the screen position and view depth of every creature
// on the board. A fragment of wall that is nearer the camera than a creature and within a small
// screen-space circle of it is dithered away, so the player looks down a soft tunnel at the
// creature rather than at the wall. Dithered rather than blended: the wall stays opaque to the
// depth buffer, sorts with everything else, and costs nothing extra.
Shader "Dragoneye/WallCutaway"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.34, 0.32, 0.30, 1)
        _CutRadius ("Cut radius (fraction of screen height)", Float) = 0.11
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _CutRadius;
            CBUFFER_END

            #define DRAGONEYE_MAX_CUTS 16

            // xy: screen position 0..1, z: view depth. Set globally by WallCutaway.
            float4 _DragoneyeCuts[DRAGONEYE_MAX_CUTS];
            int _DragoneyeCutCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;

                // A gradient up the wall, baked into the mesh. Flat stone under flat light is a
                // slab; this is what gives it a foot and a head without a texture.
                float4 colour : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 colour : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.colour = input.colour;
                return output;
            }

            static const float k_Bayer[16] =
            {
                0.0 / 16.0, 8.0 / 16.0, 2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0, 6.0 / 16.0,
                3.0 / 16.0, 11.0 / 16.0, 1.0 / 16.0, 9.0 / 16.0,
                15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0, 5.0 / 16.0
            };

            // Courses of stone on the faces of a wall: a thin dark mortar line every course of
            // height, and a joint every so often along it, staggered course to course the way
            // bricks are laid. The top face is left plain -- it is a cap, not a course. A flat
            // grey slab was the whole of what a wall looked like, and this is the cheapest thing
            // that makes it look built.
            float Masonry(float3 positionWS, float3 normal)
            {
                if (normal.y > 0.5)
                {
                    return 1.0;
                }

                const float course = 0.16;
                const float brick = 0.34;

                float row = floor(positionWS.y / course);
                float alongWall = positionWS.x + positionWS.z;
                float mortar = step(frac(positionWS.y / course), 0.14);
                float joint = step(frac(alongWall / brick + fmod(row, 2.0) * 0.5), 0.09);

                return 1.0 - 0.32 * max(mortar, joint);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screen = input.positionHCS.xy / _ScreenParams.xy;
                float depth = -TransformWorldToView(input.positionWS).z;
                float aspect = _ScreenParams.x / _ScreenParams.y;

                // How much of this fragment survives: one outside every tunnel, falling to nothing
                // at the middle of the nearest one.
                float keep = 1.0;

                for (int c = 0; c < _DragoneyeCutCount; c++)
                {
                    float4 cut = _DragoneyeCuts[c];

                    // Only a wall *in front of* the creature hides it.
                    if (depth < cut.z - 0.05)
                    {
                        float2 offset = screen - cut.xy;
                        offset.x *= aspect;
                        float inner = _CutRadius * 0.55;
                        float t = saturate((length(offset) - inner) / max(_CutRadius - inner, 0.001));
                        keep = min(keep, t);
                    }
                }

                uint2 pixel = uint2(input.positionHCS.xy) & 3;
                clip(keep - k_Bayer[pixel.y * 4 + pixel.x] - 0.001);

                float3 normal = normalize(input.normalWS);
                Light light = GetMainLight();
                float lambert = saturate(dot(normal, light.direction));
                float3 stone = _BaseColor.rgb * input.colour.rgb * Masonry(input.positionWS, normal);
                float3 lit = stone * light.color * (0.3 + 0.7 * lambert);
                float3 ambient = stone * SampleSH(normal);

                return half4(lit + ambient, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
