using System;
using System.Collections.Generic;
using UnityEngine;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game
{
    /// <summary>
    /// The handful of textures and materials the board is dressed in, made in code.
    ///
    /// The same reasoning as <see cref="CreatureToken"/> and the menu's baked frames: nothing here is
    /// content anybody would author by hand -- a soft shadow, a soft glow, a dark table -- and a
    /// gradient that lives in a diff cannot go missing from a prefab or drift from the palette.
    /// Every colour is a constant, so the arena is retuned by editing a number, not a PNG.
    ///
    /// Made once and cached. A shadow under every token is one texture and one material, not one
    /// per creature.
    /// </summary>
    public static class WorldArt
    {
        /// <summary>The near-black behind everything, the same ink the menu frames outline in.</summary>
        public static readonly Color Ink = new Color(0.035f, 0.043f, 0.063f, 1f);

        /// <summary>The table the board sits on, at its brightest, directly under the fight.</summary>
        public static readonly Color TableCentre = new Color(0.118f, 0.133f, 0.176f, 1f);

        /// <summary>What the room is lit by when nothing is lit directly: cool, low, and flat.</summary>
        public static readonly Color Ambient = new Color(0.17f, 0.18f, 0.22f, 1f);

        static readonly Dictionary<string, Texture2D> s_Textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();
        static Mesh s_Quad;

        /// <summary>A soft dark disc: dense in the middle, gone at the edge. The shadow a token casts on its tile.</summary>
        public static Texture2D Shadow => Radial("Shadow", 128,
            d => Mathf.Pow(1f - Mathf.SmoothStep(0.15f, 1f, d), 1.4f), Color.black);

        /// <summary>A soft ring: bright a little inside the edge, dark at the centre and beyond. The halo under the active creature.</summary>
        public static Texture2D Halo => Radial("Halo", 256,
            d =>
            {
                var ring = 1f - Mathf.Abs(d - 0.72f) / 0.22f;
                return Mathf.Clamp01(ring) * Mathf.Clamp01(ring) * (d < 1f ? 1f : 0f);
            },
            Color.white);

        /// <summary>
        /// The table: a cold plate that is brightest under the board and falls to ink at the edges.
        ///
        /// It replaces a skybox. The arena used to clear to Unity's default sky -- a bright blue
        /// gradient -- under a game whose every other screen is dark and ember-lit, and no amount of
        /// work on the pieces reads through a backdrop that belongs to a different game.
        /// </summary>
        public static Texture2D Table => Radial("Table", 512,
            d => 1f - Mathf.SmoothStep(0.25f, 1.05f, d), TableCentre, Ink, grain: 0.012f);

        /// <summary>A one-by-one square lying in the XZ plane, facing up, textured edge to edge.</summary>
        public static Mesh Quad
        {
            get
            {
                if (s_Quad != null)
                {
                    return s_Quad;
                }

                s_Quad = new Mesh
                {
                    name = "World Quad",
                    vertices = new[]
                    {
                        new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                        new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f)
                    },
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                    normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                    triangles = new[] { 0, 2, 1, 0, 3, 2 }
                };

                s_Quad.RecalculateBounds();
                return s_Quad;
            }
        }

        /// <summary>
        /// An unlit material showing a texture, opaque or blended, shared by name.
        ///
        /// The same recipe the move ghost uses, because two translucent things on the same board
        /// that disagree about depth flicker against each other as the camera moves.
        /// </summary>
        public static Material Unlit(string name, Texture2D texture, bool transparent)
        {
            if (s_Materials.TryGetValue(name, out var existing) && existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = name, mainTexture = texture };

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.color = Color.white;

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = 3000;
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            s_Materials[name] = material;
            return material;
        }

        /// <summary>
        /// A square texture whose alpha (and, optionally, colour) is a function of distance from the centre.
        ///
        /// <paramref name="grain"/> adds a little per-pixel noise, which is what stops a large
        /// gradient banding across a dark screen: eight bits of blue is not enough steps for a
        /// forty-unit table, and the eye finds every one of them.
        /// </summary>
        static Texture2D Radial(string name, int size, Func<float, float> strength, Color inner,
            Color? outer = null, float grain = 0f)
        {
            if (s_Textures.TryGetValue(name, out var existing) && existing != null)
            {
                return existing;
            }

            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var random = new System.Random(name.GetHashCode());
            var far = outer ?? new Color(inner.r, inner.g, inner.b, 0f);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var d = Mathf.Sqrt((dx * dx) + (dy * dy));

                    var t = Mathf.Clamp01(strength(d));
                    var noise = grain > 0f ? ((float)random.NextDouble() - 0.5f) * grain : 0f;

                    var colour = outer.HasValue
                        ? Color.Lerp(far, inner, t) + new Color(noise, noise, noise, 0f)
                        : new Color(inner.r, inner.g, inner.b, t);

                    pixels[(y * size) + x] = colour;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

            s_Textures[name] = texture;
            return texture;
        }
    }
}
