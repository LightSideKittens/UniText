using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;

namespace LightSide
{
    internal sealed class GlyphAtlas : IDisposable
    {
        public const float Pad = 0.5f;
        public const int PageStride = 16384;

        /// <summary>
        /// When true, SDF/MSDF jobs run single-threaded via job.Run() instead of Schedule().
        /// Useful for benchmarking without parallelism (e.g. WebGL parity).
        /// </summary>
        internal static bool ForceSingleThreaded;

        private static readonly int[] defaultTileSizes = { 64, 128, 256 };
        private readonly int[] tileSizes;
        private readonly int gridUnit;
        private readonly int colSlots;
        private readonly int numSizeClasses;
        internal const int PageSize = 2048;
        private Texture2DArray atlasArray;
        private IntPtr cachedNativeTexPtr;
        private int sliceCount;
        private readonly FastLongDictionary<GlyphEntry> entries = new();

        private LinkedList<long>[] evictableLists;
        private FastLongDictionary<LinkedListNode<long>>[] evictableNodeMaps;

        private readonly List<int> pageEntryCount = new();
        private readonly List<int> pageLiveCount = new();

        private readonly List<PendingGlyph> pending = new();
        private PooledBuffer<GlyphCurveCache.Segment> pendingSegments;

        private struct Shelf
        {
            public int pageIndex;
            public int y;
            public int height;
            public int nextX;
        }

        private readonly List<Shelf> shelves = new();
        private readonly List<int> pageUsedHeight = new();

        public const int DefaultBandPixels = 3;

        public struct GlyphEntry
        {
            public int encodedTile;
            public int pageIndex;
            internal int refCount;
            internal int baseFontHash;
            internal int computedBandPx;
            public GlyphMetrics metrics;
            /// <summary>Rendered pixel width (emoji only, 0 for SDF).</summary>
            public int pixelWidth;
            /// <summary>Rendered pixel height (emoji only, 0 for SDF).</summary>
            public int pixelHeight;
        }

        private struct TileSlot
        {
            public int pageIndex;
            public int encodedTile;
        }

        private struct PendingGlyph
        {
            public long key;
            public int pageIndex;
            public int encodedTile;
            public float aspect;
            public float glyphH;
            public int segmentOffset;
            public int segmentCount;
            public int bandPixels;
        }

        private struct PendingEmojiGlyph
        {
            public long key;
            public int pageIndex;
            public int encodedTile;
            public byte[] rgbaPixels;
            public int pixelWidth;
            public int pixelHeight;
            public bool isBGRA;
        }

        private readonly List<PendingEmojiGlyph> pendingEmoji = new();

        private List<TileSlot>[] freeTiles;

        private readonly UniTextBase.RenderModee mode;
        private readonly bool isEmoji;
        private readonly TextureFormat textureFormat;
        private readonly bool hasMipmaps;
        private readonly bool isLinear;
        private readonly FilterMode atlasFilterMode;
        private readonly float atlasMipMapBias;
        private readonly int tileGutter;

        private static GlyphAtlas sdfInstance;
        private static GlyphAtlas msdfInstance;
        private static GlyphAtlas emojiInstance;

