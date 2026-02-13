using System;

namespace LightSide
{
    /// <summary>
    /// Renders COLRv1 emoji using FreeType for paint data and Blend2D for rasterization.
    /// COLRv1 spec: PaintGlyph defines a clip region, child paint fills within that region.
    /// Since Blend2D lacks clipToPath, we transform path coordinates directly and use fillPath.
    /// </summary>
    internal class COLRv1Renderer : IDisposable
    {
        private const float FIXED_26_6_DIVISOR = 64f;

        private const int MAX_PAINT_DEPTH = 64;

        private IntPtr face;
        private readonly bool ownsFace;
        private IntPtr paletteColors;
        private int numPaletteEntries;
        private bool disposed;

        private IntPtr blCtx;
        private int renderSize;
        private float glyphOffsetX;
        private float glyphOffsetY;

        private IntPtr pendingGlyphPath;

        private double gradM00 = 1, gradM01, gradM10, gradM11 = 1, gradM20, gradM21;

        private double m00, m01, m10, m11, m20, m21;
        private double[] transformStack = new double[64 * 6];
        private int transformStackDepth;

        private void ResetTransform()
        {
            m00 = 1; m01 = 0;
            m10 = 0; m11 = 1;
            m20 = 0; m21 = 0;
            transformStackDepth = 0;
        }

        private void SaveTransform()
        {
            int idx = transformStackDepth * 6;
            transformStack[idx] = m00;
            transformStack[idx + 1] = m01;
            transformStack[idx + 2] = m10;
            transformStack[idx + 3] = m11;
            transformStack[idx + 4] = m20;
            transformStack[idx + 5] = m21;
            transformStackDepth++;
        }

        private void RestoreTransform()
        {
            if (transformStackDepth > 0)
            {
                transformStackDepth--;
                int idx = transformStackDepth * 6;
                m00 = transformStack[idx];
                m01 = transformStack[idx + 1];
                m10 = transformStack[idx + 2];
                m11 = transformStack[idx + 3];
                m20 = transformStack[idx + 4];
                m21 = transformStack[idx + 5];
            }
        }

        private void ApplyTransform(double a00, double a01, double a10, double a11, double a20, double a21)
        {
            double r00 = m00 * a00 + m01 * a10;
            double r01 = m00 * a01 + m01 * a11;
            double r10 = m10 * a00 + m11 * a10;
            double r11 = m10 * a01 + m11 * a11;
            double r20 = m20 * a00 + m21 * a10 + a20;
            double r21 = m20 * a01 + m21 * a11 + a21;

            m00 = r00; m01 = r01;
            m10 = r10; m11 = r11;
            m20 = r20; m21 = r21;
        }

        private void ApplyTranslate(double dx, double dy)
        {
            m20 += m00 * dx + m01 * dy;
            m21 += m10 * dx + m11 * dy;
        }

        private void ApplyScale(double sx, double sy)
        {
            m00 *= sx; m01 *= sy;
            m10 *= sx; m11 *= sy;
        }

