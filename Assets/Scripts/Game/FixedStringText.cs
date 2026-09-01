using System.Text;

namespace Dragoneye.Game
{
    /// <summary>Helpers for getting arbitrary text safely into a fixed-size network string.</summary>
    public static class FixedStringText
    {
        /// <summary>
        /// <c>FixedString64Bytes</c> stores 61 bytes of UTF-8, not 61 characters, and assigning
        /// more throws. Anything user-supplied has to be measured in bytes: counting characters
        /// happens to work for ASCII names and silently overflows on anything else.
        /// </summary>
        public const int MaxBytes = 61;

        public static string Clamp(string value)
        {
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) <= MaxBytes)
            {
                return value ?? string.Empty;
            }

            var length = value.Length;
            while (length > 0 && Encoding.UTF8.GetByteCount(value, 0, length) > MaxBytes)
            {
                length--;
            }

            // Never cut between the halves of a surrogate pair; that produces a replacement
            // character rather than a shorter name.
            if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            {
                length--;
            }

            return value.Substring(0, length);
        }
    }
}