        public static GlyphAtlas GetInstance(UniTextBase.RenderModee mode) => mode switch
        {
            UniTextBase.RenderModee.SDF => sdfInstance ??= new GlyphAtlas(UniTextBase.RenderModee.SDF),
            UniTextBase.RenderModee.MSDF => msdfInstance ??= new GlyphAtlas(UniTextBase.RenderModee.MSDF),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        /// <summary>Returns the emoji atlas instance, or null if not yet created.</summary>
        public static GlyphAtlas Emoji => emojiInstance;

        /// <summary>
        /// Creates the singleton emoji atlas. Must be called exactly once during EmojiFont initialization.
        /// </summary>
        internal static GlyphAtlas CreateEmojiInstance(int emojiPixelSize)
        {
            if (emojiInstance != null)
                throw new InvalidOperationException("Emoji atlas already created");
            emojiInstance = new GlyphAtlas(emojiPixelSize);
            return emojiInstance;
        }

        internal static void ForEachInstance(Action<GlyphAtlas> action)
        {
            if (sdfInstance != null) action(sdfInstance);
            if (msdfInstance != null) action(msdfInstance);
            if (emojiInstance != null) action(emojiInstance);
        }

#if UNITY_EDITOR
        static GlyphAtlas() => Reseter.UnmanagedCleaning += () =>
        {
            sdfInstance?.Dispose();
            msdfInstance?.Dispose();
            emojiInstance?.Dispose();
        };
#endif

        internal const int EmojiGutter = 4;

        /// <summary>Constructor for SDF/MSDF atlas.</summary>
        private GlyphAtlas(UniTextBase.RenderModee mode)
        {
            this.mode = mode;
            textureFormat = mode == UniTextBase.RenderModee.MSDF ? TextureFormat.RGBAHalf : TextureFormat.RHalf;
            hasMipmaps = false;
            isLinear = true;
            atlasFilterMode = FilterMode.Bilinear;
            atlasMipMapBias = 0f;
            tileSizes = defaultTileSizes;
            gridUnit = defaultTileSizes[0];
            colSlots = PageSize / defaultTileSizes[0];
            numSizeClasses = 3;
            InitCollections();
            pendingSegments = default;
            pendingSegments.EnsureCapacity(4096);
        }

        /// <summary>Constructor for emoji atlas. Tile size = emojiPixelSize, content area = tileSize - 2*gutter.</summary>
        private GlyphAtlas(int emojiPixelSize)
        {
            isEmoji = true;
            textureFormat = TextureFormat.RGBA32;
            hasMipmaps = true;
            isLinear = false;
            atlasFilterMode = FilterMode.Trilinear;
            atlasMipMapBias = -0.5f;
            tileGutter = EmojiGutter;
            int tileSize = emojiPixelSize + 2 * EmojiGutter;
            tileSizes = new[] { tileSize };
            gridUnit = tileSize;
            colSlots = PageSize / tileSize;
            numSizeClasses = 1;
            InitCollections();
        }

        private void InitCollections()
        {
            evictableLists = new LinkedList<long>[numSizeClasses];
            evictableNodeMaps = new FastLongDictionary<LinkedListNode<long>>[numSizeClasses];
            freeTiles = new List<TileSlot>[numSizeClasses];
            for (int i = 0; i < numSizeClasses; i++)
            {
                evictableLists[i] = new LinkedList<long>();
                evictableNodeMaps[i] = new FastLongDictionary<LinkedListNode<long>>();
                freeTiles[i] = new List<TileSlot>();
            }
        }

        public static long MakeKey(long varHash48, uint glyphIndex) =>
            (varHash48 << 16) | (glyphIndex & 0xFFFF);

        /// <summary>Computes default varHash48 for a non-variable font.</summary>
        public static long DefaultVarHash(int fontDataHash) =>
            (long)fontDataHash & 0xFFFF_FFFFFFFF;

        /// <summary>
        /// Computes varHash48 for a variable font with specific axis values.
        /// Uses FNV-1a to mix fontDataHash with axis values.
        /// Returns DefaultVarHash if axisValues is empty.
        /// </summary>
        public static long ComputeVarHash48(int fontDataHash, ReadOnlySpan<float> axisValues)
        {
            if (axisValues.Length == 0)
                return DefaultVarHash(fontDataHash);

            unchecked
            {
                const long fnvOffset = unchecked((long)0xCBF29CE484222325);
                const long fnvPrime = 0x100000001B3;

                long h = fnvOffset;
                h = (h ^ fontDataHash) * fnvPrime;
                for (int i = 0; i < axisValues.Length; i++)
                {
                    int bits = BitConverter.SingleToInt32Bits(axisValues[i]);
                    h = (h ^ bits) * fnvPrime;
                }
                return h & 0xFFFF_FFFFFFFF;
            }
        }

        public static int ClassifyTileSize(ReadOnlySpan<GlyphCurveCache.Segment> segments, float aspect, float glyphH, float detailMultiplier = 1f)
        {
            int n = segments.Length;
            int size;

            if (n <= 8)
            {
                size = defaultTileSizes[0];
            }
            else
            {
                float totalChordLen2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float ex = segments[i].p2x;
                    float ey = segments[i].p2y;
                    float dx = ex - segments[i].p0x;
                    float dy = ey - segments[i].p0y;
                    totalChordLen2 += dx * dx + dy * dy;
                }

                float area = Math.Max(aspect, 0.01f);
                float detail = totalChordLen2 * n / area * detailMultiplier;

                if (detail < 100f) size = defaultTileSizes[0];
                else if (detail < 500f) size = defaultTileSizes[1];
                else size = defaultTileSizes[2];
            }

            const float minTexels = 3f;
            float minDim = Math.Min(aspect, 1f);
            float padGlyph = Pad / Math.Max(glyphH, 1e-6f);
            float totalExtent = Math.Max(aspect, 1f) + 2f * padGlyph;

            while (size < defaultTileSizes[2])
            {
                if (minDim / totalExtent * size >= minTexels) break;
                size = size <= defaultTileSizes[0] ? defaultTileSizes[1] : defaultTileSizes[2];
            }

            return size;
        }

        private int SizeClassIndex(int tileSize)
        {
            for (int i = 0; i < numSizeClasses; i++)
                if (tileSizes[i] == tileSize) return i;
            return 0;
        }

        private int GetSizeClassFromEncoded(int encodedTile) => encodedTile / 4096;

        public int TileSizeFromEncoded(int encodedTile) => tileSizes[encodedTile / 4096];

        public void ReservePendingSegments(int additionalCount)
        {
            pendingSegments.EnsureCapacity(pendingSegments.count + additionalCount);
        }

        public GlyphEntry EnsureGlyph(long varHash48, uint glyphIndex, int baseFontHash,
            in GlyphCurveCache.GlyphCurveData curveData, ReadOnlySpan<GlyphCurveCache.Segment> segments,
            int tileSize, float glyphH, float aspect, in GlyphMetrics glyphMetrics)
        {
            long key = MakeKey(varHash48, glyphIndex);
            if (entries.TryGetValue(key, out var existing))
                return existing;

            if (curveData.isEmpty || segments.Length == 0)
                return new GlyphEntry { encodedTile = -1, pageIndex = -1 };

            var slot = AllocateTile(tileSize);

            int segOffset = pendingSegments.count;
            int segCount = segments.Length;
            pendingSegments.EnsureCapacity(segOffset + segCount);
            segments.CopyTo(pendingSegments.data.AsSpan(segOffset));
            pendingSegments.count += segCount;

            pending.Add(new PendingGlyph
            {
                key = key,
                pageIndex = slot.pageIndex,
                encodedTile = slot.encodedTile,
                aspect = aspect,
                glyphH = glyphH,
                segmentOffset = segOffset,
                segmentCount = segCount,
                bandPixels = DefaultBandPixels
            });

            var entry = new GlyphEntry
            {
                encodedTile = slot.encodedTile,
                pageIndex = slot.pageIndex,
                refCount = 0,
                baseFontHash = baseFontHash,
                computedBandPx = DefaultBandPixels,
                metrics = glyphMetrics
            };
            entries[key] = entry;
            pageEntryCount[slot.pageIndex]++;

            return entry;
        }

        /// <summary>
        /// Adds an emoji glyph to the atlas. Takes ownership of the pixel buffer —
        /// it will be returned to UniTextArrayPool&lt;byte&gt; after FlushPending copies the data.
        /// Caller must provide a pooled buffer (UniTextArrayPool&lt;byte&gt;.Rent).
        /// </summary>
        public GlyphEntry EnsureEmojiGlyph(long varHash48, uint glyphIndex, int baseFontHash,
            byte[] pixels, int w, int h, bool isBGRA, in GlyphMetrics glyphMetrics)
        {
            long key = MakeKey(varHash48, glyphIndex);
            if (entries.TryGetValue(key, out var existing))
            {
                if (pixels != null)
                    UniTextArrayPool<byte>.Return(pixels);
                return existing;
            }

            if (pixels == null || w == 0 || h == 0)
                return new GlyphEntry { encodedTile = -1, pageIndex = -1 };

            var slot = AllocateTile(tileSizes[0]);

            pendingEmoji.Add(new PendingEmojiGlyph
            {
                key = key,
                pageIndex = slot.pageIndex,
                encodedTile = slot.encodedTile,
                rgbaPixels = pixels,
                pixelWidth = w,
                pixelHeight = h,
                isBGRA = isBGRA
            });

            var entry = new GlyphEntry
            {
                encodedTile = slot.encodedTile,
                pageIndex = slot.pageIndex,
                refCount = 0,
                baseFontHash = baseFontHash,
                metrics = glyphMetrics,
                pixelWidth = w,
                pixelHeight = h
            };
            entries[key] = entry;
            pageEntryCount[slot.pageIndex]++;

            return entry;
        }

