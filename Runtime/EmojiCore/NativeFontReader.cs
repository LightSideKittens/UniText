#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// Native iOS font reader and emoji renderer using Core Text/Core Graphics API.
    /// Required because:
    /// 1. iOS sandbox blocks direct access to system fonts
    /// 2. Apple Color Emoji uses proprietary 'emjc' format (LZFSE compression)
    ///    which FreeType cannot decode - must use Core Text for rendering
    /// </summary>
    internal static class NativeFontReader
    {

        [DllImport("__Internal")]
        private static extern int UniText_GetEmojiFontData(out IntPtr outData);

        [DllImport("__Internal")]
        private static extern int UniText_IsEmojiFontAvailable();

        [DllImport("__Internal")]
        private static extern void UniText_FreeBuffer(IntPtr buffer);

        /// <summary>
        /// Checks if the system emoji font is available.
        /// </summary>
        public static bool IsEmojiFontAvailable()
        {
            return UniText_IsEmojiFontAvailable() != 0;
        }

        /// <summary>
        /// Gets the system emoji font data using Core Text API.
        /// Used for FreeType path (non-sbix fonts).
        /// </summary>
        /// <returns>Font data as byte array, or null if unavailable.</returns>
        public static byte[] GetEmojiFontData()
        {
            int length = UniText_GetEmojiFontData(out IntPtr dataPtr);

            if (length <= 0 || dataPtr == IntPtr.Zero)
            {
                Cat.Meow("[NativeFontReader] Emoji font not available");
                return null;
            }

            try
            {
                byte[] data = new byte[length];
                Marshal.Copy(dataPtr, data, 0, length);
                Cat.Meow($"[NativeFontReader] Loaded emoji font: {length} bytes");
                return data;
            }
            finally
            {
                UniText_FreeBuffer(dataPtr);
            }
        }

        [DllImport("__Internal")]
        private static extern int UniText_RenderEmojiGlyph(
            ushort glyphIndex,
            int pixelSize,
            out IntPtr outPixels,
            out int outWidth,
            out int outHeight,
            out int outBearingX,
            out int outBearingY,
            out float outAdvance
        );

        /// <summary>
        /// Renders a single emoji glyph using Core Text.
        /// Thread-safe: native code uses thread-local rendering context.
        /// </summary>
        /// <param name="glyphIndex">Glyph index in Apple Color Emoji font</param>
        /// <param name="pixelSize">Desired pixel size</param>
        /// <param name="result">Rendered glyph data with RGBA pixels</param>
        /// <returns>True if rendering succeeded</returns>
        public static bool TryRenderEmojiGlyph(
            uint glyphIndex,
            int pixelSize,
            out FreeType.RenderedGlyph result)
        {
            result = default;

            if (glyphIndex == 0 || glyphIndex > ushort.MaxValue || pixelSize <= 0)
                return false;

            int success = UniText_RenderEmojiGlyph(
                (ushort)glyphIndex,
                pixelSize,
                out IntPtr pixels,
                out int width,
                out int height,
                out int bearingX,
                out int bearingY,
                out float advance
            );

            if (success == 0)
                return false;

            if (pixels == IntPtr.Zero || width == 0 || height == 0)
            {
                result = new FreeType.RenderedGlyph
                {
                    isValid = true,
                    width = 0,
                    height = 0,
                    bearingX = 0,
                    bearingY = 0,
                    advanceX = advance,
                    advanceY = 0,
                    rgbaPixels = null,
                    isBGRA = false
                };
                return true;
            }

            int size = width * height * 4;
            byte[] rgbaPixels = UniTextArrayPool<byte>.Rent(size);

            try
            {
                Marshal.Copy(pixels, rgbaPixels, 0, size);
            }
            finally
            {
                UniText_FreeBuffer(pixels);
            }

            result = new FreeType.RenderedGlyph
            {
                isValid = true,
                width = width,
                height = height,
                bearingX = bearingX,
                bearingY = bearingY,
                advanceX = advance,
                advanceY = 0,
                rgbaPixels = rgbaPixels,
                isBGRA = false  
            };

            return true;
        }
    }
}
#endif
