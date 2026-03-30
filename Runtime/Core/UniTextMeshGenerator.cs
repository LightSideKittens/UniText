using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace LightSide
{
    /// <summary>
    /// Contains all data needed to render a text mesh segment in Unity.
    /// </summary>
    /// <remarks>
    /// Returned by <see cref="UniTextMeshGenerator.ApplyMeshesToUnity"/> for each rendering target.
    /// At most 2 entries: SDF mesh (index 0) and emoji mesh (index 1).
    /// </remarks>
    public struct UniTextRenderData
    {
        /// <summary>The Unity mesh containing vertex, UV, color, and triangle data.</summary>
        public Mesh mesh;
        
        /// <summary>The font identifier this render data belongs to.</summary>
        public int fontId;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniTextRenderData"/> struct.
        /// </summary>
        public UniTextRenderData(Mesh mesh, int fontId)
        {
            this.mesh = mesh;
            this.fontId = fontId;
        }
    }


    /// <summary>
    /// Describes a single effect render pass that modifies generator buffers before mesh upload.
    /// </summary>
    /// <remarks>
    /// Registered by subscribers via <see cref="UniTextMeshGenerator.effectPasses"/>.
    /// The system calls <see cref="apply"/> before mesh upload and <see cref="revert"/> after,
    /// without knowledge of what the callbacks do internally.
    /// </remarks>
    public struct EffectPass
    {
        /// <summary>Writes effect data (UV2, vertex shifts) into generator buffers.</summary>
        public Action apply;

        /// <summary>Reverts effect data (clears UV2, restores vertices) in generator buffers.</summary>
        public Action revert;

        /// <summary>When true, the pass modifies vertex positions (requires SetVertices re-upload).</summary>
        public bool hasVertexShifts;
    }

    /// <summary>
    /// Converts positioned glyphs into Unity mesh data for text rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the final stage of the text processing pipeline. It takes <see cref="PositionedGlyph"/>
    /// data from <see cref="TextProcessor"/> and generates vertex, UV, color, and triangle data
    /// suitable for Unity's mesh system.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Groups glyphs by rendering target to minimize draw calls: one segment per font (Texture2DArray atlas)</item>
    /// <item>Uses pooled buffers from <see cref="UniTextArrayPool{T}"/> for zero allocations</item>
    /// <item>Provides callbacks for text modifiers to inject custom processing</item>
    /// </list>
    /// </para>
    /// <para>
    /// Typical usage:
    /// <code>
    /// generator.SetRectOffset(rect);
    /// generator.GenerateMeshDataOnly(positionedGlyphs);
    /// var renderData = generator.ApplyMeshesToUnity(meshProvider);
    /// // Use renderData to render each segment
    /// generator.ReturnInstanceBuffers();
    /// </code>
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="PositionedGlyph"/>
    /// <seealso cref="UniTextRenderData"/>
    public class UniTextMeshGenerator
    {
        [ThreadStatic] private static UniTextMeshGenerator current;

        /// <summary>
        /// Gets the currently active mesh generator on this thread (set during mesh generation).
        /// </summary>
        /// <remarks>
        /// Used by text modifiers to access the current generator instance during callbacks.
        /// Only valid within <see cref="onGlyph"/>, <see cref="onAfterPage"/>, and similar callbacks.
        /// </remarks>
        public static UniTextMeshGenerator Current => current;

        /// <summary>The cluster index of the glyph currently being processed.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Maps back to codepoint indices.</remarks>
        public int currentCluster;

        /// <summary>Height of the current glyph including padding.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float height;

        /// <summary>Y coordinate of the text baseline for the current glyph.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float baselineY;

        /// <summary>Current font scale factor (FontSize / font.UnitsPerEm).</summary>
        public float scale;

        /// <summary>FontSize * FontScale — converts normalized glyph metrics to UI-space units. Constant per font.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Use for fixed-pixel-size effects in the Base shader
        /// where glyphH cancels out (outline dilate, shadow dilate/softness).</remarks>
        public float fontMetricFactor;

        /// <summary>Default vertex color applied to all glyphs.</summary>
        public Color32 defaultColor;

        /// <summary>Atlas padding in pixels from font settings.</summary>
        public float paddingPixels;

        /// <summary>Padding in font units.</summary>
        public float padding;

        /// <summary>Double padding for width/height calculations.</summary>
        public float padding2;

        /// <summary>Inverse atlas size for UV calculations: 1 / atlasSize.</summary>
        public float invAtlasSize;

        /// <summary>Current font being processed.</summary>
        /// <remarks>Valid during mesh generation for the current font segment.</remarks>
        public UniTextFont font;

        /// <summary>X offset from the rect origin.</summary>
        public float offsetX;

        /// <summary>Y offset from the rect origin.</summary>
        public float offsetY;

        /// <summary>Current number of vertices in the mesh buffers.</summary>
        public int vertexCount;

        /// <summary>Current number of triangle indices in the mesh buffers.</summary>
        public int triangleCount;


        private PooledBuffer<Vector3> vertices;
        private PooledBuffer<Vector4> uvs0;
        private PooledBuffer<Vector2> uvs1;
        private PooledBuffer<Vector4> uvs2;
        private PooledBuffer<Vector4> uvs3;
        private PooledBuffer<Color32> colors;
        private PooledBuffer<int> triangles;
        private bool hasGeneratedData;
        private int currentSegmentVertexStart;

        /// <summary>Number of vertices in the SDF segment.</summary>
        public int SdfVertexCount => sdfVertexCount;

        /// <summary>Number of triangle indices in the SDF segment.</summary>
        public int SdfTriangleCount => sdfTriangleCount;

        /// <summary>Number of vertices in the emoji segment.</summary>
        public int EmojiVertexCount => emojiVertexCount;

        /// <summary>Number of triangle indices in the emoji segment.</summary>
        public int EmojiTriangleCount => emojiTriangleCount;

        private int sdfVertexCount;
        private int sdfTriangleCount;
        private int sdfFontId;
        private int emojiVertexCount;
        private int emojiTriangleCount;
        private int emojiFontId;

        /// <summary>Glyph atlas keys used in the last mesh generation (for reference counting).</summary>
        internal PooledBuffer<long> usedGlyphKeys;
        internal PooledBuffer<long> usedEmojiKeys;

        [ThreadStatic] private static FastLongDictionary<GlyphAtlas.GlyphEntry> glyphEntryCache;

        /// <summary>
        /// Looks up a glyph entry from the per-frame cache. Returns true if found (repeated glyph).
        /// On miss, the caller should look up the atlas and call <see cref="CacheGlyphEntry"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetCachedGlyphEntry(long glyphKey, out GlyphAtlas.GlyphEntry entry)
        {
            return glyphEntryCache.TryGetValue(glyphKey, out entry);
        }

        /// <summary>
        /// Stores a glyph entry in the cache and tracks the key for atlas ref counting.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CacheGlyphEntry(long glyphKey, in GlyphAtlas.GlyphEntry entry)
        {
            glyphEntryCache.AddOrUpdate(glyphKey, entry);
            usedGlyphKeys.Add(glyphKey);
        }

        /// <summary>
        /// Tracks a glyph key for atlas ref counting. Deduplicates automatically.
        /// Use for modifier glyphs that don't need cached entry data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrackGlyphKey(long glyphKey)
        {
            if (glyphEntryCache.ContainsKey(glyphKey))
                return false;
            glyphEntryCache.AddOrUpdate(glyphKey, default);
            usedGlyphKeys.Add(glyphKey);
            return true;
        }

        /// <summary>Starting vertex index for the current segment. Used to compute relative triangle indices.</summary>
        public int CurrentSegmentVertexStart => currentSegmentVertexStart;

        private readonly UniTextFontProvider fontProvider;
        private readonly UniTextBuffers buf;
        private float lossyScale = 1f;
        private bool hasWorldCamera;

        /// <summary>Invoked after all SDF glyphs have been processed, before the segment is closed.</summary>
        /// <remarks>Decorations (underline, strikethrough) write into the open segment here.</remarks>
        public Action onAfterPage;

        /// <summary>Invoked for each glyph during mesh generation.</summary>
        /// <remarks>
        /// Primary callback for text modifiers to apply per-glyph effects.
        /// Access current glyph data via <see cref="Current"/> or public fields.
        /// </remarks>
        public Action onGlyph;

        /// <summary>Invoked after all mesh generation is complete.</summary>
        public Action onRebuildEnd;

        /// <summary>Invoked before mesh generation starts.</summary>
        public Action onRebuildStart;

        /// <summary>
        /// Maximum effect extent (UV-space padding) requested for the current glyph.
        /// Reset to 0 before each <see cref="onGlyph"/> invocation. Subscribers accumulate via max.
        /// Read by <see cref="ExpandQuadForEffects"/> to determine quad expansion.
        /// </summary>
        public float currentMaxEffectExtent;

        internal struct BandUpgradeRequest
        {
            public long glyphKey;
            public uint glyphIndex;
            public int requiredBandPx;
            public UniTextFont font;
            public long varHash48;
            public int[] ftCoords;
            public UniTextBase.RenderModee mode;
        }

        internal readonly List<BandUpgradeRequest> bandUpgradeRequests = new();

        /// <summary>Ordered list of effect render passes registered by subscribers.</summary>
        /// <remarks>
        /// Each entry provides apply/revert callbacks that modify generator buffers (UV2, vertices).
        /// The system iterates this list to create per-pass CanvasRenderers.
        /// Registration order determines render order (first = furthest back).
        /// </remarks>
        public readonly List<EffectPass> effectPasses = new();

        private Rect rectOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniTextMeshGenerator"/> class.
        /// </summary>
        /// <param name="fontProvider">The font provider for accessing font assets and materials.</param>
        /// <param name="uniTextBuffers">The shared buffer container from text processing.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="fontProvider"/> or <paramref name="uniTextBuffers"/> is <see langword="null"/>.
        /// </exception>
        public UniTextMeshGenerator(UniTextFontProvider fontProvider, UniTextBuffers uniTextBuffers)
        {
            this.fontProvider = fontProvider ?? throw new ArgumentNullException(nameof(fontProvider));
            buf = uniTextBuffers ?? throw new ArgumentNullException(nameof(uniTextBuffers));
        }

        /// <summary>Gets or sets the font size in points for mesh generation.</summary>
        public float FontSize { get; set; } = 36f;

        /// <summary>Gets or sets the atlas mode (SDF or MSDF) for glyph lookup and material selection.</summary>
        public UniTextBase.RenderModee RenderMode { get; set; }

        /// <summary>Gets a value indicating whether mesh data has been generated and is available.</summary>
        public bool HasGeneratedData => hasGeneratedData;

        /// <summary>Gets the vertex position buffer (X, Y, Z coordinates).</summary>
        public Vector3[] Vertices => vertices.data;

        /// <summary>
        /// Scales a glyph quad (4 vertices) around its left edge and baseline.
        /// Used by SizeModifier, SmallCapsModifier, ScriptPositionModifier.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ScaleGlyphQuad(Vector3[] verts, int baseIdx, float baselineY, float scale, float yOffset = 0f)
        {
            var leftX = verts[baseIdx].x;
            var pivotY = baselineY + yOffset;
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x = leftX + (v.x - leftX) * scale;
                v.y = pivotY + (v.y - baselineY) * scale;
            }
        }

        /// <summary>Gets the primary UV buffer (texture coordinates and scale in W component).</summary>
        public Vector4[] Uvs0 => uvs0.data;

        /// <summary>Gets the vertex color buffer.</summary>
        public Color32[] Colors => colors.data;

        /// <summary>Gets the triangle index buffer.</summary>
        public int[] Triangles => triangles.data;

        /// <summary>Gets the UV1 buffer: x = aspect (glyphW/glyphH), y = face dilate.</summary>
        public Vector2[] Uvs1 => uvs1.data;

        /// <summary>Gets the UV2 buffer containing layer 2 (underlay/shadow) parameters.</summary>
        /// <remarks>
        /// Layout: x = dilate, y = color (packed Color32), z = (free), w = softness.
        /// Shadow offset is applied via mesh vertex displacement, not UV.
        /// Not allocated by default. Call <see cref="EnsureUvBuffer"/> to allocate before writing.
        /// </remarks>
        public Vector4[] Uvs2 => uvs2.data;

        /// <summary>Gets the UV3 buffer containing layer 3 (underlay/shadow) parameters.</summary>
        /// <remarks>
        /// Layout: x = dilate, y = color (packed Color32), z = (free), w = softness.
        /// Shadow offset is applied via mesh vertex displacement, not UV.
        /// Not allocated by default. Call <see cref="EnsureUvBuffer"/> to allocate before writing.
        /// </remarks>
        public Vector4[] Uvs3 => uvs3.data;

        /// <summary>
        /// Allocates and zero-clears a UV effect buffer (channel 2 or 3) if not already allocated.
        /// </summary>
        /// <param name="channel">UV channel: 2 or 3.</param>
        public void EnsureUvBuffer(int channel)
        {
            ref var buf = ref (channel == 3 ? ref uvs3 : ref uvs2);
            if (buf.data != null) return;
            buf.Rent(vertices.Capacity);
            Array.Clear(buf.data, 0, buf.data.Length);
            buf.count = vertexCount;
        }

        #region Instance Buffer Management

        private void RentInstanceBuffers(int estimatedVertices, int estimatedTriangles)
        {
            vertices.Rent(estimatedVertices);
            uvs0.Rent(estimatedVertices);
            uvs1.Rent(estimatedVertices);
            colors.Rent(estimatedVertices);
            triangles.Rent(estimatedTriangles);

            current = this;
        }

        /// <summary>
        /// Returns all instance buffers to the pool and clears the generated data flag.
        /// </summary>
        /// <remarks>
        /// Must be called after mesh generation is complete and data has been applied to Unity meshes.
        /// Failing to call this method will result in buffer leaks.
        /// </remarks>
        public void ReturnInstanceBuffers()
        {
            current = null;

            vertices.Return();
            uvs0.Return();
            uvs1.Return();
            uvs2.Return();
            uvs3.Return();
            colors.Return();
            triangles.Return();
            hasGeneratedData = false;
        }

        /// <summary>
        /// Releases all pooled resources. Call when the generator is no longer needed.
        /// </summary>
        public void Dispose()
        {
            ReturnInstanceBuffers();
            usedGlyphKeys.Return();
            usedEmojiKeys.Return();
        }

        /// <summary>
        /// Ensures the vertex and triangle buffers have capacity for additional data.
        /// </summary>
        /// <param name="additionalVertices">Number of additional vertices needed.</param>
        /// <param name="additionalTriangles">Number of additional triangle indices needed.</param>
        /// <remarks>
        /// Called by text modifiers when they need to add geometry beyond the base glyph quads.
        /// Automatically grows buffers using the pooled array system if needed.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int additionalVertices, int additionalTriangles)
        {
            var requiredVertices = vertexCount + additionalVertices;
            var requiredTriangles = triangleCount + additionalTriangles;

            if (requiredVertices > vertices.Capacity)
                GrowVertexBuffers(requiredVertices);

            if (requiredTriangles > triangles.Capacity)
                GrowTriangleBuffer(requiredTriangles);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowVertexBuffers(int required)
        {
            var newCapacity = Math.Max(required, vertices.Capacity * 2);
            var currentCount = vertexCount;

            GrowBuffer(ref vertices, newCapacity, currentCount);
            GrowBuffer(ref uvs0, newCapacity, currentCount);
            GrowBuffer(ref uvs1, newCapacity, currentCount);
            if (uvs2.data != null)
            {
                GrowBuffer(ref uvs2, newCapacity, currentCount);
                Array.Clear(uvs2.data, currentCount, uvs2.data.Length - currentCount);
            }
            if (uvs3.data != null)
            {
                GrowBuffer(ref uvs3, newCapacity, currentCount);
                Array.Clear(uvs3.data, currentCount, uvs3.data.Length - currentCount);
            }
            GrowBuffer(ref colors, newCapacity, currentCount);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowTriangleBuffer(int required)
        {
            var newCapacity = Math.Max(required, triangles.Capacity * 2);
            GrowBuffer(ref triangles, newCapacity, triangleCount);
        }

        private static void GrowBuffer<T>(ref PooledBuffer<T> buffer, int newCapacity, int currentCount)
        {
            var oldData = buffer.data;

            var newData = UniTextArrayPool<T>.Rent(newCapacity);
            if (oldData != null && currentCount > 0)
                oldData.AsSpan(0, currentCount).CopyTo(newData);

            UniTextArrayPool<T>.Return(oldData);
            buffer.data = newData;
        }

        #endregion

        /// <summary>
        /// Sets cached canvas parameters for world-space text rendering.
        /// </summary>
        /// <param name="lossyScale">The canvas lossy scale for proper sizing.</param>
        /// <param name="hasWorldCamera">Whether the canvas uses a world camera.</param>
        public void SetCanvasParametersCached(float lossyScale, bool hasWorldCamera)
        {
            this.lossyScale = lossyScale;
            this.hasWorldCamera = hasWorldCamera;
        }

        /// <summary>
        /// Sets the layout rectangle for text positioning.
        /// </summary>
        /// <param name="rect">The rect defining the text layout bounds.</param>
        public void SetRectOffset(Rect rect)
        {
            rectOffset = rect;
        }
        
        private float CalculateXScale(float scale)
        {
            var absLossyScale = Mathf.Abs(lossyScale);
            var result = scale * (hasWorldCamera ? absLossyScale : 1f);

            if (result <= 0.0001f || float.IsNaN(result) || float.IsInfinity(result))
                result = scale > 0 ? scale : 1f;

            return result;
        }

        #region Parallel Mesh Generation

        /// <summary>
        /// Generates mesh data (vertices, UVs, colors, triangles) from positioned glyphs.
        /// Groups by rendering target: SDF fonts in one segment (Texture2DArray), emoji separately.
        /// </summary>
        public void GenerateMeshDataOnly(ReadOnlySpan<PositionedGlyph> glyphs, ReadOnlySpan<PositionedGlyph> virtualGlyphs)
        {
            onRebuildStart?.Invoke();
            var glyphLen = glyphs.Length + virtualGlyphs.Length;
            usedGlyphKeys.FakeClear();
            usedGlyphKeys.EnsureCapacity(glyphLen);
            usedEmojiKeys.FakeClear();
            usedEmojiKeys.EnsureCapacity(glyphLen);
            bandUpgradeRequests.Clear();

            glyphEntryCache ??= new FastLongDictionary<GlyphAtlas.GlyphEntry>(512);
            glyphEntryCache.ClearFast();
            var estimatedVertices = glyphLen * 4;
            var estimatedTriangles = glyphLen * 6;

            RentInstanceBuffers(estimatedVertices, estimatedTriangles);

            var allGlyphs = UniTextArrayPool<PositionedGlyph>.Rent(glyphLen);
            glyphs.CopyTo(allGlyphs);
            if (virtualGlyphs.Length > 0)
                virtualGlyphs.CopyTo(allGlyphs.AsSpan(glyphs.Length));

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;
            offsetX = offX;
            offsetY = offY;
            vertexCount = 0;
            triangleCount = 0;
            currentSegmentVertexStart = 0;

            PooledList<int> emojiGlyphList = null;
            var atlas = GlyphAtlas.GetInstance(RenderMode);
            var skippedGlyphs = 0;
            var lastSdfFontId = int.MinValue;

            var lastFontId = int.MinValue;
            UniTextFont lastFont = null;
            long lastVarHash = 0;
            int[] lastFtCoords = null;
            var lastIsEmoji = false;

            float upem = 0, metricsFactor = 0;
            var glyphColor = defaultColor;

            var verts = vertices.data;
            var uvData = uvs0.data;
            var uv1Data = uvs1.data;
            var cols = colors.data;
            var tris = triangles.data;

            for (var i = 0; i < glyphLen; i++)
            {
                ref var glyph = ref allGlyphs[i];
                var glyphFontId = glyph.fontId;

                if (glyphFontId != lastFontId)
                {
                    lastFontId = glyphFontId;
                    lastFont = fontProvider.GetFontAsset(glyphFontId);
                    if (buf.variationMap != null &&
                        buf.variationMap.TryGetValue(glyphFontId, out var varRunInfo))
                    {
                        lastVarHash = varRunInfo.varHash48;
                        lastFtCoords = varRunInfo.ftCoords;
                    }
                    else
                    {
                        lastVarHash = lastFont.DefaultVarHash48;
                        lastFtCoords = null;
                    }
                    lastIsEmoji = lastFont is EmojiFont;

                    if (!lastIsEmoji)
                    {
                        font = lastFont;

                        upem = lastFont.UnitsPerEm;
                        var fontScaleMul = lastFont.FontScale;
                        scale = FontSize * fontScaleMul / upem;
                        metricsFactor = FontSize * fontScaleMul;
                        fontMetricFactor = metricsFactor;

                        glyphColor = lastFont.IsColor
                            ? new Color32(255, 255, 255, defaultColor.a)
                            : defaultColor;
                    }
                }

                if (lastIsEmoji)
                {
                    emojiGlyphList ??= SharedPipelineComponents.AcquireGlyphIndexList(glyphLen);
                    emojiGlyphList.buffer[emojiGlyphList.buffer.count++] = i;
                    continue;
                }

                var glyphId = (uint)glyph.glyphId;
                var glyphKey = GlyphAtlas.MakeKey(lastVarHash, glyphId);

                if (!TryGetCachedGlyphEntry(glyphKey, out var entry))
                {
                    if (!atlas.TryGetEntry(glyphKey, out entry) || entry.encodedTile < 0)
                    {
                        skippedGlyphs++;
                        continue;
                    }
                    CacheGlyphEntry(glyphKey, in entry);
                }

                lastSdfFontId = glyphFontId;

                var cluster = glyph.cluster;
                var metrics = entry.metrics;

                const float sdfPadding = 0.02f;
                var bearingXNorm = metrics.horizontalBearingX / upem;
                var bearingYNorm = metrics.horizontalBearingY / upem;
                var glyphW = metrics.width / upem;
                var glyphH = metrics.height / upem;
                var aspect = glyphH > 1e-6f ? glyphW / glyphH : 1f;

                var maxDim = MathF.Max(aspect, 1f);
                var marginX = (maxDim - aspect) * 0.5f;
                var marginY = (maxDim - 1f) * 0.5f;

                var padEmX = (marginX + sdfPadding) * glyphH;
                var padEmY = (marginY + sdfPadding) * glyphH;
                var sideScaled = (maxDim + sdfPadding * 2) * glyphH * metricsFactor;

                var bearingXScaled = (bearingXNorm - padEmX) * metricsFactor;
                var bearingYScaled = (bearingYNorm + padEmY) * metricsFactor;
                var widthScaled = sideScaled;
                var heightScaled = sideScaled;

                var tlX = offX + glyph.x + bearingXScaled;
                var tlY = offY - glyph.y + bearingYScaled;
                var blY = tlY - heightScaled;
                var trX = tlX + widthScaled;

                var uvMinX = -(marginX + sdfPadding);
                var uvMinY = -(marginY + sdfPadding);
                var uvMaxX = aspect + marginX + sdfPadding;
                var uvMaxY = 1f + marginY + sdfPadding;

                var tileIdx = (float)(entry.encodedTile + entry.pageIndex * GlyphAtlas.PageStride);

                var i0 = vertexCount;
                var i1 = vertexCount + 1;
                var i2 = vertexCount + 2;
                var i3 = vertexCount + 3;

                ref var v0 = ref verts[i0];
                v0.x = tlX; v0.y = blY; v0.z = 0;
                ref var v1 = ref verts[i1];
                v1.x = tlX; v1.y = tlY; v1.z = 0;
                ref var v2 = ref verts[i2];
                v2.x = trX; v2.y = tlY; v2.z = 0;
                ref var v3 = ref verts[i3];
                v3.x = trX; v3.y = blY; v3.z = 0;

                ref var uv0 = ref uvData[i0];
                uv0.x = uvMinX; uv0.y = uvMinY; uv0.z = tileIdx; uv0.w = glyphH;
                ref var uv1 = ref uvData[i1];
                uv1.x = uvMinX; uv1.y = uvMaxY; uv1.z = tileIdx; uv1.w = glyphH;
                ref var uv2 = ref uvData[i2];
                uv2.x = uvMaxX; uv2.y = uvMaxY; uv2.z = tileIdx; uv2.w = glyphH;
                ref var uv3 = ref uvData[i3];
                uv3.x = uvMaxX; uv3.y = uvMinY; uv3.z = tileIdx; uv3.w = glyphH;

                cols[i0] = glyphColor;
                cols[i1] = glyphColor;
                cols[i2] = glyphColor;
                cols[i3] = glyphColor;

                var uv1Val = new Vector2(aspect, 0);
                uv1Data[i0] = uv1Val;
                uv1Data[i1] = uv1Val;
                uv1Data[i2] = uv1Val;
                uv1Data[i3] = uv1Val;

                var localI0 = i0 - currentSegmentVertexStart;
                tris[triangleCount] = localI0;
                tris[triangleCount + 1] = localI0 + 1;
                tris[triangleCount + 2] = localI0 + 2;
                tris[triangleCount + 3] = localI0 + 2;
                tris[triangleCount + 4] = localI0 + 3;
                tris[triangleCount + 5] = localI0;

                currentCluster = cluster;
                height = heightScaled;
                baselineY = offY - glyph.y;

                vertexCount += 4;
                triangleCount += 6;

                var vcBeforeOnGlyph = vertexCount;
                currentMaxEffectExtent = 0f;
                if (glyph.shapedGlyphIndex >= 0)
                    onGlyph?.Invoke();

                if (vertexCount == vcBeforeOnGlyph)
                {
                    int baseIdx = vcBeforeOnGlyph - 4;
                    ExpandQuadForEffects(baseIdx, glyphH, metricsFactor, sdfPadding);

                    if (entry.encodedTile >= 0 && glyphH > 1e-6f)
                    {
                        float faceDilate = uvs1.data[baseIdx].y;
                        float padGlyph = GlyphAtlas.Pad / glyphH;
                        float facePad = faceDilate * padGlyph;
                        float requiredPad = facePad > currentMaxEffectExtent ? facePad : currentMaxEffectExtent;
                        if (requiredPad > sdfPadding)
                        {
                            float effectExtent = requiredPad < padGlyph ? requiredPad : padGlyph;
                            int tileSize = atlas.TileSizeFromEncoded(entry.encodedTile);
                            float totalExt = (aspect > 1f ? aspect : 1f) + 2f * padGlyph;
                            int requiredBandPx = (int)Math.Ceiling(effectExtent * tileSize / totalExt);
                            if (requiredBandPx > entry.computedBandPx)
                            {
                                bandUpgradeRequests.Add(new BandUpgradeRequest
                                {
                                    glyphKey = glyphKey,
                                    glyphIndex = glyphId,
                                    requiredBandPx = requiredBandPx,
                                    font = lastFont,
                                    varHash48 = lastVarHash,
                                    ftCoords = lastFtCoords,
                                    mode = RenderMode
                                });
                            }
                        }
                    }
                }

                verts = vertices.data;
                uvData = uvs0.data;
                uv1Data = uvs1.data;
                cols = colors.data;
                tris = triangles.data;
            }

            onAfterPage?.Invoke();

            if (skippedGlyphs > 0)
                Cat.MeowFormat("[MeshGenerator] SKIPPED {0} glyphs (not in atlas)", skippedGlyphs);

            if (vertexCount > 0)
                sdfFontId = lastSdfFontId;

            sdfVertexCount = vertexCount;
            sdfTriangleCount = triangleCount;

            if (emojiGlyphList != null)
            {
                ref var firstEmojiGlyph = ref allGlyphs[emojiGlyphList[0]];
                var emojiId = firstEmojiGlyph.fontId;
                var fontAsset = fontProvider.GetFontAsset(emojiId);

                currentSegmentVertexStart = vertexCount;
                GenerateEmojiSegment(emojiGlyphList, allGlyphs, fontAsset);
                emojiFontId = emojiId;

                SharedPipelineComponents.ReleaseGlyphIndexList(emojiGlyphList);
            }

            emojiVertexCount = vertexCount - sdfVertexCount;
            emojiTriangleCount = triangleCount - sdfTriangleCount;

            vertices.count = vertexCount;
            uvs0.count = vertexCount;
            uvs1.count = vertexCount;
            if (uvs2.data != null) uvs2.count = vertexCount;
            if (uvs3.data != null) uvs3.count = vertexCount;
            colors.count = vertexCount;
            triangles.count = triangleCount;

            UniTextArrayPool<PositionedGlyph>.Return(allGlyphs);

            buf.hasValidGlyphCache = true;
            hasGeneratedData = true;

            Cat.MeowFormat("[MeshGenerator] Generated: {0} verts, {1} tris, sdf={2}+emoji={3}",
                vertices.count, triangles.count, sdfVertexCount, emojiVertexCount);

            onRebuildEnd?.Invoke();
        }

        /// <summary>
        /// Generates mesh data for emoji glyphs using bitmap SDF atlas.
        /// </summary>
        private void GenerateEmojiSegment(PooledList<int> glyphIndices, PositionedGlyph[] positionedGlyphs, UniTextFont font)
        {
            var glyphCount = glyphIndices.Count;
            var emojiFont = (EmojiFont)font;
            var emojiVarHash = GlyphAtlas.DefaultVarHash(font.FontDataHash);

            var upem = font.UnitsPerEm;
            var fontScaleMul = font.FontScale;
            var scaleVal = FontSize * fontScaleMul / upem;
            var atlasSizeVal = emojiFont.AtlasSize;

            var paddingPixelsVal = font.AtlasPadding;
            var paddingVal = (float)paddingPixelsVal;
            var padding2Val = paddingVal * 2;

            var invAtlasSizeVal = 1f / atlasSizeVal;

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;

            var xScaleVal = CalculateXScale(scaleVal);

            scale = scaleVal;
            offsetX = offX;
            offsetY = offY;
            this.font = font;
            paddingPixels = paddingPixelsVal;
            padding = paddingVal;
            padding2 = padding2Val;
            invAtlasSize = invAtlasSizeVal;

            EnsureCapacity(glyphCount * 4, glyphCount * 6);

            var isColorFont = font.IsColor;
            var glyphColor = isColorFont
                ? new Color32(255, 255, 255, defaultColor.a)
                : defaultColor;

            buf.glyphDataCache.EnsureCapacity(buf.shapedGlyphs.count);
            var glyphCache = buf.glyphDataCache.data;
            var useCache = buf.hasValidGlyphCache;

            var verts = vertices.data;
            var uvData = uvs0.data;
            var cols = colors.data;
            var tris = triangles.data;

            var skippedGlyphs = 0;
            var zeroRectGlyphs = 0;

            for (var i = 0; i < glyphCount; i++)
            {
                var glyphIndex = glyphIndices[i];
                ref var glyph = ref positionedGlyphs[glyphIndex];
                var cacheIndex = glyph.shapedGlyphIndex;

                ref var cachedData = ref glyphCache[cacheIndex];
                var emojiGlyphId = (uint)glyph.glyphId;
                var emojiKey = GlyphAtlas.MakeKey(emojiVarHash, emojiGlyphId);
                usedEmojiKeys.Add(emojiKey);

                if (!useCache || !cachedData.isValid)
                {
                    var emojiAtlas = GlyphAtlas.Emoji;
                    if (emojiAtlas == null || !emojiAtlas.TryGetEntry(emojiKey, out var entry) || entry.encodedTile < 0)
                    {
                        skippedGlyphs++;
                        cachedData.isValid = false;
                        continue;
                    }

                    int tileSize = emojiAtlas.TileSizeFromEncoded(entry.encodedTile);
                    emojiAtlas.DecodeTileXY(entry.encodedTile, tileSize, out int tileX, out int tileY);
                    int g = emojiAtlas.TileGutter;
                    var metrics = entry.metrics;
                    cachedData.rectX = tileX + g;
                    cachedData.rectY = tileY + g;
                    cachedData.rectWidth = entry.pixelWidth;
                    cachedData.rectHeight = entry.pixelHeight;
                    cachedData.bearingX = metrics.horizontalBearingX;
                    cachedData.bearingY = metrics.horizontalBearingY;
                    cachedData.width = metrics.width;
                    cachedData.height = metrics.height;
                    cachedData.atlasIndex = entry.pageIndex;
                    cachedData.isValid = true;
                }

                if (cachedData.rectWidth == 0 || cachedData.rectHeight == 0)
                {
                    zeroRectGlyphs++;
                    continue;
                }

                var cluster = glyph.cluster;

                var bearingXScaled = (cachedData.bearingX - padding) * scale;
                var bearingYScaled = (cachedData.bearingY + padding) * scale;
                var heightScaled = (cachedData.height + padding2) * scale;
                var widthScaled = (cachedData.width + padding2) * scale;

                var tlX = offX + glyph.x + bearingXScaled;
                var tlY = offY - glyph.y + bearingYScaled;
                var blY = tlY - heightScaled;
                var trX = tlX + widthScaled;

                var uvBLx = (cachedData.rectX - paddingPixels) * invAtlasSize;
                var uvBLy = (cachedData.rectY - paddingPixels) * invAtlasSize;
                var uvTLy = (cachedData.rectY + cachedData.rectHeight + paddingPixels) * invAtlasSize;
                var uvTRx = (cachedData.rectX + cachedData.rectWidth + paddingPixels) * invAtlasSize;

                var i0 = vertexCount;
                var i1 = vertexCount + 1;
                var i2 = vertexCount + 2;
                var i3 = vertexCount + 3;

                ref var v0 = ref verts[i0];
                v0.x = tlX; v0.y = blY; v0.z = 0;
                ref var v1 = ref verts[i1];
                v1.x = tlX; v1.y = tlY; v1.z = 0;
                ref var v2 = ref verts[i2];
                v2.x = trX; v2.y = tlY; v2.z = 0;
                ref var v3 = ref verts[i3];
                v3.x = trX; v3.y = blY; v3.z = 0;

                var layerZ = (float)cachedData.atlasIndex;

                ref var uv0 = ref uvData[i0];
                uv0.x = uvBLx; uv0.y = uvBLy; uv0.z = layerZ; uv0.w = 0;
                ref var uv1 = ref uvData[i1];
                uv1.x = uvBLx; uv1.y = uvTLy; uv1.z = layerZ; uv1.w = 0;
                ref var uv2 = ref uvData[i2];
                uv2.x = uvTRx; uv2.y = uvTLy; uv2.z = layerZ; uv2.w = 0;
                ref var uv3 = ref uvData[i3];
                uv3.x = uvTRx; uv3.y = uvBLy; uv3.z = layerZ; uv3.w = 0;

                cols[i0] = glyphColor;
                cols[i1] = glyphColor;
                cols[i2] = glyphColor;
                cols[i3] = glyphColor;

                var localI0 = i0 - currentSegmentVertexStart;
                tris[triangleCount] = localI0;
                tris[triangleCount + 1] = localI0 + 1;
                tris[triangleCount + 2] = localI0 + 2;
                tris[triangleCount + 3] = localI0 + 2;
                tris[triangleCount + 4] = localI0 + 3;
                tris[triangleCount + 5] = localI0;

                currentCluster = cluster;
                height = heightScaled;
                baselineY = offY - glyph.y;

                vertexCount += 4;
                triangleCount += 6;

                if (glyph.shapedGlyphIndex >= 0)
                    onGlyph?.Invoke();

                verts = vertices.data;
                uvData = uvs0.data;
                cols = colors.data;
                tris = triangles.data;
            }

            if (skippedGlyphs > 0)
                Cat.MeowFormat("[GenerateEmojiSegment] {0}: SKIPPED {1} glyphs", font.CachedName, skippedGlyphs);
            if (zeroRectGlyphs > 0)
                Cat.MeowFormat("[GenerateEmojiSegment] {0}: ZERO RECT {1} glyphs", font.CachedName, zeroRectGlyphs);
        }

        /// <summary>
        /// Expands a glyph quad's vertices and UVs if effect passes require more padding than the initial default.
        /// Called after OnGlyph callbacks so <see cref="currentMaxEffectExtent"/> is finalized.
        /// </summary>
        private void ExpandQuadForEffects(int baseIdx, float glyphH, float metricsFactor, float currentPadding)
        {
            if (glyphH < 1e-6f) return;

            var faceDilate = uvs1.data[baseIdx].y;
            var facePad = faceDilate * GlyphAtlas.Pad / glyphH;
            var requiredPad = facePad > currentMaxEffectExtent ? facePad : currentMaxEffectExtent;

            if (requiredPad <= currentPadding) return;

            var maxPad = GlyphAtlas.Pad / glyphH;
            requiredPad = requiredPad < maxPad ? requiredPad : maxPad;

            var delta = requiredPad - currentPadding;
            var deltaPixels = delta * glyphH * metricsFactor;

            var verts = vertices.data;
            verts[baseIdx].x -= deltaPixels;
            verts[baseIdx].y -= deltaPixels;
            verts[baseIdx + 1].x -= deltaPixels;
            verts[baseIdx + 1].y += deltaPixels;
            verts[baseIdx + 2].x += deltaPixels;
            verts[baseIdx + 2].y += deltaPixels;
            verts[baseIdx + 3].x += deltaPixels;
            verts[baseIdx + 3].y -= deltaPixels;

            var uvData = uvs0.data;
            uvData[baseIdx].x -= delta;
            uvData[baseIdx].y -= delta;
            uvData[baseIdx + 1].x -= delta;
            uvData[baseIdx + 1].y += delta;
            uvData[baseIdx + 2].x += delta;
            uvData[baseIdx + 2].y += delta;
            uvData[baseIdx + 3].x += delta;
            uvData[baseIdx + 3].y -= delta;
        }



        /// <summary>
        /// Creates Unity meshes from the generated mesh data and returns render data for each segment.
        /// </summary>
        /// <returns>
        /// A list of <see cref="UniTextRenderData"/> containing mesh, material, and texture for each segment.
        /// The returned list is shared and will be cleared on the next call.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This method must be called after <see cref="GenerateMeshDataOnly"/> to apply the generated
        /// vertex data to actual Unity meshes. At most 2 entries: SDF mesh (index 0) and emoji mesh (index 1).
        /// </para>
        /// <para>
        /// Uses <see cref="SharedMeshes"/> for mesh instances. CanvasRenderer.SetMesh() copies data,
        /// so the same mesh instances can be reused across all components.
        /// </para>
        /// </remarks>
        public List<UniTextRenderData> ApplyMeshesToUnity()
        {
            var resultBuffer = SharedPipelineComponents.MeshResultBuffer;
            resultBuffer.Clear();

            if (!hasGeneratedData)
                return resultBuffer;

            if (sdfVertexCount > 0)
            {
                var mesh = SharedMeshes.Get(0);
                mesh.Clear(false);
                mesh.SetVertices(vertices.data, 0, sdfVertexCount);
                mesh.SetUVs(0, uvs0.data, 0, sdfVertexCount);
                mesh.SetUVs(1, uvs1.data, 0, sdfVertexCount);
                if (uvs2.data != null)
                    mesh.SetUVs(2, uvs2.data, 0, sdfVertexCount);
                if (uvs3.data != null)
                    mesh.SetUVs(3, uvs3.data, 0, sdfVertexCount);
                mesh.SetColors(colors.data, 0, sdfVertexCount);
                mesh.SetTriangles(triangles.data, 0, sdfTriangleCount, 0);
                resultBuffer.Add(new UniTextRenderData(mesh, sdfFontId));
            }

            if (emojiVertexCount > 0)
            {
                var mesh = SharedMeshes.Get(1);
                mesh.Clear(false);
                mesh.SetVertices(vertices.data, sdfVertexCount, emojiVertexCount);
                mesh.SetUVs(0, uvs0.data, sdfVertexCount, emojiVertexCount);
                mesh.SetColors(colors.data, sdfVertexCount, emojiVertexCount);
                mesh.SetTriangles(triangles.data, sdfTriangleCount, emojiTriangleCount, 0);
                resultBuffer.Add(new UniTextRenderData(mesh, emojiFontId));
            }

            return resultBuffer;
        }

        /// <summary>
        /// Ensures atlas subscription for auto-material management.
        /// Must be called on the main thread (creates materials lazily).
        /// </summary>
        public void AssignAutoMaterials()
        {
            if (!hasGeneratedData) return;

            bool isMsdf = RenderMode == UniTextBase.RenderModee.MSDF;

            if (isMsdf)
                UniTextMaterialCache.EnsureMsdfAtlasSubscription();
            else
                UniTextMaterialCache.EnsureAtlasSubscription();
        }

        #endregion
    }

}
