using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Threading.Tasks;
#endif

namespace LightSide
{
    /// <summary>
    /// UniText partial class handling batched and parallel text processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manages the static batch processing system that processes all dirty UniText
    /// components together. Uses <see cref="UniTextWorkerPool"/> for parallel execution
    /// when multiple components need updating.
    /// </para>
    /// <para>
    /// Processing occurs in two phases:
    /// <list type="number">
    /// <item>Pre-render: First pass (shaping, BiDi, script analysis) - can be parallel</item>
    /// <item>Will-render: Mesh generation and atlas updates - main thread only</item>
    /// </list>
    /// </para>
    /// </remarks>
    public partial class UniText
    {
        #region Cached Data for Parallel

        /// <summary>Cached transform data for parallel processing (avoids Unity API calls from worker threads).</summary>
        public struct CachedTransformData
        {
            /// <summary>The RectTransform.</summary>
            public RectTransform rectTransform;
            /// <summary>The RectTransform rect.</summary>
            public Rect rect;
            /// <summary>The transform's lossy scale X component.</summary>
            public float lossyScale;
            /// <summary>Whether the canvas has a world camera.</summary>
            public bool hasWorldCamera;
        }

        /// <summary>Cached transform data captured before parallel processing.</summary>
        public CachedTransformData cachedTransformData;

        private void PrepareForParallel()
        {
            var scale = transform.lossyScale.x;

            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                scale = 1f;

            cachedTransformData = new CachedTransformData
            {
                rectTransform = rectTransform,
                rect = rectTransform.rect,
                lossyScale = scale,
                hasWorldCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            };

            PrepareModifiersForParallel();
        }
        

        private void PrepareModifiersForParallel()
        {
            for (var i = 0; i < modRegisters.Count; i++)
            {
                var reg = modRegisters[i];
                if (reg.IsRegistered)
                    reg.Modifier.PrepareForParallel();
            }

            for (var i = 0; i < runtimeConfigCopies.Count; i++)
            {
                var config = runtimeConfigCopies[i];
                if (config == null) continue;
                var configMods = config.modRegisters;
                for (var j = 0; j < configMods.Count; j++)
                {
                    var reg = configMods[j];
                    if (reg.IsRegistered)
                        reg.Modifier.PrepareForParallel();
                }
            }
        }

        #endregion

        #region Static Batch Processing

        /// <summary>Gets or sets whether parallel processing is enabled for multiple UniText components.</summary>
        public static bool UseParallel { get; set; } = true;

        /// <summary>Raised before canvas rendering begins, after all UniText components are processed.</summary>
        public static event Action MeshApplied;
        private static PooledBuffer<UniText> componentsBuffer;
        private static bool isInitialized;
        private static bool useParallel;

        private const int ParallelCharacterThreshold = 500;
        private const int MinComponentsForParallel = 3;

        #region Parallel Atlas Pipeline

        private struct FontBatchEntry
        {
            public UniTextFont font;
            public HashSet<uint> glyphSet;
            public List<(uint unicode, uint glyphIndex)> characterEntries;
            public UniTextFont.PreparedBatch? prepared;
            public object rendered;
        }

