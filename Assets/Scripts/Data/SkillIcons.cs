using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>Where an authored icon comes from, when one was authored.</summary>
    public interface ISkillIconSource
    {
        /// <summary>The sprite on the skill's asset, or null when nobody has drawn one yet.</summary>
        Sprite AuthoredIcon(int skillId);
    }

    /// <summary>
    /// The picture on a skill's slot.
    ///
    /// Authored art when the asset carries some; a glyph drawn here when it does not. The glyph is
    /// worked out from what the skill is -- a blade for a swing, an arrow for a shot, a cross for
    /// a heal, a bolt for a breath -- in the element's colour, so a bar of eight generated icons
    /// is still eight different things at a glance. Deterministic and cached: the same skill draws
    /// the same picture on every machine, every time.
    ///
    /// A seam rather than a reference, like <see cref="ElementIcons"/>: the arena HUD reads these
    /// from components on spawned prefabs, which cannot carry a serialised pointer to content.
    /// </summary>
    public static class SkillIcons
    {
        static readonly Dictionary<int, Sprite> s_Generated = new Dictionary<int, Sprite>();
        static Sprite s_Move;

        /// <summary>Filled by <see cref="ContentCatalog"/> when it builds.</summary>
        public static ISkillIconSource Current { get; internal set; }

        /// <summary>The icon for a skill: authored if there is one, drawn if not.</summary>
        public static Sprite For(SkillSpec skill)
        {
            if (skill == null)
            {
                return null;
            }

            var authored = Current?.AuthoredIcon(skill.Id);

            if (authored != null)
            {
                return authored;
            }

            if (!s_Generated.TryGetValue(skill.Id, out var sprite) || sprite == null)
            {
                sprite = SkillGlyphs.Draw(skill);
                s_Generated[skill.Id] = sprite;
            }

            return sprite;
        }

        /// <summary>The icon for walking, which is no skill and has no asset.</summary>
        public static Sprite Move
        {
            get
            {
                if (s_Move == null)
                {
                    s_Move = SkillGlyphs.DrawMove();
                }

                return s_Move;
            }
        }
    }

    /// <summary>
    /// Draws a skill's glyph: a dark plate with the skill's shape on it in its element's colour.
    ///
    /// Signed-distance drawing on a small square, so every shape is a few line segments and a
    /// ring, anti-aliased by the distance rather than by a bigger texture. Nothing here is art;
    /// it is what stands in until there is.
    /// </summary>
    public static class SkillGlyphs
    {
        /// <summary>Pixels a side. Drawn at twice the size a slot shows it, so it scales cleanly.</summary>
        public const int Size = 64;

        static readonly Color k_Plate = new Color(0.10f, 0.11f, 0.15f, 0.94f);
        static readonly Color k_Walk = new Color(0.70f, 0.75f, 0.84f, 1f);

        /// <summary>A line from one point to another, with a width. The unit every glyph is made of.</summary>
        readonly struct Stroke
        {
            public readonly Vector2 A;
            public readonly Vector2 B;
            public readonly float Width;

            public Stroke(float ax, float ay, float bx, float by, float width)
            {
                A = new Vector2(ax, ay);
                B = new Vector2(bx, by);
                Width = width;
            }

            public float Distance(Vector2 p)
            {
                var ab = B - A;
                var t = Mathf.Clamp01(Vector2.Dot(p - A, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                return Vector2.Distance(p, A + (ab * t)) - (Width * 0.5f);
            }
        }

        public static Sprite Draw(SkillSpec skill)
        {
            var tint = ElementPalette.ForElement(skill.Element);
            return Bake($"Skill {skill.Id}", tint, ShapeOf(skill), RingOf(skill));
        }

        public static Sprite DrawMove() => Bake("Move", k_Walk, Chevrons(), null);

        /// <summary>The strokes for what a skill does.</summary>
        static List<Stroke> ShapeOf(SkillSpec skill)
        {
            switch (skill.Effect.Kind)
            {
                case SkillEffectKind.Heal:
                    return Cross();

                case SkillEffectKind.RestoreAp:
                    return Bolt();

                case SkillEffectKind.ReturnElement:
                    return Breath();

                default:
                    return skill.Target == SkillTarget.Self
                        ? Cross()
                        : skill.RollsToHit ? Arrow() : Blade();
            }
        }

        /// <summary>A ring, for the shapes that are a ring with a gap: a breath.</summary>
        static (Vector2 centre, float radius, float width, float gapFrom, float gapTo)? RingOf(SkillSpec skill) =>
            skill.Effect.Kind == SkillEffectKind.ReturnElement
                ? (new Vector2(32f, 32f), 15f, 4.5f, -0.4f, 0.9f)
                : ((Vector2, float, float, float, float)?)null;

        static List<Stroke> Blade() => new List<Stroke>
        {
            new Stroke(20f, 46f, 47f, 19f, 6f),   // the blade
            new Stroke(20f, 34f, 32f, 46f, 3.5f), // the guard
            new Stroke(15f, 51f, 19f, 47f, 5f)    // the grip
        };

        static List<Stroke> Arrow() => new List<Stroke>
        {
            new Stroke(15f, 49f, 47f, 17f, 3f),   // the shaft
            new Stroke(47f, 17f, 35f, 19f, 3f),   // the head
            new Stroke(47f, 17f, 45f, 29f, 3f),
            new Stroke(17f, 47f, 13f, 41f, 2.5f), // the fletching
            new Stroke(17f, 47f, 23f, 51f, 2.5f)
        };

        static List<Stroke> Cross() => new List<Stroke>
        {
            new Stroke(32f, 17f, 32f, 47f, 8f),
            new Stroke(17f, 32f, 47f, 32f, 8f)
        };

        static List<Stroke> Bolt() => new List<Stroke>
        {
            new Stroke(37f, 12f, 25f, 34f, 4.5f),
            new Stroke(25f, 34f, 36f, 34f, 4.5f),
            new Stroke(36f, 34f, 27f, 53f, 4.5f)
        };

        static List<Stroke> Breath() => new List<Stroke>
        {
            new Stroke(44f, 17f, 46f, 27f, 3.5f), // the arrowhead where the ring reopens
            new Stroke(44f, 17f, 36f, 21f, 3.5f)
        };

        static List<Stroke> Chevrons()
        {
            var strokes = new List<Stroke>();

            for (var i = 0; i < 3; i++)
            {
                var x = 15f + (i * 12f);
                strokes.Add(new Stroke(x, 20f, x + 10f, 32f, 4f));
                strokes.Add(new Stroke(x + 10f, 32f, x, 44f, 4f));
            }

            return strokes;
        }

        /// <summary>The plate, the strokes and the ring, into a sprite.</summary>
        static Sprite Bake(string name, Color tint, List<Stroke> strokes,
            (Vector2 centre, float radius, float width, float gapFrom, float gapTo)? ring)
        {
            var pixels = new Color32[Size * Size];
            var edge = new Color(tint.r, tint.g, tint.b, 0.55f);

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var p = new Vector2(x + 0.5f, Size - (y + 0.5f));
                    var colour = Plate(p, edge);

                    var glyph = float.MaxValue;

                    foreach (var stroke in strokes)
                    {
                        glyph = Mathf.Min(glyph, stroke.Distance(p));
                    }

                    if (ring.HasValue)
                    {
                        var (centre, radius, width, gapFrom, gapTo) = ring.Value;
                        var angle = Mathf.Atan2(p.y - centre.y, p.x - centre.x);
                        var inGap = angle > gapFrom && angle < gapTo;

                        if (!inGap)
                        {
                            glyph = Mathf.Min(glyph,
                                Mathf.Abs(Vector2.Distance(p, centre) - radius) - (width * 0.5f));
                        }
                    }

                    var cover = Mathf.Clamp01(0.5f - glyph);

                    if (cover > 0f)
                    {
                        colour = Color.Lerp(colour, tint, cover);
                    }

                    pixels[(y * Size) + x] = colour;
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            sprite.name = name;
            return sprite;
        }

        /// <summary>A rounded dark square with a thin edge in the element's colour.</summary>
        static Color Plate(Vector2 p, Color edge)
        {
            const float radius = 11f;
            const float inset = 2f;

            var half = (Size * 0.5f) - inset - radius;
            var q = new Vector2(Mathf.Abs(p.x - (Size * 0.5f)) - half, Mathf.Abs(p.y - (Size * 0.5f)) - half);
            var outside = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;

            var body = Mathf.Clamp01(0.5f - outside);
            var rim = Mathf.Clamp01(0.5f - Mathf.Abs(outside + 1.2f)) ;

            var colour = new Color(k_Plate.r, k_Plate.g, k_Plate.b, k_Plate.a * body);
            return Color.Lerp(colour, edge, rim * edge.a);
        }
    }
}
