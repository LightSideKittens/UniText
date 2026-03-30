using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Per-font extraction of glyph outlines as quadratic Bézier segments.
    /// Curves are extracted via native FreeType, normalized to [0,1] glyph space
    /// (height-based), and stored directly (no flattening) for GPU upload.
    /// Includes a face pool for parallel extraction across threads.
    /// </summary>
    internal sealed unsafe class GlyphCurveCache : IDisposable
    {
        private const int MaxCurvesPerGlyph = 2048;
        private const int MaxContoursPerGlyph = 256;

        /// <summary>
        /// One quadratic Bézier segment: p0 (start), p1 (control), p2 (end).
        /// Degenerate lines have p1 = midpoint(p0, p2).
        /// channelMask: R=1, G=2, B=4. Set by EdgeColoring for MSDF; ignored by SdfJob.
        /// </summary>
        public struct Segment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public byte channelMask;
            public byte contourIndex;
            /// <summary>Bit 0: endpoint A (p0) is a corner. Bit 1: endpoint B (p2) is a corner.
            /// Bits 2-4: channels exclusive to this segment at A. Bits 5-7: exclusive at B.</summary>
            public byte cornerFlags;
            /// <summary>1 if both sides of the segment are inside the glyph (internal edge). Excluded from SDF distance.</summary>
            public byte isInternal;
        }

        /// <summary>
        /// Glyph metrics extracted from FreeType.
        /// </summary>
        public struct GlyphCurveData
        {
            public float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;
            public float bearingX, bearingY;
            public float advanceX;
            public int designWidth, designHeight;
            public bool isEmpty;
        }

        private readonly byte[] fontData;
        private readonly int faceIndex;
        private readonly int unitsPerEm;
        private readonly ConcurrentBag<IntPtr> availableFaces = new();
        private readonly List<IntPtr> createdFaces = new();
        private readonly object poolLock = new();
        private readonly int maxPoolSize;

        private PooledBuffer<Segment> segmentBuffer;


        public GlyphCurveCache(IntPtr primaryFace, byte[] fontData, int faceIndex, int unitsPerEm)
        {
            this.fontData = fontData;
            this.faceIndex = faceIndex;
            this.unitsPerEm = unitsPerEm;
            maxPoolSize = Environment.ProcessorCount;

            availableFaces.Add(primaryFace);
        }

        /// <summary>
        /// Segment texels from the last <see cref="Extract"/> call.
        /// Valid until the next Extract call (backed by reusable buffer).
        /// </summary>
        public Span<Segment> LastSegments => segmentBuffer.Span;

        #region Single-threaded API (uses shared segmentBuffer)

        /// <summary>
        /// Extracts glyph outline from FreeType as quadratic Bézier curves.
        /// Clears the buffer first. Segment data available via <see cref="LastSegments"/>.
        /// </summary>
        public GlyphCurveData Extract(uint glyphIndex)
        {
            var face = RentFace();
            try
            {
                segmentBuffer.FakeClear();
                return ExtractCore(face, glyphIndex, ref segmentBuffer);
            }
            finally
            {
                ReturnFace(face);
            }
        }

        /// <summary>
        /// Resets the segment buffer. Call before a batch of <see cref="ExtractAppend"/> calls.
        /// </summary>
        public void ResetSegmentBuffer()
        {
            segmentBuffer.FakeClear();
        }

        /// <summary>
        /// Extracts glyph outline, APPENDING Bézier curves to the existing buffer (no clear).
        /// Returns metrics and the offset/count of this glyph's curves in <see cref="LastSegments"/>.
        /// </summary>
        public GlyphCurveData ExtractAppend(uint glyphIndex, out int segOffset, out int segCount)
        {
            var face = RentFace();
            try
            {
                int startOffset = segmentBuffer.count;
                var data = ExtractCore(face, glyphIndex, ref segmentBuffer);
                segOffset = startOffset;
                segCount = segmentBuffer.count - startOffset;
                return data;
            }
            finally
            {
                ReturnFace(face);
            }
        }

        #endregion

        #region Face Pool

        /// <summary>
        /// Rent a FreeType face handle for thread-safe extraction.
        /// Creates additional faces on demand up to ProcessorCount.
        /// </summary>
        public IntPtr RentFace()
        {
            if (availableFaces.TryTake(out var face))
                return face;

            lock (poolLock)
            {
                if (availableFaces.TryTake(out face))
                    return face;

                if (createdFaces.Count < maxPoolSize - 1)
                {
                    face = FT.LoadFace(fontData, faceIndex);
                    if (face != IntPtr.Zero)
                    {
                        createdFaces.Add(face);
                        return face;
                    }
                }
            }

            SpinWait spin = default;
            while (!availableFaces.TryTake(out face))
                spin.SpinOnce();
            return face;
        }

        /// <summary>
        /// Return a rented face handle to the pool.
        /// </summary>
        public void ReturnFace(IntPtr face)
        {
            if (face != IntPtr.Zero)
                availableFaces.Add(face);
        }

        #endregion

        #region Thread-safe extraction

        /// <summary>
        /// Thread-safe extraction: uses the provided face and output buffer (no shared state).
        /// Caller must rent face via <see cref="RentFace"/> and provide a per-thread buffer.
        /// </summary>
        public GlyphCurveData ExtractWithFace(IntPtr face, uint glyphIndex, ref PooledBuffer<Segment> output)
        {
            return ExtractCore(face, glyphIndex, ref output);
        }

        #endregion

        /// <summary>
        /// Core extraction: FreeType outline → normalized quadratic Bézier segments.
        /// Thread-safe when called with independent face/buffer.
        /// </summary>
        internal static long ftTicks;

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        internal static void ResetTimers() { Interlocked.Exchange(ref ftTicks, 0); }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void BeginFtTiming(ref long t0) { t0 = Stopwatch.GetTimestamp(); }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void EndFtTiming(long t0) { Interlocked.Add(ref ftTicks, Stopwatch.GetTimestamp() - t0); }

        private GlyphCurveData ExtractCore(IntPtr face, uint glyphIndex, ref PooledBuffer<Segment> output)
        {
            var rawCurves = stackalloc float[MaxCurvesPerGlyph * 8];
            var rawTypes = stackalloc int[MaxCurvesPerGlyph];
            var rawContours = stackalloc int[MaxContoursPerGlyph];
            int curveCount, contourCount;
            long t0 = 0;
            BeginFtTiming(ref t0);
            int err = FT.OutlineDecompose(face, glyphIndex,
                rawCurves, rawTypes, &curveCount, MaxCurvesPerGlyph,
                rawContours, &contourCount, MaxContoursPerGlyph,
                out int bearingX, out int bearingY, out int advanceX,
                out int width, out int height);
            EndFtTiming(t0);

            if (err != 0 || curveCount == 0)
            {
                return new GlyphCurveData
                {
                    isEmpty = true,
                    bearingX = bearingX / (float)unitsPerEm,
                    bearingY = bearingY / (float)unitsPerEm,
                    advanceX = advanceX / (float)unitsPerEm,
                    designWidth = width,
                    designHeight = height
                };
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                for (int j = 0; j < 3; j++)
                {
                    float x = c[j * 2];
                    float y = c[j * 2 + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            float bboxH = maxY - minY;
            if (bboxH < 1e-6f) bboxH = 1f;
            float invScale = 1f / bboxH;

            output.EnsureCapacity(output.count + curveCount);
            int segStart = output.count;

            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                var seg = new Segment
                {
                    p0x = (c[0] - minX) * invScale, p0y = (c[1] - minY) * invScale,
                    p1x = (c[2] - minX) * invScale, p1y = (c[3] - minY) * invScale,
                    p2x = (c[4] - minX) * invScale, p2y = (c[5] - minY) * invScale
                };
                output.Add(seg);
            }


            curveCount = NormalizeContours(ref output, segStart, curveCount, rawContours, contourCount);

            EdgeColoring.ColorAllContours(output.data, segStart, curveCount, rawContours, contourCount);
            
            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                for (int i = cStart; i <= cEnd; i++)
                    output.data[segStart + i].contourIndex = (byte)c;
                cStart = cEnd + 1;
            }

            MarkInternalSegments(output.data, segStart, curveCount);

            SortSegmentsByMinY(output.data, segStart, output.count - segStart);

            return new GlyphCurveData
            {
                bboxMinX = minX, bboxMinY = minY,
                bboxMaxX = maxX, bboxMaxY = maxY,
                bearingX = bearingX / (float)unitsPerEm,
                bearingY = bearingY / (float)unitsPerEm,
                advanceX = advanceX / (float)unitsPerEm,
                designWidth = width,
                designHeight = height,
                isEmpty = false
            };
        }

        /// <summary>
        /// Port of msdfgen's Shape::normalize(): splits single-edge contours into 3 parts
        /// so EdgeColoring can assign distinct channel masks (instead of WHITE = all identical).
        /// Processes back-to-front to expand in-place without overwriting unprocessed data.
        /// </summary>
        private static int NormalizeContours(ref PooledBuffer<Segment> output, int segStart, int segCount,
            int* rawContours, int contourCount)
        {
            int singleCount = 0;
            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                if (cEnd == cStart) singleCount++;
                cStart = cEnd + 1;
            }
            if (singleCount == 0) return segCount;

            int extra = singleCount * 2;
            int newSegCount = segCount + extra;
            output.EnsureCapacity(segStart + newSegCount);

            int writePos = newSegCount - 1;
            for (int c = contourCount - 1; c >= 0; c--)
            {
                int cEnd = rawContours[c];
                int cStartSeg = c > 0 ? rawContours[c - 1] + 1 : 0;
                int edgeCount = cEnd - cStartSeg + 1;

                if (edgeCount == 1)
                {
                    Segment seg = output.data[segStart + cStartSeg];
                    SplitSegmentInThirds(in seg, out var p0, out var p1, out var p2);
                    output.data[segStart + writePos] = p2;
                    output.data[segStart + writePos - 1] = p1;
                    output.data[segStart + writePos - 2] = p0;
                    rawContours[c] = writePos;
                    writePos -= 3;
                }
                else
                {
                    for (int i = edgeCount - 1; i >= 0; i--)
                        output.data[segStart + writePos - (edgeCount - 1 - i)] = output.data[segStart + cStartSeg + i];
                    rawContours[c] = writePos;
                    writePos -= edgeCount;
                }
            }

            output.count = segStart + newSegCount;
            return newSegCount;
        }

        /// <summary>
        /// Exact port of msdfgen's splitInThirds for quadratic Bézier: subdivides at t=1/3 and t=2/3
        /// using de Casteljau algorithm, producing 3 sub-segments.
        /// </summary>
        private static void SplitSegmentInThirds(in Segment seg, out Segment part0, out Segment part1, out Segment part2)
        {
            part0 = default;
            part1 = default;
            part2 = default;

            float p0x = seg.p0x, p0y = seg.p0y;
            float p1x = seg.p1x, p1y = seg.p1y;
            float p2x = seg.p2x, p2y = seg.p2y;

            float m01x = Mix(p0x, p1x, 1f / 3f), m01y = Mix(p0y, p1y, 1f / 3f);
            float m12x = Mix(p1x, p2x, 1f / 3f), m12y = Mix(p1y, p2y, 1f / 3f);
            float pt13x = Mix(m01x, m12x, 1f / 3f), pt13y = Mix(m01y, m12y, 1f / 3f);

            float n01x = Mix(p0x, p1x, 2f / 3f), n01y = Mix(p0y, p1y, 2f / 3f);
            float n12x = Mix(p1x, p2x, 2f / 3f), n12y = Mix(p1y, p2y, 2f / 3f);
            float pt23x = Mix(n01x, n12x, 2f / 3f), pt23y = Mix(n01y, n12y, 2f / 3f);

            part0.p0x = p0x; part0.p0y = p0y;
            part0.p1x = m01x; part0.p1y = m01y;
            part0.p2x = pt13x; part0.p2y = pt13y;

            float a59x = Mix(p0x, p1x, 5f / 9f), a59y = Mix(p0y, p1y, 5f / 9f);
            float b49x = Mix(p1x, p2x, 4f / 9f), b49y = Mix(p1y, p2y, 4f / 9f);
            part1.p0x = pt13x; part1.p0y = pt13y;
            part1.p1x = Mix(a59x, b49x, 0.5f); part1.p1y = Mix(a59y, b49y, 0.5f);
            part1.p2x = pt23x; part1.p2y = pt23y;

            part2.p0x = pt23x; part2.p0y = pt23y;
            part2.p1x = n12x; part2.p1y = n12y;
            part2.p2x = p2x; part2.p2y = p2y;
        }

        private static float Mix(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// Detects segments where at least one endpoint is shared with another contour AND
        /// both sides are inside the glyph (internal bridge edges between contours).
        /// These create false distance gradients in SDF but are needed for winding.
        /// The shared-vertex requirement prevents false positives on self-intersecting
        /// single contours (no other contour → no shared vertices → nothing marked).
        /// </summary>
        private static void MarkInternalSegments(Segment[] data, int segStart, int segCount)
        {
            const float posEps = 1e-5f;
            const float posEpsSq = posEps * posEps;
            const float normalEps = 1e-3f;

            for (int i = 0; i < segCount; i++)
            {
                ref var seg = ref data[segStart + i];
                int myContour = seg.contourIndex;

                bool anyShared = false;
                for (int j = 0; j < segCount && !anyShared; j++)
                {
                    ref var other = ref data[segStart + j];
                    if (other.contourIndex == myContour) continue;

                    float dx, dy;
                    dx = seg.p0x - other.p0x; dy = seg.p0y - other.p0y;
                    if (dx * dx + dy * dy < posEpsSq) { anyShared = true; continue; }
                    dx = seg.p0x - other.p2x; dy = seg.p0y - other.p2y;
                    if (dx * dx + dy * dy < posEpsSq) { anyShared = true; continue; }
                    dx = seg.p2x - other.p0x; dy = seg.p2y - other.p0y;
                    if (dx * dx + dy * dy < posEpsSq) { anyShared = true; continue; }
                    dx = seg.p2x - other.p2x; dy = seg.p2y - other.p2y;
                    if (dx * dx + dy * dy < posEpsSq) { anyShared = true; continue; }
                }

                if (!anyShared) continue;

                float midX = 0.25f * seg.p0x + 0.5f * seg.p1x + 0.25f * seg.p2x;
                float midY = 0.25f * seg.p0y + 0.5f * seg.p1y + 0.25f * seg.p2y;

                float tanX = seg.p2x - seg.p0x;
                float tanY = seg.p2y - seg.p0y;
                float normLen = (float)Math.Sqrt(tanX * tanX + tanY * tanY);
                if (normLen < 1e-10f) continue;

                float invLen = normalEps / normLen;
                float nx = -tanY * invLen;
                float ny = tanX * invLen;

                int windA = PointWinding(data, segStart, segCount, midX + nx, midY + ny);
                int windB = PointWinding(data, segStart, segCount, midX - nx, midY - ny);

                if (windA != 0 && windB != 0)
                    seg.isInternal = 1;
            }
        }

        private static int PointWinding(Segment[] data, int segStart, int segCount, float px, float py)
        {
            int winding = 0;
            for (int i = 0; i < segCount; i++)
            {
                ref var seg = ref data[segStart + i];
                winding += SegmentRayCrossing(seg.p0x, seg.p0y, seg.p1x, seg.p1y, seg.p2x, seg.p2y, px, py);
            }
            return winding;
        }

        private static int SegmentRayCrossing(float p0x, float p0y, float p1x, float p1y,
            float p2x, float p2y, float px, float py)
        {
            float denom = p0y - 2f * p1y + p2y;
            if (Math.Abs(denom) > 1e-10f)
            {
                float tSplit = (p0y - p1y) / denom;
                if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
                {
                    float t = tSplit, mt = 1f - t;
                    float m01x = mt * p0x + t * p1x, m01y = mt * p0y + t * p1y;
                    float m12x = mt * p1x + t * p2x, m12y = mt * p1y + t * p2y;
                    float mx = mt * m01x + t * m12x, my = mt * m01y + t * m12y;
                    return MonoRayCrossing(p0x, p0y, m01x, m01y, mx, my, px, py)
                         + MonoRayCrossing(mx, my, m12x, m12y, p2x, p2y, px, py);
                }
            }
            return MonoRayCrossing(p0x, p0y, p1x, p1y, p2x, p2y, px, py);
        }

        private static int MonoRayCrossing(float p0x, float p0y, float p1x, float p1y,
            float p2x, float p2y, float px, float py)
        {
            float yMin, yMax;
            int dir;
            if (p2y > p0y) { yMin = p0y; yMax = p2y; dir = 1; }
            else if (p0y > p2y) { yMin = p2y; yMax = p0y; dir = -1; }
            else return 0;

            if (py < yMin || py >= yMax) return 0;

            float d01x = p1x - p0x, d01y = p1y - p0y;
            float d02x = p2x - p0x, d02y = p2y - p0y;

            float xHit;
            if (Math.Abs(d01x * d02y - d01y * d02x) < 1e-5f)
            {
                float dy = p2y - p0y;
                float t = (py - p0y) / dy;
                xHit = p0x + t * (p2x - p0x);
            }
            else
            {
                float a = p0y - 2f * p1y + p2y;
                float b = 2f * (p1y - p0y);
                float c = p0y - py;
                float disc = b * b - 4f * a * c;
                if (disc < 0f) return 0;
                float sqrtDisc = (float)Math.Sqrt(disc);
                float t0 = (-b - sqrtDisc) / (2f * a);
                float t1 = (-b + sqrtDisc) / (2f * a);
                float t;
                if (t0 >= 0f && t0 <= 1f) t = t0;
                else if (t1 >= 0f && t1 <= 1f) t = t1;
                else return 0;
                float mt = 1f - t;
                xHit = mt * mt * p0x + 2f * mt * t * p1x + t * t * p2x;
            }

            return (xHit > px) ? dir : 0;
        }

        /// <summary>
        /// Sort segments by min-Y (insertion sort — optimal for small N, no allocations).
        /// Min-Y considers all control points based on segment type.
        /// </summary>
        private static void SortSegmentsByMinY(Segment[] data, int start, int length)
        {
            for (int i = start + 1; i < start + length; i++)
            {
                var key = data[i];
                float keyMinY = SegmentMinY(ref key);
                int j = i - 1;
                while (j >= start && SegmentMinY(ref data[j]) > keyMinY)
                {
                    data[j + 1] = data[j];
                    j--;
                }
                data[j + 1] = key;
            }
        }

        private static float SegmentMinY(ref Segment s)
        {
            return Math.Min(s.p0y, Math.Min(s.p1y, s.p2y));
        }

        public void Dispose()
        {
            segmentBuffer.Return();

            lock (poolLock)
            {
                foreach (var face in createdFaces)
                    if (face != IntPtr.Zero) FT.UnloadFace(face);
                createdFaces.Clear();
            }

            while (availableFaces.TryTake(out _)) { }
        }
    }
}
