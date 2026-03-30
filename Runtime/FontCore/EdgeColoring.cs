using System;

namespace LightSide
{
    /// <summary>
    /// Assigns per-edge channel masks for MSDF rendering.
    /// Implements Chlumsky's edgeColoringSimple algorithm: detects corners
    /// by angle between adjacent edges, then cycles CYAN→MAGENTA→YELLOW at each corner.
    /// </summary>
    internal static class EdgeColoring
    {
        private const byte CYAN    = 2 | 4;
        private const byte MAGENTA = 1 | 4;
        private const byte YELLOW  = 1 | 2;
        private const byte WHITE   = 1 | 2 | 4;

        private const float CrossThreshold = 0.1411f;

        /// <summary>
        /// Colors all contours in a segment array. Must be called after normalization,
        /// before Y-sort (which destroys contour order).
        /// </summary>
        /// <param name="segments">The segment array (output buffer from ExtractCore)</param>
        /// <param name="segStart">Start index of this glyph's segments in the array</param>
        /// <param name="segCount">Number of segments for this glyph</param>
        /// <param name="rawContours">FreeType contour end indices (inclusive, into 0..segCount-1 range)</param>
        /// <param name="contourCount">Number of contours</param>
        public static unsafe void ColorAllContours(
            GlyphCurveCache.Segment[] segments, int segStart, int segCount,
            int* rawContours, int contourCount)
        {
            if (segCount == 0 || contourCount == 0) return;

            byte color = CYAN;

            int contourStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int contourEnd = rawContours[c];
                int edgeCount = contourEnd - contourStart + 1;

                if (edgeCount > 0)
                {
                    int start = segStart + contourStart;
                    ColorContour(segments, start, edgeCount, ref color);
                    ComputeCornerFlags(segments, start, edgeCount);
                }

                contourStart = contourEnd + 1;
            }
        }