        public bool TryGetEntry(long varHash48, uint glyphIndex, out GlyphEntry entry)
        {
            return entries.TryGetValue(MakeKey(varHash48, glyphIndex), out entry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetEntry(long key, out GlyphEntry entry)
        {
            return entries.TryGetValue(key, out entry);
        }

        public void UpgradeGlyphBand(long key,
            ReadOnlySpan<GlyphCurveCache.Segment> segments,
            float glyphH, float aspect, int requiredBandPx)
        {
            if (!entries.TryGetValue(key, out var entry)) return;
            if (requiredBandPx <= entry.computedBandPx) return;

            int segOffset = pendingSegments.count;
            pendingSegments.EnsureCapacity(segOffset + segments.Length);
            segments.CopyTo(pendingSegments.data.AsSpan(segOffset));
            pendingSegments.count += segments.Length;

            pending.Add(new PendingGlyph
            {
                key = key,
                pageIndex = entry.pageIndex,
                encodedTile = entry.encodedTile,
                aspect = aspect,
                glyphH = glyphH,
                segmentOffset = segOffset,
                segmentCount = segments.Length,
                bandPixels = requiredBandPx
            });

            entry.computedBandPx = requiredBandPx;
            entries[key] = entry;
        }

        public void FlushPending()
        {
            if (isEmoji)
            {
                FlushEmojiPending();
                return;
            }

            if (pending.Count == 0) return;

            var timer = new DebugTimer();
            timer.Mark();

            var segmentsNative = new NativeArray<GlyphCurveCache.Segment>(pendingSegments.count, Allocator.TempJob);
            unsafe
            {
                fixed (void* src = pendingSegments.data)
                {
                    void* dst = segmentsNative.GetUnsafePtr();
                    UnsafeUtility.MemCpy(dst, src, pendingSegments.count * sizeof(GlyphCurveCache.Segment));
                }
            }

            var tasks = new NativeArray<SdfCore.GlyphTask>(pending.Count, Allocator.TempJob);
            int count64 = 0, count128 = 0, count256 = 0;

            for (int i = 0; i < pending.Count; i++)
            {
                var pg = pending[i];
                int sizeClass = pg.encodedTile / 4096;
                int tileSize = tileSizes[sizeClass];
                int rem = pg.encodedTile - sizeClass * 4096;
                int shelfRow = rem / colSlots;
                int tileCol = rem - shelfRow * colSlots;

                if (sizeClass == 0) count64++; else if (sizeClass == 1) count128++; else count256++;

                tasks[i] = new SdfCore.GlyphTask
                {
                    segmentOffset = pg.segmentOffset,
                    segmentCount = pg.segmentCount,
                    tileSize = tileSize,
                    aspect = pg.aspect,
                    glyphH = pg.glyphH,
                    pageIndex = pg.pageIndex,
                    tileX = tileCol * tileSize,
                    tileY = shelfRow * gridUnit,
                    bandPixels = pg.bandPixels
                };
            }

            int workerCount = JobsUtility.MaxJobThreadCount;
            int maxPixelsPerTile = 256 * 256;
            timer.Mark();

            if (mode == UniTextBase.RenderModee.SDF)
                FlushSdf(segmentsNative, tasks, workerCount, maxPixelsPerTile);
            else
                FlushMsdf(segmentsNative, tasks, workerCount, maxPixelsPerTile);
            timer.Mark();

            if (GpuUpload.IsSupported)
            {
                ulong dirtyPages = 0;
                for (int i = 0; i < pending.Count; i++)
                    if (pending[i].pageIndex < 64) dirtyPages |= 1UL << pending[i].pageIndex;
                GpuUpload.UploadDirtySlices(atlasArray, dirtyPages, 1, cachedNativeTexPtr);
            }
            else
            {
                atlasArray.Apply(false, false);
            }

            tasks.Dispose();
            segmentsNative.Dispose();

            var modeLabel = mode == UniTextBase.RenderModee.SDF ? "SDF" : "MSDF";
            Cat.Meow($"[GlyphAtlas:{modeLabel}] Flushed {pending.Count} glyphs " +
                     $"(64px:{count64} 128px:{count128} 256px:{count256}), pages:{sliceCount} | " +
                     $"setup={timer.Phase(0):F1}ms render={timer.Phase(1):F1}ms " +
                     $"upload={timer.Phase(2):F1}ms total={timer.Total:F1}ms");

            pending.Clear();
            pendingSegments.FakeClear();
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct EmojiCopyJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, ReadOnly] public unsafe long* srcPtrs;
            [NativeDisableUnsafePtrRestriction] public unsafe long* pageBasePtrs;
            [NativeDisableUnsafePtrRestriction, ReadOnly] public unsafe EmojiCopyTask* tasks;
            public int pageStride;

            internal struct EmojiCopyTask
            {
                public int pageIndex;
                public int tileX, tileY;
                public int tileSize;
                public int gutter;
                public int pixelWidth, pixelHeight;
                public int copyW, copyH;
                public byte isBGRA;
            }

            public unsafe void Execute(int i)
            {
                var t = tasks[i];
                byte* dstBase = (byte*)pageBasePtrs[t.pageIndex];
                byte* srcBase = (byte*)srcPtrs[i];

                for (int y = 0; y < t.tileSize; y++)
                    UnsafeUtility.MemClear(dstBase + (t.tileY + y) * pageStride + t.tileX * 4, t.tileSize * 4);

                int gutterX = t.tileX + t.gutter;
                int gutterY = t.tileY + t.gutter;
                int srcStride = t.pixelWidth * 4;
                int copyBytes = t.copyW * 4;

                if (t.isBGRA != 0)
                {
                    for (int y = 0; y < t.copyH; y++)
                    {
                        int srcY = t.pixelHeight - 1 - y;
                        byte* srcRow = srcBase + srcY * srcStride;
                        byte* dstRow = dstBase + (gutterY + y) * pageStride + gutterX * 4;
                        for (int x = 0; x < t.copyW; x++)
                        {
                            uint px = ((uint*)srcRow)[x];
                            uint rb = ((px & 0x00FF0000u) >> 16) | ((px & 0x000000FFu) << 16);
                            ((uint*)dstRow)[x] = (px & 0xFF00FF00u) | rb;
                        }
                    }
                }
                else
                {
                    for (int y = 0; y < t.copyH; y++)
                    {
                        int srcY = t.pixelHeight - 1 - y;
                        UnsafeUtility.MemCpy(
                            dstBase + (gutterY + y) * pageStride + gutterX * 4,
                            srcBase + srcY * srcStride,
                            copyBytes);
                    }
                }
            }
        }

        private unsafe void FlushEmojiPending()
        {
            if (pendingEmoji.Count == 0) return;

            var timer = new DebugTimer();
            timer.Mark();

            int count = pendingEmoji.Count;
            int tileSize = tileSizes[0];
            int maxContent = tileSize - 2 * tileGutter;

            var pageBasePtrs = new NativeArray<long>(sliceCount, Allocator.TempJob);
            for (int i = 0; i < sliceCount; i++)
            {
                var pageData = atlasArray.GetPixelData<byte>(0, i);
                pageBasePtrs[i] = (long)pageData.GetUnsafePtr();
            }

            var gcHandles = new System.Runtime.InteropServices.GCHandle[count];
            var srcPtrs = new NativeArray<long>(count, Allocator.TempJob);
            var tasks = new NativeArray<EmojiCopyJob.EmojiCopyTask>(count, Allocator.TempJob);

            ulong dirtyPages = 0;

            for (int i = 0; i < count; i++)
            {
                var pg = pendingEmoji[i];
                gcHandles[i] = System.Runtime.InteropServices.GCHandle.Alloc(pg.rgbaPixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                srcPtrs[i] = (long)gcHandles[i].AddrOfPinnedObject();

                DecodeTileXY(pg.encodedTile, tileSize, out int tileX, out int tileY);
                int copyW = Math.Min(pg.pixelWidth, maxContent);
                int copyH = Math.Min(pg.pixelHeight, maxContent);

                tasks[i] = new EmojiCopyJob.EmojiCopyTask
                {
                    pageIndex = pg.pageIndex,
                    tileX = tileX,
                    tileY = tileY,
                    tileSize = tileSize,
                    gutter = tileGutter,
                    pixelWidth = pg.pixelWidth,
                    pixelHeight = pg.pixelHeight,
                    copyW = copyW,
                    copyH = copyH,
                    isBGRA = pg.isBGRA ? (byte)1 : (byte)0
                };

                if (pg.pageIndex < 64) dirtyPages |= 1UL << pg.pageIndex;
            }

            new EmojiCopyJob
            {
                srcPtrs = (long*)srcPtrs.GetUnsafeReadOnlyPtr(),
                pageBasePtrs = (long*)pageBasePtrs.GetUnsafePtr(),
                tasks = (EmojiCopyJob.EmojiCopyTask*)tasks.GetUnsafeReadOnlyPtr(),
                pageStride = PageSize * 4
            }.Schedule(count, 16).Complete();

            for (int i = 0; i < count; i++)
                gcHandles[i].Free();
            srcPtrs.Dispose();
            tasks.Dispose();
            pageBasePtrs.Dispose();

            timer.Mark();

            int dirtyPageCount = 0;
            if (hasMipmaps)
            {
                dirtyPageCount = MipmapGenerator.GenerateForSlices(atlasArray, dirtyPages);
            }

            timer.Mark();

            int mipCount = hasMipmaps ? atlasArray.mipmapCount : 1;
            if (GpuUpload.IsSupported)
                GpuUpload.UploadDirtySlices(atlasArray, dirtyPages, mipCount, cachedNativeTexPtr);
            else
                atlasArray.Apply(false, false);

            timer.Mark();

            Cat.Meow($"[GlyphAtlas:Emoji] Flushed {count} glyphs, pages:{sliceCount}, dirtyPages:{dirtyPageCount} | " +
                     $"copy={timer.Phase(0):F1}ms mipgen={timer.Phase(1):F1}ms upload={timer.Phase(2):F1}ms total={timer.Total:F1}ms");

            for (int i = 0; i < pendingEmoji.Count; i++)
            {
                if (pendingEmoji[i].rgbaPixels != null)
                    UniTextArrayPool<byte>.Return(pendingEmoji[i].rgbaPixels);
            }
            pendingEmoji.Clear();
        }

        private void FlushSdf(NativeArray<GlyphCurveCache.Segment> segmentsNative,
            NativeArray<SdfCore.GlyphTask> tasks, int workerCount, int maxPixelsPerTile)
        {
            var pagePointers = new NativeArray<long>(sliceCount, Allocator.TempJob);
            unsafe
            {
                for (int i = 0; i < sliceCount; i++)
                {
                    var pageData = atlasArray.GetPixelData<ushort>(0, i);
                    pagePointers[i] = (long)pageData.GetUnsafePtr();
                }
            }

            int maxScratchFloatsPerWorker = maxPixelsPerTile * 3;
            var scratchBuffer = new NativeArray<float>(
                workerCount * maxScratchFloatsPerWorker,
                Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var job = new SdfJob
            {
                segments = segmentsNative,
                tasks = tasks,
                pagePointers = pagePointers,
                scratchBuffer = scratchBuffer,
                maxScratchFloatsPerWorker = maxScratchFloatsPerWorker,
                pageStride = PageSize
            };

            if (ForceSingleThreaded)
                job.Run(pending.Count);
            else
                job.Schedule(pending.Count, 1).Complete();

            scratchBuffer.Dispose();
            pagePointers.Dispose();
        }

        private void FlushMsdf(NativeArray<GlyphCurveCache.Segment> segmentsNative,
            NativeArray<SdfCore.GlyphTask> tasks, int workerCount, int maxPixelsPerTile)
        {
            var pagePointers = new NativeArray<long>(sliceCount, Allocator.TempJob);
            unsafe
            {
                for (int i = 0; i < sliceCount; i++)
                {
                    var pageData = atlasArray.GetPixelData<ulong>(0, i);
                    pagePointers[i] = (long)pageData.GetUnsafePtr();
                }
            }

            int maxScratchFloatsPerWorker = maxPixelsPerTile * 8;
            var scratchBuffer = new NativeArray<float>(
                workerCount * maxScratchFloatsPerWorker,
                Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var job = new MsdfJob
            {
                segments = segmentsNative,
                tasks = tasks,
                pagePointers = pagePointers,
                scratchBuffer = scratchBuffer,
                maxScratchFloatsPerWorker = maxScratchFloatsPerWorker,
                pageStride = PageSize
            };

            if (ForceSingleThreaded)
                job.Run(pending.Count);
            else
                job.Schedule(pending.Count, 1).Complete();

            scratchBuffer.Dispose();
            pagePointers.Dispose();
        }

        public void AddRef(long key)
        {
            if (entries.TryGetValue(key, out var e))
            {
                if (e.refCount == 0)
                    pageLiveCount[e.pageIndex]++;

                e.refCount++;
                entries[key] = e;

                int ci = GetSizeClassFromEncoded(e.encodedTile);
                if (evictableNodeMaps[ci].TryGetValue(key, out var node))
                {
                    evictableLists[ci].Remove(node);
                    evictableNodeMaps[ci].Remove(key);
                }
            }
        }

        public void Release(long key)
        {
            if (entries.TryGetValue(key, out var e))
            {
                if (e.refCount <= 0)
                {
                    Cat.MeowWarnFormat("[GlyphAtlas] Release: key 0x{0:X} already has refCount={1}", key, e.refCount);
                    return;
                }
                e.refCount--;
                entries[key] = e;

                if (e.refCount == 0)
                {
                    pageLiveCount[e.pageIndex]--;

                    int ci = GetSizeClassFromEncoded(e.encodedTile);
                    if (!evictableNodeMaps[ci].ContainsKey(key))
                    {
                        var node = evictableLists[ci].AddLast(key);
                        evictableNodeMaps[ci][key] = node;
                    }
                }
            }
        }

        private static List<long> clearForFontKeys;

        public void ClearForFont(int fontHash)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var key = pending[i].key;
                if (entries.TryGetValue(key, out var pe) && pe.baseFontHash == fontHash)
                {
                    var pg = pending[i];
                    if (pg.encodedTile >= 0)
                    {
                        int ci = GetSizeClassFromEncoded(pg.encodedTile);
                        freeTiles[ci].Add(new TileSlot { pageIndex = pg.pageIndex, encodedTile = pg.encodedTile });
                    }
                    pending[i] = pending[^1];
                    pending.RemoveAt(pending.Count - 1);
                }
            }

            for (int i = pendingEmoji.Count - 1; i >= 0; i--)
            {
                var key = pendingEmoji[i].key;
                if (entries.TryGetValue(key, out var pe) && pe.baseFontHash == fontHash)
                {
                    var pg = pendingEmoji[i];
                    if (pg.encodedTile >= 0)
                    {
                        int ci = GetSizeClassFromEncoded(pg.encodedTile);
                        freeTiles[ci].Add(new TileSlot { pageIndex = pg.pageIndex, encodedTile = pg.encodedTile });
                    }
                    pendingEmoji[i] = pendingEmoji[^1];
                    pendingEmoji.RemoveAt(pendingEmoji.Count - 1);
                }
            }

            var keysToRemove = clearForFontKeys ??= new List<long>();
            keysToRemove.Clear();
            foreach (var kvp in entries)
            {
                if (kvp.Value.baseFontHash == fontHash)
                    keysToRemove.Add(kvp.Key);
            }

            foreach (var key in keysToRemove)
            {
                var e = entries[key];
                entries.Remove(key);

                int ci = GetSizeClassFromEncoded(e.encodedTile);
                if (evictableNodeMaps[ci].TryGetValue(key, out var node))
                {
                    evictableLists[ci].Remove(node);
                    evictableNodeMaps[ci].Remove(key);
                }

                pageEntryCount[e.pageIndex]--;

                if (e.refCount > 0)
                    pageLiveCount[e.pageIndex]--;

                if (e.encodedTile >= 0)
                    freeTiles[ci].Add(new TileSlot { pageIndex = e.pageIndex, encodedTile = e.encodedTile });
            }

            CleanupEmptyPages();

            Cat.MeowFormat("[GlyphAtlas] ClearForFont: fontHash={0}, removed {1} entries, totalEntries={2}, pages={3}",
                fontHash, keysToRemove.Count, entries.Count, sliceCount);
        }

        public Texture AtlasTexture => atlasArray;
        public int TileGutter => tileGutter;
        /// <summary>Max content size that fits inside a tile with gutter on each side.</summary>
        public int MaxContentSize => tileSizes[0] - 2 * tileGutter;
        internal event Action<Texture> AtlasTextureChanged;
        internal static event Action<Texture> AnyAtlasTextureChanged;
        internal static event Action<GlyphAtlas> AnyAtlasCompacted;
        public int PageCount => sliceCount;
        internal int EntryCount => entries.Count;

        public static float ComputeAspect(in GlyphCurveCache.GlyphCurveData metrics)
        {
            float h = metrics.bboxMaxY - metrics.bboxMinY;
            if (h < 1e-6f) return 1f;
            float w = metrics.bboxMaxX - metrics.bboxMinX;
            return w / h;
        }

        private TileSlot AllocateTile(int tileSize)
        {
            int ci = SizeClassIndex(tileSize);

            var free = freeTiles[ci];
            if (free.Count > 0)
            {
                var slot = free[^1];
                free.RemoveAt(free.Count - 1);
                return slot;
            }

            for (int i = 0; i < shelves.Count; i++)
            {
                var shelf = shelves[i];
                if (shelf.height == tileSize && shelf.nextX + tileSize <= PageSize)
                {
                    int tileCol = shelf.nextX / tileSize;
                    int shelfRow = shelf.y / gridUnit;
                    shelf.nextX += tileSize;
                    shelves[i] = shelf;
                    return new TileSlot
                    {
                        pageIndex = shelf.pageIndex,
                        encodedTile = tileCol + shelfRow * colSlots + ci * 4096
                    };
                }
            }

            for (int p = 0; p < sliceCount; p++)
            {
                int usedH = pageUsedHeight[p];
                if (usedH + tileSize <= PageSize)
                {
                    int y = usedH;
                    shelves.Add(new Shelf { pageIndex = p, y = y, height = tileSize, nextX = tileSize });
                    pageUsedHeight[p] = y + tileSize;
                    int shelfRow = y / gridUnit;
                    return new TileSlot
                    {
                        pageIndex = p,
                        encodedTile = shelfRow * colSlots + ci * 4096
                    };
                }
            }

            if (evictableLists[ci].Count > 0)
            {
                var key = evictableLists[ci].First.Value;
                evictableLists[ci].RemoveFirst();
                evictableNodeMaps[ci].Remove(key);

                var e = entries[key];
                entries.Remove(key);

                pageEntryCount[e.pageIndex]--;

                return new TileSlot { pageIndex = e.pageIndex, encodedTile = e.encodedTile };
            }

            return GrowAtlas(tileSize);
        }

        public void PreAllocate(long estimatedTileAreaPixels)
        {
            long pageArea = (long)PageSize * PageSize;
            int estimatedNewPages = (int)((estimatedTileAreaPixels * 115 / 100 + pageArea - 1) / pageArea);
            int requestedCapacity = sliceCount + estimatedNewPages;
            int depthBefore = atlasArray != null ? atlasArray.depth : 0;
            EnsureAtlasCapacity(requestedCapacity);
            int depthAfter = atlasArray != null ? atlasArray.depth : 0;
            if (depthAfter != depthBefore)
            {
                Cat.MeowFormat("[GlyphAtlas] PreAllocate: estimatedArea={0}, newPages={1}, slices={2}, depth {3}→{4}",
                    estimatedTileAreaPixels, estimatedNewPages, sliceCount, depthBefore, depthAfter);
            }
        }

        private void EnsureAtlasCapacity(int minCapacity)
        {
            if (atlasArray != null && atlasArray.depth >= minCapacity) return;
            Cat.MeowFormat("[GlyphAtlas] EnsureAtlasCapacity: requested={0}, currentDepth={1}, slices={2}",
                minCapacity, atlasArray != null ? atlasArray.depth : 0, sliceCount);

            int newCapacity = atlasArray == null
                ? minCapacity
                : Math.Max(atlasArray.depth * 2, minCapacity);

            var gfxFormat = GraphicsFormatUtility.GetGraphicsFormat(textureFormat, !isLinear);
            var flags = TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
            if (hasMipmaps) flags |= TextureCreationFlags.MipChain;

            var newTex = new Texture2DArray(PageSize, PageSize, newCapacity, gfxFormat, flags, hasMipmaps ? -1 : 1)
            {
                filterMode = atlasFilterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                mipMapBias = atlasMipMapBias
            };

            if (atlasArray != null)
            {
                int mipCount = atlasArray.mipmapCount;
                for (int i = 0; i < sliceCount; i++)
                    for (int m = 0; m < mipCount; m++)
                    {
                        var oldData = atlasArray.GetPixelData<byte>(m, i);
                        var newData = newTex.GetPixelData<byte>(m, i);
                        NativeArray<byte>.Copy(oldData, newData);
                    }

                if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
                {
                    for (int i = 0; i < sliceCount; i++)
                        for (int m = 0; m < mipCount; m++)
                            Graphics.CopyTexture(atlasArray, i, m, newTex, i, m);
                }

                UnityEngine.Object.DestroyImmediate(atlasArray);
            }

            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
                newTex.Apply(false, false);
            atlasArray = newTex;
            cachedNativeTexPtr = newTex.GetNativeTexturePtr();
            AtlasTextureChanged?.Invoke(newTex);
            AnyAtlasTextureChanged?.Invoke(newTex);
        }

        
        private TileSlot GrowAtlas(int tileSize)
        {
            int newSliceIndex = sliceCount;

            if (atlasArray == null || sliceCount >= atlasArray.depth)
                EnsureAtlasCapacity(atlasArray == null ? 1 : atlasArray.depth * 2);

            sliceCount++;

            int ci = SizeClassIndex(tileSize);
            while (pageUsedHeight.Count < sliceCount) pageUsedHeight.Add(0);
            while (pageLiveCount.Count < sliceCount) pageLiveCount.Add(0);
            while (pageEntryCount.Count < sliceCount) pageEntryCount.Add(0);
            pageUsedHeight[newSliceIndex] = tileSize;

            shelves.Add(new Shelf { pageIndex = newSliceIndex, y = 0, height = tileSize, nextX = tileSize });

            return new TileSlot
            {
                pageIndex = newSliceIndex,
                encodedTile = ci * 4096
            };
        }

        /// <summary>
        /// Recycles pages where all entries are dead (refCount == 0).
        /// Bulk-evicts all entries on such pages and resets shelf state,
        /// making the page available for fresh allocations.
        /// Call periodically after UpdateGlyphAtlasRefCounts.
        /// </summary>
        public void TryRecyclePages()
        {
            for (int p = 0; p < sliceCount; p++)
                pageLiveCount[p] = 0;
            foreach (var kvp in entries)
            {
                if (kvp.Value.refCount > 0)
                    pageLiveCount[kvp.Value.pageIndex]++;
            }

            Cat.MeowOnce(GetHashCode().ToString(),
                "[GlyphAtlas] TryRecyclePages: entries={0}, pages={1}, depth={2}, pageLive=[{3}], pageEntry=[{4}]",
                entries.Count, sliceCount, atlasArray != null ? atlasArray.depth : 0,
                FormatPageCounts(pageLiveCount, sliceCount), FormatPageCounts(pageEntryCount, sliceCount));

            bool anyDead = false;
            for (int p = 0; p < sliceCount; p++)
            {
                if (pageLiveCount[p] == 0 && pageEntryCount[p] > 0)
                { anyDead = true; break; }
            }
            if (!anyDead) return;

            var keysToRemove = clearForFontKeys ??= new List<long>();
            keysToRemove.Clear();
            foreach (var kvp in entries)
            {
                int p = kvp.Value.pageIndex;
                if (pageLiveCount[p] == 0 && pageEntryCount[p] > 0)
                    keysToRemove.Add(kvp.Key);
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                var key = keysToRemove[i];
                var e = entries[key];
                entries.Remove(key);
                pageEntryCount[e.pageIndex]--;

                int ci = GetSizeClassFromEncoded(e.encodedTile);
                if (evictableNodeMaps[ci].TryGetValue(key, out var node))
                {
                    evictableLists[ci].Remove(node);
                    evictableNodeMaps[ci].Remove(key);
                }
            }

            Cat.MeowFormat("[GlyphAtlas] TryRecyclePages: evicted {0} dead entries, remainingEntries={1}",
                keysToRemove.Count, entries.Count);

            CleanupEmptyPages();
        }

        private void CleanupEmptyPages()
        {
            for (int p = 0; p < sliceCount; p++)
            {
                if (pageEntryCount[p] > 0 || pageUsedHeight[p] == 0) continue;

                for (int i = shelves.Count - 1; i >= 0; i--)
                {
                    if (shelves[i].pageIndex == p)
                    {
                        shelves[i] = shelves[^1];
                        shelves.RemoveAt(shelves.Count - 1);
                    }
                }

                for (int ci = 0; ci < numSizeClasses; ci++)
                {
                    var list = freeTiles[ci];
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i].pageIndex == p)
                        {
                            list[i] = list[^1];
                            list.RemoveAt(list.Count - 1);
                        }
                    }
                }

                pageUsedHeight[p] = 0;
            }
        }

