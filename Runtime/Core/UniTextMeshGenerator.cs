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
    /// Returned by <see cref="UniTextMeshGenerator.ApplyMeshesToUnity"/> for each font/atlas combination.
    /// Consumers use this data to set up renderers with the appropriate mesh, material, and texture.
    /// For 2-pass rendering, materials array contains [0]=outline material, [1]=face material.
    /// </remarks>
    public struct UniTextRenderData
    {
        /// <summary>The Unity mesh containing vertex, UV, color, and triangle data.</summary>
        public Mesh mesh;

        /// <summary>Materials for rendering. Single for normal, two for 2-pass (outline + face).</summary>
        public Material[] materials;

        /// <summary>The font atlas texture containing glyph images.</summary>
        public Texture texture;

        /// <summary>The font identifier this render data belongs to.</summary>
        public int fontId;

        /// <summary>Gets the primary material (first in array, for backward compatibility).</summary>
        public Material material => materials != null && materials.Length > 0 ? materials[0] : null;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniTextRenderData"/> struct.
        /// </summary>
        /// <param name="mesh">The Unity mesh.</param>
        /// <param name="materials">The rendering materials (single or multiple for 2-pass).</param>
        /// <param name="texture">The atlas texture.</param>
        /// <param name="fontId">The font identifier.</param>
        public UniTextRenderData(Mesh mesh, Material[] materials, Texture texture, int fontId)
        {
            this.mesh = mesh;
            this.materials = materials;
            this.texture = texture;
            this.fontId = fontId;
        }

        /// <summary>
        /// Initializes a new instance with a single material (backward compatibility).
        /// </summary>
        public UniTextRenderData(Mesh mesh, Material material, Texture texture, int fontId)
        {
            this.mesh = mesh;
            this.materials = material != null ? new[] { material } : null;
            this.texture = texture;
            this.fontId = fontId;
        }
    }


    /// <summary>
    /// Represents a contiguous segment of mesh data for a single font and atlas combination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// During mesh generation, glyphs are grouped by font and atlas to minimize draw calls.
    /// Each segment contains indices into the shared vertex and triangle buffers.
    /// </para>
    /// <para>
    /// When <see cref="UniTextMeshGenerator.ApplyMeshesToUnity"/> is called, each segment
    /// becomes a separate <see cref="UniTextRenderData"/> for rendering.
    /// </para>
    /// </remarks>
    internal struct GeneratedMeshSegment
    {
        /// <summary>The font identifier for this segment.</summary>
        public int fontId;

        /// <summary>The atlas texture index within the font (for multi-atlas fonts).</summary>
        public int atlasIndex;

        /// <summary>Starting index in the shared vertex buffer.</summary>
        public int vertexStart;

        /// <summary>Number of vertices in this segment.</summary>
        public int vertexCount;

        /// <summary>Starting index in the shared triangle buffer.</summary>
        public int triangleStart;

        /// <summary>Number of triangle indices in this segment.</summary>
        public int triangleCount;

        /// <summary>Materials for rendering this segment. Single for normal, multiple for 2-pass.</summary>
        public Material[] materials;

        /// <summary>Gets the primary material (backward compatibility).</summary>
        public Material material => materials != null && materials.Length > 0 ? materials[0] : null;

        /// <summary>The atlas texture for this segment.</summary>
        public Texture texture;
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
    /// <item>Groups glyphs by font and atlas to minimize draw calls</item>
    /// <item>Uses pooled buffers from <see cref="UniTextArrayPool{T}"/> for zero allocations</item>
    /// <item>Supports multi-atlas fonts with automatic segment splitting</item>
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
        /// Only valid within <see cref="OnGlyph"/>, <see cref="OnBeforeMesh"/>, and similar callbacks.
        /// </remarks>
        public static UniTextMeshGenerator Current => current;

        /// <summary>The cluster index of the glyph currently being processed.</summary>
        /// <remarks>Valid during <see cref="OnGlyph"/> callback. Maps back to codepoint indices.</remarks>
        public int currentCluster;

        /// <summary>X position of the current glyph in text coordinates.</summary>
        /// <remarks>Valid during <see cref="OnGlyph"/> callback.</remarks>
        public float x;

        /// <summary>Y position of the current glyph in text coordinates.</summary>
        /// <remarks>Valid during <see cref="OnGlyph"/> callback.</remarks>
        public float y;

        /// <summary>Width of the current glyph including padding.</summary>
        /// <remarks>Valid during <see cref="OnGlyph"/> callback.</remarks>
        public float width;

        /// <summary>Height of the current glyph including padding.</summary>
        /// <remarks>Valid during <see cref="OnGlyph"/> callback.</remarks>
        public float height;

        /// <summary>Y coordinate of the text baseline for the current glyph.</summary>
        /// <remarks>Valid during <see cref="OnGlyph"/> callback.</remarks>
        public float baselineY;

        /// <summary>Current font scale factor (FontSize / font.UnitsPerEm).</summary>
        public float scale;

        /// <summary>Horizontal scale factor accounting for canvas and world space.</summary>
        public float xScale;

        /// <summary>Default vertex color applied to all glyphs.</summary>
        public Color32 defaultColor;

        /// <summary>Atlas padding in pixels from font settings.</summary>
        public float paddingPixels;

        /// <summary>Conversion factor from font units to atlas pixels: upem / samplingPointSize.</summary>
        public float paddingConversion;

        /// <summary>Padding in font units: paddingPixels * paddingConversion.</summary>
        public float padding;

        /// <summary>Double padding for width/height calculations.</summary>
        public float padding2;

        /// <summary>Gradient scale for SDF edge sharpness: samplingPointSize * paddingPixels / 72.</summary>
        public float gradientScale;

        /// <summary>Spread ratio for effect normalization: paddingPixels / samplingPointSize.</summary>
        /// <remarks>Used to make visual effect strength independent of PointSize and Padding settings.</remarks>
        public float spreadRatio;

        /// <summary>Inverse atlas size for UV calculations: 1 / atlasSize.</summary>
        public float invAtlasSize;

        /// <summary>Atlas size in pixels.</summary>
        public int atlasSize;

        /// <summary>Current font being processed.</summary>
        /// <remarks>Valid during mesh generation for the current font segment.</remarks>
        public UniTextFont font;

        /// <summary>X offset from the rect origin.</summary>
        public float offsetX;

        /// <summary>Y offset from the rect origin.</summary>
        public float offsetY;

        /// <summary>Width of the layout rect.</summary>
        public float rectWidth;

        /// <summary>Current horizontal alignment setting.</summary>
        public HorizontalAlignment hAlignment;

        /// <summary>Current number of vertices in the mesh buffers.</summary>
        public int vertexCount;

        /// <summary>Current number of triangle indices in the mesh buffers.</summary>
        public int triangleCount;

        [ThreadStatic] private static FastIntDictionary<PooledList<int>> glyphsByAtlas;

        private PooledBuffer<Vector3> vertices;
        private PooledBuffer<Vector4> uvs0;
        private PooledBuffer<Vector4> uvs1;
        private PooledBuffer<Color32> colors;
        private PooledBuffer<int> triangles;
        private PooledList<GeneratedMeshSegment> generatedSegments;
        private bool hasGeneratedData;
        private int currentSegmentVertexStart;
        private int currentAtlasIndex;

        /// <summary>Starting vertex index for the current segment. Used to compute relative triangle indices.</summary>
        public int CurrentSegmentVertexStart => currentSegmentVertexStart;

        /// <summary>Current atlas index being processed. Used by modifiers to ensure glyphs match the current atlas.</summary>
        public int CurrentAtlasIndex => currentAtlasIndex;

        private readonly UniTextFontProvider fontProvider;
        private readonly UniTextBuffers buf;
        private Canvas canvas;
        private float lossyScale = 1f;
        private bool hasWorldCamera;

        /// <summary>Invoked after all glyphs for a single font have been processed.</summary>
        /// <remarks>Called once per font in the text. Useful for font-specific post-processing.</remarks>
        public Action OnAfterGlyphsPerFont;

        /// <summary>Invoked before mesh generation begins for a font.</summary>
        /// <remarks>Called once per font. Useful for initialization in modifiers.</remarks>
        public Action OnBeforeMesh;

        /// <summary>Invoked for each glyph during mesh generation.</summary>
        /// <remarks>
        /// Primary callback for text modifiers to apply per-glyph effects.
        /// Access current glyph data via <see cref="Current"/> or public fields.
        /// </remarks>
        public Action OnGlyph;

        /// <summary>Invoked after all mesh generation is complete.</summary>
        public Action OnRebuildEnd;

        /// <summary>Invoked before mesh generation starts.</summary>
        public Action OnRebuildStart;

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

        /// <summary>Gets a value indicating whether mesh data has been generated and is available.</summary>
        public bool HasGeneratedData => hasGeneratedData;

        /// <summary>Gets the vertex position buffer (X, Y, Z coordinates).</summary>
        public Vector3[] Vertices => vertices.data;

        /// <summary>Gets the primary UV buffer (texture coordinates and scale in W component).</summary>
        public Vector4[] Uvs0 => uvs0.data;

        /// <summary>Gets the vertex color buffer.</summary>
        public Color32[] Colors => colors.data;

        /// <summary>Gets the triangle index buffer.</summary>
        public int[] Triangles => triangles.data;

        /// <summary>Gets the list of generated mesh segments, one per font/atlas combination.</summary>
        internal PooledList<GeneratedMeshSegment> GeneratedSegments => generatedSegments;

        /// <summary>Gets the secondary UV buffer containing effect normalization data.</summary>
        /// <remarks>
        /// Layout: xy = line position (LineRenderHelper), z = effectNormFactor, w = reserved.
        /// effectNormFactor normalizes effect strength across fonts with different settings.
        /// </remarks>
        public Vector4[] Uvs1 => uvs1.data;

        #region Instance Buffer Management

        private void RentInstanceBuffers(int estimatedVertices, int estimatedTriangles)
        {
            vertices.Rent(estimatedVertices);
            uvs0.Rent(estimatedVertices);
            uvs1.Rent(estimatedVertices);
            colors.Rent(estimatedVertices);
            triangles.Rent(estimatedTriangles);
            generatedSegments ??= new PooledList<GeneratedMeshSegment>(4);
            generatedSegments.FakeClear();

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
            colors.Return();
            triangles.Return();
            generatedSegments?.Return();
            hasGeneratedData = false;
        }

        /// <summary>
        /// Releases all pooled resources. Call when the generator is no longer needed.
        /// </summary>
        public void Dispose()
        {
            ReturnInstanceBuffers();
            generatedSegments = null;
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
            rectWidth = rect.width;
        }

        /// <summary>
        /// Sets the horizontal text alignment.
        /// </summary>
        /// <param name="alignment">The alignment to apply.</param>
        public void SetHorizontalAlignment(HorizontalAlignment alignment)
        {
            hAlignment = alignment;
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
        /// </summary>
        /// <param name="glyphs">The positioned glyphs from text layout.</param>
        /// <remarks>
        /// <para>
        /// This method performs the following:
        /// <list type="number">
        /// <item>Groups glyphs by font to minimize draw calls</item>
        /// <item>For multi-atlas fonts, further groups by atlas index</item>
        /// <item>Generates quad geometry for each glyph (4 vertices, 6 triangle indices)</item>
        /// <item>Invokes callbacks (<see cref="OnGlyph"/>, <see cref="OnBeforeMesh"/>, etc.) for modifiers</item>
        /// <item>Creates <see cref="GeneratedMeshSegment"/> entries for each font/atlas combination</item>
        /// </list>
        /// </para>
        /// <para>
        /// After calling this method, use <see cref="ApplyMeshesToUnity"/> to create Unity meshes,
        /// then <see cref="ReturnInstanceBuffers"/> to release pooled buffers.
        /// </para>
        /// </remarks>
        /// <seealso cref="ApplyMeshesToUnity"/>
        /// <seealso cref="ReturnInstanceBuffers"/>
        public void GenerateMeshDataOnly(ReadOnlySpan<PositionedGlyph> glyphs)
        {
            OnRebuildStart?.Invoke();

            var glyphLen = glyphs.Length;
            var estimatedVertices = glyphLen * 4;
            var estimatedTriangles = glyphLen * 6;

            RentInstanceBuffers(estimatedVertices, estimatedTriangles);

            var glyphsByFont = SharedPipelineComponents.GlyphsByFont;
            SharedPipelineComponents.ClearGlyphsByFont();

            var lastFontId = int.MinValue;
            PooledList<int> lastList = null;

            for (var i = 0; i < glyphLen; i++)
            {
                var fontId = glyphs[i].fontId;
                if (lastFontId != fontId)
                {
                    lastFontId = fontId;
                    if (!glyphsByFont.TryGetValue(fontId, out var list))
                    {
                        list = SharedPipelineComponents.AcquireGlyphIndexList(glyphLen);
                        glyphsByFont[fontId] = list;
                    }
                    lastList = list;
                }
                lastList.buffer[lastList.buffer.count++] = i;
            }

            var positionedGlyphs = buf.positionedGlyphs.data;
            foreach (var kvp in glyphsByFont)
            {
                var fontId = kvp.Key;
                var glyphIndices = kvp.Value;
                var fontAsset = fontProvider.GetFontAsset(fontId);
                var glyphLookup = fontAsset.GlyphLookupTable;
                var hasMultipleAtlases = fontAsset.AtlasTextures is { Count: > 1 };
                var materials = fontProvider.GetMaterials(fontId);

                if (!hasMultipleAtlases)
                {
                    var vertexStart = vertices.count;
                    var triangleStart = triangles.count;
                    currentSegmentVertexStart = vertexStart;
                    currentAtlasIndex = 0;

                    GenerateMeshDataForFont(glyphIndices, positionedGlyphs, fontAsset);

                    generatedSegments.Add(new GeneratedMeshSegment
                    {
                        fontId = fontId,
                        atlasIndex = 0,
                        vertexStart = vertexStart,
                        vertexCount = vertices.count - vertexStart,
                        triangleStart = triangleStart,
                        triangleCount = triangles.count - triangleStart,
                        materials = materials,
                        texture = fontAsset.AtlasTexture
                    });
                }
                else
                {
                    glyphsByAtlas ??= new FastIntDictionary<PooledList<int>>();
                    glyphsByAtlas.ClearFast();

                    var count = glyphIndices.Count;
                    for (var i = 0; i < count; i++)
                    {
                        var glyphIndex = glyphIndices[i];
                        ref readonly var glyph = ref positionedGlyphs[glyphIndex];
                        var atlasIndex = 0;
                        if (glyphLookup.TryGetValue((uint)glyph.glyphId, out var glyphData))
                            atlasIndex = glyphData.atlasIndex;

                        if (!glyphsByAtlas.TryGetValue(atlasIndex, out var atlasList))
                        {
                            atlasList = SharedPipelineComponents.AcquireGlyphIndexList(count);
                            glyphsByAtlas[atlasIndex] = atlasList;
                        }
                        atlasList.buffer[atlasList.buffer.count++] = glyphIndex;
                    }

                    foreach (var atlasKvp in glyphsByAtlas)
                    {
                        var atlasIndex = atlasKvp.Key;
                        var atlasIndices = atlasKvp.Value;

                        var vertexStart = vertices.count;
                        var triangleStart = triangles.count;
                        currentSegmentVertexStart = vertexStart;
                        currentAtlasIndex = atlasIndex;

                        GenerateMeshDataForFont(atlasIndices, positionedGlyphs, fontAsset);

                        var atlasTexture = fontAsset.AtlasTextures != null && atlasIndex < fontAsset.AtlasTextures.Count
                            ? fontAsset.AtlasTextures[atlasIndex]
                            : fontAsset.AtlasTexture;

                        generatedSegments.Add(new GeneratedMeshSegment
                        {
                            fontId = fontId,
                            atlasIndex = atlasIndex,
                            vertexStart = vertexStart,
                            vertexCount = vertices.count - vertexStart,
                            triangleStart = triangleStart,
                            triangleCount = triangles.count - triangleStart,
                            materials = materials,
                            texture = atlasTexture
                        });

                        SharedPipelineComponents.ReleaseGlyphIndexList(atlasIndices);
                    }

                    var virtualCps = buf.virtualCodepoints;
                    if (virtualCps.count > 0)
                    {
                        var charLookup = fontAsset.CharacterLookupTable;
                        if (charLookup != null)
                        {
                            for (var v = 0; v < virtualCps.count; v++)
                            {
                                var vcp = virtualCps.data[v];
                                if (!charLookup.TryGetValue(vcp, out var character) || character == null)
                                    continue;

                                var vcpAtlasIndex = character.glyph.atlasIndex;
                                if (glyphsByAtlas.ContainsKey(vcpAtlasIndex))
                                    continue;

                                var vertexStart = vertices.count;
                                var triangleStart = triangles.count;
                                currentSegmentVertexStart = vertexStart;
                                currentAtlasIndex = vcpAtlasIndex;

                                var emptyList = SharedPipelineComponents.AcquireGlyphIndexList(0);
                                GenerateMeshDataForFont(emptyList, positionedGlyphs, fontAsset);
                                SharedPipelineComponents.ReleaseGlyphIndexList(emptyList);

                                var atlasTexture = fontAsset.AtlasTextures != null && vcpAtlasIndex < fontAsset.AtlasTextures.Count
                                    ? fontAsset.AtlasTextures[vcpAtlasIndex]
                                    : fontAsset.AtlasTexture;

                                generatedSegments.Add(new GeneratedMeshSegment
                                {
                                    fontId = fontId,
                                    atlasIndex = vcpAtlasIndex,
                                    vertexStart = vertexStart,
                                    vertexCount = vertices.count - vertexStart,
                                    triangleStart = triangleStart,
                                    triangleCount = triangles.count - triangleStart,
                                    materials = materials,
                                    texture = atlasTexture
                                });

                                glyphsByAtlas[vcpAtlasIndex] = emptyList;
                            }
                        }
                    }

                    glyphsByAtlas.ClearFast();
                }
            }

            buf.hasValidGlyphCache = true;
            hasGeneratedData = true;

            Cat.MeowFormat("[MeshGenerator] Generated: {0} verts, {1} tris, {2} segments",
                vertices.count, triangles.count, generatedSegments.Count);

            OnRebuildEnd?.Invoke();
        }

        
        private void GenerateMeshDataForFont(PooledList<int> glyphIndices, PositionedGlyph[] positionedGlyphs, UniTextFont font)
        {
            var glyphCount = glyphIndices.Count;

            var upem = font.UnitsPerEm;
            var fontScaleMul = font.FontScale;
            var scaleVal = FontSize * fontScaleMul / upem;
            var atlasSizeVal = font.AtlasSize;

            var paddingPixelsVal = font.AtlasPadding;

            var samplingPointSize = font.FaceInfo.pointSize;
            var paddingConversionVal = samplingPointSize > 0 ? (float)upem / samplingPointSize : 1f;
            var paddingVal = paddingPixelsVal * paddingConversionVal;
            var padding2Val = paddingVal * 2;

            var invAtlasSizeVal = 1f / atlasSizeVal;

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;

            var shaderScale = samplingPointSize > 0 ? FontSize * fontScaleMul / samplingPointSize : scaleVal;
            var xScaleVal = CalculateXScale(shaderScale);

            var gradientScaleVal = paddingPixelsVal > 0 ? samplingPointSize * paddingPixelsVal / 72f : 20f;
            var spreadRatioVal = samplingPointSize > 0 ? (float)paddingPixelsVal / samplingPointSize : 0.1f;

            scale = scaleVal;
            xScale = xScaleVal;
            offsetX = offX;
            offsetY = offY;
            this.font = font;
            paddingPixels = paddingPixelsVal;
            paddingConversion = paddingConversionVal;
            padding = paddingVal;
            padding2 = padding2Val;
            gradientScale = gradientScaleVal;
            spreadRatio = spreadRatioVal;
            invAtlasSize = invAtlasSizeVal;
            atlasSize = (int)atlasSizeVal;
            vertexCount = vertices.count;
            triangleCount = triangles.count;

            EnsureCapacity(glyphCount * 4, glyphCount * 6);

            var isColorFont = font.IsColor;
            var glyphColor = isColorFont
                ? new Color32(255, 255, 255, defaultColor.a)
                : defaultColor;

            OnBeforeMesh?.Invoke();

            var glyphLookup = font.GlyphLookupTable;

            buf.glyphDataCache.EnsureCapacity(buf.shapedGlyphs.count);
            var glyphCache = buf.glyphDataCache.data;
            var useCache = buf.hasValidGlyphCache;

            var verts = vertices.data;
            var uvData = uvs0.data;
            var uv1Data = uvs1.data;
            var cols = colors.data;
            var tris = triangles.data;

            var skippedGlyphs = 0;
            var zeroRectGlyphs = 0;
            Cat.MeowFormat("[GenerateMeshDataForFont] {0}: processing {1} glyphs, glyphLookup={2}, glyphTable={3}, atlas={4}",
                font.CachedName, glyphCount, glyphLookup?.Count ?? 0, font.GlyphTableDiagCount, font.AtlasTexturesDiagCount);

            for (var i = 0; i < glyphCount; i++)
            {
                var glyphIndex = glyphIndices[i];
                ref var glyph = ref positionedGlyphs[glyphIndex];
                var cacheIndex = glyph.shapedGlyphIndex;

                ref var cachedData = ref glyphCache[cacheIndex];
                if (!useCache || !cachedData.isValid)
                {
                    var glyphId = (uint)glyph.glyphId;
                    if (!glyphLookup.TryGetValue(glyphId, out var glyphData))
                    {
                        skippedGlyphs++;
                        cachedData.isValid = false;
                        continue;
                    }

                    var rect = glyphData.glyphRect;
                    var metrics = glyphData.metrics;
                    cachedData.rectX = rect.x;
                    cachedData.rectY = rect.y;
                    cachedData.rectWidth = rect.width;
                    cachedData.rectHeight = rect.height;
                    cachedData.bearingX = metrics.horizontalBearingX;
                    cachedData.bearingY = metrics.horizontalBearingY;
                    cachedData.width = metrics.width;
                    cachedData.height = metrics.height;
                    cachedData.isValid = true;
                }

                if (cachedData.rectWidth == 0 || cachedData.rectHeight == 0)
                {
                    if (zeroRectGlyphs == 0)
                        Cat.MeowFormat("[GenerateMeshDataForFont] {0}: ZERO RECT glyph id={1}, rect=({2},{3},{4},{5}), metrics w={6} h={7}",
                            font.CachedName, (uint)glyph.glyphId, cachedData.rectX, cachedData.rectY, cachedData.rectWidth, cachedData.rectHeight,
                            cachedData.width, cachedData.height);
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

                ref var uv0 = ref uvData[i0];
                uv0.x = uvBLx; uv0.y = uvBLy; uv0.z = gradientScale; uv0.w = xScale;
                ref var uv1 = ref uvData[i1];
                uv1.x = uvBLx; uv1.y = uvTLy; uv1.z = gradientScale; uv1.w = xScale;
                ref var uv2 = ref uvData[i2];
                uv2.x = uvTRx; uv2.y = uvTLy; uv2.z = gradientScale; uv2.w = xScale;
                ref var uv3 = ref uvData[i3];
                uv3.x = uvTRx; uv3.y = uvBLy; uv3.z = gradientScale; uv3.w = xScale;

                cols[i0] = glyphColor;
                cols[i1] = glyphColor;
                cols[i2] = glyphColor;
                cols[i3] = glyphColor;

                var uv1Val = new Vector4(spreadRatio, 0, 0, 0);
                uv1Data[i0] = uv1Val;
                uv1Data[i1] = uv1Val;
                uv1Data[i2] = uv1Val;
                uv1Data[i3] = uv1Val;

                var localI0 = i0 - currentSegmentVertexStart;
                var localI1 = i1 - currentSegmentVertexStart;
                var localI2 = i2 - currentSegmentVertexStart;
                var localI3 = i3 - currentSegmentVertexStart;

                tris[triangleCount] = localI0;
                tris[triangleCount + 1] = localI1;
                tris[triangleCount + 2] = localI2;
                tris[triangleCount + 3] = localI2;
                tris[triangleCount + 4] = localI3;
                tris[triangleCount + 5] = localI0;

                currentCluster = cluster;
                x = glyph.x;
                y = glyph.y;
                width = widthScaled;
                height = heightScaled;
                baselineY = offY - glyph.y;

                vertexCount += 4;
                triangleCount += 6;

                OnGlyph?.Invoke();

                verts = vertices.data;
                uvData = uvs0.data;
                uv1Data = uvs1.data;
                cols = colors.data;
                tris = triangles.data;
            }

            if (skippedGlyphs > 0)
                Cat.MeowFormat("[GenerateMeshDataForFont] {0}: SKIPPED {1} glyphs (not in lookup)", font.CachedName, skippedGlyphs);
            if (zeroRectGlyphs > 0)
                Cat.MeowFormat("[GenerateMeshDataForFont] {0}: ZERO RECT {1} glyphs (in lookup but rect is 0x0)", font.CachedName, zeroRectGlyphs);

            OnAfterGlyphsPerFont?.Invoke();

            vertices.count = vertexCount;
            uvs0.count = vertexCount;
            uvs1.count = vertexCount;
            colors.count = vertexCount;
            triangles.count = triangleCount;
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
        /// vertex data to actual Unity meshes. Each <see cref="GeneratedMeshSegment"/> becomes one
        /// <see cref="UniTextRenderData"/> entry.
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

            if (!hasGeneratedData || generatedSegments == null || generatedSegments.Count == 0)
                return resultBuffer;

            for (var i = 0; i < generatedSegments.Count; i++)
            {
                ref var segment = ref generatedSegments.buffer[i];

                var mesh = SharedMeshes.Get(i);
                mesh.Clear();

                if (segment.vertexCount > 0)
                {
                    mesh.SetVertices(vertices.data, segment.vertexStart, segment.vertexCount);
                    mesh.SetUVs(0, uvs0.data, segment.vertexStart, segment.vertexCount);
                    mesh.SetUVs(1, uvs1.data, segment.vertexStart, segment.vertexCount);
                    mesh.SetColors(colors.data, segment.vertexStart, segment.vertexCount);

                    var matCount = segment.materials?.Length ?? 1;
                    if (matCount > 1)
                    {
                        mesh.subMeshCount = matCount;
                        for (var m = 0; m < matCount; m++)
                            mesh.SetTriangles(triangles.data, segment.triangleStart, segment.triangleCount, m);
                    }
                    else
                    {
                        mesh.SetTriangles(triangles.data, segment.triangleStart, segment.triangleCount, 0);
                    }
                }

                resultBuffer.Add(new UniTextRenderData(mesh, segment.materials, segment.texture, segment.fontId));
            }

            return resultBuffer;
        }

        #endregion
    }

}
