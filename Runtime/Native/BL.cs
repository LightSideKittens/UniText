using System;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.CallingConvention;

namespace LightSide
{
    /// <summary>
    /// Blend2D bindings for COLRv1 emoji rendering.
    /// Provides 2D vector graphics with full gradient support (linear, radial, conic/sweep).
    /// </summary>
    internal static class BL
    {
        #region Constants

        public const uint FORMAT_NONE = 0;
        public const uint FORMAT_PRGB32 = 1;
        public const uint FORMAT_XRGB32 = 2;
        public const uint FORMAT_A8 = 3;

        public const uint COMP_OP_SRC_OVER = 0;
        public const uint COMP_OP_SRC_COPY = 1;
        public const uint COMP_OP_SRC_IN = 2;
        public const uint COMP_OP_SRC_OUT = 3;
        public const uint COMP_OP_SRC_ATOP = 4;
        public const uint COMP_OP_DST_OVER = 5;
        public const uint COMP_OP_DST_COPY = 6;
        public const uint COMP_OP_DST_IN = 7;
        public const uint COMP_OP_DST_OUT = 8;
        public const uint COMP_OP_DST_ATOP = 9;
        public const uint COMP_OP_XOR = 10;
        public const uint COMP_OP_CLEAR = 11;
        public const uint COMP_OP_PLUS = 12;
        public const uint COMP_OP_MULTIPLY = 13;
        public const uint COMP_OP_SCREEN = 14;
        public const uint COMP_OP_OVERLAY = 15;
        public const uint COMP_OP_DARKEN = 16;
        public const uint COMP_OP_LIGHTEN = 17;

        #endregion

        #region Platform bindings

#if UNITY_WEBGL && !UNITY_EDITOR
        public static IntPtr ImageCreate(int w, int h, uint format = FORMAT_PRGB32) => IntPtr.Zero;
        public static void ImageDestroy(IntPtr img) { }
        public static IntPtr ImageGetData(IntPtr img, out int stride) { stride = 0; return IntPtr.Zero; }

        public static IntPtr ContextCreate(IntPtr img) => IntPtr.Zero;
        public static void ContextDestroy(IntPtr ctx) { }
        public static void ContextEnd(IntPtr ctx) { }
        public static void ContextSetFillStyleRgba32(IntPtr ctx, uint rgba32) { }
        public static void ContextFillAll(IntPtr ctx) { }
        public static void ContextFillRect(IntPtr ctx, double x, double y, double w, double h) { }
        public static void ContextFillPath(IntPtr ctx, IntPtr path) { }
        public static void ContextSetFillStyleGradient(IntPtr ctx, IntPtr gradient) { }
        public static void ContextSave(IntPtr ctx) { }
        public static void ContextRestore(IntPtr ctx) { }
        public static void ContextTranslate(IntPtr ctx, double x, double y) { }
        public static void ContextScale(IntPtr ctx, double x, double y) { }
        public static void ContextRotate(IntPtr ctx, double angle) { }
        public static void ContextTransform(IntPtr ctx, double m00, double m01, double m10, double m11, double m20, double m21) { }
        public static void ContextResetMatrix(IntPtr ctx) { }
        public static void ContextSetCompOp(IntPtr ctx, uint compOp) { }
        public static void ContextClipToRect(IntPtr ctx, double x, double y, double w, double h) { }
        public static void ContextRestoreClipping(IntPtr ctx) { }
        public static void ContextBlitImage(IntPtr ctx, IntPtr img, double x, double y) { }

        public static IntPtr PathCreate() => IntPtr.Zero;
        public static void PathDestroy(IntPtr path) { }
        public static void PathClear(IntPtr path) { }
        public static void PathMoveTo(IntPtr path, double x, double y) { }
        public static void PathLineTo(IntPtr path, double x, double y) { }
        public static void PathQuadTo(IntPtr path, double x1, double y1, double x2, double y2) { }
        public static void PathCubicTo(IntPtr path, double x1, double y1, double x2, double y2, double x3, double y3) { }
        public static void PathClose(IntPtr path) { }
        public static void PathTransform(IntPtr path, double m00, double m01, double m10, double m11, double m20, double m21) { }