        private sealed class FontReferenceComparer : IEqualityComparer<UniTextFont>
        {
            public static readonly FontReferenceComparer Instance = new();
            public bool Equals(UniTextFont x, UniTextFont y) => ReferenceEquals(x, y);
            public int GetHashCode(UniTextFont obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static Dictionary<UniTextFont, int> fontIndexMap;
        private static FontBatchEntry[] fontBatches;
        private static int fontBatchCount;
        private static Stack<HashSet<uint>> glyphSetPool;
        private static Stack<List<(uint, uint)>> charEntryPool;
        private static List<uint> tempGlyphList;

        private static int CollectGlyphRequestsFromAllComponents(PooledBuffer<UniText> components, int count)
        {
            fontIndexMap ??= new Dictionary<UniTextFont, int>(16, FontReferenceComparer.Instance);
            glyphSetPool ??= new Stack<HashSet<uint>>();
            charEntryPool ??= new Stack<List<(uint, uint)>>();
            fontBatches ??= new FontBatchEntry[8];

            for (int i = 0; i < fontBatchCount; i++)
            {
                ref var prev = ref fontBatches[i];
                if (prev.glyphSet != null) { prev.glyphSet.Clear(); glyphSetPool.Push(prev.glyphSet); }
                if (prev.characterEntries != null) { prev.characterEntries.Clear(); charEntryPool.Push(prev.characterEntries); }
                prev = default;
            }
            fontIndexMap.Clear();
            fontBatchCount = 0;

            for (int c = 0; c < count; c++)
            {
                var tp = components[c].textProcessor;
                if (tp == null || !tp.HasValidFirstPassData) continue;
                if (tp.HasValidGlyphsInAtlas) continue;

                var fontProvider = tp.FontProviderForAtlas;
                if (fontProvider == null) continue;

                var shapedRuns = tp.buf.shapedRuns.Span;
                var shapedGlyphs = tp.buf.shapedGlyphs.Span;

                for (int r = 0; r < shapedRuns.Length; r++)
                {
                    ref readonly var run = ref shapedRuns[r];
                    var fontAsset = fontProvider.GetFontAsset(run.fontId);
                    if (fontAsset == null) continue;

                    var codepoints = tp.buf.codepoints;
                    var provider = UnicodeData.Provider;
                    var end = run.glyphStart + run.glyphCount;
                    for (int g = run.glyphStart; g < end; g++)
                    {
                        var glyphIndex = (uint)shapedGlyphs[g].glyphId;
                        if (glyphIndex == 0)
                        {
                            var cp = codepoints.data[shapedGlyphs[g].cluster];
                            var cat = provider.GetGeneralCategory(cp);
                            if (cat is GeneralCategory.Cc or GeneralCategory.Cf
                                or GeneralCategory.Zl or GeneralCategory.Zp)
                                continue;
                        }
                        if (fontAsset.HasGlyphInAtlas(glyphIndex)) continue;

                        GetOrCreateEntry(fontAsset).glyphSet.Add(glyphIndex);
                    }
                }

                var vc = tp.buf.virtualCodepoints;
                for (int i = 0; i < vc.count; i++)
                {
                    var unicode = vc.data[i];
                    var fontId = fontProvider.FindFontForCodepoint((int)unicode);
                    var fontAsset = fontProvider.GetFontAsset(fontId);
                    if (fontAsset == null) continue;

                    var glyphIndex = fontAsset.GetGlyphIndexForUnicode(unicode);
                    if (fontAsset.HasGlyphInAtlas(glyphIndex)) continue;

                    ref var entry = ref GetOrCreateEntry(fontAsset);
                    entry.glyphSet.Add(glyphIndex);

                    entry.characterEntries ??= charEntryPool.Count > 0
                        ? charEntryPool.Pop()
                        : new List<(uint, uint)>(64);
                    entry.characterEntries.Add((unicode, glyphIndex));
                }

                tp.HasValidGlyphsInAtlas = true;
            }

            return fontBatchCount;
        }

        private static ref FontBatchEntry GetOrCreateEntry(UniTextFont font)
        {
            if (!fontIndexMap.TryGetValue(font, out var index))
            {
                index = fontBatchCount++;
                if (fontBatches.Length <= index)
                    Array.Resize(ref fontBatches, fontBatches.Length * 2);

                fontBatches[index] = new FontBatchEntry
                {
                    font = font,
                    glyphSet = glyphSetPool.Count > 0 ? glyphSetPool.Pop() : new HashSet<uint>(),
                };
                fontIndexMap[font] = index;
            }
            return ref fontBatches[index];
        }

        #endregion

        private static void EnsureInitialized()
        {
    #if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
    #endif
            if (isInitialized) return;

            var d = CanvasUpdateRegistry.instance;
            EmojiFont.EnsureInitialized();
            Canvas.preWillRenderCanvases += OnPreWillRenderCanvases;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            componentsBuffer.EnsureCapacity(64);
            isInitialized = true;

            Cat.Meow("[UniText] Initialized");
        }

        private static void RegisterDirty(UniText component)
        {
            EnsureInitialized();

            if (component.isRegisteredDirty)
                return;

            component.isRegisteredDirty = true;
            componentsBuffer.Add(component);
        }

        private static void UnregisterDirty(UniText component)
        {
            component.isRegisteredDirty = false;
        }

        private static bool CanWork
        {
            get
            {
                if (!UnicodeData.IsInitialized)
                {
                    UnicodeData.EnsureInitialized();
                    if (!UnicodeData.IsInitialized)
                    {
                        UniTextDebug.EndSample();
                        return false;
                    }
                }

                return true;
            }
        }

        private static void FilterAndPrepareComponents(bool validate)
        {
            for (var i = componentsBuffer.count - 1; i >= 0; i--)
            {
                var comp = componentsBuffer[i];
                if (comp == null || !comp.isRegisteredDirty || comp.sourceText.IsEmpty ||
                    (validate && !comp.ValidateAndInitialize()))
                {
                    if (comp != null)
                        comp.isRegisteredDirty = false;
                    componentsBuffer.SwapRemoveAt(i);
                    continue;
                }

                comp.isRegisteredDirty = false;
            }

            for (var i = 0; i < componentsBuffer.count; i++)
            {
                componentsBuffer[i].isRegisteredDirty = true;
                componentsBuffer[i].PrepareForParallel();
            }
        }
        

        private static void OnPreWillRenderCanvases()
        {
    #if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
    #endif
            if (componentsBuffer.count == 0) return;
            if (!CanWork) return;

            UniTextDebug.BeginSample("UniText.PreWillRender.FirstPass");

            FilterAndPrepareComponents(true);
            var count = componentsBuffer.count;
            var totalChars = 0;

            for (var i = 0; i < count; i++)
                totalChars += componentsBuffer[i].sourceText.Length;

            useParallel = totalChars > ParallelCharacterThreshold &&
                          count >= MinComponentsForParallel &&
                          UniTextWorkerPool.IsParallelSupported;

            LogBatchInfo(count, totalChars, useParallel && UseParallel);

            if (useParallel && UseParallel)
            {
                UniTextWorkerPool.Execute(componentsBuffer.data, count, static comp => comp.DoFirstPass());
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    componentsBuffer[i].DoFirstPass();
                }
            }

            UniTextDebug.EndSample();

            Cat.Meow("[UniText] OnPreWillRenderCanvases completed");
        }