        /// <summary>
        /// Creates a COLRv1 renderer from font data. Renderer owns the FreeType face.
        /// </summary>
        public COLRv1Renderer(byte[] fontData, int faceIndex)
        {
            if (fontData == null || fontData.Length == 0)
                throw new ArgumentNullException(nameof(fontData));

            face = FT.LoadFace(fontData, faceIndex);
            if (face == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create FreeType face for COLRv1");

            ownsFace = true;
            InitializePaletteAndPath();
        }

        /// <summary>
        /// Creates a COLRv1 renderer from existing FreeType face. Renderer does NOT own the face.
        /// </summary>
        public COLRv1Renderer(IntPtr ftFace)
        {
            face = ftFace;
            ownsFace = false;
            InitializePaletteAndPath();
        }

        private void InitializePaletteAndPath()
        {
            if (FT.GetPaletteData(face, out var paletteData))
            {
                numPaletteEntries = paletteData.num_palette_entries;

                FT.SelectPalette(face, 0, out paletteColors);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (ownsFace && face != IntPtr.Zero)
            {
                FT.UnloadFace(face);
                face = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Check if glyph has COLRv1 data
        /// </summary>
        public bool HasCOLRv1(uint glyphIndex)
        {
            return FT.HasColorGlyphPaint(face, glyphIndex);
        }

        /// <summary>
        /// Render COLRv1 glyph to RGBA buffer
        /// </summary>
        public bool TryRenderGlyph(uint glyphIndex, int pixelSize, out byte[] rgbaPixels, out int width, out int height,
            out float bearingX, out float bearingY)
        {
            rgbaPixels = null;
            width = height = 0;
            bearingX = 0;
            bearingY = 0;

            if (!BL.IsSupported)
            {
                Cat.MeowError("[COLRv1] Blend2D not supported");
                return false;
            }

            FT.SetPixelSize(face, pixelSize);

            if (!FT.GetColorGlyphPaint(face, glyphIndex, true, out var rootPaint))
            {
                Cat.MeowWarnFormat("[COLRv1] GetColorGlyphPaint failed for glyph {0}", glyphIndex);
                return false;
            }

            int renderWidth = pixelSize;
            int renderHeight = pixelSize;
            float offsetX = 0;
            float offsetY = 0;

            if (FT.GetColorGlyphClipBox(face, glyphIndex, out var clipBox))
            {
                float minX = clipBox.bottom_left_x / FIXED_26_6_DIVISOR;
                float minY = clipBox.bottom_left_y / FIXED_26_6_DIVISOR;
                float maxX = clipBox.top_right_x / FIXED_26_6_DIVISOR;
                float maxY = clipBox.top_right_y / FIXED_26_6_DIVISOR;

                float clipWidth = maxX - minX;
                float clipHeight = maxY - minY;

                renderWidth = (int)Math.Ceiling(clipWidth);
                renderHeight = (int)Math.Ceiling(clipHeight);
                if (renderWidth < 1) renderWidth = pixelSize;
                if (renderHeight < 1) renderHeight = pixelSize;

                offsetX = -minX;
                offsetY = -minY;

                bearingX = minX;
                bearingY = maxY;
            }
            else
            {
                bearingX = 0;
                bearingY = renderHeight;
            }

            glyphOffsetX = offsetX;
            glyphOffsetY = offsetY;

            width = renderWidth;
            height = renderHeight;
            renderSize = Math.Max(renderWidth, renderHeight);

            IntPtr blImage = BL.ImageCreate(renderWidth, renderHeight, BL.FORMAT_PRGB32);
            if (blImage == IntPtr.Zero)
            {
                Cat.MeowError("[COLRv1] Failed to create Blend2D image");
                return false;
            }

            blCtx = BL.ContextCreate(blImage);
            if (blCtx == IntPtr.Zero)
            {
                BL.ImageDestroy(blImage);
                Cat.MeowError("[COLRv1] Failed to create Blend2D context");
                return false;
            }

            try
            {
                BL.ContextSetCompOp(blCtx, BL.COMP_OP_SRC_COPY);
                BL.ContextSetFillStyleRgba32(blCtx, 0x00000000);
                BL.ContextFillAll(blCtx);
                BL.ContextSetCompOp(blCtx, BL.COMP_OP_SRC_OVER);

                BL.ContextTranslate(blCtx, 0, renderHeight);
                BL.ContextScale(blCtx, 1, -1);
                if (glyphOffsetX != 0 || glyphOffsetY != 0)
                {
                    BL.ContextTranslate(blCtx, glyphOffsetX, glyphOffsetY);
                }

                ResetTransform();
                pendingGlyphPath = IntPtr.Zero;
                ApplyTranslate(0, renderHeight);
                ApplyScale(1, -1);
                if (glyphOffsetX != 0 || glyphOffsetY != 0)
                {
                    ApplyTranslate(glyphOffsetX, glyphOffsetY);
                }

                paintDepth = 0;
                RenderPaint(rootPaint);

                BL.ContextEnd(blCtx);

                IntPtr dataPtr = BL.ImageGetData(blImage, out int stride);
                if (dataPtr == IntPtr.Zero)
                {
                    Cat.MeowError("[COLRv1] Failed to get image data");
                    return false;
                }

                int pixelDataSize = renderWidth * renderHeight * 4;
                rgbaPixels = UniTextArrayPool<byte>.Rent(pixelDataSize);
                CopyBGRAtoRGBA(dataPtr, stride, rgbaPixels, renderWidth, renderHeight);

                return true;
            }
            finally
            {
                BL.ContextDestroy(blCtx);
                BL.ImageDestroy(blImage);
                blCtx = IntPtr.Zero;
            }
        }

        private unsafe void CopyBGRAtoRGBA(IntPtr src, int srcStride, byte[] dst, int width, int height)
        {
            fixed (byte* dstPtr = dst)
            {
                byte* srcBase = (byte*)src;

                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = srcBase + y * srcStride;
                    byte* dstRow = dstPtr + y * width * 4;

                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = x * 4;
                        int dstIdx = x * 4;

                        dstRow[dstIdx + 0] = srcRow[srcIdx + 2];
                        dstRow[dstIdx + 1] = srcRow[srcIdx + 1];
                        dstRow[dstIdx + 2] = srcRow[srcIdx + 0];
                        dstRow[dstIdx + 3] = srcRow[srcIdx + 3];
                    }
                }
            }
        }

        private int paintDepth = 0;

        /// <summary>
        /// Fills pending glyph path if set.
        /// Per COLRv1 spec, fills should only occur within a PaintGlyph context.
        /// Filling without a glyph path would fill the entire canvas (causing artifacts).
        /// </summary>
        private void FillPendingOrAll()
        {
            if (pendingGlyphPath != IntPtr.Zero)
                BL.ContextFillPath(blCtx, pendingGlyphPath);
        }

        private void RenderPaint(FT.FT_OpaquePaint opaquePaint)
        {
            if (opaquePaint.p == IntPtr.Zero)
                return;

            if (paintDepth >= MAX_PAINT_DEPTH)
            {
                Cat.MeowWarn("[COLRv1] Maximum paint depth exceeded");
                return;
            }

            int format = FT.GetPaintFormat(face, opaquePaint);
            if (format < 0)
                return;

            paintDepth++;
            switch (format)
            {
                case FT.FT_COLR_PAINTFORMAT_COLR_LAYERS:
                    RenderPaintColrLayers(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_SOLID:
                    RenderPaintSolid(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_LINEAR_GRADIENT:
                    RenderPaintLinearGradient(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_RADIAL_GRADIENT:
                    RenderPaintRadialGradient(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_SWEEP_GRADIENT:
                    RenderPaintSweepGradient(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_GLYPH:
                    RenderPaintGlyph(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_COLR_GLYPH:
                    RenderPaintColrGlyph(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_TRANSFORM:
                    RenderPaintTransform(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_TRANSLATE:
                    RenderPaintTranslate(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_SCALE:
                    RenderPaintScale(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_ROTATE:
                    RenderPaintRotate(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_SKEW:
                    RenderPaintSkew(opaquePaint);
                    break;

                case FT.FT_COLR_PAINTFORMAT_COMPOSITE:
                    RenderPaintComposite(opaquePaint);
                    break;

                default:
                    Cat.MeowWarnFormat("[COLRv1] Unknown paint format: {0}", format);
                    break;
            }
            paintDepth--;
        }

        private void RenderPaintColrLayers(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintColrLayers(face, opaquePaint, out var iterator))
                return;

            while (FT.GetPaintLayers(face, ref iterator, out var layerPaint))
            {
                RenderPaint(layerPaint);
            }
        }

        private void RenderPaintSolid(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintSolid(face, opaquePaint, out var colorIndex, out var alpha))
                return;

            uint color = GetPaletteColorRGBA(colorIndex);
            float alphaF = FT.F2Dot14ToFloat(alpha);

            byte a = (byte)((color >> 24) * alphaF);
            color = (color & 0x00FFFFFF) | ((uint)a << 24);

            BL.ContextSetFillStyleRgba32(blCtx, color);

            FillPendingOrAll();
        }

        private void RenderPaintLinearGradient(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintLinearGradient(face, opaquePaint, out var p0x, out var p0y, out var p1x, out var p1y, out _, out _, out _, out var colorStops))
                return;

            float x0 = FT.Fixed16Dot16ToFloat(p0x);
            float y0 = FT.Fixed16Dot16ToFloat(p0y);
            float x1 = FT.Fixed16Dot16ToFloat(p1x);
            float y1 = FT.Fixed16Dot16ToFloat(p1y);

            IntPtr linearGrad = BL.GradientCreateLinear(x0, y0, x1, y1);
            AddColorStops(linearGrad, ref colorStops);

            if (pendingGlyphPath != IntPtr.Zero)
            {
                BL.GradientApplyTransform(linearGrad, gradM00, gradM10, gradM01, gradM11, gradM20, gradM21);
            }

            BL.ContextSetFillStyleGradient(blCtx, linearGrad);
            FillPendingOrAll();
            BL.GradientDestroy(linearGrad);
        }

        private void RenderPaintRadialGradient(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintRadialGradient(face, opaquePaint, out var c0x, out var c0y, out var r0, out var c1x, out var c1y, out var r1, out _, out var colorStops))
                return;

            float cx = FT.Fixed16Dot16ToFloat(c1x);
            float cy = FT.Fixed16Dot16ToFloat(c1y);
            float fx = FT.Fixed16Dot16ToFloat(c0x);
            float fy = FT.Fixed16Dot16ToFloat(c0y);
            float r1f = FT.Fixed16Dot16ToFloat(r1);

            IntPtr radialGrad = BL.GradientCreateRadial(cx, cy, fx, fy, r1f);
            if (radialGrad == IntPtr.Zero)
                return;

            AddColorStops(radialGrad, ref colorStops);

            if (pendingGlyphPath != IntPtr.Zero)
            {
                BL.GradientApplyTransform(radialGrad, gradM00, gradM10, gradM01, gradM11, gradM20, gradM21);
            }

            BL.ContextSetFillStyleGradient(blCtx, radialGrad);
            FillPendingOrAll();
            BL.GradientDestroy(radialGrad);
        }

        private void RenderPaintSweepGradient(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintSweepGradient(face, opaquePaint, out var cx, out var cy, out var startAngle, out _, out _, out var colorStops))
                return;

            float cxF = FT.Fixed16Dot16ToFloat(cx);
            float cyF = FT.Fixed16Dot16ToFloat(cy);
            float angle = FT.Fixed16Dot16ToFloat(startAngle);

            IntPtr conicGrad = BL.GradientCreateConic(cxF, cyF, angle);
            AddColorStops(conicGrad, ref colorStops);

            if (pendingGlyphPath != IntPtr.Zero)
            {
                BL.GradientApplyTransform(conicGrad, gradM00, gradM10, gradM01, gradM11, gradM20, gradM21);
            }

            BL.ContextSetFillStyleGradient(blCtx, conicGrad);
            FillPendingOrAll();
            BL.GradientDestroy(conicGrad);
        }

        /// <summary>
        /// PaintGlyph: glyph outline defines the clip region for child paint.
        /// Child paint fills within the glyph outline. Transforms inside PaintGlyph
        /// accumulate in gradM for gradients (path coordinates are fixed).
        /// </summary>
        private void RenderPaintGlyph(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintGlyph(face, opaquePaint, out var glyphId, out var childPaint))
                return;

            if (!FT.LoadGlyph(face, glyphId, FT.LOAD_NO_SCALE | FT.LOAD_NO_BITMAP))
                return;

            IntPtr glyphPath = BL.PathCreate();
            if (glyphPath == IntPtr.Zero)
                return;

            if (!FT.OutlineToBlendPath(face, glyphPath))
            {
                BL.PathDestroy(glyphPath);
                return;
            }

            IntPtr previousPendingPath = pendingGlyphPath;
            double prevGradM00 = gradM00, prevGradM01 = gradM01;
            double prevGradM10 = gradM10, prevGradM11 = gradM11;
            double prevGradM20 = gradM20, prevGradM21 = gradM21;

            gradM00 = 1; gradM01 = 0;
            gradM10 = 0; gradM11 = 1;
            gradM20 = 0; gradM21 = 0;

            pendingGlyphPath = glyphPath;

            RenderPaint(childPaint);

            pendingGlyphPath = previousPendingPath;
            gradM00 = prevGradM00; gradM01 = prevGradM01;
            gradM10 = prevGradM10; gradM11 = prevGradM11;
            gradM20 = prevGradM20; gradM21 = prevGradM21;

            BL.PathDestroy(glyphPath);
        }

        private void RenderPaintColrGlyph(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintColrGlyph(face, opaquePaint, out var glyphId))
                return;

            if (FT.GetColorGlyphPaint(face, glyphId, false, out var childPaint))
            {
                RenderPaint(childPaint);
            }
        }

        private void RenderPaintTransform(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintTransform(face, opaquePaint, out var affine, out var childPaint))
                return;

            float xx = FT.Fixed16Dot16ToFloat(affine.xx);
            float xy = FT.Fixed16Dot16ToFloat(affine.xy);
            float yx = FT.Fixed16Dot16ToFloat(affine.yx);
            float yy = FT.Fixed16Dot16ToFloat(affine.yy);
            float dx = FT.Fixed16Dot16ToFloat(affine.dx);
            float dy = FT.Fixed16Dot16ToFloat(affine.dy);

            bool isZeroTransform = Math.Abs(xx) < 0.0001f && Math.Abs(yy) < 0.0001f &&
                                   Math.Abs(xy) < 0.0001f && Math.Abs(yx) < 0.0001f;

            if (isZeroTransform)
            {
                RenderPaint(childPaint);
                return;
            }

            if (pendingGlyphPath != IntPtr.Zero)
            {
                double r00 = gradM00 * xx + gradM01 * yx;
                double r01 = gradM00 * xy + gradM01 * yy;
                double r10 = gradM10 * xx + gradM11 * yx;
                double r11 = gradM10 * xy + gradM11 * yy;
                double r20 = gradM20 * xx + gradM21 * yx + dx;
                double r21 = gradM20 * xy + gradM21 * yy + dy;

                double prev00 = gradM00, prev01 = gradM01;
                double prev10 = gradM10, prev11 = gradM11;
                double prev20 = gradM20, prev21 = gradM21;

                gradM00 = r00; gradM01 = r01;
                gradM10 = r10; gradM11 = r11;
                gradM20 = r20; gradM21 = r21;

                RenderPaint(childPaint);

                gradM00 = prev00; gradM01 = prev01;
                gradM10 = prev10; gradM11 = prev11;
                gradM20 = prev20; gradM21 = prev21;
            }
            else
            {
                BL.ContextSave(blCtx);
                SaveTransform();
                BL.ContextTransform(blCtx, xx, yx, xy, yy, dx, dy);
                ApplyTransform(xx, xy, yx, yy, dx, dy);

                RenderPaint(childPaint);

                BL.ContextRestore(blCtx);
                RestoreTransform();
            }
        }

        private void RenderPaintTranslate(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintTranslate(face, opaquePaint, out var dx, out var dy, out var childPaint))
                return;

            float dxF = FT.Fixed16Dot16ToFloat(dx);
            float dyF = FT.Fixed16Dot16ToFloat(dy);

            if (pendingGlyphPath != IntPtr.Zero)
            {
                double prev20 = gradM20, prev21 = gradM21;
                gradM20 += gradM00 * dxF + gradM01 * dyF;
                gradM21 += gradM10 * dxF + gradM11 * dyF;

                RenderPaint(childPaint);

                gradM20 = prev20; gradM21 = prev21;
            }
            else
            {
                BL.ContextSave(blCtx);
                BL.ContextTranslate(blCtx, dxF, dyF);
                RenderPaint(childPaint);
                BL.ContextRestore(blCtx);
            }
        }

        private void RenderPaintScale(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintScale(face, opaquePaint, out var scaleX, out var scaleY, out var centerX, out var centerY, out var childPaint))
                return;

            float cx = FT.Fixed16Dot16ToFloat(centerX);
            float cy = FT.Fixed16Dot16ToFloat(centerY);
            float sx = FT.Fixed16Dot16ToFloat(scaleX);
            float sy = FT.Fixed16Dot16ToFloat(scaleY);

            if (pendingGlyphPath != IntPtr.Zero)
            {
                double prev00 = gradM00, prev01 = gradM01;
                double prev10 = gradM10, prev11 = gradM11;
                double prev20 = gradM20, prev21 = gradM21;

                gradM20 += gradM00 * cx + gradM01 * cy;
                gradM21 += gradM10 * cx + gradM11 * cy;
                gradM00 *= sx; gradM01 *= sy;
                gradM10 *= sx; gradM11 *= sy;
                gradM20 -= gradM00 * cx + gradM01 * cy;
                gradM21 -= gradM10 * cx + gradM11 * cy;

                RenderPaint(childPaint);

                gradM00 = prev00; gradM01 = prev01;
                gradM10 = prev10; gradM11 = prev11;
                gradM20 = prev20; gradM21 = prev21;
            }
            else
            {
                BL.ContextSave(blCtx);
                BL.ContextTranslate(blCtx, cx, cy);
                BL.ContextScale(blCtx, sx, sy);
                BL.ContextTranslate(blCtx, -cx, -cy);
                RenderPaint(childPaint);
                BL.ContextRestore(blCtx);
            }
        }

        private void RenderPaintRotate(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintRotate(face, opaquePaint, out var angle, out var centerX, out var centerY, out var childPaint))
                return;

            float cx = FT.Fixed16Dot16ToFloat(centerX);
            float cy = FT.Fixed16Dot16ToFloat(centerY);
            float angleF = FT.Fixed16Dot16ToFloat(angle);
            double rad = angleF * Math.PI * 2;

            if (pendingGlyphPath != IntPtr.Zero)
            {
                double cosA = Math.Cos(rad);
                double sinA = Math.Sin(rad);

                double prev00 = gradM00, prev01 = gradM01;
                double prev10 = gradM10, prev11 = gradM11;
                double prev20 = gradM20, prev21 = gradM21;

                gradM20 += gradM00 * cx + gradM01 * cy;
                gradM21 += gradM10 * cx + gradM11 * cy;
                double r00 = gradM00 * cosA + gradM01 * sinA;
                double r01 = gradM01 * cosA - gradM00 * sinA;
                double r10 = gradM10 * cosA + gradM11 * sinA;
                double r11 = gradM11 * cosA - gradM10 * sinA;
                gradM00 = r00; gradM01 = r01;
                gradM10 = r10; gradM11 = r11;
                gradM20 -= gradM00 * cx + gradM01 * cy;
                gradM21 -= gradM10 * cx + gradM11 * cy;

                RenderPaint(childPaint);

                gradM00 = prev00; gradM01 = prev01;
                gradM10 = prev10; gradM11 = prev11;
                gradM20 = prev20; gradM21 = prev21;
            }
            else
            {
                BL.ContextSave(blCtx);
                BL.ContextTranslate(blCtx, cx, cy);
                BL.ContextRotate(blCtx, rad);
                BL.ContextTranslate(blCtx, -cx, -cy);
                RenderPaint(childPaint);
                BL.ContextRestore(blCtx);
            }
        }

        private void RenderPaintSkew(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintSkew(face, opaquePaint, out var xSkew, out var ySkew, out var centerX, out var centerY, out var childPaint))
                return;

            RenderPaint(childPaint);
        }

        private void RenderPaintComposite(FT.FT_OpaquePaint opaquePaint)
        {
            if (!FT.GetPaintComposite(face, opaquePaint, out var mode, out var backdrop, out var source))
                return;

            RenderPaint(backdrop);

            uint blCompOp = MapCompositeMode(mode);

            BL.ContextSetCompOp(blCtx, blCompOp);
            RenderPaint(source);

            BL.ContextSetCompOp(blCtx, BL.COMP_OP_SRC_OVER);
        }

        private static uint MapCompositeMode(int ftMode)
        {
            return ftMode switch
            {
                FT.FT_COLR_COMPOSITE_CLEAR => BL.COMP_OP_CLEAR,
                FT.FT_COLR_COMPOSITE_SRC => BL.COMP_OP_SRC_COPY,
                FT.FT_COLR_COMPOSITE_DEST => BL.COMP_OP_DST_COPY,
                FT.FT_COLR_COMPOSITE_SRC_OVER => BL.COMP_OP_SRC_OVER,
                FT.FT_COLR_COMPOSITE_DEST_OVER => BL.COMP_OP_DST_OVER,
                FT.FT_COLR_COMPOSITE_SRC_IN => BL.COMP_OP_SRC_IN,
                FT.FT_COLR_COMPOSITE_DEST_IN => BL.COMP_OP_DST_IN,
                FT.FT_COLR_COMPOSITE_SRC_OUT => BL.COMP_OP_SRC_OUT,
                FT.FT_COLR_COMPOSITE_DEST_OUT => BL.COMP_OP_DST_OUT,
                FT.FT_COLR_COMPOSITE_SRC_ATOP => BL.COMP_OP_SRC_ATOP,
                FT.FT_COLR_COMPOSITE_DEST_ATOP => BL.COMP_OP_DST_ATOP,
                FT.FT_COLR_COMPOSITE_XOR => BL.COMP_OP_XOR,
                FT.FT_COLR_COMPOSITE_PLUS => BL.COMP_OP_PLUS,
                FT.FT_COLR_COMPOSITE_SCREEN => BL.COMP_OP_SCREEN,
                FT.FT_COLR_COMPOSITE_OVERLAY => BL.COMP_OP_OVERLAY,
                FT.FT_COLR_COMPOSITE_DARKEN => BL.COMP_OP_DARKEN,
                FT.FT_COLR_COMPOSITE_LIGHTEN => BL.COMP_OP_LIGHTEN,
                FT.FT_COLR_COMPOSITE_MULTIPLY => BL.COMP_OP_MULTIPLY,
                _ => BL.COMP_OP_SRC_OVER
            };
        }

        private void AddColorStops(IntPtr gradient, ref FT.FT_ColorStopIterator iterator)
        {
            while (FT.GetColorStop(face, ref iterator, out var stopOffset, out var colorIndex, out var alpha))
            {
                float offset = FT.Fixed16Dot16ToFloat(stopOffset);
                uint color = GetPaletteColorRGBA(colorIndex);

                float alphaF = FT.F2Dot14ToFloat(alpha);
                byte a = (byte)((color >> 24) * alphaF);
                color = (color & 0x00FFFFFF) | ((uint)a << 24);

                BL.GradientAddStop(gradient, offset, color);
            }
        }

        private uint GetPaletteColorRGBA(ushort colorIndex)
        {
            if (colorIndex == 0xFFFF)
            {
                return 0xFF000000;
            }

            if (paletteColors == IntPtr.Zero || colorIndex >= numPaletteEntries)
            {
                return 0xFF000000;
            }

            var color = FT.GetPaletteColor(paletteColors, colorIndex);
            return color.ToRGBA32();
        }
    }
}
