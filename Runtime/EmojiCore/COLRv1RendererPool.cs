#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LightSide
{
    /// <summary>
    /// Thread-safe pool of COLRv1Renderer instances for parallel emoji rendering.
    /// Each renderer owns its own FreeType face and Blend2D resources.
    /// </summary>
    internal sealed class COLRv1RendererPool : IDisposable
    {
        public static bool UseParallel = true;

        private readonly byte[] fontData;
        private readonly int faceIndex;
        private readonly ConcurrentBag<COLRv1Renderer> availableRenderers;
        private readonly List<COLRv1Renderer> allRenderers;
        private readonly object createLock = new();
        private readonly int maxRenderers;
        private bool disposed;

        private const int ParallelThreshold = 32;

        public COLRv1RendererPool(byte[] fontData, int faceIndex, int maxRenderers = 0)
        {
            this.fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
            this.faceIndex = faceIndex;
            this.maxRenderers = maxRenderers > 0 ? maxRenderers : Environment.ProcessorCount;
            this.availableRenderers = new ConcurrentBag<COLRv1Renderer>();
            this.allRenderers = new List<COLRv1Renderer>(this.maxRenderers);
        }

        private COLRv1Renderer RentRenderer()
        {
            if (availableRenderers.TryTake(out var renderer))
                return renderer;

            lock (createLock)
            {
                if (allRenderers.Count >= maxRenderers)
                {
                    SpinWait spin = default;
                    while (!availableRenderers.TryTake(out renderer))
                        spin.SpinOnce();
                    return renderer;
                }

                renderer = new COLRv1Renderer(fontData, faceIndex);
                allRenderers.Add(renderer);
                return renderer;
            }
        }

        private void ReturnRenderer(COLRv1Renderer renderer)
        {
            if (renderer != null)
                availableRenderers.Add(renderer);
        }

        /// <summary>
        /// Rendered COLRv1 glyph result.
        /// </summary>
        public struct RenderedGlyph
        {
            public bool isValid;
            public int width;
            public int height;
            public float bearingX;
            public float bearingY;
            public float advanceX;
            public byte[] rgbaPixels;
        }

        public bool TryRenderGlyph(COLRv1Renderer renderer, uint glyphIndex, int pixelSize, out RenderedGlyph result)
        {
            result = default;

            if (renderer == null || glyphIndex == 0)
                return false;

            if (!renderer.TryRenderGlyph(glyphIndex, pixelSize, out var rgbaPixels, out int width, out int height,
                out float bearingX, out float bearingY))
                return false;

            result = new RenderedGlyph
            {
                isValid = true,
                width = width,
                height = height,
                bearingX = bearingX,
                bearingY = bearingY,
                advanceX = width,
                rgbaPixels = rgbaPixels
            };

            return true;
        }

        public RenderedGlyph[] RenderGlyphsBatch(IReadOnlyList<uint> glyphIndices, int pixelSize)
        {
            int count = glyphIndices.Count;
            var results = new RenderedGlyph[count];

            if (count == 0)
                return results;

            if (!UseParallel || count < ParallelThreshold)
                RenderSequential(glyphIndices, pixelSize, results);
            else
                RenderParallel(glyphIndices, pixelSize, results);

            return results;
        }

        private void RenderSequential(IReadOnlyList<uint> glyphIndices, int pixelSize, RenderedGlyph[] results)
        {
            var renderer = RentRenderer();
            try
            {
                for (int i = 0; i < glyphIndices.Count; i++)
                {
                    TryRenderGlyph(renderer, glyphIndices[i], pixelSize, out results[i]);
                }
            }
            finally
            {
                ReturnRenderer(renderer);
            }
        }

        private void RenderParallel(IReadOnlyList<uint> glyphIndices, int pixelSize, RenderedGlyph[] results)
        {
            int count = glyphIndices.Count;
            int workerCount = Math.Min(maxRenderers, count);
            int chunkSize = (count + workerCount - 1) / workerCount;

            Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, workerId =>
            {
                int start = workerId * chunkSize;
                int end = Math.Min(start + chunkSize, count);

                if (start >= end)
                    return;

                var renderer = RentRenderer();
                try
                {
                    for (int i = start; i < end; i++)
                    {
                        TryRenderGlyph(renderer, glyphIndices[i], pixelSize, out results[i]);
                    }
                }
                finally
                {
                    ReturnRenderer(renderer);
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
                foreach (var renderer in allRenderers)
                {
                    renderer?.Dispose();
                }
                allRenderers.Clear();
            }

            while (availableRenderers.TryTake(out _)) { }
        }
    }
}
#endif