        /// <summary>
        /// Trims trailing empty pages and shrinks the Texture2DArray if possible.
        /// Call after TryRecyclePages at lower frequency.
        /// </summary>
        public void TryShrinkAtlas()
        {
            int sliceCountBefore = sliceCount;
            while (sliceCount > 0)
            {
                int last = sliceCount - 1;
                bool hasKeys = pageEntryCount[last] > 0;
                bool hasHeight = pageUsedHeight[last] > 0;
                if (hasKeys || hasHeight) break;
                sliceCount--;
            }

            if (sliceCount < sliceCountBefore)
            {
                Cat.MeowFormat("[GlyphAtlas] TryShrinkAtlas: trimmed trailing empty pages {0}→{1}",
                    sliceCountBefore, sliceCount);
            }

            if (sliceCount == 0)
            {
                if (atlasArray != null)
                {
                    Cat.MeowFormat("[GlyphAtlas] TryShrinkAtlas: DESTROYING atlas (0 active pages, entries={0})",
                        entries.Count);
                    UnityEngine.Object.DestroyImmediate(atlasArray);
                    atlasArray = null;
                    cachedNativeTexPtr = IntPtr.Zero;
                    AtlasTextureChanged?.Invoke(null);
                    AnyAtlasTextureChanged?.Invoke(null);
                }
                return;
            }

            if (atlasArray != null && sliceCount <= atlasArray.depth / 2 && atlasArray.depth > 1)
            {
                int newDepth = Math.Max(sliceCount, 1);
                Cat.MeowFormat("[GlyphAtlas] TryShrinkAtlas: SHRINKING atlas depth {0}→{1}, activePages={2}, entries={3}",
                    atlasArray.depth, newDepth, sliceCount, entries.Count);
                var newTex = new Texture2DArray(PageSize, PageSize, newDepth, textureFormat, hasMipmaps, isLinear)
                {
                    filterMode = atlasFilterMode,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                    mipMapBias = atlasMipMapBias
                };

                int mipCount = atlasArray.mipmapCount;
                for (int i = 0; i < sliceCount; i++)
                    for (int m = 0; m < mipCount; m++)
                    {
                        var oldData = atlasArray.GetPixelData<byte>(m, i);
                        var newData = newTex.GetPixelData<byte>(m, i);
                        NativeArray<byte>.Copy(oldData, newData);
                    }

                if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
                {
                    for (int i = 0; i < sliceCount; i++)
                        for (int m = 0; m < mipCount; m++)
                            Graphics.CopyTexture(atlasArray, i, m, newTex, i, m);
                }
                else
                {
                    newTex.Apply(false, false);
                }

                UnityEngine.Object.DestroyImmediate(atlasArray);
                atlasArray = newTex;
                cachedNativeTexPtr = newTex.GetNativeTexturePtr();
                AtlasTextureChanged?.Invoke(newTex);
                AnyAtlasTextureChanged?.Invoke(newTex);
            }
        }

