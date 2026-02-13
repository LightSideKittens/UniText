using System;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace LightSide
{
    /// <summary>Result of SDF glyph rendering — metrics + Alpha8 bitmap copy.</summary>
    internal struct SdfRenderedGlyph
    {
        public bool isValid;
        public uint glyphIndex;
        public int metricWidth;
        public int metricHeight;
        public int metricBearingX;
        public int metricBearingY;
        public int metricAdvanceX26_6;

        public int bmpWidth;
        public int bmpHeight;
        public int bmpPitch;
        public int bitmapLeft;
        public int bitmapTop;
        public byte[] sdfPixels;
    }

    /// <summary>Static helper for rendering individual SDF glyphs via EDT.</summary>
    internal static class SdfGlyphRenderer
    {
        /// <summary>
        /// Renders a single glyph as SDF (normal bitmap + EDT in native code).
        /// Copies bitmap to a pooled byte[] buffer (caller must return via UniTextArrayPool).
        /// </summary>
        public static unsafe bool TryRender(IntPtr face, uint glyphIndex, int sdfPixelSize,
            int loadFlags, int spread, out SdfRenderedGlyph result)
        {
            result = default;
            result.glyphIndex = glyphIndex;

            if (face == IntPtr.Zero)
                return false;

            FT.SetPixelSize(face, sdfPixelSize);

            if (!FT.RenderSdfGlyph(face, glyphIndex, loadFlags, spread, out var native))
                return false;

            result.metricWidth = native.metricWidth;
            result.metricHeight = native.metricHeight;
            result.metricBearingX = native.metricBearingX;
            result.metricBearingY = native.metricBearingY;
            result.metricAdvanceX26_6 = native.metricAdvanceX;
            result.bmpWidth = native.bmpWidth;
            result.bmpHeight = native.bmpHeight;
            result.bmpPitch = native.bmpPitch;
            result.bitmapLeft = native.bitmapLeft;
            result.bitmapTop = native.bitmapTop;

            if (native.bmpWidth <= 0 || native.bmpHeight <= 0 || native.bmpBuffer == IntPtr.Zero)
            {
                result.isValid = true;
                return true;
            }

            int pixelCount = native.bmpWidth * native.bmpHeight;
            var pixels = UniTextArrayPool<byte>.Rent(pixelCount);
            byte* src = (byte*)native.bmpBuffer;
            int pitch = native.bmpPitch;
            int bw = native.bmpWidth;
            int bh = native.bmpHeight;

            fixed (byte* dst = pixels)
            {
                for (int y = 0; y < bh; y++)
                    Buffer.MemoryCopy(src + y * pitch, dst + y * bw, bw, bw);
            }

            FT.FreeSdfBuffer(native.bmpBuffer);

            result.sdfPixels = pixels;
            result.isValid = true;
            return true;
        }
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    internal sealed class FreeTypeFacePool : IDisposable
    {
        public static bool UseParallel = true;

        private readonly byte[] fontData;
        private readonly int faceIndex;
        private readonly int pixelSize;
        private readonly ConcurrentBag<IntPtr> availableFaces;
        private readonly List<IntPtr> allFaces;
        private readonly object createLock = new();
        private readonly int maxFaces;
        private readonly bool hasFixedSizes;
        private readonly int bestFixedSizeIdx;
        private bool disposed;

        private const int ParallelThreshold = 16;

        public FreeTypeFacePool(byte[] fontData, int faceIndex, int pixelSize, int maxFaces = 0)
        {
            this.fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
            this.faceIndex = faceIndex;
            this.pixelSize = pixelSize;
            this.maxFaces = maxFaces > 0 ? maxFaces : Environment.ProcessorCount;
            availableFaces = new ConcurrentBag<IntPtr>();
            allFaces = new List<IntPtr>(this.maxFaces);

            if (!FT.Initialize())
                throw new InvalidOperationException("Failed to initialize FreeType");

            var probeFace = FT.LoadFace(fontData, faceIndex);
            if (probeFace != IntPtr.Zero)
            {
                var info = FT.GetFaceInfo(probeFace);
                if (info.numFixedSizes > 0)
                {
                    hasFixedSizes = true;
                    int bestDiff = int.MaxValue;
                    for (int i = 0; i < info.numFixedSizes; i++)
                    {
                        int size = FT.GetFixedSize(probeFace, i);
                        int diff = Math.Abs(size - pixelSize);
                        if (diff < bestDiff) { bestDiff = diff; bestFixedSizeIdx = i; }
                    }
                }

                allFaces.Add(probeFace);
                availableFaces.Add(probeFace);
            }
        }

        private IntPtr RentFace()
        {
            if (availableFaces.TryTake(out var face))
                return face;

            lock (createLock)
            {
                if (allFaces.Count >= maxFaces)
                {
                    SpinWait spin = default;
                    while (!availableFaces.TryTake(out face))
                        spin.SpinOnce();
                    return face;
                }

                face = FT.LoadFace(fontData, faceIndex);
                if (face == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create FreeType face");

                allFaces.Add(face);
                return face;
            }
        }

        private void ReturnFace(IntPtr face)
        {
            if (face != IntPtr.Zero)
                availableFaces.Add(face);
        }

        public bool TryRenderGlyph(IntPtr face, uint glyphIndex, out FreeType.RenderedGlyph result)
        {
            result = default;

            if (face == IntPtr.Zero)
                return false;

            if (hasFixedSizes)
                FT.SelectFixedSize(face, bestFixedSizeIdx);
            else if (!FT.SetPixelSize(face, pixelSize))
                return false;

            bool loaded = FT.LoadGlyph(face, glyphIndex, FT.LOAD_COLOR | FT.LOAD_RENDER);
            if (!loaded)
            {
                loaded = FT.LoadGlyph(face, glyphIndex, FT.LOAD_RENDER);
                if (!loaded)
                {
                    loaded = FT.LoadGlyph(face, glyphIndex, FT.LOAD_DEFAULT);
                    if (!loaded)
                        return false;

                    if (!FT.RenderGlyph(face))
                        return false;
                }
            }

            var metrics = FT.GetGlyphMetrics(face);
            var bitmap = FT.GetBitmapData(face);

            if (bitmap.width <= 0 || bitmap.height <= 0)
                return false;

            int pixelDataSize = bitmap.width * bitmap.height * 4;
            byte[] pixelsCopy = UniTextArrayPool<byte>.Rent(pixelDataSize);

            if (!FT.CopyBitmapAsRGBA(face, pixelsCopy))
            {
                UniTextArrayPool<byte>.Return(pixelsCopy);
                return false;
            }

            result = new FreeType.RenderedGlyph
            {
                isValid = true,
                width = bitmap.width,
                height = bitmap.height,
                bearingX = metrics.bearingX,
                bearingY = FT.GetBitmapTop(face),
                advanceX = metrics.advanceX / 64f,
                advanceY = metrics.advanceY / 64f,
                rgbaPixels = pixelsCopy,
                isBGRA = false
            };

            return true;
        }

        public FreeType.RenderedGlyph[] RenderGlyphsBatch(IReadOnlyList<uint> glyphIndices)
        {
            int count = glyphIndices.Count;
            var results = new FreeType.RenderedGlyph[count];

            if (count == 0)
                return results;

            if (!UseParallel || count < ParallelThreshold)
                RenderSequential(glyphIndices, results);
            else
                RenderParallel(glyphIndices, results);

            return results;
        }

        private void RenderSequential(IReadOnlyList<uint> glyphIndices, FreeType.RenderedGlyph[] results)
        {
            var face = RentFace();
            try
            {
                for (int i = 0; i < glyphIndices.Count; i++)
                {
                    TryRenderGlyph(face, glyphIndices[i], out results[i]);
                }
            }
            finally
            {
                ReturnFace(face);
            }
        }

        private void RenderParallel(IReadOnlyList<uint> glyphIndices, FreeType.RenderedGlyph[] results)
        {
            int count = glyphIndices.Count;
            int workerCount = Math.Min(maxFaces, count);
            int chunkSize = (count + workerCount - 1) / workerCount;

            Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, workerId =>
            {
                int start = workerId * chunkSize;
                int end = Math.Min(start + chunkSize, count);

                if (start >= end)
                    return;

                var face = RentFace();
                try
                {
                    for (int i = start; i < end; i++)
                    {
                        TryRenderGlyph(face, glyphIndices[i], out results[i]);
                    }
                }
                finally
                {
                    ReturnFace(face);
                }
            });
        }

        /// <summary>
        /// Batch-renders SDF glyphs, potentially in parallel using the face pool.
        /// </summary>
        public SdfRenderedGlyph[] RenderSdfBatch(IReadOnlyList<uint> glyphIndices, int sdfPixelSize,
            int loadFlags, int spread)
        {
            int count = glyphIndices.Count;
            var results = new SdfRenderedGlyph[count];

            if (count == 0)
                return results;

            if (!UseParallel || count < ParallelThreshold)
            {
                var face = RentFace();
                try
                {
                    for (int i = 0; i < count; i++)
                        SdfGlyphRenderer.TryRender(face, glyphIndices[i], sdfPixelSize, loadFlags, spread, out results[i]);
                }
                finally { ReturnFace(face); }
            }
            else
            {
                int workerCount = Math.Min(maxFaces, count);
                int chunkSize = (count + workerCount - 1) / workerCount;

                Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, workerId =>
                {
                    int start = workerId * chunkSize;
                    int end = Math.Min(start + chunkSize, count);
                    if (start >= end) return;

                    var face = RentFace();
                    try
                    {
                        for (int i = start; i < end; i++)
                            SdfGlyphRenderer.TryRender(face, glyphIndices[i], sdfPixelSize, loadFlags, spread, out results[i]);
                    }
                    finally { ReturnFace(face); }
                });
            }

            return results;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            lock (createLock)
            {
                foreach (var face in allFaces)
                {
                    if (face != IntPtr.Zero)
                        FT.UnloadFace(face);
                }
                allFaces.Clear();
            }

            while (availableFaces.TryTake(out _)) { }
        }
    }
#endif
}
