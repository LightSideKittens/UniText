#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// WebGL-specific emoji rendering using browser Canvas 2D API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides native emoji rendering in WebGL builds by leveraging the browser's
    /// text rendering capabilities through JavaScript interop.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Single emoji rendering via <see cref="TryRenderEmoji"/></item>
    /// <item>Batch rendering for performance via <see cref="TryRenderEmojiBatch"/></item>
    /// <item>Emoji sequence caching by hash for efficient lookup</item>
    /// <item>ZWJ (Zero Width Joiner) sequence support detection</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="EmojiFont"/>
    /// <seealso cref="WebGLEmojiShaper"/>
    public static class WebGLEmoji
    {
        [DllImport("__Internal")]
        private static extern bool JS_BrowserEmoji_IsSupported();

        [DllImport("__Internal")]
        private static extern bool JS_BrowserEmoji_IsZwjSupported(IntPtr codepointsPtr, int length, int pixelSize);

        [DllImport("__Internal")]
        private static extern float JS_BrowserEmoji_MeasureEmoji(IntPtr codepointsPtr, int length, int pixelSize);

        [DllImport("__Internal")]
        private static extern IntPtr JS_BrowserEmoji_RenderEmoji(IntPtr codepointsPtr, int length, int pixelSize, IntPtr outMetricsPtr);

        [DllImport("__Internal")]
        private static extern void JS_BrowserEmoji_FreeRenderedData();

        [DllImport("__Internal")]
        private static extern IntPtr JS_BrowserEmoji_RenderEmojiBatch(
            IntPtr codepointsPtr, IntPtr offsetsPtr, IntPtr lengthsPtr,
            int count, int pixelSize, IntPtr outMetricsPtr, IntPtr outPixelOffsetsPtr);

        private static bool? isSupported;

        /// <summary>Returns true if browser emoji rendering is supported.</summary>
        public static bool IsSupported
        {
            get
            {
                isSupported ??= JS_BrowserEmoji_IsSupported();
                return isSupported.Value;
            }
        }

        /// <summary>Result structure from emoji rendering operations.</summary>
        public struct RenderedEmoji
        {
            /// <summary>Rendered bitmap width in pixels.</summary>
            public int width;
            /// <summary>Rendered bitmap height in pixels.</summary>
            public int height;
            /// <summary>Horizontal bearing from origin to left edge.</summary>
            public int bearingX;
            /// <summary>Vertical bearing from baseline to top edge.</summary>
            public int bearingY;
            /// <summary>Horizontal advance width.</summary>
            public float advanceX;
            /// <summary>RGBA pixel data (4 bytes per pixel).</summary>
            public byte[] rgbaPixels;
        }

        private static readonly FastIntDictionary<int[]> sequenceCache = new();

        /// <summary>Computes a hash value for an emoji codepoint sequence.</summary>
        /// <param name="codepoints">The codepoint sequence to hash.</param>
        /// <returns>FNV-1a hash of the sequence.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeSequenceHash(ReadOnlySpan<int> codepoints)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < codepoints.Length; i++)
                {
                    hash = (hash ^ (uint)codepoints[i]) * 16777619;
                }
                return hash;
            }
        }

        /// <summary>Registers an emoji codepoint sequence in the cache.</summary>
        /// <param name="codepoints">The codepoint sequence to register.</param>
        /// <returns>Hash value that can be used as a glyph ID.</returns>
        public static uint RegisterSequence(ReadOnlySpan<int> codepoints)
        {
            uint hash = ComputeSequenceHash(codepoints);
            int hashInt = (int)hash;

            if (!sequenceCache.ContainsKey(hashInt))
            {
                var copy = new int[codepoints.Length];
                codepoints.CopyTo(copy);
                sequenceCache[hashInt] = copy;
            }

            return hash;
        }

        /// <summary>Retrieves a cached codepoint sequence by its hash.</summary>
        /// <param name="hash">Hash value from <see cref="RegisterSequence"/>.</param>
        /// <param name="codepoints">The retrieved codepoint array.</param>
        /// <returns>True if the sequence was found in cache.</returns>
        public static bool TryGetSequence(uint hash, out int[] codepoints)
        {
            return sequenceCache.TryGetValue((int)hash, out codepoints);
        }

        [ThreadStatic] private static int[] tempCodepointBuffer;

        /// <summary>Checks if the browser supports a ZWJ (Zero Width Joiner) emoji sequence.</summary>
        /// <param name="codepoints">The codepoint sequence containing ZWJ.</param>
        /// <param name="pixelSize">Pixel size for measurement.</param>
        /// <returns>True if the browser renders the sequence as a single glyph.</returns>
        public static bool IsZwjSupported(ReadOnlySpan<int> codepoints, int pixelSize)
        {
            if (!IsSupported || codepoints.IsEmpty)
                return true;

            if (tempCodepointBuffer == null || tempCodepointBuffer.Length < codepoints.Length)
                tempCodepointBuffer = new int[Math.Max(codepoints.Length, 16)];

            codepoints.CopyTo(tempCodepointBuffer);

            GCHandle handle = GCHandle.Alloc(tempCodepointBuffer, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                return JS_BrowserEmoji_IsZwjSupported(ptr, codepoints.Length, pixelSize);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Measures the width of an emoji sequence.</summary>
        /// <param name="codepoints">The codepoint sequence to measure.</param>
        /// <param name="pixelSize">Font size in pixels.</param>
        /// <returns>Width in pixels, or 0 if rendering is unsupported.</returns>
        public static float MeasureEmoji(ReadOnlySpan<int> codepoints, int pixelSize)
        {
            if (!IsSupported || codepoints.IsEmpty)
                return 0;

            if (tempCodepointBuffer == null || tempCodepointBuffer.Length < codepoints.Length)
                tempCodepointBuffer = new int[Math.Max(codepoints.Length, 16)];

            codepoints.CopyTo(tempCodepointBuffer);

            GCHandle handle = GCHandle.Alloc(tempCodepointBuffer, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                return JS_BrowserEmoji_MeasureEmoji(ptr, codepoints.Length, pixelSize);
            }
            finally
            {
                handle.Free();
            }
        }

        [ThreadStatic] private static int[] metricsBuffer;

        [ThreadStatic] private static byte[] s_PixelBuffer;

        private static int MinPixelBufferSize
        {
            get
            {
                int size = EmojiFont.Instance?.EmojiPixelSize ?? EmojiFont.DefaultSize;
                return size * size * 4;
            }
        }

        /// <summary>Renders a single emoji sequence to RGBA pixels.</summary>
        /// <param name="codepoints">The codepoint sequence to render.</param>
        /// <param name="pixelSize">Font size in pixels.</param>
        /// <param name="result">Rendered emoji data including metrics and pixels.</param>
        /// <returns>True if rendering succeeded.</returns>
        public static bool TryRenderEmoji(ReadOnlySpan<int> codepoints, int pixelSize, out RenderedEmoji result)
        {
            result = default;

            if (!IsSupported || codepoints.IsEmpty)
                return false;

            if (tempCodepointBuffer == null || tempCodepointBuffer.Length < codepoints.Length)
                tempCodepointBuffer = new int[Math.Max(codepoints.Length, 16)];

            codepoints.CopyTo(tempCodepointBuffer);

            metricsBuffer ??= new int[5];

            GCHandle cpHandle = GCHandle.Alloc(tempCodepointBuffer, GCHandleType.Pinned);
            GCHandle metricsHandle = GCHandle.Alloc(metricsBuffer, GCHandleType.Pinned);

            try
            {
                IntPtr cpPtr = cpHandle.AddrOfPinnedObject();
                IntPtr metricsPtr = metricsHandle.AddrOfPinnedObject();

                IntPtr pixelsPtr = JS_BrowserEmoji_RenderEmoji(cpPtr, codepoints.Length, pixelSize, metricsPtr);

                int width = metricsBuffer[0];
                int height = metricsBuffer[1];
                int bearingX = metricsBuffer[2];
                int bearingY = metricsBuffer[3];
                float advanceX = metricsBuffer[4] / 64f;

                if (width == 0 || height == 0 || pixelsPtr == IntPtr.Zero)
                {
                    result = new RenderedEmoji
                    {
                        width = 0,
                        height = 0,
                        bearingX = 0,
                        bearingY = 0,
                        advanceX = advanceX,
                        rgbaPixels = null
                    };
                    return false;
                }

                int dataSize = width * height * 4;

                if (s_PixelBuffer == null || s_PixelBuffer.Length < dataSize)
                    s_PixelBuffer = new byte[Math.Max(dataSize, MinPixelBufferSize)];

                Marshal.Copy(pixelsPtr, s_PixelBuffer, 0, dataSize);

                JS_BrowserEmoji_FreeRenderedData();

                result = new RenderedEmoji
                {
                    width = width,
                    height = height,
                    bearingX = bearingX,
                    bearingY = bearingY,
                    advanceX = advanceX,
                    rgbaPixels = s_PixelBuffer
                };

                return true;
            }
            finally
            {
                cpHandle.Free();
                metricsHandle.Free();
            }
        }

        /// <summary>Renders an emoji by its cached hash value.</summary>
        /// <param name="hash">Hash from <see cref="RegisterSequence"/>.</param>
        /// <param name="pixelSize">Font size in pixels.</param>
        /// <param name="result">Rendered emoji data.</param>
        /// <returns>True if the hash was found and rendering succeeded.</returns>
        public static bool TryRenderEmojiByHash(uint hash, int pixelSize, out RenderedEmoji result)
        {
            result = default;

            if (!TryGetSequence(hash, out var codepoints))
                return false;

            return TryRenderEmoji(codepoints.AsSpan(), pixelSize, out result);
        }

        #region Batch Rendering

        [ThreadStatic] private static int[] s_BatchCodepoints;
        [ThreadStatic] private static int[] s_BatchOffsets;
        [ThreadStatic] private static int[] s_BatchLengths;
        [ThreadStatic] private static int[] s_BatchMetrics;
        [ThreadStatic] private static int[] s_BatchPixelOffsets;

        private const int InitialBatchCapacity = 256;
        private const int MetricsPerEmoji = 5;

        /// <summary>Result from batch emoji rendering.</summary>
        public struct BatchRenderResult
        {
            /// <summary>Number of successfully rendered emoji.</summary>
            public int count;
            /// <summary>Pointer to combined pixel data (freed by <see cref="FreeBatchData"/>).</summary>
            public IntPtr pixelsPtr;
            /// <summary>Total size of pixel data in bytes.</summary>
            public int totalPixelSize;
        }

        /// <summary>Renders multiple emoji sequences in a single batch.</summary>
        /// <param name="hashes">List of sequence hashes from <see cref="RegisterSequence"/>.</param>
        /// <param name="pixelSize">Font size in pixels.</param>
        /// <param name="result">Batch render result with pixel data pointer.</param>
        /// <returns>True if batch rendering succeeded.</returns>
        /// <remarks>Call <see cref="FreeBatchData"/> when done with the pixel data.</remarks>
        public static bool TryRenderEmojiBatch(List<uint> hashes, int pixelSize, out BatchRenderResult result)
        {
            result = default;

            if (!IsSupported || hashes == null || hashes.Count == 0)
                return false;

            int count = hashes.Count;

            int totalCodepoints = 0;
            for (int i = 0; i < count; i++)
            {
                if (TryGetSequence(hashes[i], out var seq))
                    totalCodepoints += seq.Length;
            }

            if (totalCodepoints == 0)
            {
                Cat.MeowWarn($"[WebGLEmoji] TryRenderEmojiBatch: No sequences found for {count} hashes. Cache size: {sequenceCache.Count}");
                return false;
            }

            EnsureBatchBuffers(count, totalCodepoints);

            int cpIndex = 0;
            int validCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (!TryGetSequence(hashes[i], out var seq))
                    continue;

                s_BatchOffsets[validCount] = cpIndex;
                s_BatchLengths[validCount] = seq.Length;

                for (int j = 0; j < seq.Length; j++)
                    s_BatchCodepoints[cpIndex++] = seq[j];

                validCount++;
            }

            if (validCount == 0)
                return false;

            var cpHandle = GCHandle.Alloc(s_BatchCodepoints, GCHandleType.Pinned);
            var offsetsHandle = GCHandle.Alloc(s_BatchOffsets, GCHandleType.Pinned);
            var lengthsHandle = GCHandle.Alloc(s_BatchLengths, GCHandleType.Pinned);
            var metricsHandle = GCHandle.Alloc(s_BatchMetrics, GCHandleType.Pinned);
            var pixelOffsetsHandle = GCHandle.Alloc(s_BatchPixelOffsets, GCHandleType.Pinned);

            try
            {
                var pixelsPtr = JS_BrowserEmoji_RenderEmojiBatch(
                    cpHandle.AddrOfPinnedObject(),
                    offsetsHandle.AddrOfPinnedObject(),
                    lengthsHandle.AddrOfPinnedObject(),
                    validCount,
                    pixelSize,
                    metricsHandle.AddrOfPinnedObject(),
                    pixelOffsetsHandle.AddrOfPinnedObject());

                result = new BatchRenderResult
                {
                    count = validCount,
                    pixelsPtr = pixelsPtr,
                    totalPixelSize = pixelsPtr != IntPtr.Zero ? GetTotalPixelSize(validCount) : 0
                };

                if (pixelsPtr == IntPtr.Zero)
                    Cat.MeowWarn($"[WebGLEmoji] JS_BrowserEmoji_RenderEmojiBatch returned null for {validCount} emoji");

                return pixelsPtr != IntPtr.Zero;
            }
            finally
            {
                cpHandle.Free();
                offsetsHandle.Free();
                lengthsHandle.Free();
                metricsHandle.Free();
                pixelOffsetsHandle.Free();
            }
        }

        private static void EnsureBatchBuffers(int count, int totalCodepoints)
        {
            if (s_BatchCodepoints == null || s_BatchCodepoints.Length < totalCodepoints)
                s_BatchCodepoints = new int[Math.Max(totalCodepoints, InitialBatchCapacity * 8)];

            if (s_BatchOffsets == null || s_BatchOffsets.Length < count)
                s_BatchOffsets = new int[Math.Max(count, InitialBatchCapacity)];

            if (s_BatchLengths == null || s_BatchLengths.Length < count)
                s_BatchLengths = new int[Math.Max(count, InitialBatchCapacity)];

            if (s_BatchMetrics == null || s_BatchMetrics.Length < count * MetricsPerEmoji)
                s_BatchMetrics = new int[Math.Max(count * MetricsPerEmoji, InitialBatchCapacity * MetricsPerEmoji)];

            if (s_BatchPixelOffsets == null || s_BatchPixelOffsets.Length < count)
                s_BatchPixelOffsets = new int[Math.Max(count, InitialBatchCapacity)];
        }

        private static int GetTotalPixelSize(int count)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                int w = s_BatchMetrics[i * MetricsPerEmoji];
                int h = s_BatchMetrics[i * MetricsPerEmoji + 1];
                total += w * h * 4;
            }
            return total;
        }

        /// <summary>Gets metrics for an emoji from a batch render result.</summary>
        /// <param name="index">Index of the emoji in the batch (0-based).</param>
        /// <param name="width">Output width in pixels.</param>
        /// <param name="height">Output height in pixels.</param>
        /// <param name="bearingX">Output horizontal bearing.</param>
        /// <param name="bearingY">Output vertical bearing.</param>
        /// <param name="advanceX">Output horizontal advance.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBatchMetrics(int index, out int width, out int height,
            out int bearingX, out int bearingY, out float advanceX)
        {
            int baseIdx = index * MetricsPerEmoji;
            width = s_BatchMetrics[baseIdx];
            height = s_BatchMetrics[baseIdx + 1];
            bearingX = s_BatchMetrics[baseIdx + 2];
            bearingY = s_BatchMetrics[baseIdx + 3];
            advanceX = s_BatchMetrics[baseIdx + 4] / 64f;
        }

        /// <summary>Gets the byte offset for an emoji's pixel data within the batch buffer.</summary>
        /// <param name="index">Index of the emoji in the batch (0-based).</param>
        /// <returns>Byte offset from the start of the pixel data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetBatchPixelOffset(int index)
        {
            return s_BatchPixelOffsets[index];
        }

        /// <summary>Frees the pixel data allocated by the last batch render operation.</summary>
        /// <remarks>Must be called after processing batch render results.</remarks>
        public static void FreeBatchData()
        {
            JS_BrowserEmoji_FreeRenderedData();
        }

        #endregion
    }
}
#endif