        /// <summary>
        /// Detects severe atlas fragmentation and defragments by relocating live tiles.
        /// Live entries scattered across many pages prevent TryShrinkAtlas from reclaiming memory.
        /// When at least half the pages have no live entries, we re-pack all live tiles into
        /// a compact layout by copying pixel data — no re-rasterization needed.
        /// Call after TryShrinkAtlas at the same frequency.
        /// </summary>
        public void CompactIfFragmented()
        {
            if (sliceCount < 4 || atlasArray == null) return;

            int pagesWithLive = 0;
            for (int p = 0; p < sliceCount; p++)
                if (pageLiveCount[p] > 0) pagesWithLive++;

            if (pagesWithLive == 0) return;
            if (pagesWithLive > sliceCount / 2) return;

            var compList = compactList ??= new List<CompactRecord>();
            compList.Clear();
            foreach (var kvp in entries)
            {
                if (kvp.Value.refCount > 0)
                {
                    compList.Add(new CompactRecord
                    {
                        key = kvp.Key,
                        oldPage = kvp.Value.pageIndex,
                        oldEncodedTile = kvp.Value.encodedTile,
                        refCount = kvp.Value.refCount,
                        baseFontHash = kvp.Value.baseFontHash,
                        metrics = kvp.Value.metrics,
                        pixelWidth = kvp.Value.pixelWidth,
                        pixelHeight = kvp.Value.pixelHeight
                    });
                }
            }

            if (compList.Count == 0) return;

            int oldSliceCount = sliceCount;
            var oldTex = atlasArray;

            Cat.MeowFormat("[GlyphAtlas] CompactIfFragmented: RELOCATING {0} live entries, slices={1}, livePages={2}",
                compList.Count, sliceCount, pagesWithLive);

            entries.Clear();
            for (int i = 0; i < numSizeClasses; i++)
            {
                evictableLists[i].Clear();
                evictableNodeMaps[i].Clear();
                freeTiles[i].Clear();
            }
            pageEntryCount.Clear();
            pageLiveCount.Clear();
            shelves.Clear();
            pageUsedHeight.Clear();
            sliceCount = 0;
            atlasArray = null;
            cachedNativeTexPtr = IntPtr.Zero;

            for (int i = 0; i < compList.Count; i++)
            {
                var rec = compList[i];
                int ci = GetSizeClassFromEncoded(rec.oldEncodedTile);
                int tileSize = tileSizes[ci];

                var newSlot = AllocateTile(tileSize);

                entries[rec.key] = new GlyphEntry
                {
                    encodedTile = newSlot.encodedTile,
                    pageIndex = newSlot.pageIndex,
                    refCount = rec.refCount,
                    baseFontHash = rec.baseFontHash,
                    metrics = rec.metrics,
                    pixelWidth = rec.pixelWidth,
                    pixelHeight = rec.pixelHeight
                };
                pageEntryCount[newSlot.pageIndex]++;
                pageLiveCount[newSlot.pageIndex]++;

                rec.newPage = newSlot.pageIndex;
                rec.newEncodedTile = newSlot.encodedTile;
                compList[i] = rec;
            }

            for (int i = 0; i < compList.Count; i++)
            {
                var rec = compList[i];
                int ci = GetSizeClassFromEncoded(rec.oldEncodedTile);
                int tileSize = tileSizes[ci];

                DecodeTileXY(rec.oldEncodedTile, tileSize, out int srcX, out int srcY);
                DecodeTileXY(rec.newEncodedTile, tileSize, out int dstX, out int dstY);

                CopyTilePixelsCPU(oldTex, rec.oldPage, srcX, srcY,
                                  atlasArray, rec.newPage, dstX, dstY, tileSize);
            }

            if (sliceCount > 0)
            {
                ulong allPagesMask = sliceCount >= 64 ? ~0UL : (1UL << sliceCount) - 1;
                if (hasMipmaps)
                    MipmapGenerator.GenerateForSlices(atlasArray, allPagesMask);

                int mipCount = hasMipmaps ? atlasArray.mipmapCount : 1;
                if (GpuUpload.IsSupported)
                    GpuUpload.UploadDirtySlices(atlasArray, allPagesMask, mipCount, cachedNativeTexPtr);
                else
                    atlasArray.Apply(false, false);
            }

            UnityEngine.Object.DestroyImmediate(oldTex);

            Cat.MeowFormat("[GlyphAtlas] CompactIfFragmented: done — slices {0}→{1}, depth={2}",
                oldSliceCount, sliceCount, atlasArray != null ? atlasArray.depth : 0);

            AnyAtlasCompacted?.Invoke(this);
            AtlasTextureChanged?.Invoke(atlasArray);
            AnyAtlasTextureChanged?.Invoke(atlasArray);
        }

