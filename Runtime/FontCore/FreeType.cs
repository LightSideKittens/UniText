using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// High-level FreeType API wrapper for font loading and glyph rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides a managed interface to FreeType for rendering color emoji
    /// and bitmap fonts that Unity's FontEngine doesn't support natively.
    /// </para>
    /// <para>
    /// Supports CBDT/CBLC (Google), SBIX (Apple), and SVG emoji formats.
    /// </para>
    /// </remarks>
    /// <seealso cref="FT"/>
    /// <seealso cref="EmojiFont"/>
    internal static class FreeType
    {
        /// <summary>
        /// Information about a loaded font face.
        /// </summary>
        public struct FaceInfo
        {
            /// <summary>True if the info was retrieved successfully.</summary>
            public bool isValid;
            /// <summary>True if the font contains color glyph data.</summary>
            public bool hasColor;
            /// <summary>True if the font contains fixed-size bitmaps.</summary>
            public bool hasFixedSizes;
            /// <summary>True if the font contains SVG glyph data.</summary>
            public bool hasSVG;
            /// <summary>True if the font uses Apple SBIX format.</summary>
            public bool hasSbix;
            /// <summary>True if SBIX has overlay flag.</summary>
            public bool hasSbixOverlay;
            /// <summary>True if the font is scalable (outline).</summary>
            public bool isScalable;
            /// <summary>True if the font is SFNT-based (TrueType/OpenType).</summary>
            public bool isSfnt;
            /// <summary>Number of fixed bitmap sizes available.</summary>
            public int numFixedSizes;
            /// <summary>Available fixed sizes in pixels.</summary>
            public int[] availableSizes;
            /// <summary>Font design units per em.</summary>
            public int unitsPerEm;
            /// <summary>Number of glyphs in the font.</summary>
            public int numGlyphs;
            /// <summary>Number of faces in the font file.</summary>
            public int numFaces;
            /// <summary>Currently loaded face index.</summary>
            public int faceIndex;
            /// <summary>Font family name.</summary>
            public string familyName;
            /// <summary>Font style name (Regular, Bold, etc.).</summary>
            public string styleName;
            /// <summary>Raw FreeType face flags.</summary>
            public long faceFlags;
        }

        /// <summary>
        /// Result of rendering a glyph to a bitmap.
        /// </summary>
        public struct RenderedGlyph
        {
            /// <summary>True if rendering succeeded.</summary>
            public bool isValid;
            /// <summary>Bitmap width in pixels.</summary>
            public int width;
            /// <summary>Bitmap height in pixels.</summary>
            public int height;
            /// <summary>Horizontal bearing (offset from origin).</summary>
            public int bearingX;
            /// <summary>Vertical bearing (offset from baseline).</summary>
            public int bearingY;
            /// <summary>Horizontal advance to next glyph.</summary>
            public float advanceX;
            /// <summary>Vertical advance (for vertical text).</summary>
            public float advanceY;
            /// <summary>RGBA pixel data (4 bytes per pixel).</summary>
            public byte[] rgbaPixels;
            /// <summary>True if pixel data is in BGRA order instead of RGBA.</summary>
            public bool isBGRA;
        }

        private static IntPtr currentFace;
        private static byte[] currentFontData;
        private static int currentPixelSize;
        private static int currentFaceIndex;
        private static int[] cachedFixedSizes;

        /// <summary>Initializes the FreeType library.</summary>
        /// <returns>True if initialization succeeded.</returns>
        public static bool Initialize()
        {
            return FT.Initialize();
        }

        /// <summary>Gets the number of faces in a font file.</summary>
        /// <param name="fontData">Font file data.</param>
        /// <returns>Number of faces, or 0 on error.</returns>
        public static int GetNumFaces(byte[] fontData)
        {
            if (!FT.Initialize() || fontData == null || fontData.Length == 0)
                return 0;

            IntPtr tempFace = FT.LoadFace(fontData, 0);
            if (tempFace == IntPtr.Zero)
                return 0;

            var info = FT.GetFaceInfo(tempFace);
            int numFaces = info.numFaces;

            FT.UnloadFace(tempFace);
            return numFaces;
        }


    #if !UNITY_WEBGL || UNITY_EDITOR
        public static int GetNumFaces(string fontPath)
        {
            var fontData = System.IO.File.ReadAllBytes(fontPath);
            return GetNumFaces(fontData);
        }

        public static bool LoadFontFromPath(string fontPath, int faceIndex = 0)
        {
            var fontData = System.IO.File.ReadAllBytes(fontPath);
            return LoadFontFromData(fontData, faceIndex);
        }
    #endif

        /// <summary>Loads a font from raw font file data.</summary>
        /// <param name="fontData">TTF/OTF font file bytes.</param>
        /// <param name="faceIndex">Face index for multi-face fonts.</param>
        /// <returns>True if the font was loaded successfully.</returns>
        public static bool LoadFontFromData(byte[] fontData, int faceIndex = 0)
        {
            if (!FT.Initialize())
                return false;

            DisposeFace();

            if (fontData == null || fontData.Length == 0)
            {
                Debug.LogError("[FreeType] Font data is empty");
                return false;
            }

            currentFontData = fontData;
            currentFace = FT.LoadFace(fontData, faceIndex);

            if (currentFace == IntPtr.Zero)
            {
                currentFontData = null;
                Debug.LogError("[FreeType] Failed to load font");
                return false;
            }

            currentFaceIndex = faceIndex;
            currentPixelSize = 0;
            cachedFixedSizes = null;

            return true;
        }

        /// <summary>Gets information about the currently loaded font face.</summary>
        /// <returns>Face information, with isValid=false if no font is loaded.</returns>
        public static FaceInfo GetFaceInfo()
        {
            var info = new FaceInfo { isValid = false };
            if (currentFace == IntPtr.Zero) return info;

            var ftInfo = FT.GetFaceInfo(currentFace);

            info.isValid = true;
            info.numGlyphs = ftInfo.numGlyphs;
            info.numFaces = ftInfo.numFaces;
            info.faceIndex = currentFaceIndex;
            info.unitsPerEm = ftInfo.unitsPerEm > 0 ? ftInfo.unitsPerEm : 1000;
            info.faceFlags = ftInfo.faceFlags;

            info.familyName = "Unknown";
            info.styleName = "";

            info.numFixedSizes = ftInfo.numFixedSizes;
            info.hasFixedSizes = info.numFixedSizes > 0;

            if (info.hasFixedSizes)
            {
                if (cachedFixedSizes == null)
                    cachedFixedSizes = FT.GetAllFixedSizes(currentFace);
                info.availableSizes = cachedFixedSizes;
            }

            info.hasColor = ftInfo.HasColor;
            info.hasSVG = ftInfo.HasSVG;
            info.hasSbix = ftInfo.HasSbix;
            info.hasSbixOverlay = false;
            info.isScalable = ftInfo.IsScalable;
            info.isSfnt = (ftInfo.faceFlags & FT.FACE_FLAG_SFNT) != 0;

            return info;
        }

        public static string GetColorFormatDescription(FaceInfo info)
        {
            var formats = new System.Collections.Generic.List<string>();

            if (info.hasSVG)
                formats.Add("SVG (requires external renderer)");

            if (info.hasSbix)
                formats.Add("SBIX (Apple PNG bitmaps)");

            if (info.hasColor && !info.hasSVG && !info.hasSbix)
            {
                if (info.hasFixedSizes)
                    formats.Add("CBDT/CBLC (Google PNG bitmaps)");
                else
                    formats.Add("COLR/CPAL (vector layers)");
            }

            if (formats.Count == 0)
            {
                if (info.hasFixedSizes)
                    formats.Add("Bitmap (no color)");
                else
                    formats.Add("Outline only (no color)");
            }

            return string.Join(" + ", formats);
        }

        /// <summary>Gets the glyph index for a Unicode codepoint.</summary>
        /// <param name="codepoint">Unicode codepoint.</param>
        /// <returns>Glyph index, or 0 if not found.</returns>
        public static uint GetGlyphIndex(uint codepoint)
        {
            if (currentFace == IntPtr.Zero) return 0;
            return FT.GetCharIndex(currentFace, codepoint);
        }

        /// <summary>Checks if the current font contains a glyph for the codepoint.</summary>
        public static bool HasGlyph(uint codepoint)
        {
            return GetGlyphIndex(codepoint) != 0;
        }

        /// <summary>Sets the rendering pixel size for the current font.</summary>
        /// <param name="size">Pixel size.</param>
        /// <returns>True if the size was set successfully.</returns>
        public static bool SetPixelSize(int size)
        {
            if (currentFace == IntPtr.Zero) return false;
            if (currentPixelSize == size) return true;

            var info = GetFaceInfo();

            if (info.hasFixedSizes && info.availableSizes != null && info.availableSizes.Length > 0)
            {
                int bestIndex = 0;
                int bestDiff = int.MaxValue;

                for (int i = 0; i < info.numFixedSizes; i++)
                {
                    int diff = Math.Abs(info.availableSizes[i] - size);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestIndex = i;
                    }
                }

                if (!FT.SelectFixedSize(currentFace, bestIndex))
                {
                    Debug.LogError("[FreeType] SelectSize failed");
                    return false;
                }

                currentPixelSize = info.availableSizes[bestIndex];
            }
            else
            {
                if (!FT.SetPixelSize(currentFace, size))
                {
                    Debug.LogError("[FreeType] SetPixelSizes failed");
                    return false;
                }

                currentPixelSize = size;
            }

            return true;
        }

        /// <summary>Gets the current rendering pixel size.</summary>
        public static int GetCurrentPixelSize() => currentPixelSize;

    #if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>Attempts to render a glyph to RGBA pixels.</summary>
        /// <param name="glyphIndex">Glyph index to render.</param>
        /// <param name="targetSize">Target pixel size.</param>
        /// <param name="result">Rendered glyph data.</param>
        /// <returns>True if rendering succeeded.</returns>
        public static bool TryRenderGlyph(uint glyphIndex, int targetSize, out RenderedGlyph result)
        {
            return TryRenderGlyph(glyphIndex, targetSize, out result, out _);
        }

        /// <summary>Attempts to render a glyph with detailed failure information.</summary>
        public static bool TryRenderGlyph(uint glyphIndex, int targetSize, out RenderedGlyph result, out string failReason)
        {
            result = new RenderedGlyph { isValid = false };
            failReason = null;

            if (currentFace == IntPtr.Zero)
            {
                failReason = "No face loaded";
                return false;
            }

            if (!SetPixelSize(targetSize))
            {
                failReason = "Failed to set pixel size";
                return false;
            }

            bool loaded = FT.LoadGlyph(currentFace, glyphIndex, FT.LOAD_COLOR | FT.LOAD_RENDER);
            if (!loaded)
            {
                loaded = FT.LoadGlyph(currentFace, glyphIndex, FT.LOAD_RENDER);
                if (!loaded)
                {
                    loaded = FT.LoadGlyph(currentFace, glyphIndex, FT.LOAD_DEFAULT);
                    if (!loaded)
                    {
                        var info = GetFaceInfo();
                        failReason = "FT_Load_Glyph failed";

                        if (info.hasSbix)
                            failReason += "\n  → SBIX format detected! Ensure WASM build includes libpng.";
                        else if (info.hasSVG)
                            failReason += "\n  → SVG format detected. FreeType needs external SVG renderer.";

                        return false;
                    }

                    if (!FT.RenderGlyph(currentFace))
                    {
                        failReason = "FT_Render_Glyph failed";
                        return false;
                    }
                }
            }

            var metrics = FT.GetGlyphMetrics(currentFace);
            var bitmap = FT.GetBitmapData(currentFace);

            if (bitmap.width <= 0 || bitmap.height <= 0)
            {
                failReason = "Glyph has zero dimensions";
                return false;
            }

            byte[] pixels = FT.GetBitmapRGBA(currentFace, out _);
            if (pixels == null)
            {
                failReason = "Failed to get bitmap data";
                return false;
            }

            result = new RenderedGlyph
            {
                isValid = true,
                width = bitmap.width,
                height = bitmap.height,
                bearingX = metrics.bearingX,
                bearingY = FT.GetBitmapTop(currentFace),
                advanceX = metrics.advanceX / 64f,
                advanceY = metrics.advanceY / 64f,
                rgbaPixels = pixels
            };

            return true;
        }

        /// <summary>Renders a Unicode codepoint directly.</summary>
        /// <param name="codepoint">Unicode codepoint to render.</param>
        /// <param name="targetSize">Target pixel size.</param>
        /// <param name="result">Rendered glyph data.</param>
        /// <returns>True if rendering succeeded.</returns>
        public static bool TryRenderCodepoint(uint codepoint, int targetSize, out RenderedGlyph result)
        {
            result = new RenderedGlyph { isValid = false };

            var glyphIndex = GetGlyphIndex(codepoint);
            return TryRenderGlyph(glyphIndex, targetSize, out result);
        }
    #endif

        private static void DisposeFace()
        {
            if (currentFace != IntPtr.Zero)
            {
                FT.UnloadFace(currentFace);
                currentFace = IntPtr.Zero;
            }

            currentFontData = null;
            currentPixelSize = 0;
            currentFaceIndex = 0;
            cachedFixedSizes = null;
        }

    #if UNITY_EDITOR
        static FreeType() => Reseter.UnmanagedCleaning += Dispose;
    #endif

        /// <summary>Disposes the current font face and releases resources.</summary>
        private static void Dispose()
        {
            DisposeFace();
        }

        public static IntPtr GetCurrentFacePtr() => currentFace;
    }

}
