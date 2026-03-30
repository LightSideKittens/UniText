using Unity.Burst;

namespace LightSide
{
    /// <summary>
    /// Shared structs for SDF and MSDF tile generation.
    /// All algorithms are inlined in SdfJob/MsdfJob for optimal Burst codegen.
    /// </summary>
    [BurstCompile]
    internal static class SdfCore
    {
        internal struct GlyphTask
        {
            public int segmentOffset;
            public int segmentCount;
            public int tileSize;
            public float aspect;
            public float glyphH;
            public int pageIndex;
            public int tileX;
            public int tileY;
            public int bandPixels;
        }
    }
}