        private static void OnWillRenderCanvases()
        {
    #if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
    #endif
            if (componentsBuffer.count == 0) return;
            if (!CanWork) return;

            UniTextDebug.BeginSample("UniText.WillRender.MeshGeneration");

            FilterAndPrepareComponents(false);
            var count = componentsBuffer.count;

            UniTextDebug.BeginSample("Rasterization");

            var batchCount = CollectGlyphRequestsFromAllComponents(componentsBuffer, count);

            if (batchCount > 0)
            {
                tempGlyphList ??= new List<uint>(256);

                for (int i = 0; i < batchCount; i++)
                {
                    ref var batch = ref fontBatches[i];
                    if (batch.glyphSet.Count == 0) continue;

                    tempGlyphList.Clear();
                    foreach (var glyph in batch.glyphSet)
                        tempGlyphList.Add(glyph);

                    batch.prepared = batch.font.PrepareGlyphBatch(tempGlyphList);

                    if (!batch.prepared.HasValue)
                        batch.font.TryAddGlyphsBatch(tempGlyphList);
                }

                int fontsToRender = 0;
                for (int i = 0; i < batchCount; i++)
                {
                    if (fontBatches[i].prepared.HasValue)
                    {
                        fontsToRender++;
                    }
                }
                
#if !UNITY_WEBGL || UNITY_EDITOR
                if (fontsToRender > 1)
                {
                    Parallel.For(0, batchCount, i =>
                    {
                        if (fontBatches[i].prepared.HasValue)
                            fontBatches[i].rendered = fontBatches[i].font.RenderPreparedBatch(fontBatches[i].prepared.Value);
                    });
                }
                else
#endif
                {
                    for (int i = 0; i < batchCount; i++)
                    {
                        if (fontBatches[i].prepared.HasValue)
                            fontBatches[i].rendered = fontBatches[i].font.RenderPreparedBatch(fontBatches[i].prepared.Value);
                    }
                }
                
                for (int i = 0; i < batchCount; i++)
                {
                    ref var batch = ref fontBatches[i];
                    if (batch.rendered != null)
                        batch.font.PackRenderedBatch(batch.rendered, batch.prepared.Value);
                    if (batch.characterEntries is { Count: > 0 })
                        batch.font.RegisterCharacterEntries(batch.characterEntries);
                }
            }

            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("MeshDataGeneration");
            if (useParallel && UseParallel)
            {
                UniTextWorkerPool.Execute(componentsBuffer.data, count, static comp => comp.DoGenerateMeshData());
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    componentsBuffer[i].DoGenerateMeshData();
                }
            }
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("ApplyMeshes");

            for (var i = 0; i < count; i++)
            {
                componentsBuffer[i].DoApplyMesh();
            }

            MeshApplied?.Invoke();

            UniTextDebug.EndSample();

            for (var i = 0; i < componentsBuffer.count; i++)
                componentsBuffer[i].isRegisteredDirty = false;
            componentsBuffer.Clear();

            UniTextDebug.EndSample();

            Cat.Meow("[UniText] OnWillRenderCanvases completed");
        }

