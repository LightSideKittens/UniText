using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using static System.Runtime.InteropServices.CallingConvention;

namespace LightSide
{
    internal static unsafe class FT
    {
        #region Enums & Constants

        public const int FACE_FLAG_SCALABLE = 1 << 0;
        public const int FACE_FLAG_FIXED_SIZES = 1 << 1;
        public const int FACE_FLAG_FIXED_WIDTH = 1 << 2;
        public const int FACE_FLAG_SFNT = 1 << 3;
        public const int FACE_FLAG_HORIZONTAL = 1 << 4;
        public const int FACE_FLAG_VERTICAL = 1 << 5;
        public const int FACE_FLAG_KERNING = 1 << 6;
        public const int FACE_FLAG_GLYPH_NAMES = 1 << 9;
        public const int FACE_FLAG_COLOR = 1 << 14;
        public const int FACE_FLAG_SVG = 1 << 16;
        public const int FACE_FLAG_SBIX = 1 << 17;

        public const int LOAD_DEFAULT = 0;
        public const int LOAD_NO_SCALE = 1 << 0;
        public const int LOAD_NO_HINTING = 1 << 1;
        public const int LOAD_RENDER = 1 << 2;
        public const int LOAD_NO_BITMAP = 1 << 3;
        public const int LOAD_FORCE_AUTOHINT = 1 << 5;
        public const int LOAD_MONOCHROME = 1 << 12;
        public const int LOAD_NO_AUTOHINT = 1 << 15;
        public const int LOAD_COLOR = 1 << 20;

        public const int RENDER_MODE_NORMAL = 0;
        public const int RENDER_MODE_LIGHT = 1;
        public const int RENDER_MODE_MONO = 2;
        public const int RENDER_MODE_LCD = 3;
        public const int RENDER_MODE_SDF = 5;

        public const int PIXEL_MODE_NONE = 0;
        public const int PIXEL_MODE_MONO = 1;
        public const int PIXEL_MODE_GRAY = 2;
        public const int PIXEL_MODE_BGRA = 7;

        public const int FT_COLR_PAINTFORMAT_COLR_LAYERS = 1;
        public const int FT_COLR_PAINTFORMAT_SOLID = 2;
        public const int FT_COLR_PAINTFORMAT_LINEAR_GRADIENT = 4;
        public const int FT_COLR_PAINTFORMAT_RADIAL_GRADIENT = 6;
        public const int FT_COLR_PAINTFORMAT_SWEEP_GRADIENT = 8;
        public const int FT_COLR_PAINTFORMAT_GLYPH = 10;
        public const int FT_COLR_PAINTFORMAT_COLR_GLYPH = 11;
        public const int FT_COLR_PAINTFORMAT_TRANSFORM = 12;
        public const int FT_COLR_PAINTFORMAT_TRANSLATE = 14;
        public const int FT_COLR_PAINTFORMAT_SCALE = 16;
        public const int FT_COLR_PAINTFORMAT_ROTATE = 24;
        public const int FT_COLR_PAINTFORMAT_SKEW = 28;
        public const int FT_COLR_PAINTFORMAT_COMPOSITE = 32;

        public const int FT_COLR_PAINT_EXTEND_PAD = 0;
        public const int FT_COLR_PAINT_EXTEND_REPEAT = 1;
        public const int FT_COLR_PAINT_EXTEND_REFLECT = 2;

        public const int FT_COLR_COMPOSITE_CLEAR = 0;
        public const int FT_COLR_COMPOSITE_SRC = 1;
        public const int FT_COLR_COMPOSITE_DEST = 2;
        public const int FT_COLR_COMPOSITE_SRC_OVER = 3;
        public const int FT_COLR_COMPOSITE_DEST_OVER = 4;
        public const int FT_COLR_COMPOSITE_SRC_IN = 5;
        public const int FT_COLR_COMPOSITE_DEST_IN = 6;
        public const int FT_COLR_COMPOSITE_SRC_OUT = 7;
        public const int FT_COLR_COMPOSITE_DEST_OUT = 8;
        public const int FT_COLR_COMPOSITE_SRC_ATOP = 9;
        public const int FT_COLR_COMPOSITE_DEST_ATOP = 10;
        public const int FT_COLR_COMPOSITE_XOR = 11;
        public const int FT_COLR_COMPOSITE_PLUS = 12;
        public const int FT_COLR_COMPOSITE_SCREEN = 13;
        public const int FT_COLR_COMPOSITE_OVERLAY = 14;
        public const int FT_COLR_COMPOSITE_DARKEN = 15;
        public const int FT_COLR_COMPOSITE_LIGHTEN = 16;
        public const int FT_COLR_COMPOSITE_COLOR_DODGE = 17;
        public const int FT_COLR_COMPOSITE_COLOR_BURN = 18;
        public const int FT_COLR_COMPOSITE_HARD_LIGHT = 19;
        public const int FT_COLR_COMPOSITE_SOFT_LIGHT = 20;
        public const int FT_COLR_COMPOSITE_DIFFERENCE = 21;
        public const int FT_COLR_COMPOSITE_EXCLUSION = 22;
        public const int FT_COLR_COMPOSITE_MULTIPLY = 23;
        public const int FT_COLR_COMPOSITE_HSL_HUE = 24;
        public const int FT_COLR_COMPOSITE_HSL_SATURATION = 25;
        public const int FT_COLR_COMPOSITE_HSL_COLOR = 26;
        public const int FT_COLR_COMPOSITE_HSL_LUMINOSITY = 27;

        #endregion

        #region Structs

        public struct FaceInfo
        {
            public int numFaces;
            public int faceIndex;
            public long faceFlags;
            public int numGlyphs;
            public int unitsPerEm;
            public int numFixedSizes;
            public short ascender;
            public short descender;
            public short height;

            public bool IsScalable => (faceFlags & FACE_FLAG_SCALABLE) != 0;
            public bool HasFixedSizes => (faceFlags & FACE_FLAG_FIXED_SIZES) != 0;
            public bool HasColor => (faceFlags & FACE_FLAG_COLOR) != 0;
            public bool HasSVG => (faceFlags & FACE_FLAG_SVG) != 0;
            public bool HasSbix => (faceFlags & FACE_FLAG_SBIX) != 0;
        }

