using System;
using System.IO;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// Reads an image file into a texture, and finds one to read.
    ///
    /// Unity ships no runtime file dialog, so choosing a picture is split in two: a path arrives
    /// from somewhere, and this turns it into a texture. In the editor the path can come from a real
    /// dialog; in a build it comes from a text field. Both go through the same loading and the same
    /// size limit, so a portrait behaves identically either way.
    /// </summary>
    public static class PortraitLoader
    {
        /// <summary>
        /// Longest edge a stored portrait may have.
        ///
        /// Portraits are drawn at about sixty pixels. Keeping a four-thousand-pixel photograph in
        /// memory and on disk for that costs megabytes per character and buys nothing.
        /// </summary>
        public const int MaxEdge = 256;

        public static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>Whether the extension is one <see cref="FromFile"/> can decode.</summary>
        public static bool IsSupported(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();

            foreach (var supported in SupportedExtensions)
            {
                if (extension == supported)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Loads and downscales an image. Null when the file is missing or not an image.
        /// </summary>
        public static Texture2D FromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !IsSupported(path))
            {
                return null;
            }

            try
            {
                // Size and format are placeholders; LoadImage replaces both from the file header.
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                texture.name = Path.GetFileNameWithoutExtension(path);
                return Downscaled(texture);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not read image '{path}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Shrinks a texture so its longest edge fits <see cref="MaxEdge"/>, preserving aspect.
        ///
        /// Returns the original untouched when it already fits, so a small portrait is not resampled
        /// for nothing.
        /// </summary>
        static Texture2D Downscaled(Texture2D source)
        {
            var longest = Mathf.Max(source.width, source.height);

            if (longest <= MaxEdge)
            {
                return source;
            }

            var scale = (float)MaxEdge / longest;
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            var scaled = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // Bilinear sampling in a plain loop rather than a Graphics.Blit: this runs once when a
            // player picks a picture, and a blit would need a render texture and a readback for the
            // same result.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    scaled.SetPixel(x, y, source.GetPixelBilinear(
                        (x + 0.5f) / width, (y + 0.5f) / height));
                }
            }

            scaled.Apply();
            scaled.name = source.name;

            UnityEngine.Object.Destroy(source);
            return scaled;
        }
    }
}
