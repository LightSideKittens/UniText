using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Interface for custom attribute data stored in <see cref="UniTextBuffers"/>.
    /// </summary>
    /// <remarks>
    /// Implement this interface to create custom per-text attribute storage for modifiers.
    /// Use <see cref="UniTextBuffers.GetOrCreateAttributeData{T}"/> to retrieve instances.
    /// </remarks>
    /// <seealso cref="PooledArrayAttribute{T}"/>
    /// <seealso cref="UniTextBuffers"/>
    public interface IAttributeData
    {
        /// <summary>
        /// Releases all pooled resources back to the pool.
        /// </summary>
        void Release();
    }


    /// <summary>
    /// A pooled array-based implementation of <see cref="IAttributeData"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the array.</typeparam>
    /// <remarks>
    /// <para>
    /// Use this class to store per-codepoint or per-glyph attribute data in modifiers.
    /// The underlying array is rented from <see cref="UniTextArrayPool{T}"/> for zero-allocation operation.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var colors = buffers.GetOrCreateAttributeData&lt;PooledArrayAttribute&lt;Color32&gt;&gt;("colors");
    /// colors.EnsureCount(codepointCount);
    /// colors[0] = Color.red;
    /// </code>
    /// </example>
    public sealed class PooledArrayAttribute<T> : IAttributeData
    {
        /// <summary>
        /// The underlying pooled buffer.
        /// </summary>
        public PooledBuffer<T> buffer;

        /// <summary>
        /// Gets the number of elements in the buffer.
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.count;
        }

        /// <summary>
        /// Gets a reference to the element at the specified index.
        /// </summary>
        /// <param name="i">The zero-based index of the element.</param>
        /// <returns>A reference to the element at the specified index.</returns>
        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref buffer[i];
        }

        /// <summary>
        /// Adds an item to the end of the buffer.
        /// </summary>
        /// <param name="item">The item to add.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item) => buffer.Add(item);

        /// <summary>
        /// Ensures the buffer has at least the specified capacity, clearing if newly allocated.
        /// </summary>
        /// <param name="required">The minimum required element count.</param>
        public void EnsureCountAndClear(int required)
        {
            buffer.EnsureCount(required);
            buffer.ClearData();
            buffer.count = 0;
        }

        /// <inheritdoc/>
        public void Release() => buffer.Return();
    }


    /// <summary>
    /// Container for all intermediate and final buffers used during text processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UniTextBuffers"/> holds all data produced during the text processing pipeline:
    /// codepoints, BiDi levels, text runs, shaped glyphs, line breaks, and positioned glyphs.
    /// </para>
    /// <para>
    /// <b>Performance:</b> All buffers use <see cref="PooledBuffer{T}"/> for zero-allocation
    /// operation in steady state. Call <see cref="EnsureRentBuffers"/> before processing
    /// and <see cref="EnsureReturnBuffers"/> when done.
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="PooledBuffer{T}"/>
    public sealed class UniTextBuffers
    {
        private const int MinCodepointCapacity = 32;
        private const int MinRunCapacity = 64;
        private const int MinGlyphCapacity = 32;
        private const int MinLineCapacity = 32;
        private const int MinParagraphCapacity = 8;

        /// <summary>Parsed Unicode codepoints from the input text.</summary>
        public PooledBuffer<int> codepoints;

        /// <summary>BiDi paragraph boundaries and base directions.</summary>
        internal PooledBuffer<BidiParagraph> bidiParagraphs;

        /// <summary>Text runs before shaping (segmented by script, direction, font).</summary>
        public PooledBuffer<TextRun> runs;

        /// <summary>Shaped runs with glyph ranges and metrics.</summary>
        public PooledBuffer<ShapedRun> shapedRuns;

        /// <summary>Shaped glyphs with glyph IDs, advances, and offsets.</summary>
        public PooledBuffer<ShapedGlyph> shapedGlyphs;

        /// <summary>Width of each codepoint for line breaking calculations.</summary>
        public PooledBuffer<float> cpWidths;

        /// <summary>Line break types per codepoint (UAX #14).</summary>
        public PooledBuffer<LineBreakType> breakOpportunities;

        /// <summary>Grapheme cluster boundaries per codepoint (UAX #29).</summary>
        public PooledBuffer<bool> graphemeBreaks;

        /// <summary>Computed text lines after line breaking.</summary>
        public PooledBuffer<TextLine> lines;

        /// <summary>Runs reordered for visual display within each line.</summary>
        public PooledBuffer<ShapedRun> orderedRuns;

        /// <summary>Final positioned glyphs ready for rendering.</summary>
        public PooledBuffer<PositionedGlyph> positionedGlyphs;

        /// <summary>Virtual codepoints for synthesized glyphs (e.g., modifiers).</summary>
        public PooledBuffer<uint> virtualCodepoints;

        /// <summary>BiDi embedding levels per codepoint (UAX #9).</summary>
        public PooledBuffer<byte> bidiLevels;

        /// <summary>Unicode script per codepoint (UAX #24).</summary>
        public PooledBuffer<UnicodeScript> scripts;

        /// <summary>Start margins per codepoint (for list items, indentation).</summary>
        public PooledBuffer<float> startMargins;

        /// <summary>Advance width per line for alignment calculations.</summary>
        public PooledBuffer<float> perLineAdvances;

        /// <summary>The resolved base paragraph direction.</summary>
        public TextDirection baseDirection;

        /// <summary>Font size used during shaping (for scaling calculations).</summary>
        public float shapingFontSize;

        /// <summary>Cached glyph rendering data for mesh generation.</summary>
        internal PooledBuffer<CachedGlyphData> glyphDataCache;

        /// <summary>Indicates whether <see cref="glyphDataCache"/> contains valid data.</summary>
        public bool hasValidGlyphCache;

        /// <summary>Indicates whether buffers are currently rented from the pool.</summary>
        public bool isRented;

        private Dictionary<string, IAttributeData> attributeData;

        /// <summary>
        /// Gets or creates typed attribute data for the specified key.
        /// </summary>
        /// <typeparam name="T">The attribute data type, must implement <see cref="IAttributeData"/>.</typeparam>
        /// <param name="key">The unique key identifying this attribute data.</param>
        /// <returns>The existing or newly created attribute data instance.</returns>
        /// <remarks>
        /// Use this method in modifiers to store per-text custom data that persists
        /// across processing passes but is reset when text changes.
        /// </remarks>
        public T GetOrCreateAttributeData<T>(string key) where T : class, IAttributeData, new()
        {
            attributeData ??= new Dictionary<string, IAttributeData>(8);

            if (attributeData.TryGetValue(key, out var existing))
                return (T)existing;

            var data = new T();
            attributeData[key] = data;
            return data;
        }

        /// <summary>
        /// Gets typed attribute data for the specified key if it exists.
        /// </summary>
        /// <typeparam name="T">The attribute data type.</typeparam>
        /// <param name="key">The unique key identifying this attribute data.</param>
        /// <returns>The attribute data if found; otherwise, <see langword="null"/>.</returns>
        public T GetAttributeData<T>(string key) where T : class, IAttributeData
        {
            if (attributeData != null && attributeData.TryGetValue(key, out var data))
                return (T)data;
            return null;
        }

        /// <summary>
        /// Releases and removes attribute data for the specified key.
        /// </summary>
        /// <param name="key">The unique key identifying the attribute data to release.</param>
        public void ReleaseAttributeData(string key)
        {
            if (attributeData == null || !attributeData.TryGetValue(key, out var data))
                return;

            data.Release();
            attributeData.Remove(key);
        }

        private void ReleaseAllAttributeData()
        {
            if (attributeData == null) return;
            foreach (var data in attributeData.Values)
                data.Release();
            attributeData.Clear();
        }

        /// <summary>
        /// Gets the scale factor to convert from shaping font size to target font size.
        /// </summary>
        /// <param name="targetFontSize">The desired font size.</param>
        /// <returns>The scale factor to apply to glyph positions and advances.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetGlyphScale(float targetFontSize)
        {
            return shapingFontSize > 0 ? targetFontSize / shapingFontSize : 1f;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EstimateCapacity(int textLength, int minCapacity)
        {
            if (textLength <= minCapacity) return minCapacity;
            return Mathf.NextPowerOfTwo(textLength);
        }

        /// <summary>
        /// Ensures all buffers are rented from the pool with appropriate initial capacity.
        /// </summary>
        /// <param name="textLength">The expected text length for capacity estimation.</param>
        /// <remarks>
        /// <para>
        /// Call this method before starting text processing. If buffers are already rented,
        /// this method returns immediately.
        /// </para>
        /// <para>
        /// <b>Performance:</b> Capacities are estimated from text length and rounded to
        /// power-of-two values for efficient pooling.
        /// </para>
        /// </remarks>
        public void EnsureRentBuffers(int textLength)
        {
            if (isRented) return;
            UniTextDebug.Increment(ref UniTextDebug.Buffers_RentCount);

            var codepointCapacity = EstimateCapacity(textLength, MinCodepointCapacity);
            var glyphCapacity = EstimateCapacity(textLength, MinGlyphCapacity);

            codepoints.Rent(codepointCapacity);
            bidiParagraphs.Rent(MinParagraphCapacity);
            runs.Rent(MinRunCapacity);
            shapedRuns.Rent(MinRunCapacity);
            shapedGlyphs.Rent(glyphCapacity);
            cpWidths.Rent(codepointCapacity);
            breakOpportunities.Rent(codepointCapacity + 1);
            graphemeBreaks.Rent(codepointCapacity + 1);
            lines.Rent(MinLineCapacity);
            orderedRuns.Rent(MinRunCapacity);
            positionedGlyphs.Rent(glyphCapacity);

            bidiLevels.Rent(codepointCapacity);
            scripts.Rent(codepointCapacity);
            startMargins.EnsureCount(codepointCapacity);
            startMargins.ClearData();
            glyphDataCache.Rent(glyphCapacity);

            isRented = true;
            Reset();
        }

        /// <summary>
        /// Returns all rented buffers back to the pool.
        /// </summary>
        /// <remarks>
        /// Call this method when text processing is complete and the buffers are no longer needed.
        /// This releases memory back to the pool for reuse by other instances.
        /// </remarks>
        public void EnsureReturnBuffers()
        {
            if (!isRented) return;

            hasValidGlyphCache = false;

            codepoints.Return();
            bidiParagraphs.Return();
            runs.Return();
            shapedRuns.Return();
            shapedGlyphs.Return();
            cpWidths.Return();
            breakOpportunities.Return();
            graphemeBreaks.Return();
            lines.Return();
            orderedRuns.Return();
            positionedGlyphs.Return();
            virtualCodepoints.Return();

            bidiLevels.Return();
            scripts.Return();
            startMargins.Return();
            perLineAdvances.Return();
            glyphDataCache.Return();

            ReleaseAllAttributeData();

            isRented = false;
        }

        /// <summary>
        /// Resets all buffer counts to zero without releasing pooled memory.
        /// </summary>
        /// <remarks>
        /// Use this method between processing passes to reuse buffers for new text
        /// without the overhead of returning and re-renting from the pool.
        /// </remarks>
        public void Reset()
        {
            var cpCount = codepoints.count;

            if (startMargins.Capacity > 0 && cpCount > 0)
                startMargins.data.AsSpan(0, cpCount).Clear();

            codepoints.FakeClear();
            bidiParagraphs.FakeClear();
            runs.FakeClear();
            shapedRuns.FakeClear();
            shapedGlyphs.FakeClear();
            cpWidths.FakeClear();
            breakOpportunities.FakeClear();
            graphemeBreaks.FakeClear();
            lines.FakeClear();
            orderedRuns.FakeClear();
            positionedGlyphs.FakeClear();
            virtualCodepoints.FakeClear();
            bidiLevels.FakeClear();
            scripts.FakeClear();
            glyphDataCache.FakeClear();

            hasValidGlyphCache = false;
            baseDirection = TextDirection.LeftToRight;
        }

        /// <summary>
        /// Ensures codepoint-related buffers have at least the specified capacity.
        /// </summary>
        /// <param name="required">The minimum required codepoint capacity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCodepointCapacity(int required)
        {
            if (codepoints.Capacity < required)
                GrowCodepointBuffers(required);
        }

        private void GrowCodepointBuffers(int required)
        {
            var newSize = Math.Max(required, codepoints.Capacity * 2);

            codepoints.EnsureCapacity(newSize);
            bidiLevels.EnsureCapacity(newSize);
            scripts.EnsureCapacity(newSize);

            var oldCapacity = startMargins.Capacity;
            startMargins.EnsureCapacity(newSize);
            if (startMargins.Capacity > oldCapacity)
                startMargins.data.AsSpan(oldCapacity).Clear();
        }
    }


    /// <summary>
    /// Thread-safe cache for font lookup results to avoid repeated font fallback searches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Performance:</b> Uses a fixed-size hash table with O(1) lookup. Thread-local storage
    /// avoids locking overhead.
    /// </para>
    /// <para>
    /// Call <see cref="InvalidateAll"/> when fonts are added or removed to ensure
    /// correct cache behavior.
    /// </para>
    /// </remarks>
    internal static class SharedFontCache
    {
        private readonly struct FontCacheEntry
        {
            public readonly int codepoint;
            public readonly int preferredFontId;
            public readonly int resultFontId;
            public readonly int version;

            public FontCacheEntry(int codepoint, int preferredFontId, int resultFontId, int version)
            {
                this.codepoint = codepoint;
                this.preferredFontId = preferredFontId;
                this.resultFontId = resultFontId;
                this.version = version;
            }
        }

        private const int CacheSize = 16384;
        private const int CacheMask = CacheSize - 1;
        [ThreadStatic] private static FontCacheEntry[] cache;
        private static int fontStateVersion;

        private static void EnsureInitialized()
        {
            if (cache != null) return;
            cache = new FontCacheEntry[CacheSize];
        }

        /// <summary>
        /// Attempts to get a cached font lookup result.
        /// </summary>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <param name="preferredFontId">The preferred font ID.</param>
        /// <param name="resultFontId">When successful, contains the cached result font ID.</param>
        /// <returns><see langword="true"/> if a cached result was found; otherwise, <see langword="false"/>.</returns>
        public static bool TryGet(int codepoint, int preferredFontId, out int resultFontId)
        {
            EnsureInitialized();
            var index = (codepoint ^ (preferredFontId << 16)) & CacheMask;
            ref var entry = ref cache[index];

            if (entry.codepoint == codepoint &&
                entry.preferredFontId == preferredFontId &&
                entry.version == fontStateVersion)
            {
                resultFontId = entry.resultFontId;
                return true;
            }

            resultFontId = -1;
            return false;
        }

        /// <summary>
        /// Caches a font lookup result.
        /// </summary>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <param name="preferredFontId">The preferred font ID.</param>
        /// <param name="resultFontId">The resolved font ID to cache.</param>
        public static void Set(int codepoint, int preferredFontId, int resultFontId)
        {
            EnsureInitialized();
            var index = (codepoint ^ (preferredFontId << 16)) & CacheMask;
            cache[index] = new FontCacheEntry(codepoint, preferredFontId, resultFontId, fontStateVersion);
        }

        /// <summary>
        /// Invalidates all cached entries by incrementing the version number.
        /// </summary>
        /// <remarks>
        /// Call this method when fonts are added, removed, or their glyph coverage changes.
        /// </remarks>
        public static void InvalidateAll()
        {
            fontStateVersion++;
        }

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        public static void Clear()
        {
            if (cache == null) return;
            for (var i = 0; i < CacheSize; i++)
                cache[i] = default;
        }
    }


    /// <summary>
    /// Shared mesh instances for UI rendering.
    /// </summary>
    /// <remarks>
    /// CanvasRenderer.SetMesh() copies data, so we can reuse the same mesh instances
    /// across all UniText components. No pooling needed - just a simple array.
    /// </remarks>
    internal static class SharedMeshes
    {
        private static readonly List<Mesh> meshes = new(4);

        /// <summary>
        /// Gets a shared mesh by index, creating it if necessary.
        /// </summary>
        /// <param name="index">The mesh index (typically submesh index).</param>
        /// <returns>A reusable Mesh instance.</returns>
        public static Mesh Get(int index)
        {
            while (meshes.Count <= index)
                meshes.Add(null);

            var mesh = meshes[index];
            if (mesh == null)
            {
                mesh = new Mesh { name = "UniText Shared Mesh" };
                meshes[index] = mesh;
            }
            return mesh;
        }

    #if UNITY_EDITOR
        static SharedMeshes()
        {
            Reseter.UnmanagedCleaning += DestroyAll;
        }

        private static void DestroyAll()
        {
            for (var i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null)
                    Object.DestroyImmediate(meshes[i]);
            }
            meshes.Clear();
        }
    #endif
    }
}
