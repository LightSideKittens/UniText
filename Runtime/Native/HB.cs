using System;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.CallingConvention;

namespace LightSide
{
    internal static unsafe class HB
    {
        #region Enums & Constants

        public const int DIRECTION_LTR = 4;
        public const int DIRECTION_RTL = 5;
        public const int DIRECTION_TTB = 6;
        public const int DIRECTION_BTT = 7;

        public const uint BUFFER_FLAG_DEFAULT = 0x0;
        public const uint BUFFER_FLAG_BOT = 0x1;
        public const uint BUFFER_FLAG_EOT = 0x2;
        public const uint BUFFER_FLAG_PRESERVE_DEFAULT_IGNORABLES = 0x4;
        public const uint BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES = 0x8;

        public static class Script
        {
            public const uint Common = ('Z' << 24) | ('y' << 16) | ('y' << 8) | 'y';
            public const uint Arabic = ('A' << 24) | ('r' << 16) | ('a' << 8) | 'b';
            public const uint Armenian = ('A' << 24) | ('r' << 16) | ('m' << 8) | 'n';
            public const uint Bengali = ('B' << 24) | ('e' << 16) | ('n' << 8) | 'g';
            public const uint Cyrillic = ('C' << 24) | ('y' << 16) | ('r' << 8) | 'l';
            public const uint Devanagari = ('D' << 24) | ('e' << 16) | ('v' << 8) | 'a';
            public const uint Georgian = ('G' << 24) | ('e' << 16) | ('o' << 8) | 'r';
            public const uint Greek = ('G' << 24) | ('r' << 16) | ('e' << 8) | 'k';
            public const uint Gujarati = ('G' << 24) | ('u' << 16) | ('j' << 8) | 'r';
            public const uint Gurmukhi = ('G' << 24) | ('u' << 16) | ('r' << 8) | 'u';
            public const uint Han = ('H' << 24) | ('a' << 16) | ('n' << 8) | 'i';
            public const uint Hangul = ('H' << 24) | ('a' << 16) | ('n' << 8) | 'g';
            public const uint Hebrew = ('H' << 24) | ('e' << 16) | ('b' << 8) | 'r';
            public const uint Hiragana = ('H' << 24) | ('i' << 16) | ('r' << 8) | 'a';
            public const uint Kannada = ('K' << 24) | ('n' << 16) | ('d' << 8) | 'a';
            public const uint Katakana = ('K' << 24) | ('a' << 16) | ('n' << 8) | 'a';
            public const uint Khmer = ('K' << 24) | ('h' << 16) | ('m' << 8) | 'r';
            public const uint Lao = ('L' << 24) | ('a' << 16) | ('o' << 8) | 'o';
            public const uint Latin = ('L' << 24) | ('a' << 16) | ('t' << 8) | 'n';
            public const uint Malayalam = ('M' << 24) | ('l' << 16) | ('y' << 8) | 'm';
            public const uint Myanmar = ('M' << 24) | ('y' << 16) | ('m' << 8) | 'r';
            public const uint Oriya = ('O' << 24) | ('r' << 16) | ('y' << 8) | 'a';
            public const uint Sinhala = ('S' << 24) | ('i' << 16) | ('n' << 8) | 'h';
            public const uint Tamil = ('T' << 24) | ('a' << 16) | ('m' << 8) | 'l';
            public const uint Telugu = ('T' << 24) | ('e' << 16) | ('l' << 8) | 'u';
            public const uint Thai = ('T' << 24) | ('h' << 16) | ('a' << 8) | 'i';
            public const uint Tibetan = ('T' << 24) | ('i' << 16) | ('b' << 8) | 't';
        }

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct GlyphInfo
        {
            public uint glyphId;
            public uint cluster;
            public int xAdvance;
            public int yAdvance;
            public int xOffset;
            public int yOffset;
        }

        #endregion

        #region Platform-specific bindings

        [ThreadStatic] private static GlyphInfo[] glyphInfoBuffer;

    #if (UNITY_IOS || UNITY_TVOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
    #else
        private const string LibraryName = "unitext_native";
    #endif