        #endregion

        #region Instance Batch Methods

        private void DoFirstPass()
        {
            if (sourceText.IsEmpty) return;

            var textSpan = ParseOrGetParsedAttributes();
            var shapingFontSize = autoSize ? maxFontSize : fontSize;
            var settings = new TextProcessSettings
            {
                fontSize = shapingFontSize,
                baseDirection = baseDirection
            };
            textProcessor.EnsureFirstPass(textSpan, settings);
        }

        private void DoGenerateMeshData()
        {
            if (textProcessor == null || !textProcessor.HasValidFirstPassData) return;
            if (meshGenerator == null) return;

            Rebuilding?.Invoke();

            ref readonly var cached = ref cachedTransformData;

            var effectiveFontSize = autoSize
                ? (cachedEffectiveFontSize > 0 ? cachedEffectiveFontSize : maxFontSize)
                : fontSize;

            var positionsInvalid = !textProcessor.HasValidPositionedGlyphs;

            if (positionsInvalid)
            {
                var settings = CreateProcessSettings(cached.rect, effectiveFontSize);
                textProcessor.EnsurePositions(settings);
            }

            var glyphs = textProcessor.PositionedGlyphs;
            if (glyphs.IsEmpty) return;

            meshGenerator.FontSize = effectiveFontSize;
            meshGenerator.defaultColor = color;
            meshGenerator.SetCanvasParametersCached(cached.lossyScale, cached.hasWorldCamera);
            meshGenerator.SetRectOffset(cached.rect);
            meshGenerator.SetHorizontalAlignment(horizontalAlignment);

            meshGenerator.GenerateMeshDataOnly(glyphs);
        }

        private void DoApplyMesh()
        {
            if (sourceText.IsEmpty || meshGenerator == null || !meshGenerator.HasGeneratedData)
            {
                DeInit();
                dirtyFlags = DirtyFlags.None;
                return;
            }

            renderData = meshGenerator.ApplyMeshesToUnity();
    #if UNITEXT_TESTS
            CopyMeshesForTests();
    #endif
            meshGenerator.ReturnInstanceBuffers();

            if (textProcessor != null)
            {
                resultWidth = textProcessor.ResultWidth;
                resultHeight = textProcessor.ResultHeight;
            }

            UpdateRendering();

            dirtyFlags = DirtyFlags.None;
        }

        #endregion

        #region Debug

        [Conditional("UNITEXT_DEBUG")]
        private static void LogBatchInfo(int componentCount, int totalChars, bool parallel)
        {
            Cat.MeowFormat("[UniText] Batch: {0} components, {1} chars, parallel={2}", componentCount, totalChars, parallel);
        }

        #endregion

        public struct TestSegmentFontInfo
        {
            public int fontId;
            public int atlasIndex;
        }

    #if UNITEXT_TESTS
        #region Test Support

        private List<Mesh> testMeshSnapshots;
        private List<TestSegmentFontInfo> testSegmentFontInfo;
        private static List<Vector4> tempUvBuffer;
        public IReadOnlyList<Mesh> TestMeshSnapshots => testMeshSnapshots;
        public IReadOnlyList<TestSegmentFontInfo> TestSegmentFontInfoList => testSegmentFontInfo;

        private void CopyMeshesForTests()
        {
            if (renderData == null || renderData.Count == 0) return;

            testMeshSnapshots ??= new List<Mesh>();
            testSegmentFontInfo ??= new List<TestSegmentFontInfo>();
            tempUvBuffer ??= new List<Vector4>();

            foreach (var m in testMeshSnapshots)
            {
                ObjectUtils.SafeDestroy(m);
            }
            testMeshSnapshots.Clear();
            testSegmentFontInfo.Clear();

            foreach (var rd in renderData)
            {
                var copy = new Mesh();
                copy.vertices = rd.mesh.vertices;
                copy.triangles = rd.mesh.triangles;

                tempUvBuffer.Clear();
                rd.mesh.GetUVs(0, tempUvBuffer);
                copy.SetUVs(0, tempUvBuffer);

                copy.colors32 = rd.mesh.colors32;
                testMeshSnapshots.Add(copy);
            }

            var segments = meshGenerator.GeneratedSegments;
            if (segments != null)
            {
                for (var s = 0; s < segments.Count; s++)
                {
                    var seg = segments[s];
                    testSegmentFontInfo.Add(new TestSegmentFontInfo
                    {
                        fontId = seg.fontId,
                        atlasIndex = seg.atlasIndex
                    });
                }
            }
        }

        #endregion
    #endif
    }

}