        private struct CompactRecord
        {
            public long key;
            public int oldPage, oldEncodedTile;
            public int newPage, newEncodedTile;
            public int refCount, baseFontHash;
            public GlyphMetrics metrics;
            public int pixelWidth, pixelHeight;
        }

        private static List<CompactRecord> compactList;

        internal void DecodeTileXY(int encodedTile, int tileSize, out int x, out int y)
        {
            int remainder = encodedTile % 4096;
            int shelfRow = remainder / colSlots;
            int tileCol = remainder % colSlots;
            x = tileCol * tileSize;
            y = shelfRow * gridUnit;
        }

        private static void CopyTilePixelsCPU(
            Texture2DArray src, int srcPage, int srcX, int srcY,
            Texture2DArray dst, int dstPage, int dstX, int dstY, int tileSize)
        {
            var srcData = src.GetPixelData<byte>(0, srcPage);
            var dstData = dst.GetPixelData<byte>(0, dstPage);
            int bpp = srcData.Length / (PageSize * PageSize);
            int stride = PageSize * bpp;
            int rowBytes = tileSize * bpp;

            for (int row = 0; row < tileSize; row++)
            {
                int srcOff = (srcY + row) * stride + srcX * bpp;
                int dstOff = (dstY + row) * stride + dstX * bpp;
                NativeArray<byte>.Copy(srcData, srcOff, dstData, dstOff, rowBytes);
            }
        }

        private static string FormatPageCounts(List<int> counts, int len)
        {
            var sb = new System.Text.StringBuilder(len * 4);
            for (int i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(counts[i]);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            if (atlasArray != null)
            {
                UnityEngine.Object.DestroyImmediate(atlasArray);
                atlasArray = null;
                cachedNativeTexPtr = IntPtr.Zero;
            }
            sliceCount = 0;

            entries.Clear();
            for (int i = 0; i < numSizeClasses; i++)
            {
                evictableLists[i].Clear();
                evictableNodeMaps[i].Clear();
                freeTiles[i].Clear();
            }
            pageEntryCount.Clear();
            pageLiveCount.Clear();
            shelves.Clear();
            pageUsedHeight.Clear();
            pending.Clear();
            pendingEmoji.Clear();
            pendingSegments.Return();

            if (sdfInstance == this) sdfInstance = null;
            else if (msdfInstance == this) msdfInstance = null;
            else if (emojiInstance == this) emojiInstance = null;
        }
    }
}