        private const int HB_MEMORY_MODE_READONLY = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct hb_glyph_info_t
        {
            public uint codepoint;
            public uint mask;
            public uint cluster;
            private uint var1, var2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct hb_glyph_position_t
        {
            public int x_advance, y_advance;
            public int x_offset, y_offset;
            private uint var;
        }

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_blob_create(IntPtr data, uint length, int mode, IntPtr userData, IntPtr destroy);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_blob_destroy(IntPtr blob);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_face_create(IntPtr blob, uint index);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_face_destroy(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_face_get_upem(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_font_create(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_font_destroy(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_ot_font_set_funcs(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl, EntryPoint = "ut_hb_font_get_glyph_h_advance")] private static extern int ut_hb_font_get_glyph_h_advance(IntPtr font, uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ut_hb_font_get_glyph(IntPtr font, uint unicode, uint variationSelector, out uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_font_get_face(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_buffer_create();
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_destroy(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_clear_contents(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_direction(IntPtr buffer, int direction);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_script(IntPtr buffer, uint script);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_content_type(IntPtr buffer, int contentType);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_flags(IntPtr buffer, uint flags);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_add_codepoints(IntPtr buffer, uint* codepoints, int textLength, uint itemOffset, int itemLength);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_buffer_get_length(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern hb_glyph_info_t* ut_hb_buffer_get_glyph_infos(IntPtr buffer, out uint length);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern hb_glyph_position_t* ut_hb_buffer_get_glyph_positions(IntPtr buffer, out uint length);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_shape(IntPtr font, IntPtr buffer, IntPtr features, uint numFeatures);

        #endregion

        #region Unified Font API

        public static IntPtr CreateFont(IntPtr ftFace, IntPtr fontDataPtr, int fontDataLength, out IntPtr blob, out IntPtr hbFace, out int upem)
        {
            blob = IntPtr.Zero;
            hbFace = IntPtr.Zero;
            upem = 1000;

            if (fontDataPtr == IntPtr.Zero || fontDataLength <= 0)
                return IntPtr.Zero;

            blob = ut_hb_blob_create(fontDataPtr, (uint)fontDataLength, HB_MEMORY_MODE_READONLY, IntPtr.Zero, IntPtr.Zero);
            if (blob == IntPtr.Zero)
                return IntPtr.Zero;

            hbFace = ut_hb_face_create(blob, 0);
            if (hbFace == IntPtr.Zero)
            {
                ut_hb_blob_destroy(blob);
                blob = IntPtr.Zero;
                return IntPtr.Zero;
            }

            upem = (int)ut_hb_face_get_upem(hbFace);

            IntPtr font = ut_hb_font_create(hbFace);
            if (font == IntPtr.Zero)
            {
                ut_hb_face_destroy(hbFace);
                ut_hb_blob_destroy(blob);
                hbFace = IntPtr.Zero;
                blob = IntPtr.Zero;
                return IntPtr.Zero;
            }

            ut_hb_ot_font_set_funcs(font);
            return font;
        }

        public static void DestroyFont(IntPtr font, IntPtr blob, IntPtr hbFace)
        {
            if (font == IntPtr.Zero) return;

            ut_hb_font_destroy(font);
            if (hbFace != IntPtr.Zero) ut_hb_face_destroy(hbFace);
            if (blob != IntPtr.Zero) ut_hb_blob_destroy(blob);
        }

        public static int GetGlyphAdvance(IntPtr font, uint glyphIndex)
        {
            if (font == IntPtr.Zero) return 0;
            return ut_hb_font_get_glyph_h_advance(font, glyphIndex);
        }

        public static bool TryGetGlyph(IntPtr font, uint codepoint, out uint glyphIndex)
        {
            glyphIndex = 0;
            if (font == IntPtr.Zero) return false;
            return ut_hb_font_get_glyph(font, codepoint, 0, out glyphIndex);
        }

        #endregion

        #region Unified Buffer API

        public static IntPtr CreateBuffer()
        {
            return ut_hb_buffer_create();
        }

        public static void DestroyBuffer(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_destroy(buffer);
        }

        public static void ClearBuffer(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_clear_contents(buffer);
        }

        public static void SetDirection(IntPtr buffer, int direction)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_direction(buffer, direction);
        }

        public static void SetScript(IntPtr buffer, uint script)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_script(buffer, script);
        }

        public static void SetFlags(IntPtr buffer, uint flags)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_flags(buffer, flags);
        }

        /// <summary>
        /// Adds codepoints to the buffer with context support for cross-run shaping.
        /// </summary>
        /// <param name="buffer">HarfBuzz buffer.</param>
        /// <param name="codepoints">Full text as context.</param>
        /// <param name="itemOffset">Start of the item to shape within codepoints.</param>
        /// <param name="itemLength">Length of the item to shape.</param>
        /// <remarks>
        /// Per HarfBuzz documentation: "When shaping part of a larger text, pass the whole
        /// paragraph and specify item_offset and item_length to enable cross-run Arabic shaping
        /// and proper handling of combining marks at run boundaries."
        /// </remarks>
        public static void AddCodepoints(IntPtr buffer, ReadOnlySpan<int> codepoints, int itemOffset, int itemLength)
        {
            if (buffer == IntPtr.Zero || codepoints.Length == 0) return;

            fixed (int* ptr = codepoints)
            {
                ut_hb_buffer_add_codepoints(buffer, (uint*)ptr, codepoints.Length, (uint)itemOffset, itemLength);
            }
        }

        public static void Shape(IntPtr font, IntPtr buffer)
        {
            if (font == IntPtr.Zero || buffer == IntPtr.Zero) return;
            ut_hb_shape(font, buffer, IntPtr.Zero, 0);
        }

        public static int GetGlyphCount(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero) return 0;
            return (int)ut_hb_buffer_get_length(buffer);
        }

        public static ReadOnlySpan<GlyphInfo> GetGlyphInfos(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero)
                return ReadOnlySpan<GlyphInfo>.Empty;

            hb_glyph_info_t* infos = ut_hb_buffer_get_glyph_infos(buffer, out uint infoCount);
            hb_glyph_position_t* positions = ut_hb_buffer_get_glyph_positions(buffer, out uint posCount);

            int count = (int)Math.Min(infoCount, posCount);
            if (count == 0)
                return ReadOnlySpan<GlyphInfo>.Empty;

            if (glyphInfoBuffer == null || glyphInfoBuffer.Length < count)
                glyphInfoBuffer = new GlyphInfo[Math.Max(count, 256)];

            for (int i = 0; i < count; i++)
            {
                glyphInfoBuffer[i] = new GlyphInfo
                {
                    glyphId = infos[i].codepoint,
                    cluster = infos[i].cluster,
                    xAdvance = positions[i].x_advance,
                    yAdvance = positions[i].y_advance,
                    xOffset = positions[i].x_offset,
                    yOffset = positions[i].y_offset
                };
            }
            return glyphInfoBuffer.AsSpan(0, count);
        }

        #endregion
    }

}
