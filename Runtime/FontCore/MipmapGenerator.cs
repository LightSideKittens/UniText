using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// CPU-side mipmap generation for a single slice of a Texture2DArray.
    /// Uses Burst-compiled box-filter (2×2 average) downsampling for RGBA32 textures.
    /// Mip 0→1 (the largest) runs as IJobParallelFor across rows for multi-core + SIMD.
    /// Remaining mip levels run sequentially (diminishing returns on small sizes).
    /// </summary>
    internal static class MipmapGenerator
    {
        /// <summary>
        /// Generates mipmaps for all dirty slices in one batched operation.
        /// Mip 0→1 for ALL dirty pages runs as one parallel job (rows × pages combined).
        /// Returns number of dirty pages processed.
        /// </summary>
        public static unsafe int GenerateForSlices(Texture2DArray atlas, ulong dirtyMask)
        {
            if (dirtyMask == 0) return 0;

            int mipCount = atlas.mipmapCount;
            if (mipCount <= 1) return 0;

            int w = atlas.width;
            int h = atlas.height;
            int dstW = Math.Max(1, w >> 1);
            int dstH = Math.Max(1, h >> 1);

            int dirtyCount = 0;
            {
                ulong tmp = dirtyMask;
                while (tmp != 0) { dirtyCount++; tmp &= tmp - 1; }
            }
            var dirtyIndices = new NativeArray<int>(dirtyCount, Allocator.TempJob);
            {
                ulong bits = dirtyMask;
                int idx = 0;
                while (bits != 0)
                {
                    int bit = 0;
                    ulong b = bits;
                    while ((b & 1) == 0) { bit++; b >>= 1; }
                    dirtyIndices[idx++] = bit;
                    bits &= bits - 1;
                }
            }

            var srcPtrs = new NativeArray<long>(dirtyCount, Allocator.TempJob);
            var dstPtrs = new NativeArray<long>(dirtyCount, Allocator.TempJob);
            for (int i = 0; i < dirtyCount; i++)
            {
                int slice = dirtyIndices[i];
                srcPtrs[i] = (long)atlas.GetPixelData<byte>(0, slice).GetUnsafeReadOnlyPtr();
                dstPtrs[i] = (long)atlas.GetPixelData<byte>(1, slice).GetUnsafePtr();
            }

            int totalRows = dirtyCount * dstH;
            new BoxFilterBatchJob
            {
                srcPtrs = (long*)srcPtrs.GetUnsafeReadOnlyPtr(),
                dstPtrs = (long*)dstPtrs.GetUnsafePtr(),
                srcW = w,
                dstW = dstW,
                dstH = dstH,
            }.Schedule(totalRows, 64).Complete();

            srcPtrs.Dispose();
            dstPtrs.Dispose();

            for (int mip = 2; mip < mipCount; mip++)
            {
                int mSrcW = Math.Max(1, w >> (mip - 1));
                int mDstW = Math.Max(1, w >> mip);
                int mDstH = Math.Max(1, h >> mip);

                for (int i = 0; i < dirtyCount; i++)
                {
                    int slice = dirtyIndices[i];
                    var srcData = atlas.GetPixelData<byte>(mip - 1, slice);
                    var dstData = atlas.GetPixelData<byte>(mip, slice);
                    BoxFilterSmall(
                        (uint*)srcData.GetUnsafeReadOnlyPtr(),
                        (uint*)dstData.GetUnsafePtr(),
                        mSrcW, mDstW, mDstH);
                }
            }

            dirtyIndices.Dispose();
            return dirtyCount;
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct BoxFilterBatchJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, ReadOnly] public unsafe long* srcPtrs;
            [NativeDisableUnsafePtrRestriction] public unsafe long* dstPtrs;
            public int srcW;
            public int dstW;
            public int dstH;

            public unsafe void Execute(int index)
            {
                int pageLocal = index / dstH;
                int y = index - pageLocal * dstH;

                uint* src = (uint*)srcPtrs[pageLocal];
                uint* dst = (uint*)dstPtrs[pageLocal];

                int sy0 = y * 2;
                int sy1 = Math.Min(sy0 + 1, dstH * 2 - 1);
                uint* row0 = src + sy0 * srcW;
                uint* row1 = src + sy1 * srcW;
                uint* dstRow = dst + y * dstW;

                for (int x = 0; x < dstW; x++)
                {
                    int sx = x * 2;
                    int sx1 = Math.Min(sx + 1, srcW - 1);

                    uint c00 = row0[sx];
                    uint c10 = row0[sx1];
                    uint c01 = row1[sx];
                    uint c11 = row1[sx1];

                    uint r = ((c00 & 0xFFu) + (c10 & 0xFFu) + (c01 & 0xFFu) + (c11 & 0xFFu) + 2u) >> 2;
                    uint g = (((c00 >> 8) & 0xFFu) + ((c10 >> 8) & 0xFFu) + ((c01 >> 8) & 0xFFu) + ((c11 >> 8) & 0xFFu) + 2u) >> 2;
                    uint b = (((c00 >> 16) & 0xFFu) + ((c10 >> 16) & 0xFFu) + ((c01 >> 16) & 0xFFu) + ((c11 >> 16) & 0xFFu) + 2u) >> 2;
                    uint a = ((c00 >> 24) + (c10 >> 24) + (c01 >> 24) + (c11 >> 24) + 2u) >> 2;

                    dstRow[x] = r | (g << 8) | (b << 16) | (a << 24);
                }
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct BoxFilterRowJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, ReadOnly] public unsafe uint* src;
            [NativeDisableUnsafePtrRestriction] public unsafe uint* dst;
            public int srcW;
            public int dstW;
            public int dstH;

            public unsafe void Execute(int y)
            {
                int sy0 = y * 2;
                int sy1 = Math.Min(sy0 + 1, dstH * 2 - 1);
                uint* row0 = src + sy0 * srcW;
                uint* row1 = src + sy1 * srcW;
                uint* dstRow = dst + y * dstW;

                int lastSx = (dstW - 1) * 2;
                for (int x = 0; x < dstW; x++)
                {
                    int sx = x * 2;
                    int sx1 = Math.Min(sx + 1, srcW - 1);

                    uint c00 = row0[sx];
                    uint c10 = row0[sx1];
                    uint c01 = row1[sx];
                    uint c11 = row1[sx1];

                    uint r = ((c00 & 0xFFu) + (c10 & 0xFFu) + (c01 & 0xFFu) + (c11 & 0xFFu) + 2u) >> 2;
                    uint g = (((c00 >> 8) & 0xFFu) + ((c10 >> 8) & 0xFFu) + ((c01 >> 8) & 0xFFu) + ((c11 >> 8) & 0xFFu) + 2u) >> 2;
                    uint b = (((c00 >> 16) & 0xFFu) + ((c10 >> 16) & 0xFFu) + ((c01 >> 16) & 0xFFu) + ((c11 >> 16) & 0xFFu) + 2u) >> 2;
                    uint a = ((c00 >> 24) + (c10 >> 24) + (c01 >> 24) + (c11 >> 24) + 2u) >> 2;

                    dstRow[x] = r | (g << 8) | (b << 16) | (a << 24);
                }
            }
        }

        [BurstCompile]
        private static unsafe void BoxFilterSmall(uint* src, uint* dst, int srcW, int dstW, int dstH)
        {
            int srcH = dstH * 2;
            for (int y = 0; y < dstH; y++)
            {
                int sy0 = y * 2;
                int sy1 = Math.Min(sy0 + 1, srcH - 1);
                uint* row0 = src + sy0 * srcW;
                uint* row1 = src + sy1 * srcW;
                uint* dstRow = dst + y * dstW;

                for (int x = 0; x < dstW; x++)
                {
                    int sx = x * 2;
                    int sx1 = Math.Min(sx + 1, srcW - 1);

                    uint c00 = row0[sx];
                    uint c10 = row0[sx1];
                    uint c01 = row1[sx];
                    uint c11 = row1[sx1];

                    uint r = ((c00 & 0xFFu) + (c10 & 0xFFu) + (c01 & 0xFFu) + (c11 & 0xFFu) + 2u) >> 2;
                    uint g = (((c00 >> 8) & 0xFFu) + ((c10 >> 8) & 0xFFu) + ((c01 >> 8) & 0xFFu) + ((c11 >> 8) & 0xFFu) + 2u) >> 2;
                    uint b = (((c00 >> 16) & 0xFFu) + ((c10 >> 16) & 0xFFu) + ((c01 >> 16) & 0xFFu) + ((c11 >> 16) & 0xFFu) + 2u) >> 2;
                    uint a = ((c00 >> 24) + (c10 >> 24) + (c01 >> 24) + (c11 >> 24) + 2u) >> 2;

                    dstRow[x] = r | (g << 8) | (b << 16) | (a << 24);
                }
            }
        }
    }
}
