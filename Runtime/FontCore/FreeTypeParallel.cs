using System;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace LightSide
{
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

        public FreeType.RenderedGlyph[] RenderGlyphsBatch(PooledBuffer<uint> glyphIndices)
        {
            int count = glyphIndices.count;
            var results = new FreeType.RenderedGlyph[count];

            if (count == 0)
                return results;

            if (!UseParallel || count < ParallelThreshold)
                RenderSequential(glyphIndices, results);
            else
                RenderParallel(glyphIndices, results);

            return results;
        }

        private void RenderSequential(PooledBuffer<uint> glyphIndices, FreeType.RenderedGlyph[] results)
        {
            var face = RentFace();
            try
            {
                for (int i = 0; i < glyphIndices.count; i++)
                {
                    TryRenderGlyph(face, glyphIndices[i], out results[i]);
                }
            }
            finally
            {
                ReturnFace(face);
            }
        }

        private void RenderParallel(PooledBuffer<uint> glyphIndices, FreeType.RenderedGlyph[] results)
        {
            int count = glyphIndices.count;
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
