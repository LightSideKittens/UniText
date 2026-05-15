using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Static container for shared text processing pipeline components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides lazy-initialized singletons for expensive-to-create pipeline components
    /// (BidiEngine, ScriptAnalyzer, etc.) and thread-local instances for thread-safe
    /// components (LineBreaker, TextLayout).
    /// </para>
    /// <para>
    /// Also manages pooled buffers for glyph grouping during mesh generation.
    /// </para>
    /// </remarks>
    internal static class SharedPipelineComponents
    {
        #region Pipeline Components

        private static readonly Lazy<BidiEngine> bidiEngine = new(() => new BidiEngine());
        private static readonly Lazy<ScriptAnalyzer> scriptAnalyzer = new(() => new ScriptAnalyzer());
        private static readonly Lazy<LineBreakAlgorithm> lineBreakAlgorithm = new(() => new LineBreakAlgorithm());
        private static readonly Lazy<GraphemeBreaker> graphemeBreaker = new(() => new GraphemeBreaker(UnicodeData.Provider));
        private static readonly Lazy<Shaper> shapingEngine = new(() => LightSide.Shaper.Instance);

        [ThreadStatic] private static LineBreaker lineBreaker;
        [ThreadStatic] private static TextLayout textLayout;

        /// <summary>Gets the shared BiDi engine (UAX #9).</summary>
        public static BidiEngine BidiEngine => bidiEngine.Value;

        /// <summary>Gets the shared script analyzer (UAX #24).</summary>
        public static ScriptAnalyzer ScriptAnalyzer => scriptAnalyzer.Value;

        /// <summary>Gets the shared line break algorithm (UAX #14).</summary>
        public static LineBreakAlgorithm LineBreakAlgorithm => lineBreakAlgorithm.Value;

        /// <summary>Gets the shared grapheme breaker (UAX #29).</summary>
        public static GraphemeBreaker GraphemeBreaker => graphemeBreaker.Value;

        /// <summary>Gets the shared text shaper.</summary>
        public static Shaper Shaper => shapingEngine.Value;

        /// <summary>Gets the thread-local line breaker instance.</summary>
        public static LineBreaker LineBreaker => lineBreaker ??= new LineBreaker();

        /// <summary>Gets the thread-local text layout instance.</summary>
        public static TextLayout Layout => textLayout ??= new TextLayout();

        #endregion

        [ThreadStatic] public static PooledBuffer<ShapedGlyph> shapingOutputBuffer;

        #region Glyph Grouping (ThreadStatic, for mesh generator)

        [ThreadStatic] private static FastIntDictionary<PooledList<int>> glyphsByFont;
        [ThreadStatic] private static Stack<PooledList<int>> glyphListPool;
        [ThreadStatic] private static List<UniTextRenderData> meshResultBuffer;

        public static FastIntDictionary<PooledList<int>> GlyphsByFont
            => glyphsByFont ??= new FastIntDictionary<PooledList<int>>();

        public static Stack<PooledList<int>> GlyphListPool
            => glyphListPool ??= new Stack<PooledList<int>>();

        public static List<UniTextRenderData> MeshResultBuffer
            => meshResultBuffer ??= new List<UniTextRenderData>(4);

        public static PooledList<int> AcquireGlyphIndexList(int capacity)
        {
            var pool = GlyphListPool;
            if (pool.Count > 0)
            {
                var result = pool.Pop();
                result.EnsureCapacity(capacity);
                return result;
            }

            return new PooledList<int>(capacity);
        }

        public static void ReleaseGlyphIndexList(PooledList<int> list)
        {
            list.FakeClear();
            GlyphListPool.Push(list);
        }

        public static void ClearGlyphsByFont()
        {
            var dict = GlyphsByFont;
            var pool = GlyphListPool;
            foreach (var kvp in dict)
            {
                kvp.Value.FakeClear();
                pool.Push(kvp.Value);
            }

            dict.ClearFast();
        }

        #endregion
    }

}
