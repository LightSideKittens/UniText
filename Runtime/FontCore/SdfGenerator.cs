using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace LightSide
{
    /// <summary>
    /// Burst-compiled Job for generating single-channel SDF tiles (RHalf format).
    /// Fully self-contained: all algorithms inlined to guarantee optimal Burst codegen
    /// (no cross-struct method calls that could alter FloatMode.Fast FMA patterns).
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    internal unsafe struct SdfJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<GlyphCurveCache.Segment> segments;
        [ReadOnly] public NativeArray<SdfCore.GlyphTask> tasks;
        [ReadOnly] public NativeArray<long> pagePointers;

        [NativeDisableParallelForRestriction]
        public NativeArray<float> scratchBuffer;

        public int maxScratchFloatsPerWorker;
        public int pageStride;

        [NativeSetThreadIndex] int threadIndex;

        const float INF = 1e20f;

        struct MonoSegment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public float yMin, yMax;
            public int windingDir;
            public bool isLinear;
        }

        public void Execute(int index)
        {
            SdfCore.GlyphTask task = tasks[index];
            int tileSize = task.tileSize;
            int pixelCount = tileSize * tileSize;

            float* vecGrid = (float*)scratchBuffer.GetUnsafePtr() + threadIndex * maxScratchFloatsPerWorker;
            byte* signGrid = (byte*)(vecGrid + pixelCount * 2);

            ushort* pageBase = (ushort*)pagePointers[task.pageIndex];

            if (task.segmentCount == 0)
            {
                ClearTile(pageBase, task.tileX, task.tileY, tileSize);
                return;
            }

            ComputeTileTransform(in task, out float scale, out float offsetX, out float offsetY);

            int gxMin = (int)math.floor(offsetX * scale);
            int gyMin = (int)math.floor(offsetY * scale);
            int gxMax = (int)math.ceil((offsetX + task.aspect) * scale);
            int gyMax = (int)math.ceil((offsetY + 1f) * scale);
            int band = task.bandPixels;
            int rxMin = math.max(0, gxMin - band);
            int ryMin = math.max(0, gyMin - band);
            int rxMax = math.min(tileSize - 1, gxMax + band);
            int ryMax = math.min(tileSize - 1, gyMax + band);

            for (int y = ryMin; y <= ryMax; y++)
                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = (y * tileSize + x) * 2;
                    vecGrid[idx] = INF;
                    vecGrid[idx + 1] = INF;
                }

            UnsafeUtility.MemClear(signGrid + ryMin * tileSize, (ryMax - ryMin + 1) * tileSize);
            const int StackMaxSegs = 2048;
            MonoSegment* monoSegs = stackalloc MonoSegment[StackMaxSegs];
            int monoCount = YMonotoneSplit(task.segmentOffset, task.segmentCount, monoSegs, StackMaxSegs);
            ComputeWinding(monoSegs, monoCount, tileSize, scale, offsetX, offsetY, signGrid, rxMin, ryMin, rxMax, ryMax);

            SeedContour(task.segmentOffset, task.segmentCount, tileSize, scale, offsetX, offsetY, vecGrid);

            PropagateVectors(vecGrid, tileSize, rxMin, ryMin, rxMax, ryMax);

            float invSpread = task.glyphH / scale;
            EncodeToHalf16(vecGrid, signGrid, invSpread, tileSize, pageBase, task.tileX, task.tileY,
                rxMin, ryMin, rxMax, ryMax);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeTileTransform(in SdfCore.GlyphTask task,
            out float scale, out float offsetX, out float offsetY)
        {
            float padGlyph = GlyphAtlas.Pad / math.max(task.glyphH, 1e-6f);
            float maxDim = math.max(task.aspect, 1f);
            float totalExtent = maxDim + 2f * padGlyph;
            scale = task.tileSize / totalExtent;
            offsetX = (maxDim - task.aspect) * 0.5f + padGlyph;
            offsetY = (maxDim - 1f) * 0.5f + padGlyph;
        }

        private int YMonotoneSplit(int segOffset, int segCount, MonoSegment* output, int maxCount)
        {
            int count = 0;
            for (int i = 0; i < segCount; i++)
            {
                GlyphCurveCache.Segment s = segments[segOffset + i];

                float denom = s.p0y - 2f * s.p1y + s.p2y;
                float tSplit = (math.abs(denom) > 1e-10f) ? (s.p0y - s.p1y) / denom : -1f;

                if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
                {
                    float t = tSplit, mt = 1f - t;
                    float m01x = mt * s.p0x + t * s.p1x;
                    float m01y = mt * s.p0y + t * s.p1y;
                    float m12x = mt * s.p1x + t * s.p2x;
                    float m12y = mt * s.p1y + t * s.p2y;
                    float mx = mt * m01x + t * m12x;
                    float my = mt * m01y + t * m12y;
                    if (count < maxCount) AddMonoSegment(output, ref count, s.p0x, s.p0y, m01x, m01y, mx, my);
                    if (count < maxCount) AddMonoSegment(output, ref count, mx, my, m12x, m12y, s.p2x, s.p2y);
                }
                else
                {
                    if (count < maxCount) AddMonoSegment(output, ref count, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y);
                }
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddMonoSegment(MonoSegment* output, ref int count,
            float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
        {
            ref var m = ref output[count];
            m.p0x = p0x; m.p0y = p0y; m.p1x = p1x; m.p1y = p1y; m.p2x = p2x; m.p2y = p2y;
            m.yMin = math.min(p0y, p2y); m.yMax = math.max(p0y, p2y);
            m.windingDir = (p2y > p0y) ? 1 : -1;

            float d01x = p1x - p0x, d01y = p1y - p0y;
            float d02x = p2x - p0x, d02y = p2y - p0y;
            m.isLinear = math.abs(d01x * d02y - d01y * d02x) < 1e-5f;
            count++;
        }

        private static void ComputeWinding(MonoSegment* monoSegs, int monoCount,
            int tileSize, float scale, float offsetX, float offsetY, byte* signGrid,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            for (int i = 1; i < monoCount; i++)
            {
                var key = monoSegs[i];
                float keyYMin = key.yMin;
                int j = i - 1;
                while (j >= 0 && monoSegs[j].yMin > keyYMin)
                {
                    monoSegs[j + 1] = monoSegs[j];
                    j--;
                }
                monoSegs[j + 1] = key;
            }

            int windingRowLen = rxMax + 1;
            int* windingRow = stackalloc int[windingRowLen];
            int startIdx = 0;

            float yGlyphAtRyMin = (ryMin + 0.5f) / scale - offsetY;
            while (startIdx < monoCount && monoSegs[startIdx].yMax <= yGlyphAtRyMin)
                startIdx++;

            for (int y = ryMin; y <= ryMax; y++)
            {
                UnsafeUtility.MemClear(windingRow, windingRowLen * sizeof(int));
                float yGlyph = (y + 0.5f) / scale - offsetY;

                while (startIdx < monoCount && monoSegs[startIdx].yMax <= yGlyph)
                    startIdx++;

                for (int si = startIdx; si < monoCount; si++)
                {
                    ref var seg = ref monoSegs[si];
                    if (seg.yMin > yGlyph) break;
                    if (yGlyph >= seg.yMax) continue;

                    float xPx;
                    if (seg.isLinear)
                    {
                        float dySeg = seg.p2y - seg.p0y;
                        if (math.abs(dySeg) < 1e-9f) continue;
                        float t = (yGlyph - seg.p0y) / dySeg;
                        xPx = (seg.p0x + t * (seg.p2x - seg.p0x) + offsetX) * scale;
                    }
                    else
                    {
                        float a = seg.p0y - 2f * seg.p1y + seg.p2y;
                        float b = 2f * (seg.p1y - seg.p0y);
                        float c = seg.p0y - yGlyph;
                        int roots = SolveQuadratic(a, b, c, out float t0, out _);
                        if (roots == 0 || t0 < 0f || t0 > 1f) continue;
                        float mt = 1f - t0;
                        xPx = ((mt * mt * seg.p0x + 2f * mt * t0 * seg.p1x + t0 * t0 * seg.p2x) + offsetX) * scale;
                    }

                    int ixWind = (int)(xPx + 0.5f);
                    if (ixWind >= 0 && ixWind <= rxMax) windingRow[ixWind] += seg.windingDir;
                }

                int winding = 0;
                int rowOffset = y * tileSize;
                for (int x = 0; x <= rxMax; x++)
                {
                    winding += windingRow[x];
                    if (x >= rxMin)
                        signGrid[rowOffset + x] = (winding != 0) ? (byte)1 : (byte)0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SolveQuadratic(float a, float b, float c, out float t0, out float t1)
        {
            t0 = t1 = -1f;
            if (math.abs(a) < 1e-8f)
            {
                if (math.abs(b) < 1e-8f) return 0;
                t0 = -c / b;
                return (t0 >= 0f && t0 <= 1f) ? 1 : 0;
            }
            float disc = b * b - 4f * a * c;
            if (disc < -1e-7f) return 0;
            if (disc < 0f) disc = 0f;
            float sqrtDisc = math.sqrt(disc);

            float q = -0.5f * (b + math.select(-sqrtDisc, sqrtDisc, b >= 0f));
            if (math.abs(q) < 1e-12f)
            {
                t0 = 0f;
                t1 = -b / a;
            }
            else
            {
                t0 = q / a;
                t1 = c / q;
            }
            bool v0 = t0 >= 0f && t0 <= 1f;
            bool v1 = t1 >= 0f && t1 <= 1f;
            if (v0 && v1) return 2;
            if (v0) return 1;
            if (v1) { t0 = t1; return 1; }
            return 0;
        }

        private void SeedContour(int segOffset, int segCount, int tileSize, float scale, float offsetX, float offsetY, float* vecGrid)
        {
            float invScale = 1f / scale;

            for (int i = 0; i < segCount; i++)
            {
                GlyphCurveCache.Segment s = segments[segOffset + i];
                if (s.isInternal != 0) continue;
                SeedQuadratic(s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y, tileSize, scale, invScale, offsetX, offsetY, vecGrid);
            }
        }

        private void SeedQuadratic(float ax, float ay, float bx, float by, float cx, float cy,
            int tileSize, float scale, float invScale, float offsetX, float offsetY, float* vecGrid)
        {
            float mx = 0.25f * ax + 0.5f * bx + 0.25f * cx;
            float my = 0.25f * ay + 0.5f * by + 0.25f * cy;
            float h1x = mx - ax, h1y = my - ay;
            float h2x = cx - mx, h2y = cy - my;
            float pixelLen = (math.sqrt(h1x * h1x + h1y * h1y) + math.sqrt(h2x * h2x + h2y * h2y)) * scale;

            int steps = (int)math.ceil(pixelLen);
            if (steps < 1) steps = 1;
            float dt = 1f / steps;

            for (int j = 0; j <= steps; j++)
            {
                float t = j * dt;
                float mt = 1f - t;

                float gx = mt * mt * ax + 2f * mt * t * bx + t * t * cx;
                float gy = mt * mt * ay + 2f * mt * t * by + t * t * cy;
                float px = (gx + offsetX) * scale;
                float py = (gy + offsetY) * scale;

                int ix0 = (int)math.floor(px - 0.5f);
                int iy0 = (int)math.floor(py - 0.5f);

                for (int dy2 = 0; dy2 <= 1; dy2++)
                {
                    int iy = iy0 + dy2;
                    if ((uint)iy >= (uint)tileSize) continue;
                    for (int dx2 = 0; dx2 <= 1; dx2++)
                    {
                        int ix = ix0 + dx2;
                        if ((uint)ix >= (uint)tileSize) continue;

                        int idx = (iy * tileSize + ix) * 2;
                        float curD2 = vecGrid[idx] * vecGrid[idx] + vecGrid[idx + 1] * vecGrid[idx + 1];
                        if (curD2 < 0.01f) continue;

                        float pxG = (ix + 0.5f) * invScale - offsetX;
                        float pyG = (iy + 0.5f) * invScale - offsetY;

                        float tn = NewtonStep(pxG, pyG, ax, ay, bx, by, cx, cy, t);

                        float mtn = 1f - tn;
                        float vxG = mtn * mtn * ax + 2f * mtn * tn * bx + tn * tn * cx - pxG;
                        float vyG = mtn * mtn * ay + 2f * mtn * tn * by + tn * tn * cy - pyG;

                        float vxPx = vxG * scale;
                        float vyPx = vyG * scale;
                        float d2Px = vxPx * vxPx + vyPx * vyPx;

                        if (d2Px < curD2)
                        {
                            vecGrid[idx] = vxPx;
                            vecGrid[idx + 1] = vyPx;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float NewtonStep(float px, float py,
            float ax, float ay, float bx, float by, float cx, float cy, float t)
        {
            float mt = 1f - t;
            float dpx = 2f * ((bx - ax) + (ax - 2f * bx + cx) * t);
            float dpy = 2f * ((by - ay) + (ay - 2f * by + cy) * t);
            float ddpx = 2f * (ax - 2f * bx + cx);
            float ddpy = 2f * (ay - 2f * by + cy);
            float btx = mt * mt * ax + 2f * mt * t * bx + t * t * cx;
            float bty = mt * mt * ay + 2f * mt * t * by + t * t * cy;
            float diffx = btx - px, diffy = bty - py;

            float dpSq = dpx * dpx + dpy * dpy;
            if (dpSq < 1e-6f)
            {
                float ddSq = ddpx * ddpx + ddpy * ddpy;
                if (ddSq < 1e-12f) return t;
                float dot = diffx * ddpx + diffy * ddpy;
                if (dot >= 0f) return t;
                float s = math.sqrt(-2f * dot / ddSq);
                float t1 = math.clamp(t + s, 0f, 1f);
                float t2 = math.clamp(t - s, 0f, 1f);
                float m1 = 1f - t1;
                float d1x = m1 * m1 * ax + 2f * m1 * t1 * bx + t1 * t1 * cx - px;
                float d1y = m1 * m1 * ay + 2f * m1 * t1 * by + t1 * t1 * cy - py;
                float m2 = 1f - t2;
                float d2x = m2 * m2 * ax + 2f * m2 * t2 * bx + t2 * t2 * cx - px;
                float d2y = m2 * m2 * ay + 2f * m2 * t2 * by + t2 * t2 * cy - py;
                return (d1x * d1x + d1y * d1y <= d2x * d2x + d2y * d2y) ? t1 : t2;
            }

            float f = diffx * dpx + diffy * dpy;
            float fp = dpSq + diffx * ddpx + diffy * ddpy;
            if (math.abs(fp) < 1e-12f) return t;
            float tn = t - f / fp;
            return tn < 0f ? 0f : (tn > 1f ? 1f : tn);
        }

        private static void PropagateVectors(float* vecGrid, int size,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            for (int y = ryMin; y <= ryMax; y++)
            {
                int rowOffset = y * size * 2;
                int rowUp = (y - 1) * size * 2;

                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = rowOffset + x * 2;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    float curD2 = curVx * curVx + curVy * curVy;

                    if (x > rxMin) CheckProp(idx - 2, ref curVx, ref curVy, ref curD2, -1f, 0f, vecGrid);
                    if (y > ryMin)
                    {
                        CheckProp(rowUp + x * 2, ref curVx, ref curVy, ref curD2, 0f, -1f, vecGrid);
                        if (x > rxMin) CheckProp(rowUp + (x - 1) * 2, ref curVx, ref curVy, ref curD2, -1f, -1f, vecGrid);
                        if (x < rxMax) CheckProp(rowUp + (x + 1) * 2, ref curVx, ref curVy, ref curD2, 1f, -1f, vecGrid);
                    }
                    vecGrid[idx] = curVx;
                    vecGrid[idx + 1] = curVy;
                }
            }

            for (int y = ryMax; y >= ryMin; y--)
            {
                int rowOffset = y * size * 2;
                int rowDown = (y + 1) * size * 2;

                for (int x = rxMax; x >= rxMin; x--)
                {
                    int idx = rowOffset + x * 2;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    float curD2 = curVx * curVx + curVy * curVy;

                    if (x < rxMax) CheckProp(idx + 2, ref curVx, ref curVy, ref curD2, 1f, 0f, vecGrid);
                    if (y < ryMax)
                    {
                        CheckProp(rowDown + x * 2, ref curVx, ref curVy, ref curD2, 0f, 1f, vecGrid);
                        if (x < rxMax) CheckProp(rowDown + (x + 1) * 2, ref curVx, ref curVy, ref curD2, 1f, 1f, vecGrid);
                        if (x > rxMin) CheckProp(rowDown + (x - 1) * 2, ref curVx, ref curVy, ref curD2, -1f, 1f, vecGrid);
                    }
                    vecGrid[idx] = curVx;
                    vecGrid[idx + 1] = curVy;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckProp(int nIdx, ref float curVx, ref float curVy, ref float curD2,
            float dx, float dy, float* vecGrid)
        {
            float nVx = vecGrid[nIdx] + dx;
            float nVy = vecGrid[nIdx + 1] + dy;
            float nD2 = nVx * nVx + nVy * nVy;
            if (nD2 < curD2)
            {
                curD2 = nD2;
                curVx = nVx;
                curVy = nVy;
            }
        }

        private void EncodeToHalf16(float* vecGrid, byte* signGrid, float invSpread, int tileSize,
            ushort* pageBase, int tileX, int tileY,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            ushort halfOne = (ushort)math.f32tof16(1f);

            for (int y = 0; y < tileSize; y++)
            {
                ushort* dstRow = pageBase + (tileY + y) * pageStride + tileX;

                if (y < ryMin || y > ryMax)
                {
                    for (int x = 0; x < tileSize; x++)
                        dstRow[x] = halfOne;
                    continue;
                }

                for (int x = 0; x < rxMin; x++)
                    dstRow[x] = halfOne;

                int srcRow = y * tileSize;
                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = (srcRow + x) * 2;
                    float vx = vecGrid[idx], vy = vecGrid[idx + 1];
                    float dist = math.sqrt(vx * vx + vy * vy);
                    if (dist > 1e5f) dist = 1e5f;

                    float sign = (signGrid[srcRow + x] != 0) ? -1f : 1f;
                    float v = sign * dist * invSpread + 0.5f;
                    float encoded = v < 0f ? 0f : (v > 1f ? 1f : v);
                    dstRow[x] = (ushort)math.f32tof16(encoded);
                }

                for (int x = rxMax + 1; x < tileSize; x++)
                    dstRow[x] = halfOne;
            }
        }

        private void ClearTile(ushort* pageBase, int tileX, int tileY, int tileSize)
        {
            ushort halfOne = (ushort)math.f32tof16(1f);
            for (int y = 0; y < tileSize; y++)
            {
                ushort* dstRow = pageBase + (tileY + y) * pageStride + tileX;
                for (int x = 0; x < tileSize; x++)
                    dstRow[x] = halfOne;
            }
        }
    }
}
