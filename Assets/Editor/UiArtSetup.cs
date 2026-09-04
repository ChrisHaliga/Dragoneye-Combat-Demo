using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Bakes the menu's frames, wells, buttons and backdrop as PNGs.
    ///
    /// USS has no gradients, no shadows and no bevels, so a stylesheet alone can only ever produce
    /// flat rectangles -- which is exactly what the menu looked like. Everything with depth in it has
    /// to be an image, and generating those images here rather than shipping art keeps the whole look
    /// in a diff: a colour is changed by editing a constant and re-running, not by opening Photoshop.
    ///
    /// The frames are nine-sliced, so one 48px image dresses a panel of any size. Corners are
    /// chamfered rather than rounded -- forged metal rather than a web card -- and the chamfer is
    /// kept inside the corner slice so stretching never distorts it.
    ///
    /// Safe to re-run: it overwrites the same files.
    /// </summary>
    static class UiArtSetup
    {
        const string k_Folder = "Assets/UI/Generated";

        // The palette. Everything else in the menu is a tint of these.
        static readonly Color32 k_Ink = new Color32(9, 11, 16, 255);
        static readonly Color32 k_Panel = new Color32(30, 34, 45, 255);
        static readonly Color32 k_PanelLow = new Color32(21, 24, 32, 255);
        static readonly Color32 k_Well = new Color32(11, 13, 18, 255);
        static readonly Color32 k_Edge = new Color32(58, 66, 84, 255);
        static readonly Color32 k_Bevel = new Color32(92, 103, 128, 255);
        static readonly Color32 k_Shade = new Color32(6, 7, 10, 255);
        static readonly Color32 k_Gold = new Color32(198, 158, 88, 255);
        static readonly Color32 k_Ember = new Color32(196, 88, 40, 255);
        static readonly Color32 k_EmberHot = new Color32(226, 112, 54, 255);

        [MenuItem("ClaudeCode/Set Up UI Art")]
        static void FromMenu() => Run();

        /// <summary>Runs the whole step. Called directly by the master setup.</summary>
        internal static void Run()
        {
            EnsureFolder();

            Write("ui-backdrop", Backdrop(512, 512));

            // Frames. The slice border is the corner size; the chamfer and the baked shadow both
            // have to fit inside it or stretching would smear them across the middle.
            Write("ui-panel", Frame(48, 16, k_Panel, k_PanelLow, k_Edge, k_Bevel, chamfer: 13,
                gilded: true, pad: 4));
            Write("ui-well", Frame(32, 11, k_Well, k_Well, k_Edge, k_Shade, chamfer: 8,
                recessed: true));
            Write("ui-card", Frame(40, 14, k_PanelLow, k_Ink, k_Edge, k_Bevel, chamfer: 9, pad: 2));

            Write("ui-button", Frame(32, 11, new Color32(46, 52, 68, 255),
                new Color32(28, 32, 43, 255), k_Edge, k_Bevel, chamfer: 8));
            Write("ui-button-hot", Frame(32, 11, new Color32(64, 72, 94, 255),
                new Color32(40, 46, 61, 255), new Color32(118, 130, 158, 255),
                new Color32(140, 154, 184, 255), chamfer: 8));
            Write("ui-button-primary", Frame(32, 11, k_EmberHot, new Color32(168, 70, 28, 255),
                new Color32(250, 168, 108, 255), new Color32(255, 208, 164, 255), chamfer: 8));

            WriteSized("ui-rule", Rule(64, 9), 64, 9);
            Write("ui-gem", Gem(48));
            Write("ui-glow", Glow(64));

            AssetDatabase.Refresh();
            Debug.Log($"UI art baked into {k_Folder}.");
        }

        // ---------- the backdrop ----------

        /// <summary>
        /// The screen behind everything: a cold vertical fall, a warm ember low in the frame, and a
        /// vignette that pulls the eye off the corners.
        ///
        /// Stretched to fill, so it carries no detail that would show the stretching -- the noise is
        /// per-pixel and reads as grain at any size.
        /// </summary>
        static Color32[] Backdrop(int w, int h)
        {
            var pixels = new Color32[w * h];
            var random = new System.Random(20260903);

            for (var y = 0; y < h; y++)
            {
                var v = y / (float)(h - 1);

                for (var x = 0; x < w; x++)
                {
                    var u = x / (float)(w - 1);

                    // Cold fall from top to bottom.
                    var r = Mathf.Lerp(15f, 6f, v);
                    var g = Mathf.Lerp(18f, 8f, v);
                    var b = Mathf.Lerp(26f, 13f, v);

                    // A warm source below the horizon, so the frame has somewhere to look.
                    var glow = Falloff(u - 0.5f, (v - 0.92f) * 1.6f, 0.85f);
                    r += glow * 46f;
                    g += glow * 20f;
                    b += glow * 8f;

                    // And a cooler one high and left, to keep the top from going flat.
                    var cool = Falloff((u - 0.22f) * 1.2f, (v + 0.05f) * 1.4f, 0.7f);
                    r += cool * 6f;
                    g += cool * 10f;
                    b += cool * 18f;

                    // Vignette.
                    var vignette = 1f - 0.55f * Mathf.Clamp01(
                        Mathf.Sqrt((u - 0.5f) * (u - 0.5f) * 1.1f + (v - 0.5f) * (v - 0.5f)) * 1.7f);
                    r *= vignette;
                    g *= vignette;
                    b *= vignette;

                    var grain = (float)random.NextDouble() * 4f - 2f;

                    pixels[y * w + x] = new Color32(
                        Byte(r + grain), Byte(g + grain), Byte(b + grain), 255);
                }
            }

            return pixels;
        }

        static float Falloff(float dx, float dy, float radius)
        {
            var d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
            return d >= 1f ? 0f : (1f - d) * (1f - d);
        }

        // ---------- frames ----------

        /// <summary>
        /// A nine-sliceable frame: chamfered outline, hairline edge, one-pixel bevel, and a fill
        /// that falls from <paramref name="top"/> to <paramref name="bottom"/>.
        ///
        /// <paramref name="recessed"/> flips the bevel so the shape reads as a hole rather than a
        /// plate -- which is the only difference between a panel and the well sunk into it.
        /// </summary>
        static Color32[] Frame(int size, int border, Color32 top, Color32 bottom, Color32 edge,
            Color32 bevel, int chamfer, bool recessed = false, bool gilded = false, int pad = 0)
        {
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Distance to the nearest edge, counting the chamfered corners as edges too.
                    var left = x;
                    var right = size - 1 - x;
                    var down = y;
                    var up = size - 1 - y;

                    var straight = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));

                    // The diagonal cut at each corner. Only the near corner can bite.
                    var diagonal = Mathf.Min(
                        Mathf.Min(left + down, right + down),
                        Mathf.Min(left + up, right + up)) - chamfer;

                    var depth = Mathf.Min(straight, diagonal) - pad;
                    var onCut = diagonal <= straight;

                    if (depth < -pad)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    if (depth < 0)
                    {
                        // The shadow the panel casts. Baked in because USS has no box-shadow, and
                        // without it every panel sits flush against the backdrop.
                        var falloff = 1f + depth / (float)(pad + 1);
                        pixels[y * size + x] = new Color32(0, 0, 0, Byte(150f * falloff * falloff));
                        continue;
                    }

                    // The fill, before the edge treatment. Vertical, so a tall panel reads as lit
                    // from above once it is stretched.
                    var t = size <= 1 ? 0f : 1f - y / (float)(size - 1);
                    var fill = Mix(bottom, top, t);

                    if (depth == 0)
                    {
                        // A near-black outline, so the shape separates from whatever is behind it.
                        pixels[y * size + x] = new Color32(0, 0, 0, 225);
                        continue;
                    }

                    if (depth == 1)
                    {
                        // Gold on the corner cuts only: four short brackets rather than a gilded
                        // outline, which is the difference between a frame and a picture mount.
                        pixels[y * size + x] = gilded && onCut ? k_Gold : edge;
                        continue;
                    }

                    // The bevel: bright where the light falls, dark where it does not. Which half is
                    // which is the whole difference between raised and sunk.
                    if (depth <= 3)
                    {
                        var lit = recessed ? up > down : down > up;
                        var strength = depth == 2 ? 0.85f : 0.3f;

                        pixels[y * size + x] = lit
                            ? Mix(fill, bevel, strength)
                            : Mix(fill, k_Shade, depth == 2 ? 0.7f : 0.3f);
                        continue;
                    }

                    pixels[y * size + x] = fill;
                }
            }

            return pixels;
        }

        // ---------- ornaments ----------

        /// <summary>
        /// A horizontal rule with a diamond at its middle, tiled by stretching the ends.
        ///
        /// The one piece of ornament in the whole menu. It is what stops a stack of headings reading
        /// as a list of form sections.
        /// </summary>
        static Color32[] Rule(int w, int h)
        {
            var pixels = new Color32[w * h];
            var mid = h / 2;
            var centre = w / 2;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var colour = new Color32(0, 0, 0, 0);

                    // The line, fading out towards the ends so a stretched rule does not stop dead.
                    if (y == mid)
                    {
                        var reach = Mathf.Abs(x - centre) / (float)centre;
                        colour = Fade(k_Gold, 0.75f * (1f - 0.55f * reach));
                    }

                    // The diamond.
                    var d = Mathf.Abs(x - centre) + Mathf.Abs(y - mid);

                    if (d <= mid)
                    {
                        colour = d == mid ? Fade(k_Gold, 0.9f) : Fade(k_Gold, 0.35f);
                    }

                    pixels[y * w + x] = colour;
                }
            }

            return pixels;
        }

        /// <summary>
        /// A round gem, white so USS can tint it per element.
        ///
        /// Elements are a resource a player counts at a glance. Seven coloured words in a row is a
        /// legend; seven lit gems is a hand.
        /// </summary>
        static Color32[] Gem(int size)
        {
            var pixels = new Color32[size * size];
            var centre = (size - 1) / 2f;
            var radius = centre - 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / radius;
                    var dy = (y - centre) / radius;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);

                    if (d > 1f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Bright rim, so the gem has an edge against a dark well.
                    var rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.98f, d));

                    // A highlight up and left, which is where every other light in this menu is.
                    var hx = dx + 0.34f;
                    var hy = dy - 0.34f;
                    var highlight = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) * 1.5f);

                    var value = 0.55f + 0.45f * highlight + 0.3f * rim;
                    var alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.94f, 1f, d));

                    pixels[y * size + x] = new Color32(
                        Byte(255f * Mathf.Clamp01(value)),
                        Byte(255f * Mathf.Clamp01(value)),
                        Byte(255f * Mathf.Clamp01(value)),
                        Byte(255f * alpha));
                }
            }

            return pixels;
        }

        /// <summary>A soft round glow, white, for tinting behind whatever needs attention.</summary>
        static Color32[] Glow(int size)
        {
            var pixels = new Color32[size * size];
            var centre = (size - 1) / 2f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    var a = (1f - d) * (1f - d) * (1f - d);

                    pixels[y * size + x] = new Color32(255, 255, 255, Byte(255f * a));
                }
            }

            return pixels;
        }

        // ---------- writing ----------

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI"))
            {
                AssetDatabase.CreateFolder("Assets", "UI");
            }

            if (!AssetDatabase.IsValidFolder(k_Folder))
            {
                AssetDatabase.CreateFolder("Assets/UI", "Generated");
            }
        }

        /// <summary>
        /// Writes a PNG and forces the import settings the UI needs.
        ///
        /// Point filtering and no compression: these are pixel-exact frames, and a bilinear mip of a
        /// one-pixel bevel is a smear. No mips for the same reason.
        /// </summary>
        static void Write(string name, Color32[] pixels)
        {
            var size = (int)Math.Round(Math.Sqrt(pixels.Length));
            WriteSized(name, pixels, size, size);
        }

        static void WriteSized(string name, Color32[] pixels, int w, int h)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply();

            var path = $"{k_Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // ---------- colour helpers ----------

        static Color32 Mix(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);

            return new Color32(
                Byte(a.r + (b.r - a.r) * t),
                Byte(a.g + (b.g - a.g) * t),
                Byte(a.b + (b.b - a.b) * t),
                Byte(a.a + (b.a - a.a) * t));
        }

        static Color32 Fade(Color32 c, float alpha) =>
            new Color32(c.r, c.g, c.b, Byte(255f * Mathf.Clamp01(alpha)));

        static byte Byte(float value) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
    }
}
