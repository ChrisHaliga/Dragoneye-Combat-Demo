using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Bakes the interface's frames, wells, buttons and backdrop as PNGs.
    ///
    /// USS has no gradients, no shadows and no bevels, so a stylesheet alone can only ever produce
    /// flat rectangles -- which is exactly what the menu looked like. Everything with depth in it has
    /// to be an image, and generating those images here rather than shipping art keeps the whole look
    /// in a diff: a colour is changed by editing a constant and re-running, not by opening Photoshop.
    ///
    /// **The second forging.** The first set of frames was a hairline on flat navy, and a hairline on
    /// flat navy is a dashboard whatever is written on it. These are the frames of a game: a warm
    /// surface with grain in it, a gold trim line inside the outline, a lit bevel and a dark inset
    /// line so the edge reads as a moulding rather than a border, and a bright stud at each corner
    /// cut. The same seven constants still drive all of it.
    ///
    /// Everything bakes at twice the size it is drawn, because the panel scales with the window and
    /// a point-sampled bevel at one and a half pixels is a stair. The stylesheets take the images at
    /// half scale, so nothing in the layout moves when a frame is retuned.
    ///
    /// Safe to re-run: it overwrites the same files.
    /// </summary>
    static class UiArtSetup
    {
        const string k_Folder = "Assets/UI/Generated";

        /// <summary>Everything that references these images by path, and so has to be reimported.</summary>
        static readonly string[] k_Stylesheets =
        {
            "Assets/UI/SessionMenu.uss",
            "Assets/UI/DraftPanel.uss",
            "Assets/UI/ArenaHud.uss",
            "Assets/UI/PauseMenu.uss",
            "Assets/UI/Help.uss",
            "Assets/UI/Chrome.uss"
        };

        // The palette. Warm darks, not blue ones: gold trim on a cold navy reads as a website with
        // a theme, and gold trim on warm charcoal reads as a thing somebody could pick up.
        static readonly Color32 k_Ink = new Color32(9, 8, 10, 255);
        static readonly Color32 k_Panel = new Color32(46, 41, 41, 255);
        static readonly Color32 k_PanelLow = new Color32(27, 24, 25, 255);
        static readonly Color32 k_Card = new Color32(34, 30, 31, 255);
        static readonly Color32 k_CardLow = new Color32(20, 18, 19, 255);
        static readonly Color32 k_Well = new Color32(13, 12, 14, 255);
        static readonly Color32 k_Edge = new Color32(74, 66, 62, 255);
        static readonly Color32 k_Bevel = new Color32(112, 100, 92, 255);
        static readonly Color32 k_Shade = new Color32(5, 4, 5, 255);
        static readonly Color32 k_Trim = new Color32(132, 104, 58, 255);
        static readonly Color32 k_Gold = new Color32(198, 158, 88, 255);
        static readonly Color32 k_GoldHot = new Color32(240, 206, 140, 255);
        static readonly Color32 k_Ember = new Color32(196, 88, 40, 255);
        static readonly Color32 k_EmberHot = new Color32(232, 118, 58, 255);
        static readonly Color32 k_EmberLow = new Color32(142, 58, 24, 255);

        /// <summary>Runs the whole step. Called directly by the master setup.</summary>
        internal static void Run()
        {
            EnsureFolder();

            Write("ui-backdrop", Backdrop(1024, 1024));

            // Frames. The slice border is the corner size; the chamfer, the trim and the baked
            // shadow all have to fit inside it or stretching would smear them across the middle.
            Write("ui-panel", Frame(96, 32, k_Panel, k_PanelLow, k_Trim, k_Bevel,
                chamfer: 26, pad: 8, studded: true, grain: 3f));
            Write("ui-well", Frame(64, 22, k_Well, k_Well, k_Edge, k_Shade,
                chamfer: 14, recessed: true));
            Write("ui-card", Frame(80, 28, k_Card, k_CardLow, k_Edge, k_Bevel,
                chamfer: 16, pad: 4, grain: 2f));

            Write("ui-button", Frame(64, 22, new Color32(58, 52, 50, 255),
                new Color32(34, 30, 30, 255), k_Edge, k_Bevel, chamfer: 14, grain: 2f));
            Write("ui-button-hot", Frame(64, 22, new Color32(84, 74, 68, 255),
                new Color32(48, 42, 40, 255), k_Trim, new Color32(150, 134, 116, 255),
                chamfer: 14, grain: 2f));
            Write("ui-button-primary", Frame(64, 22, k_EmberHot, k_EmberLow,
                new Color32(246, 190, 122, 255), new Color32(255, 222, 176, 255),
                chamfer: 14, grain: 2f));

            WriteSized("ui-rule", Rule(128, 18), 128, 18);
            WriteSized("ui-shade", Shade(2, 128), 2, 128);
            Write("ui-gem", Gem(96));
            Write("ui-glow", Glow(128));

            AssetDatabase.Refresh();

            // The stylesheets name these images by path. A USS imported while they were missing
            // keeps its unresolved references until something asks for it again, so a first run on
            // a fresh clone would bake the art and still show a menu with no frames on it.
            foreach (var sheet in k_Stylesheets)
            {
                if (File.Exists(sheet))
                {
                    AssetDatabase.ImportAsset(sheet, ImportAssetOptions.ForceUpdate);
                }
            }

            Debug.Log($"UI art baked into {k_Folder}; {k_Stylesheets.Length} stylesheets reimported.");
        }

        // ---------- the backdrop ----------

        /// <summary>
        /// The screen behind everything: a warm fall from a lit top to a dark floor, an ember
        /// burning low in the frame, and a vignette that pulls the eye off the corners.
        ///
        /// Loud enough to exist. The first version of this was authored in single digits and read
        /// as flat black on every monitor it was looked at on; a backdrop nobody can see is a
        /// backdrop that is not doing its job, which is to make the panels sit *in* somewhere.
        /// </summary>
        static Color32[] Backdrop(int w, int h)
        {
            var pixels = new Color32[w * h];
            var random = new System.Random(20260903);

            for (var y = 0; y < h; y++)
            {
                var v = 1f - y / (float)(h - 1);

                for (var x = 0; x < w; x++)
                {
                    var u = x / (float)(w - 1);

                    // A warm fall from top to bottom.
                    var r = Mathf.Lerp(30f, 12f, v);
                    var g = Mathf.Lerp(26f, 10f, v);
                    var b = Mathf.Lerp(30f, 13f, v);

                    // The ember, below the horizon and a little off centre, so the frame has
                    // somewhere to look and the two halves of a screen are not mirror images.
                    var glow = Falloff((u - 0.56f) * 0.9f, (v - 0.96f) * 1.5f, 0.95f);
                    r += glow * 78f;
                    g += glow * 34f;
                    b += glow * 10f;

                    // A cooler light high and to the left, to keep the top from going flat.
                    var cool = Falloff((u - 0.18f) * 1.1f, (v + 0.08f) * 1.3f, 0.75f);
                    r += cool * 8f;
                    g += cool * 12f;
                    b += cool * 24f;

                    // Vignette.
                    var vignette = 1f - 0.62f * Mathf.Clamp01(
                        Mathf.Sqrt((u - 0.5f) * (u - 0.5f) * 1.15f + (v - 0.5f) * (v - 0.5f)) * 1.75f);
                    r *= vignette;
                    g *= vignette;
                    b *= vignette;

                    var grain = ((float)random.NextDouble() - 0.5f) * 7f;

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
        /// A nine-sliceable frame with a moulded edge and a grained surface.
        ///
        /// Read from the outside in, each band two texels wide: the shadow it casts, a near-black
        /// outline, the trim line, a lit bevel (or a dark one, when <paramref name="recessed"/>
        /// turns the plate into a hole), a dark inset line, and then the fill -- which falls from
        /// <paramref name="top"/> to <paramref name="bottom"/> and carries a little noise so a large
        /// panel does not band.
        ///
        /// <paramref name="studded"/> puts a bright stud on each corner cut. It is the one ornament
        /// on the frame and the thing that makes it a frame rather than a box.
        /// </summary>
        static Color32[] Frame(int size, int border, Color32 top, Color32 bottom, Color32 trim,
            Color32 bevel, int chamfer, bool recessed = false, bool studded = false, int pad = 0,
            float grain = 0f)
        {
            var pixels = new Color32[size * size];
            var random = new System.Random(size * 31 + chamfer);

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

                    var raw = Mathf.Min(straight, diagonal) - pad;
                    var onCut = diagonal <= straight;

                    if (raw < -pad)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    if (raw < 0)
                    {
                        // The shadow the panel casts. Baked in because USS has no box-shadow, and
                        // without it every panel sits flush against the backdrop.
                        var falloff = 1f + raw / (float)(pad + 1);
                        pixels[y * size + x] = new Color32(0, 0, 0, Byte(170f * falloff * falloff));
                        continue;
                    }

                    // Bands are two texels wide: the frame is drawn at half size.
                    var depth = raw / 2;

                    // The fill, before the edge treatment. Vertical, so a tall panel reads as lit
                    // from above once it is stretched. Grain is per texel and does not scale, which
                    // is what makes it read as surface rather than as pattern.
                    var t = size <= 1 ? 0f : 1f - y / (float)(size - 1);
                    var noise = grain > 0f ? ((float)random.NextDouble() - 0.5f) * grain : 0f;
                    var fill = Shift(Mix(bottom, top, t), noise);

                    if (depth == 0)
                    {
                        // A near-black outline, so the shape separates from whatever is behind it.
                        pixels[y * size + x] = new Color32(0, 0, 0, 235);
                        continue;
                    }

                    if (depth == 1)
                    {
                        // The trim line, all the way round. Studs on the corner cuts where asked.
                        pixels[y * size + x] = studded && onCut ? k_GoldHot : trim;
                        continue;
                    }

                    if (depth == 2)
                    {
                        // The bevel: bright where the light falls, dark where it does not. Which
                        // half is which is the whole difference between raised and sunk.
                        var lit = recessed ? up > down : down > up;
                        pixels[y * size + x] = lit ? Mix(fill, bevel, 0.8f) : Mix(fill, k_Shade, 0.75f);
                        continue;
                    }

                    if (depth == 3)
                    {
                        // A dark inset line inside the bevel. It is what turns one edge into a
                        // moulding: the eye reads outline, trim, light, dark, surface as a profile.
                        pixels[y * size + x] = Mix(fill, k_Shade, recessed ? 0.35f : 0.55f);
                        continue;
                    }

                    if (recessed && depth < 8)
                    {
                        // A well is shadowed just inside its top edge, which is how a hole reads
                        // as a hole once there is something drawn in it.
                        var inner = up < down && up - pad < 16
                            ? Mathf.Clamp01(1f - (up - pad - 6) / 10f) * 0.45f
                            : 0f;

                        pixels[y * size + x] = Mix(fill, k_Shade, inner);
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

                    // Constant along its length. The line is stretched to the width of a panel, so
                    // any fade authored into the source becomes most of the rule. Two texels tall,
                    // because the rule is drawn at half size.
                    if (y == mid || y == mid - 1)
                    {
                        colour = Fade(k_Gold, 0.6f);
                    }

                    // The diamond.
                    var d = Mathf.Abs(x - centre) + Mathf.Abs(y - mid);

                    if (d <= mid)
                    {
                        colour = d == mid ? Fade(k_Gold, 0.9f) : Fade(k_Gold, 0.45f);
                    }

                    pixels[y * w + x] = colour;
                }
            }

            return pixels;
        }

        /// <summary>
        /// A vertical fall from ink to nothing. Stretched across the top of the arena under the
        /// turn bar: a strip of HUD needs something to sit on, and a framed panel across the whole
        /// width would box the board in.
        /// </summary>
        static Color32[] Shade(int w, int h)
        {
            var pixels = new Color32[w * h];

            for (var y = 0; y < h; y++)
            {
                // Row zero is the bottom of the texture, so the dense end is the last row.
                var t = 1f - y / (float)(h - 1);
                var alpha = Mathf.Pow(t, 1.6f) * 0.82f;

                for (var x = 0; x < w; x++)
                {
                    pixels[y * w + x] = new Color32(4, 5, 8, Byte(255f * alpha));
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
        /// No compression and no mips: these are frames drawn at close to their own size, and a
        /// compressed or mipped bevel is a smear.
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

            // Bilinear, now that there are twice the texels to filter. Point filtering was right
            // when the frames were drawn one texel per pixel and wrong the moment the panel
            // scaled, which on anything larger than the reference resolution is always.
            importer.filterMode = FilterMode.Bilinear;
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

        static Color32 Shift(Color32 c, float by) =>
            new Color32(Byte(c.r + by), Byte(c.g + by), Byte(c.b + by), c.a);

        static Color32 Fade(Color32 c, float alpha) =>
            new Color32(c.r, c.g, c.b, Byte(255f * Mathf.Clamp01(alpha)));

        static byte Byte(float value) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
    }
}