        public static IntPtr GradientCreateLinear(double x0, double y0, double x1, double y1) => IntPtr.Zero;
        public static IntPtr GradientCreateRadial(double cx, double cy, double fx, double fy, double r) => IntPtr.Zero;
        public static IntPtr GradientCreateConic(double cx, double cy, double angle) => IntPtr.Zero;
        public static void GradientDestroy(IntPtr grad) { }
        public static void GradientAddStop(IntPtr grad, double offset, uint rgba32) { }
        public static void GradientResetStops(IntPtr grad) { }
        public static void GradientApplyTransform(IntPtr grad, double m00, double m01, double m10, double m11, double m20, double m21) { }

        public static bool IsSupported => false;

#else

    #if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
    #else
        private const string LibraryName = "unitext_native";
    #endif

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blImageCreate(int w, int h, uint format);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blImageDestroy(IntPtr img);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blImageGetData(IntPtr img, out int outStride);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blContextCreate(IntPtr img);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextDestroy(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextEnd(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextSetFillStyleRgba32(IntPtr ctx, uint rgba32);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextFillAll(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextFillRect(IntPtr ctx, double x, double y, double w, double h);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextFillPath(IntPtr ctx, IntPtr path);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextSetFillStyleGradient(IntPtr ctx, IntPtr gradient);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextSave(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextRestore(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextTranslate(IntPtr ctx, double x, double y);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextScale(IntPtr ctx, double x, double y);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextRotate(IntPtr ctx, double angle);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextTransform(IntPtr ctx, double m00, double m01, double m10, double m11, double m20, double m21);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextResetMatrix(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextSetCompOp(IntPtr ctx, uint compOp);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextClipToRect(IntPtr ctx, double x, double y, double w, double h);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextRestoreClipping(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blContextBlitImage(IntPtr ctx, IntPtr img, double x, double y);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blPathCreate();

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathDestroy(IntPtr path);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathClear(IntPtr path);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathMoveTo(IntPtr path, double x, double y);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathLineTo(IntPtr path, double x, double y);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathQuadTo(IntPtr path, double x1, double y1, double x2, double y2);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathCubicTo(IntPtr path, double x1, double y1, double x2, double y2, double x3, double y3);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathClose(IntPtr path);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blPathTransform(IntPtr path, double m00, double m01, double m10, double m11, double m20, double m21);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blGradientCreateLinear(double x0, double y0, double x1, double y1);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blGradientCreateRadial(double cx, double cy, double fx, double fy, double r);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern IntPtr ut_blGradientCreateConic(double cx, double cy, double angle);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blGradientDestroy(IntPtr grad);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blGradientAddStop(IntPtr grad, double offset, uint rgba32);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blGradientResetStops(IntPtr grad);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern void ut_blGradientApplyTransform(IntPtr grad, double m00, double m01, double m10, double m11, double m20, double m21);

        public static IntPtr ImageCreate(int w, int h, uint format = FORMAT_PRGB32)
            => ut_blImageCreate(w, h, format);

        public static void ImageDestroy(IntPtr img)
            => ut_blImageDestroy(img);

        public static IntPtr ImageGetData(IntPtr img, out int stride)
            => ut_blImageGetData(img, out stride);

        public static IntPtr ContextCreate(IntPtr img)
            => ut_blContextCreate(img);

        public static void ContextDestroy(IntPtr ctx)
            => ut_blContextDestroy(ctx);

        public static void ContextEnd(IntPtr ctx)
            => ut_blContextEnd(ctx);

        public static void ContextSetFillStyleRgba32(IntPtr ctx, uint rgba32)
            => ut_blContextSetFillStyleRgba32(ctx, rgba32);

        public static void ContextFillAll(IntPtr ctx)
            => ut_blContextFillAll(ctx);

        public static void ContextFillRect(IntPtr ctx, double x, double y, double w, double h)
            => ut_blContextFillRect(ctx, x, y, w, h);

        public static void ContextFillPath(IntPtr ctx, IntPtr path)
            => ut_blContextFillPath(ctx, path);

        public static void ContextSetFillStyleGradient(IntPtr ctx, IntPtr gradient)
            => ut_blContextSetFillStyleGradient(ctx, gradient);

        public static void ContextSave(IntPtr ctx)
            => ut_blContextSave(ctx);

        public static void ContextRestore(IntPtr ctx)
            => ut_blContextRestore(ctx);

        public static void ContextTranslate(IntPtr ctx, double x, double y)
            => ut_blContextTranslate(ctx, x, y);

        public static void ContextScale(IntPtr ctx, double x, double y)
            => ut_blContextScale(ctx, x, y);

        public static void ContextRotate(IntPtr ctx, double angle)
            => ut_blContextRotate(ctx, angle);

        public static void ContextTransform(IntPtr ctx, double m00, double m01, double m10, double m11, double m20, double m21)
            => ut_blContextTransform(ctx, m00, m01, m10, m11, m20, m21);

        public static void ContextResetMatrix(IntPtr ctx)
            => ut_blContextResetMatrix(ctx);

        public static void ContextSetCompOp(IntPtr ctx, uint compOp)
            => ut_blContextSetCompOp(ctx, compOp);

        public static void ContextClipToRect(IntPtr ctx, double x, double y, double w, double h)
            => ut_blContextClipToRect(ctx, x, y, w, h);

        public static void ContextRestoreClipping(IntPtr ctx)
            => ut_blContextRestoreClipping(ctx);

        public static void ContextBlitImage(IntPtr ctx, IntPtr img, double x, double y)
            => ut_blContextBlitImage(ctx, img, x, y);

        public static IntPtr PathCreate()
            => ut_blPathCreate();

        public static void PathDestroy(IntPtr path)
            => ut_blPathDestroy(path);

        public static void PathClear(IntPtr path)
            => ut_blPathClear(path);

        public static void PathMoveTo(IntPtr path, double x, double y)
            => ut_blPathMoveTo(path, x, y);

        public static void PathLineTo(IntPtr path, double x, double y)
            => ut_blPathLineTo(path, x, y);

        public static void PathQuadTo(IntPtr path, double x1, double y1, double x2, double y2)
            => ut_blPathQuadTo(path, x1, y1, x2, y2);

        public static void PathCubicTo(IntPtr path, double x1, double y1, double x2, double y2, double x3, double y3)
            => ut_blPathCubicTo(path, x1, y1, x2, y2, x3, y3);

        public static void PathClose(IntPtr path)
            => ut_blPathClose(path);

        public static void PathTransform(IntPtr path, double m00, double m01, double m10, double m11, double m20, double m21)
            => ut_blPathTransform(path, m00, m01, m10, m11, m20, m21);

        public static IntPtr GradientCreateLinear(double x0, double y0, double x1, double y1)
            => ut_blGradientCreateLinear(x0, y0, x1, y1);

        public static IntPtr GradientCreateRadial(double cx, double cy, double fx, double fy, double r)
            => ut_blGradientCreateRadial(cx, cy, fx, fy, r);

        public static IntPtr GradientCreateConic(double cx, double cy, double angle)
            => ut_blGradientCreateConic(cx, cy, angle);

        public static void GradientDestroy(IntPtr grad)
            => ut_blGradientDestroy(grad);

        public static void GradientAddStop(IntPtr grad, double offset, uint rgba32)
            => ut_blGradientAddStop(grad, offset, rgba32);

        public static void GradientResetStops(IntPtr grad)
            => ut_blGradientResetStops(grad);

        public static void GradientApplyTransform(IntPtr grad, double m00, double m01, double m10, double m11, double m20, double m21)
            => ut_blGradientApplyTransform(grad, m00, m01, m10, m11, m20, m21);

        public static bool IsSupported => true;

#endif

        #endregion

        #region Helper methods

        /// <summary>
        /// Create RGBA32 color from components (0-255).
        /// </summary>
        public static uint Rgba32(byte r, byte g, byte b, byte a = 255)
            => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

        /// <summary>
        /// Create RGBA32 color from normalized components (0.0-1.0).
        /// </summary>
        public static uint Rgba32(float r, float g, float b, float a = 1f)
            => Rgba32(
                (byte)(r * 255f + 0.5f),
                (byte)(g * 255f + 0.5f),
                (byte)(b * 255f + 0.5f),
                (byte)(a * 255f + 0.5f));

        #endregion
    }
}