        /// <summary>
        /// Compute corner flags at each segment endpoint for MSDF tangent suppression.
        /// Bit 0/1: endpoint A/B is a corner (channel masks share at most one channel).
        /// Bits 2-4 / 5-7: channels exclusive to this segment at A/B.
        /// </summary>
        private static void ComputeCornerFlags(GlyphCurveCache.Segment[] segments, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int prev = (i + count - 1) % count;
                int next = (i + 1) % count;

                ref var segPrev = ref segments[start + prev];
                ref var segCur = ref segments[start + i];
                ref var segNext = ref segments[start + next];

                byte cf = 0;
                int commonA = segPrev.channelMask & segCur.channelMask;
                if ((commonA & (commonA - 1)) == 0) cf |= 1;
                int commonB = segCur.channelMask & segNext.channelMask;
                if ((commonB & (commonB - 1)) == 0) cf |= 2;
                byte aExcl = (byte)(segCur.channelMask & ~segPrev.channelMask & 7);
                byte bExcl = (byte)(segCur.channelMask & ~segNext.channelMask & 7);
                segCur.cornerFlags = (byte)(cf | (aExcl << 2) | (bExcl << 5));
            }
        }

        private static void SegmentEntryDir(ref GlyphCurveCache.Segment s, out float ox, out float oy)
        {
            float dx = s.p1x - s.p0x, dy = s.p1y - s.p0y;
            if (dx * dx + dy * dy > 1e-20f) { NormalizeInPlace(ref dx, ref dy); ox = dx; oy = dy; return; }
            dx = s.p2x - s.p0x; dy = s.p2y - s.p0y;
            NormalizeInPlace(ref dx, ref dy); ox = dx; oy = dy;
        }

        private static void SegmentExitDir(ref GlyphCurveCache.Segment s, out float ox, out float oy)
        {
            float dx = s.p2x - s.p1x, dy = s.p2y - s.p1y;
            if (dx * dx + dy * dy > 1e-20f) { NormalizeInPlace(ref dx, ref dy); ox = dx; oy = dy; return; }
            dx = s.p2x - s.p0x; dy = s.p2y - s.p0y;
            NormalizeInPlace(ref dx, ref dy); ox = dx; oy = dy;
        }

        private static void NormalizeInPlace(ref float x, ref float y)
        {
            float len = MathF.Sqrt(x * x + y * y);
            if (len > 1e-10f) { float inv = 1f / len; x *= inv; y *= inv; }
            else { x = 0f; y = 0f; }
        }

        /// <summary>
        /// Exact port of msdfgen's switchColor(color, seed) with seed=0.
        /// Rotates: CYAN→MAGENTA→YELLOW→CYAN.
        /// </summary>
        private static byte SwitchColor(byte color)
        {
            int shifted = color << 1;
            return (byte)((shifted | (shifted >> 3)) & WHITE);
        }

        /// <summary>
        /// Exact port of msdfgen's switchColor(color, seed, banned) with seed=0.
        /// Avoids producing a color that shares exactly one channel with banned.
        /// </summary>
        private static byte SwitchColorBanned(byte color, byte banned)
        {
            byte combined = (byte)(color & banned);
            if (combined == 1 || combined == 2 || combined == 4)
                return (byte)(combined ^ WHITE);
            return SwitchColor(color);
        }

        /// <summary>
        /// Exact port of msdfgen's symmetricalTrichotomy.
        /// Returns -1, 0, or 1 for balanced three-way split.
        /// </summary>
        private static int SymmetricalTrichotomy(int position, int n)
        {
            return (int)(3 + 2.875 * position / (n - 1) - 1.4375 + 0.5) - 3;
        }

        /// <summary>
        /// Exact port of msdfgen's edgeColoringSimple per-contour logic.
        /// color is a running state across contours (matches msdfgen behavior with seed=0).
        /// </summary>
        private static void ColorContour(GlyphCurveCache.Segment[] segments, int start, int count, ref byte color)
        {
            if (count == 1)
            {
                segments[start].channelMask = WHITE;
                return;
            }

            Span<int> corners = count <= 256 ? stackalloc int[count] : new int[count];
            int cornerCount = 0;

            for (int i = 0; i < count; i++)
            {
                int prev = (i + count - 1) % count;

                ref var segPrev = ref segments[start + prev];
                ref var segCur = ref segments[start + i];

                SegmentExitDir(ref segPrev, out float exitX, out float exitY);
                SegmentEntryDir(ref segCur, out float entryX, out float entryY);

                float cross = exitX * entryY - exitY * entryX;
                float dot = exitX * entryX + exitY * entryY;

                if (dot <= 0f || MathF.Abs(cross) > CrossThreshold)
                    corners[cornerCount++] = i;
            }

            if (cornerCount == 0)
            {
                color = SwitchColor(color);
                for (int i = 0; i < count; i++)
                    segments[start + i].channelMask = color;
            }
            else if (cornerCount == 1)
            {
                color = SwitchColor(color);
                byte color0 = color;
                color = SwitchColor(color);
                byte color2 = color;

                int corner = corners[0];
                for (int i = 0; i < count; i++)
                {
                    int idx = (corner + i) % count;
                    int tri = 1 + SymmetricalTrichotomy(i, count);
                    byte c = tri == 0 ? color0 : (tri == 1 ? WHITE : color2);
                    segments[start + idx].channelMask = c;
                }
            }
            else
            {
                color = SwitchColor(color);
                byte initialColor = color;

                int spline = 0;
                int cornerStart = corners[0];

                for (int i = 0; i < count; i++)
                {
                    int idx = (cornerStart + i) % count;

                    if (spline + 1 < cornerCount && corners[spline + 1] == idx)
                    {
                        spline++;
                        byte banned = (spline == cornerCount - 1) ? initialColor : (byte)0;
                        color = SwitchColorBanned(color, banned);
                    }

                    segments[start + idx].channelMask = color;
                }
            }
        }
    }
}