        public struct GlyphMetrics
        {
            public int width;
            public int height;
            public int bearingX;
            public int bearingY;
            public int advanceX;
            public int advanceY;
        }

        public struct BitmapData
        {
            public int width;
            public int height;
            public int pitch;
            public int pixelMode;
            public IntPtr buffer;
        }

        /// <summary>
        /// Combined result of load + render SDF glyph in one native call.
        /// Must match ut_sdf_glyph_result layout in unitext_native.cpp.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SdfGlyphResult
        {
            public int success;
            public int metricWidth;
            public int metricHeight;
            public int metricBearingX;
            public int metricBearingY;
            public int metricAdvanceX;
            public int bmpWidth;
            public int bmpHeight;
            public int bmpPitch;
            public int bitmapLeft;
            public int bitmapTop;
            public IntPtr bmpBuffer;
        }

        /// <summary>BGRA color (FT_Color)</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FT_Color
        {
            public byte blue;
            public byte green;
            public byte red;
            public byte alpha;

            public uint ToRGBA32() => ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>Opaque paint handle for iterating COLRv1 paint tree</summary>
        /// <remarks>
        /// C struct layout: FT_Byte* p (8 bytes) + FT_Bool (1 byte) + 7 bytes padding = 16 bytes total.
        /// Must use Pack=8 to match native alignment.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct FT_OpaquePaint
        {
            public IntPtr p;
            public byte insert_root_transform;
        }

        /// <summary>Color stop for gradients</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FT_ColorStop
        {
            public nint stop_offset;
            public ushort color_index;
            public nint alpha;
        }

        /// <summary>Color line (gradient stops and extend mode)</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FT_ColorLine
        {
            public int extend;
            public IntPtr color_stop_iterator;
        }

        /// <summary>Affine transform matrix</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FT_Affine23
        {
            public nint xx, xy, dx;
            public nint yx, yy, dy;
        }

        /// <summary>Clip box for COLRv1 (coordinates in 26.6 fixed point)</summary>
        public struct FT_ClipBox
        {
            public int bottom_left_x, bottom_left_y;
            public int top_left_x, top_left_y;
            public int top_right_x, top_right_y;
            public int bottom_right_x, bottom_right_y;
        }

        /// <summary>Palette data info</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FT_Palette_Data
        {
            public ushort num_palettes;
            public IntPtr palette_name_ids;
            public IntPtr palette_flags;
            public ushort num_palette_entries;
            public IntPtr palette_entry_name_ids;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintColrLayers
        {
            public IntPtr layer_iterator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintSolid
        {
            public ushort color_index;
            public nint alpha;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintLinearGradient
        {
            public FT_ColorLine colorline;
            public nint p0_x, p0_y;
            public nint p1_x, p1_y;
            public nint p2_x, p2_y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintRadialGradient
        {
            public FT_ColorLine colorline;
            public nint c0_x, c0_y;
            public nint r0;
            public nint c1_x, c1_y;
            public nint r1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintSweepGradient
        {
            public FT_ColorLine colorline;
            public nint center_x, center_y;
            public nint start_angle;
            public nint end_angle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintGlyph
        {
            public FT_OpaquePaint paint;
            public uint glyph_id;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintColrGlyph
        {
            public uint glyph_id;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintTransform
        {
            public FT_OpaquePaint paint;
            public FT_Affine23 affine;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintTranslate
        {
            public FT_OpaquePaint paint;
            public nint dx, dy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintScale
        {
            public FT_OpaquePaint paint;
            public nint scale_x, scale_y;
            public nint center_x, center_y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintRotate
        {
            public FT_OpaquePaint paint;
            public nint angle;
            public nint center_x, center_y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintSkew
        {
            public FT_OpaquePaint paint;
            public nint x_skew_angle, y_skew_angle;
            public nint center_x, center_y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FT_PaintComposite
        {
            public FT_OpaquePaint source_paint;
            public int composite_mode;
            public FT_OpaquePaint backdrop_paint;
        }

        /// <summary>Layer iterator for COLRv0/v1</summary>
        /// <remarks>
        /// C struct: FT_UInt num_layers (4) + FT_UInt layer (4) + FT_Byte* p (8) = 16 bytes
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct FT_LayerIterator
        {
            public uint num_layers;
            public uint layer;
            public IntPtr p;
        }

        /// <summary>Color stop iterator</summary>
        /// <remarks>
        /// C struct: FT_UInt (4) + FT_UInt (4) + FT_Byte* (8) + FT_Bool (1) + padding (7) = 24 bytes
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct FT_ColorStopIterator
        {
            public uint num_color_stops;
            public uint current_color_stop;
            public IntPtr p;
            public byte read_variable;
        }

        #endregion

        #region State

        private static bool initialized;
        private static IntPtr library;
        private static readonly ConcurrentDictionary<IntPtr, GCHandle> pinnedFontData = new ConcurrentDictionary<IntPtr, GCHandle>();

        [ThreadStatic] private static byte[] pixelBuffer;
        private const int MinPixelBufferSize = 256 * 256 * 4;

        public static bool IsInitialized => initialized;

        #endregion

        #region Platform-specific bindings

    #if (UNITY_IOS || UNITY_TVOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
    #else
        private const string LibraryName = "unitext_native";
    #endif
        

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_init(out IntPtr library);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_done(IntPtr library);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_new_memory_face(IntPtr library, byte* fileBase, nint fileSize, nint faceIndex, out IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_done_face(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_ft_get_char_index(IntPtr face, nuint charcode);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_set_pixel_sizes(IntPtr face, uint pixelWidth, uint pixelHeight);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_select_size(IntPtr face, int strikeIndex);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_load_glyph(IntPtr face, uint glyphIndex, int loadFlags);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_render_glyph(IntPtr slot, int renderMode);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_palette_data_get(IntPtr face, out FT_Palette_Data paletteData);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_palette_select(IntPtr face, ushort paletteIndex, out IntPtr palette);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_color_glyph_clipbox(IntPtr face, uint baseGlyph, out FT_ClipBox clipBox);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_color_glyph_layer(IntPtr face, uint baseGlyph, out uint glyphIndex, out uint colorIndex, ref FT_LayerIterator iterator);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_glyph_paint(IntPtr face, uint baseGlyph, int rootTransform, out IntPtr paintP, out int paintInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_debug_glyph_paint(IntPtr face, uint baseGlyph, out int hasColr, out int hasCpal, out int ftResult);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_outline_to_blpath(IntPtr face, IntPtr blPath);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_outline_info(IntPtr face, out int numContours, out int numPoints);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_format(IntPtr face, IntPtr paintP, int paintInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_solid(IntPtr face, IntPtr paintP, int paintInsert, out ushort colorIndex, out int alpha);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_layers(IntPtr face, IntPtr paintP, int paintInsert, out uint numLayers, out uint layer, out IntPtr iterP);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_next_layer(IntPtr face, ref uint numLayers, ref uint layer, ref IntPtr iterP, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_glyph(IntPtr face, IntPtr paintP, int paintInsert, out uint glyphId, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_colr_glyph(IntPtr face, IntPtr paintP, int paintInsert, out uint glyphId);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_translate(IntPtr face, IntPtr paintP, int paintInsert, out int dx, out int dy, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_scale(IntPtr face, IntPtr paintP, int paintInsert, out int scaleX, out int scaleY, out int centerX, out int centerY, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_rotate(IntPtr face, IntPtr paintP, int paintInsert, out int angle, out int centerX, out int centerY, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_skew(IntPtr face, IntPtr paintP, int paintInsert, out int xSkew, out int ySkew, out int centerX, out int centerY, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_transform(IntPtr face, IntPtr paintP, int paintInsert, out int xx, out int xy, out int dx, out int yx, out int yy, out int dy, out IntPtr childP, out int childInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_composite(IntPtr face, IntPtr paintP, int paintInsert, out int mode, out IntPtr backdropP, out int backdropInsert, out IntPtr sourceP, out int sourceInsert);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_linear_gradient(IntPtr face, IntPtr paintP, int paintInsert, out int p0x, out int p0y, out int p1x, out int p1y, out int p2x, out int p2y, out int extend, out uint numStops, out uint currentStop, out IntPtr stopIterP, out int readVar);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_radial_gradient(IntPtr face, IntPtr paintP, int paintInsert, out int c0x, out int c0y, out int r0, out int c1x, out int c1y, out int r1, out int extend, out uint numStops, out uint currentStop, out IntPtr stopIterP, out int readVar);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_paint_sweep_gradient(IntPtr face, IntPtr paintP, int paintInsert, out int cx, out int cy, out int startAngle, out int endAngle, out int extend, out uint numStops, out uint currentStop, out IntPtr stopIterP, out int readVar);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_colorstop(IntPtr face, ref uint numStops, ref uint currentStop, ref IntPtr iterP, ref int readVar, out int stopOffset, out ushort colorIndex, out int alpha);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_colr_get_clipbox(IntPtr face, uint baseGlyph, out int blX, out int blY, out int tlX, out int tlY, out int trX, out int trY, out int brX, out int brY);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_ft_get_face_info(IntPtr face, out long faceFlags, out int numGlyphs, out int unitsPerEm, out int numFixedSizes, out int numFaces, out int faceIndex, out short ascender, out short descender, out short height);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_extended_face_info(IntPtr face, out short capHeight, out short xHeight, out short superscriptYOffset, out short superscriptYSize, out short subscriptYOffset, out short subscriptYSize, out short strikeoutPosition, out short strikeoutSize, out short underlinePosition, out short underlineThickness, out IntPtr familyName, out IntPtr styleName);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_fixed_size(IntPtr face, int index);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_ft_get_glyph_metrics(IntPtr face, out int width, out int height, out int bearingX, out int bearingY, out int advanceX, out int advanceY);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_ft_get_bitmap_info(IntPtr face, out int width, out int height, out int pitch, out int pixelMode, out IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_ft_get_glyph_slot(IntPtr face);
    #if !UNITY_WEBGL || UNITY_EDITOR
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_bitmap_top(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_get_bitmap_left(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_set_sdf_spread(IntPtr library, int spread);
    #endif
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_ft_render_sdf_glyph(IntPtr face, uint glyphIndex, int loadFlags, int spread, out SdfGlyphResult result);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_ft_free_sdf_buffer(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_debug_sbix_graphic_type(IntPtr face, byte[] outGraphicType, out int outNumStrikes);

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_Vector { public nint x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_Bitmap
        {
            public uint rows, width;
            public int pitch;
            public byte* buffer;
            public ushort num_grays;
            public byte pixel_mode, palette_mode;
            public void* palette;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_Glyph_Metrics
        {
            public nint width, height;
            public nint horiBearingX, horiBearingY, horiAdvance;
            public nint vertBearingX, vertBearingY, vertAdvance;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_Bitmap_Size
        {
            public short height, width;
            public nint size, x_ppem, y_ppem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_Size_Metrics
        {
            public ushort x_ppem, y_ppem;
            public nint x_scale, y_scale;
            public nint ascender, descender, height, max_advance;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_SizeRec
        {
            public IntPtr face;
            public IntPtr generic_data, generic_finalizer;
            public FT_Size_Metrics metrics;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_BBox { public nint xMin, yMin, xMax, yMax; }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_GlyphSlotRec
        {
            public IntPtr library, face, next;
            public uint glyph_index;
            public IntPtr generic_data, generic_finalizer;
            public FT_Glyph_Metrics metrics;
            public nint linearHoriAdvance, linearVertAdvance;
            public FT_Vector advance;
            public uint format;
            public FT_Bitmap bitmap;
            public int bitmap_left, bitmap_top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FT_FaceRec
        {
            public nint num_faces, face_index, face_flags, style_flags, num_glyphs;
            public byte* family_name, style_name;
            public int num_fixed_sizes;
            public FT_Bitmap_Size* available_sizes;
            public int num_charmaps;
            public IntPtr charmaps, generic_data, generic_finalizer;
            public FT_BBox bbox;
            public ushort units_per_EM;
            public short ascender, descender, height;
            public short max_advance_width, max_advance_height;
            public short underline_position, underline_thickness;
            public FT_GlyphSlotRec* glyph;
            public FT_SizeRec* size;
            public IntPtr charmap;
        }

        #endregion

        #region Unified API

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void AutoInitialize()
        {
            Initialize();
        }

        public static bool Initialize()
        {
            if (initialized) return true;

            initialized = ut_ft_init(out library) == 0;

            if (initialized)
            {
                Cat.Meow("[FT] Initialized");
            }
            else
            {
                Cat.MeowError("[FT] Initialization failed");
            }

            return initialized;
        }

    #if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>
        /// Sets the SDF spread (max distance in pixels) for both "sdf" and "bsdf" FreeType modules.
        /// Must be called after Initialize(). Default FreeType spread is 8.
        /// </summary>
        /// <param name="spread">Spread in pixels. Typical values: 4-32.</param>
        /// <returns>True if both modules accepted the value.</returns>
        public static bool SetSdfSpread(int spread)
        {
            if (!initialized || library == IntPtr.Zero) return false;
            return ut_ft_set_sdf_spread(library, spread) == 0;
        }
    #endif

    #if UNITY_EDITOR
        static FT()
        {
            Reseter.LibraryShutdown += Shutdown;
        }
    #endif

        
        private static void Shutdown()
        {
            Cat.Meow($"[FT] Shutdown called, initialized={initialized}, library={(library != IntPtr.Zero)}, pinnedData={pinnedFontData.Count}");
            if (!initialized) return;

            foreach (var handle in pinnedFontData.Values)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            pinnedFontData.Clear();

            if (library != IntPtr.Zero)
            {
                ut_ft_done(library);
                library = IntPtr.Zero;
            }
            initialized = false;
            Cat.Meow("[FT] Shutdown completed");
        }

        public static IntPtr LoadFace(byte[] fontData, int faceIndex = 0)
        {
            if (!initialized || fontData == null || fontData.Length == 0)
                return IntPtr.Zero;

            GCHandle handle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            byte* ptr = (byte*)handle.AddrOfPinnedObject();

            IntPtr face = IntPtr.Zero;
            if (ut_ft_new_memory_face(library, ptr, fontData.Length, faceIndex, out face) != 0)
                face = IntPtr.Zero;

            if (face != IntPtr.Zero)
            {
                pinnedFontData[face] = handle;
            }
            else
            {
                handle.Free();
            }

            return face;
        }

        private static int unloadedFacesCount;

        public static void UnloadFace(IntPtr face)
        {
            if (!initialized)
            {
                Debug.LogWarning($"[FT] UnloadFace skipped - not initialized! face={face}");
                return;
            }
            if (face == IntPtr.Zero)
            {
                Debug.LogWarning("[FT] UnloadFace skipped - face is zero");
                return;
            }

            ut_ft_done_face(face);

            if (pinnedFontData.TryRemove(face, out GCHandle handle))
            {
                handle.Free();
            }

            unloadedFacesCount++;
        }

        public static void ResetUnloadCounter()
        {
            var count = unloadedFacesCount;
            unloadedFacesCount = 0;
            Cat.Meow($"[FT] Total faces unloaded this cycle: {count}");
        }

        public static FaceInfo GetFaceInfo(IntPtr face)
        {
            if (!initialized || face == IntPtr.Zero)
                return default;

            ut_ft_get_face_info(face, out long faceFlags, out int numGlyphs, out int unitsPerEm, out int numFixedSizes, out int numFaces, out int faceIndex, out short ascender, out short descender, out short height);
            return new FaceInfo
            {
                numFaces = numFaces,
                faceIndex = faceIndex,
                faceFlags = faceFlags,
                numGlyphs = numGlyphs,
                unitsPerEm = unitsPerEm > 0 ? unitsPerEm : 1000,
                numFixedSizes = numFixedSizes,
                ascender = ascender,
                descender = descender,
                height = height
            };
        }

        /// <summary>
        /// Reads extended metrics: OS/2 table (cap height, x-height, super/subscript, strikeout),
        /// post table (underline), and name table (family/style names).
        /// </summary>
        public struct ExtendedFaceInfo
        {
            public string familyName, styleName;
            public short capHeight, xHeight;
            public short superscriptYOffset, superscriptYSize;
            public short subscriptYOffset, subscriptYSize;
            public short strikeoutPosition, strikeoutSize;
            public short underlinePosition, underlineThickness;
            public bool hasOS2;
        }

        public static ExtendedFaceInfo GetExtendedFaceInfo(IntPtr face)
        {
            var result = new ExtendedFaceInfo();
            if (!initialized || face == IntPtr.Zero) return result;

            int hasOS2 = ut_ft_get_extended_face_info(face,
                out result.capHeight, out result.xHeight,
                out result.superscriptYOffset, out result.superscriptYSize,
                out result.subscriptYOffset, out result.subscriptYSize,
                out result.strikeoutPosition, out result.strikeoutSize,
                out result.underlinePosition, out result.underlineThickness,
                out IntPtr familyPtr, out IntPtr stylePtr);

            result.hasOS2 = hasOS2 != 0;

            if (familyPtr != IntPtr.Zero)
                result.familyName = Marshal.PtrToStringAnsi(familyPtr);
            if (stylePtr != IntPtr.Zero)
                result.styleName = Marshal.PtrToStringAnsi(stylePtr);

            return result;
        }

        /// <summary>
        /// Loads a glyph by codepoint with LOAD_NO_SCALE and returns its bearingY in font design units.
        /// Used to derive capHeight (from 'H') and xHeight (from 'x') when OS/2 table is absent.
        /// </summary>
        public static int GetGlyphBearingYUnscaled(IntPtr face, uint codepoint)
        {
            if (!initialized || face == IntPtr.Zero) return 0;

            uint glyphIndex = ut_ft_get_char_index(face, codepoint);
            if (glyphIndex == 0) return 0;

            if (ut_ft_load_glyph(face, glyphIndex, LOAD_NO_SCALE) != 0) return 0;

            ut_ft_get_glyph_metrics(face, out _, out _, out _, out int bearingY, out _, out _);
            return bearingY;
        }

        /// <summary>
        /// Loads a glyph by codepoint with LOAD_NO_SCALE and returns its horizontal advance in font design units.
        /// </summary>
        public static int GetGlyphAdvanceUnscaled(IntPtr face, uint codepoint)
        {
            if (!initialized || face == IntPtr.Zero) return 0;

            uint glyphIndex = ut_ft_get_char_index(face, codepoint);
            if (glyphIndex == 0) return 0;

            if (ut_ft_load_glyph(face, glyphIndex, LOAD_NO_SCALE) != 0) return 0;

            ut_ft_get_glyph_metrics(face, out _, out _, out _, out _, out int advanceX, out _);
            return advanceX;
        }

        public static int GetFixedSize(IntPtr face, int index)
        {
            if (!initialized || face == IntPtr.Zero) return 0;

            return ut_ft_get_fixed_size(face, index);
        }

        public static int[] GetAllFixedSizes(IntPtr face)
        {
            var info = GetFaceInfo(face);
            if (info.numFixedSizes <= 0)
                return Array.Empty<int>();

            var sizes = new int[info.numFixedSizes];
            for (int i = 0; i < info.numFixedSizes; i++)
                sizes[i] = GetFixedSize(face, i);
            return sizes;
        }

        public static bool SetPixelSize(IntPtr face, int size)
        {
            if (!initialized || face == IntPtr.Zero) return false;

            return ut_ft_set_pixel_sizes(face, (uint)size, (uint)size) == 0;
        }

        public static bool SelectFixedSize(IntPtr face, int strikeIndex)
        {
            if (!initialized || face == IntPtr.Zero) return false;

            return ut_ft_select_size(face, strikeIndex) == 0;
        }

        /// <summary>Selects a fixed size and returns the FreeType error code.</summary>
        /// <returns>0 on success, FreeType error code otherwise.</returns>
        public static int SelectFixedSizeWithError(IntPtr face, int strikeIndex)
        {
            if (!initialized || face == IntPtr.Zero) return -1;
            return ut_ft_select_size(face, strikeIndex);
        }

        public static uint GetCharIndex(IntPtr face, uint codepoint)
        {
            if (!initialized || face == IntPtr.Zero) return 0;

            return ut_ft_get_char_index(face, codepoint);
        }

        public static bool LoadGlyph(IntPtr face, uint glyphIndex, int loadFlags = LOAD_DEFAULT)
        {
            if (!initialized || face == IntPtr.Zero) return false;

            return ut_ft_load_glyph(face, glyphIndex, loadFlags) == 0;
        }

        /// <summary>Loads a glyph and returns the FreeType error code.</summary>
        /// <returns>0 on success, FreeType error code otherwise.</returns>
        public static int LoadGlyphWithError(IntPtr face, uint glyphIndex, int loadFlags = LOAD_DEFAULT)
        {
            if (!initialized || face == IntPtr.Zero) return -1;
            return ut_ft_load_glyph(face, glyphIndex, loadFlags);
        }

        public static bool RenderGlyph(IntPtr face, int renderMode = RENDER_MODE_NORMAL)
        {
            if (!initialized || face == IntPtr.Zero) return false;

            IntPtr slot = ut_ft_get_glyph_slot(face);
            if (slot == IntPtr.Zero) return false;
            return ut_ft_render_glyph(slot, renderMode) == 0;
        }

        /// <summary>
        /// Loads, renders, and reads all glyph data in a single native call.
        /// Combines LoadGlyph + GetMetrics + RenderGlyph + GetBitmapData + GetBitmapLeft/Top.
        /// </summary>
        /// <summary>
        /// Loads glyph, renders as grayscale, computes SDF via EDT. Output buffer is malloc'd.
        /// Caller MUST call FreeSdfBuffer(result.bmpBuffer) after copying pixel data.
        /// </summary>
        public static bool RenderSdfGlyph(IntPtr face, uint glyphIndex, int loadFlags, int spread, out SdfGlyphResult result)
        {
            if (!initialized || face == IntPtr.Zero)
            {
                result = default;
                return false;
            }
            return ut_ft_render_sdf_glyph(face, glyphIndex, loadFlags, spread, out result) == 0;
        }

        /// <summary>Frees the SDF bitmap buffer returned by RenderSdfGlyph.</summary>
        public static void FreeSdfBuffer(IntPtr buffer)
        {
            if (buffer != IntPtr.Zero)
                ut_ft_free_sdf_buffer(buffer);
        }

        public static GlyphMetrics GetGlyphMetrics(IntPtr face)
        {
            if (!initialized || face == IntPtr.Zero)
                return default;

            ut_ft_get_glyph_metrics(face, out int width, out int height, out int bearingX, out int bearingY, out int advanceX, out int advanceY);
            return new GlyphMetrics
            {
                width = width,
                height = height,
                bearingX = bearingX,
                bearingY = bearingY,
                advanceX = advanceX,
                advanceY = advanceY
            };
        }

    #if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>
        /// Gets bitmap_top from the glyph slot after rendering.
        /// For sbix glyphs, this includes the glyf bbox.yMin correction that
        /// metrics.horiBearingY does not include (FreeType 2.12+ fix).
        /// </summary>
        public static int GetBitmapTop(IntPtr face)
        {
            if (!initialized || face == IntPtr.Zero) return 0;
            return ut_ft_get_bitmap_top(face);
        }

        /// <summary>
        /// Gets bitmap_left from the glyph slot after rendering.
        /// Horizontal distance from the pen position to the left edge of the bitmap.
        /// </summary>
        public static int GetBitmapLeft(IntPtr face)
        {
            if (!initialized || face == IntPtr.Zero) return 0;
            return ut_ft_get_bitmap_left(face);
        }
    #endif

        public static BitmapData GetBitmapData(IntPtr face)
        {
            if (!initialized || face == IntPtr.Zero)
                return default;

            ut_ft_get_bitmap_info(face, out int width, out int height, out int pitch, out int pixelMode, out IntPtr buffer);
            return new BitmapData
            {
                width = width,
                height = height,
                pitch = pitch,
                pixelMode = pixelMode,
                buffer = buffer
            };
        }

        public static byte[] GetBitmapRGBA(IntPtr face, out IntPtr nativePtr)
        {
            nativePtr = IntPtr.Zero;
            if (!initialized || face == IntPtr.Zero)
                return null;

            var bitmap = GetBitmapData(face);
            if (bitmap.width <= 0 || bitmap.height <= 0 || bitmap.buffer == IntPtr.Zero)
                return null;

            int w = bitmap.width;
            int h = bitmap.height;
            int requiredSize = w * h * 4;

            if (pixelBuffer == null || pixelBuffer.Length < requiredSize)
                pixelBuffer = new byte[Math.Max(requiredSize, MinPixelBufferSize)];

            byte* src = (byte*)bitmap.buffer;

            if (bitmap.pixelMode == PIXEL_MODE_BGRA)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = src + y * bitmap.pitch;
                    int dstOffset = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int srcIdx = x * 4;
                        int dstIdx = dstOffset + x * 4;
                        pixelBuffer[dstIdx + 0] = row[srcIdx + 2];
                        pixelBuffer[dstIdx + 1] = row[srcIdx + 1];
                        pixelBuffer[dstIdx + 2] = row[srcIdx + 0];
                        pixelBuffer[dstIdx + 3] = row[srcIdx + 3];
                    }
                }
            }
            else if (bitmap.pixelMode == PIXEL_MODE_GRAY)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = src + y * bitmap.pitch;
                    int dstOffset = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        byte gray = row[x];
                        int dstIdx = dstOffset + x * 4;
                        pixelBuffer[dstIdx + 0] = 255;
                        pixelBuffer[dstIdx + 1] = 255;
                        pixelBuffer[dstIdx + 2] = 255;
                        pixelBuffer[dstIdx + 3] = gray;
                    }
                }
            }
            else if (bitmap.pixelMode == PIXEL_MODE_MONO)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = src + y * bitmap.pitch;
                    int dstOffset = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        bool set = (row[x >> 3] & (0x80 >> (x & 7))) != 0;
                        byte val = set ? (byte)255 : (byte)0;
                        int dstIdx = dstOffset + x * 4;
                        pixelBuffer[dstIdx + 0] = 255;
                        pixelBuffer[dstIdx + 1] = 255;
                        pixelBuffer[dstIdx + 2] = 255;
                        pixelBuffer[dstIdx + 3] = val;
                    }
                }
            }

            return pixelBuffer;
        }

        #region COLRv1 API

        /// <summary>Check if font has COLRv1 data for a glyph</summary>
        public static bool HasColorGlyphPaint(IntPtr face, uint glyphIndex)
        {
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_colr_get_glyph_paint(face, glyphIndex, 1, out _, out _) != 0;
        }

        /// <summary>Debug: check COLR/CPAL tables and FT_Get_Color_Glyph_Paint result</summary>
        public static void DebugColorGlyphPaint(IntPtr face, uint glyphIndex, out bool hasColr, out bool hasCpal, out int ftResult)
        {
            hasColr = hasCpal = false;
            ftResult = 0;
            if (!initialized || face == IntPtr.Zero) return;
            ut_colr_debug_glyph_paint(face, glyphIndex, out int colr, out int cpal, out ftResult);
            hasColr = colr != 0;
            hasCpal = cpal != 0;
        }

        /// <summary>Get palette data from font</summary>
        public static bool GetPaletteData(IntPtr face, out FT_Palette_Data paletteData)
        {
            paletteData = default;
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_ft_palette_data_get(face, out paletteData) == 0;
        }

        /// <summary>Select a color palette and get the colors</summary>
        public static bool SelectPalette(IntPtr face, ushort paletteIndex, out IntPtr paletteColors)
        {
            paletteColors = IntPtr.Zero;
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_ft_palette_select(face, paletteIndex, out paletteColors) == 0;
        }

        /// <summary>Get COLRv0 layer info (older format)</summary>
        public static bool GetColorGlyphLayer(IntPtr face, uint baseGlyph, out uint glyphIndex, out uint colorIndex, ref FT_LayerIterator iterator)
        {
            glyphIndex = 0;
            colorIndex = 0;
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_ft_get_color_glyph_layer(face, baseGlyph, out glyphIndex, out colorIndex, ref iterator) != 0;
        }

        /// <summary>Get COLRv1 paint tree root</summary>
        public static bool GetColorGlyphPaint(IntPtr face, uint baseGlyph, bool rootTransform, out FT_OpaquePaint paint)
        {
            paint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_glyph_paint(face, baseGlyph, rootTransform ? 1 : 0, out IntPtr p, out int insert);
            if (result != 0)
            {
                paint.p = p;
                paint.insert_root_transform = (byte)insert;
                return true;
            }
            return false;
        }

        /// <summary>Get clip box for COLRv1 glyph (uses wrapper for platform-safe struct handling)</summary>
        public static bool GetColorGlyphClipBox(IntPtr face, uint baseGlyph, out FT_ClipBox clipBox)
        {
            clipBox = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_clipbox(face, baseGlyph,
                out clipBox.bottom_left_x, out clipBox.bottom_left_y,
                out clipBox.top_left_x, out clipBox.top_left_y,
                out clipBox.top_right_x, out clipBox.top_right_y,
                out clipBox.bottom_right_x, out clipBox.bottom_right_y);
            return result != 0;
        }

        /// <summary>Iterate through paint layers (uses primitives internally)</summary>
        public static bool GetPaintLayers(IntPtr face, ref FT_LayerIterator iterator, out FT_OpaquePaint paint)
        {
            paint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            uint numLayers = iterator.num_layers;
            uint layer = iterator.layer;
            IntPtr iterP = iterator.p;
            int result = ut_colr_get_next_layer(face, ref numLayers, ref layer, ref iterP, out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                iterator.num_layers = numLayers;
                iterator.layer = layer;
                iterator.p = iterP;
                paint.p = childP;
                paint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get palette color at index</summary>
        public static FT_Color GetPaletteColor(IntPtr paletteColors, int index)
        {
            if (paletteColors == IntPtr.Zero) return default;
            return Marshal.PtrToStructure<FT_Color>(paletteColors + index * 4);
        }

        /// <summary>Convert 16.16 fixed point to float</summary>
        public static float Fixed16Dot16ToFloat(nint value) => value / 65536f;

        /// <summary>Convert F2DOT14 fixed point to float</summary>
        public static float F2Dot14ToFloat(nint value) => value / 16384f;

        /// <summary>Convert FreeType outline directly to Blend2D path</summary>
        public static bool OutlineToBlendPath(IntPtr face, IntPtr blPath)
        {
            if (!initialized || face == IntPtr.Zero || blPath == IntPtr.Zero) return false;
            return ut_ft_outline_to_blpath(face, blPath) != 0;
        }

        /// <summary>Get outline info (number of contours and points)</summary>
        public static bool GetOutlineInfo(IntPtr face, out int numContours, out int numPoints)
        {
            numContours = numPoints = 0;
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_ft_get_outline_info(face, out numContours, out numPoints) != 0;
        }

        /// <summary>Get paint format from opaque paint</summary>
        public static int GetPaintFormat(IntPtr face, FT_OpaquePaint paint)
        {
            if (!initialized || face == IntPtr.Zero) return -1;
            return ut_colr_get_paint_format(face, paint.p, paint.insert_root_transform);
        }

        /// <summary>Get solid color paint data</summary>
        public static bool GetPaintSolid(IntPtr face, FT_OpaquePaint paint, out ushort colorIndex, out int alpha)
        {
            colorIndex = 0;
            alpha = 0;
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_colr_get_paint_solid(face, paint.p, paint.insert_root_transform, out colorIndex, out alpha) != 0;
        }

        /// <summary>Get COLR layers iterator</summary>
        public static bool GetPaintColrLayers(IntPtr face, FT_OpaquePaint paint, out FT_LayerIterator iterator)
        {
            iterator = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_layers(face, paint.p, paint.insert_root_transform,
                out uint numLayers, out uint layer, out IntPtr iterP);
            if (result != 0)
            {
                iterator.num_layers = numLayers;
                iterator.layer = layer;
                iterator.p = iterP;
                return true;
            }
            return false;
        }

        /// <summary>Get glyph paint data</summary>
        public static bool GetPaintGlyph(IntPtr face, FT_OpaquePaint paint, out uint glyphId, out FT_OpaquePaint childPaint)
        {
            glyphId = 0;
            childPaint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_glyph(face, paint.p, paint.insert_root_transform,
                out glyphId, out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                childPaint.p = childP;
                childPaint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get COLR glyph reference</summary>
        public static bool GetPaintColrGlyph(IntPtr face, FT_OpaquePaint paint, out uint glyphId)
        {
            glyphId = 0;
            if (!initialized || face == IntPtr.Zero) return false;
            return ut_colr_get_paint_colr_glyph(face, paint.p, paint.insert_root_transform, out glyphId) != 0;
        }

        /// <summary>Get translate paint data</summary>
        public static bool GetPaintTranslate(IntPtr face, FT_OpaquePaint paint, out int dx, out int dy, out FT_OpaquePaint childPaint)
        {
            dx = dy = 0;
            childPaint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_translate(face, paint.p, paint.insert_root_transform,
                out dx, out dy, out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                childPaint.p = childP;
                childPaint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get scale paint data</summary>
        public static bool GetPaintScale(IntPtr face, FT_OpaquePaint paint, out int scaleX, out int scaleY, out int centerX, out int centerY, out FT_OpaquePaint childPaint)
        {
            scaleX = scaleY = centerX = centerY = 0;
            childPaint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_scale(face, paint.p, paint.insert_root_transform,
                out scaleX, out scaleY, out centerX, out centerY, out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                childPaint.p = childP;
                childPaint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get rotate paint data</summary>
        public static bool GetPaintRotate(IntPtr face, FT_OpaquePaint paint, out int angle, out int centerX, out int centerY, out FT_OpaquePaint childPaint)
        {
            angle = centerX = centerY = 0;
            childPaint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_rotate(face, paint.p, paint.insert_root_transform,
                out angle, out centerX, out centerY, out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                childPaint.p = childP;
                childPaint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get skew paint data</summary>
        public static bool GetPaintSkew(IntPtr face, FT_OpaquePaint paint, out int xSkew, out int ySkew, out int centerX, out int centerY, out FT_OpaquePaint childPaint)
        {
            xSkew = ySkew = centerX = centerY = 0;
            childPaint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_skew(face, paint.p, paint.insert_root_transform,
                out xSkew, out ySkew, out centerX, out centerY, out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                childPaint.p = childP;
                childPaint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get transform paint data</summary>
        public static bool GetPaintTransform(IntPtr face, FT_OpaquePaint paint, out FT_Affine23 affine, out FT_OpaquePaint childPaint)
        {
            affine = default;
            childPaint = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_transform(face, paint.p, paint.insert_root_transform,
                out int xx, out int xy, out int dx, out int yx, out int yy, out int dy,
                out IntPtr childP, out int childInsert);
            if (result != 0)
            {
                affine.xx = xx;
                affine.xy = xy;
                affine.dx = dx;
                affine.yx = yx;
                affine.yy = yy;
                affine.dy = dy;
                childPaint.p = childP;
                childPaint.insert_root_transform = (byte)childInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get composite paint data</summary>
        public static bool GetPaintComposite(IntPtr face, FT_OpaquePaint paint, out int mode, out FT_OpaquePaint backdrop, out FT_OpaquePaint source)
        {
            mode = 0;
            backdrop = source = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_composite(face, paint.p, paint.insert_root_transform,
                out mode, out IntPtr backdropP, out int backdropInsert, out IntPtr sourceP, out int sourceInsert);
            if (result != 0)
            {
                backdrop.p = backdropP;
                backdrop.insert_root_transform = (byte)backdropInsert;
                source.p = sourceP;
                source.insert_root_transform = (byte)sourceInsert;
                return true;
            }
            return false;
        }

        /// <summary>Get linear gradient paint data</summary>
        public static bool GetPaintLinearGradient(IntPtr face, FT_OpaquePaint paint, out int p0x, out int p0y, out int p1x, out int p1y, out int p2x, out int p2y, out int extend, out FT_ColorStopIterator colorStops)
        {
            p0x = p0y = p1x = p1y = p2x = p2y = extend = 0;
            colorStops = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_linear_gradient(face, paint.p, paint.insert_root_transform,
                out p0x, out p0y, out p1x, out p1y, out p2x, out p2y, out extend,
                out uint numStops, out uint currentStop, out IntPtr iterP, out int readVar);
            if (result != 0)
            {
                colorStops.num_color_stops = numStops;
                colorStops.current_color_stop = currentStop;
                colorStops.p = iterP;
                colorStops.read_variable = (byte)readVar;
                return true;
            }
            return false;
        }

        /// <summary>Get radial gradient paint data</summary>
        public static bool GetPaintRadialGradient(IntPtr face, FT_OpaquePaint paint, out int c0x, out int c0y, out int r0, out int c1x, out int c1y, out int r1, out int extend, out FT_ColorStopIterator colorStops)
        {
            c0x = c0y = r0 = c1x = c1y = r1 = extend = 0;
            colorStops = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_radial_gradient(face, paint.p, paint.insert_root_transform,
                out c0x, out c0y, out r0, out c1x, out c1y, out r1, out extend,
                out uint numStops, out uint currentStop, out IntPtr iterP, out int readVar);
            if (result != 0)
            {
                colorStops.num_color_stops = numStops;
                colorStops.current_color_stop = currentStop;
                colorStops.p = iterP;
                colorStops.read_variable = (byte)readVar;
                return true;
            }
            return false;
        }

        /// <summary>Get sweep gradient paint data</summary>
        public static bool GetPaintSweepGradient(IntPtr face, FT_OpaquePaint paint, out int cx, out int cy, out int startAngle, out int endAngle, out int extend, out FT_ColorStopIterator colorStops)
        {
            cx = cy = startAngle = endAngle = extend = 0;
            colorStops = default;
            if (!initialized || face == IntPtr.Zero) return false;
            int result = ut_colr_get_paint_sweep_gradient(face, paint.p, paint.insert_root_transform,
                out cx, out cy, out startAngle, out endAngle, out extend,
                out uint numStops, out uint currentStop, out IntPtr iterP, out int readVar);
            if (result != 0)
            {
                colorStops.num_color_stops = numStops;
                colorStops.current_color_stop = currentStop;
                colorStops.p = iterP;
                colorStops.read_variable = (byte)readVar;
                return true;
            }
            return false;
        }

        /// <summary>Get next color stop</summary>
        public static bool GetColorStop(IntPtr face, ref FT_ColorStopIterator iterator, out int stopOffset, out ushort colorIndex, out int alpha)
        {
            stopOffset = 0;
            colorIndex = 0;
            alpha = 0;
            if (!initialized || face == IntPtr.Zero) return false;
            uint numStops = iterator.num_color_stops;
            uint currentStop = iterator.current_color_stop;
            IntPtr iterP = iterator.p;
            int readVar = iterator.read_variable;
            int result = ut_colr_get_colorstop(face, ref numStops, ref currentStop, ref iterP, ref readVar,
                out stopOffset, out colorIndex, out alpha);
            if (result != 0)
            {
                iterator.num_color_stops = numStops;
                iterator.current_color_stop = currentStop;
                iterator.p = iterP;
                iterator.read_variable = (byte)readVar;
                return true;
            }
            return false;
        }

        /// <summary>Convert 16.16 fixed point int to float</summary>
        public static float Fixed16Dot16ToFloat(int value) => value / 65536f;

        /// <summary>Convert F2DOT14 int to float</summary>
        public static float F2Dot14ToFloat(int value) => value / 16384f;

        #endregion

        public static unsafe bool CopyBitmapAsRGBA(IntPtr face, byte[] destBuffer)
        {
            if (!initialized || face == IntPtr.Zero)
                return false;

            var bitmap = GetBitmapData(face);
            if (bitmap.width <= 0 || bitmap.height <= 0 || bitmap.buffer == IntPtr.Zero)
                return false;

            int w = bitmap.width;
            int h = bitmap.height;

            byte* src = (byte*)bitmap.buffer;

            fixed (byte* dstPtr = destBuffer)
            {
                if (bitmap.pixelMode == PIXEL_MODE_BGRA)
                {
                    for (int y = 0; y < h; y++)
                    {
                        uint* srcRow = (uint*)(src + y * bitmap.pitch);
                        uint* dstRow = (uint*)(dstPtr + y * w * 4);

                        for (int x = 0; x < w; x++)
                        {
                            uint pixel = srcRow[x];
                            uint rb = ((pixel & 0x00FF0000) >> 16) | ((pixel & 0x000000FF) << 16);
                            dstRow[x] = (pixel & 0xFF00FF00) | rb;
                        }
                    }
                }
                else if (bitmap.pixelMode == PIXEL_MODE_GRAY)
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = src + y * bitmap.pitch;
                        int dstOffset = y * w * 4;
                        for (int x = 0; x < w; x++)
                        {
                            byte gray = row[x];
                            int dstIdx = dstOffset + x * 4;
                            dstPtr[dstIdx + 0] = 255;
                            dstPtr[dstIdx + 1] = 255;
                            dstPtr[dstIdx + 2] = 255;
                            dstPtr[dstIdx + 3] = gray;
                        }
                    }
                }
                else if (bitmap.pixelMode == PIXEL_MODE_MONO)
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = src + y * bitmap.pitch;
                        int dstOffset = y * w * 4;
                        for (int x = 0; x < w; x++)
                        {
                            int byteIndex = x >> 3;
                            int bitIndex = 7 - (x & 7);
                            byte val = ((row[byteIndex] >> bitIndex) & 1) == 1 ? (byte)255 : (byte)0;
                            int dstIdx = dstOffset + x * 4;
                            dstPtr[dstIdx + 0] = 255;
                            dstPtr[dstIdx + 1] = 255;
                            dstPtr[dstIdx + 2] = 255;
                            dstPtr[dstIdx + 3] = val;
                        }
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// Gets the graphicType from the sbix table (e.g., "png ", "emjc", "jpg ").
        /// Used to diagnose why emoji rendering fails on iOS.
        /// </summary>
        /// <param name="face">FreeType face handle</param>
        /// <param name="graphicType">Output: 4-character graphic type string</param>
        /// <param name="numStrikes">Output: number of bitmap strikes in sbix table</param>
        /// <returns>True if sbix table was found and parsed successfully</returns>
        public static bool GetSbixGraphicType(IntPtr face, out string graphicType, out int numStrikes)
        {
            graphicType = null;
            numStrikes = 0;

            if (!initialized || face == IntPtr.Zero)
                return false;

            byte[] buffer = new byte[5];
            int result = ut_debug_sbix_graphic_type(face, buffer, out numStrikes);
            if (result != 0 && buffer[0] != 0)
            {
                graphicType = System.Text.Encoding.ASCII.GetString(buffer, 0, 4).TrimEnd('\0');
                return true;
            }
            return false;
        }

        #endregion
    }

}
