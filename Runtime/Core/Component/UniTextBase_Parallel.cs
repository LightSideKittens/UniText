using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Threading.Tasks;
#endif

namespace LightSide
{
    /// <summary>
    /// UniTextBase partial class handling batched and parallel text processing.
    /// Processes all dirty UniTextBase components (both UniText and UniTextWorld) together.
    /// </summary>
    public abstract partial class UniTextBase
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

        protected virtual void PrepareForParallel()
        {
            var scale = transform.lossyScale.x;

            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                scale = 1f;

            cachedTransformData = new CachedTransformData
            {
                rectTransform = rectTransform,
                rect = rectTransform.rect,
                lossyScale = scale,
                hasWorldCamera = GetHasWorldCamera()
            };

            PrepareModifiersForParallel();
        }

        private void PrepareModifiersForParallel()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                var reg = styles[i];
                if (reg.IsRegistered)
                    reg.Modifier.PrepareForParallel();
            }

            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                var config = runtimeStylePresetCopies[i];
                if (config == null) continue;
                var configMods = config.styles;
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

        /// <summary>Gets or sets whether parallel processing is enabled for multiple components.</summary>
        public static bool UseParallel { get; set; } = true;

        /// <summary>Raised before canvas rendering begins, after all components are processed.</summary>
        public static event Action MeshApplied;
        public static event Action AfterProcess;
        private static PooledBuffer<UniTextBase> componentsBuffer;
        private static bool isInitialized;
        private static bool useParallel;

        private const int ParallelCharacterThreshold = 500;
        private const int MinComponentsForParallel = 3;

        #region Parallel Atlas Pipeline

        private struct FontBatchEntry
        {
            public UniTextFont font;
            public RenderModee mode;
            public long[] glyphBits;
            public int glyphBitsMax;
            public List<(uint unicode, uint glyphIndex)> characterEntries;
            public UniTextFont.PreparedBatch? prepared;
            public object rendered;
            /// <summary>varHash48 for variable font runs. 0 = use DefaultVarHash48.</summary>
            public long varHash48;
            /// <summary>FT design coordinates for variable fonts. Null = default axes.</summary>
            public int[] ftCoords;
        }

        private const int GlyphBitsLength = 1024;

        private struct FontBatchKey : IEquatable<FontBatchKey>
        {
            public UniTextFont font;
            public RenderModee mode;
            public long varHash48;

            public bool Equals(FontBatchKey other) =>
                ReferenceEquals(font, other.font) && mode == other.mode && varHash48 == other.varHash48;

            public override bool Equals(object obj) => obj is FontBatchKey k && Equals(k);
            public override int GetHashCode() => font.GetCachedInstanceId() ^ ((int)mode * 397) ^ varHash48.GetHashCode();
        }

        private static Dictionary<FontBatchKey, int> fontIndexMap;
        private static FontBatchEntry[] fontBatches;
        private static int fontBatchCount;
        private static Stack<List<(uint, uint)>> charEntryPool;
        private static List<uint> tempGlyphList;

        private static int CollectGlyphRequestsFromAllComponents(PooledBuffer<UniTextBase> components, int count)
        {
            fontIndexMap ??= new Dictionary<FontBatchKey, int>(16);
            charEntryPool ??= new Stack<List<(uint, uint)>>();
            fontBatches ??= new FontBatchEntry[8];

            for (int i = 0; i < fontBatchCount; i++)
            {
                ref var prev = ref fontBatches[i];
                if (prev.glyphBits != null && prev.glyphBitsMax >= 0)
                    Array.Clear(prev.glyphBits, 0, (prev.glyphBitsMax >> 6) + 1);
                if (prev.characterEntries != null) { prev.characterEntries.Clear(); charEntryPool.Push(prev.characterEntries); }
                if (prev.prepared.HasValue) prev.prepared.Value.filteredGlyphs.Return();
                var keepBits = prev.glyphBits;
                prev = default;
                prev.glyphBits = keepBits;
                prev.glyphBitsMax = -1;
            }
            fontIndexMap.Clear();
            fontBatchCount = 0;

            for (int c = 0; c < count; c++)
            {
                var comp = components[c];
                var tp = comp.textProcessor;
                if (tp == null || !tp.HasValidFirstPassData) continue;
                if (tp.HasValidGlyphsInAtlas) continue;

                var fontProvider = tp.FontProviderForAtlas;
                if (fontProvider == null) continue;

                var renderMode = comp.RenderMode;
                var shapedRuns = tp.buf.shapedRuns.Span;
                var shapedGlyphs = tp.buf.shapedGlyphs.Span;

                var varMap = tp.buf.variationMap;

                for (int r = 0; r < shapedRuns.Length; r++)
                {
                    ref readonly var run = ref shapedRuns[r];
                    var fontAsset = fontProvider.GetFontAsset(run.fontId);
                    if (fontAsset is null) continue;

                    long runVarHash = 0;
                    int[] runFtCoords = null;
                    if (varMap != null && varMap.TryGetValue(run.fontId, out var varInfo))
                    {
                        runVarHash = varInfo.varHash48;
                        runFtCoords = varInfo.ftCoords;
                    }

                    var codepoints = tp.buf.codepoints;
                    var provider = UnicodeData.Provider;
                    var end = run.glyphStart + run.glyphCount;

                    ref var batchEntry = ref GetOrCreateEntry(fontAsset, renderMode, runVarHash);
                    if (runFtCoords != null)
                        batchEntry.ftCoords = runFtCoords;

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

                        if (glyphIndex >= GlyphBitsLength * 64)
                            continue;
                        batchEntry.glyphBits[glyphIndex >> 6] |= 1L << (int)(glyphIndex & 63);
                        if ((int)glyphIndex > batchEntry.glyphBitsMax)
                            batchEntry.glyphBitsMax = (int)glyphIndex;
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

                    ref var entry = ref GetOrCreateEntry(fontAsset, renderMode);
                    entry.glyphBits[glyphIndex >> 6] |= 1L << (int)(glyphIndex & 63);
                    if ((int)glyphIndex > entry.glyphBitsMax)
                        entry.glyphBitsMax = (int)glyphIndex;

                    entry.characterEntries ??= charEntryPool.Count > 0
                        ? charEntryPool.Pop()
                        : new List<(uint, uint)>(64);
                    entry.characterEntries.Add((unicode, glyphIndex));

                    if (varMap != null)
                    {
                        var baseFontHash = fontAsset.FontDataHash;
                        foreach (var kvp in varMap)
                        {
                            if (kvp.Value.baseFontHash != baseFontHash) continue;

                            ref var varEntry = ref GetOrCreateEntry(fontAsset, renderMode, kvp.Value.varHash48);
                            varEntry.glyphBits[glyphIndex >> 6] |= 1L << (int)(glyphIndex & 63);
                            if ((int)glyphIndex > varEntry.glyphBitsMax)
                                varEntry.glyphBitsMax = (int)glyphIndex;
                            if (varEntry.ftCoords == null)
                                varEntry.ftCoords = kvp.Value.ftCoords;

                            varEntry.characterEntries ??= charEntryPool.Count > 0
                                ? charEntryPool.Pop()
                                : new List<(uint, uint)>(64);
                            varEntry.characterEntries.Add((unicode, glyphIndex));
                        }
                    }
                }

                tp.HasValidGlyphsInAtlas = true;
            }

            return fontBatchCount;
        }

        private static ref FontBatchEntry GetOrCreateEntry(UniTextFont font, RenderModee mode, long varHash48 = 0)
        {
            var key = new FontBatchKey { font = font, mode = mode, varHash48 = varHash48 };
            if (!fontIndexMap.TryGetValue(key, out var index))
            {
                index = fontBatchCount++;
                if (fontBatches.Length <= index)
                    Array.Resize(ref fontBatches, fontBatches.Length * 2);

                var existingBits = fontBatches[index].glyphBits ?? new long[GlyphBitsLength];
                fontBatches[index] = new FontBatchEntry
                {
                    font = font,
                    mode = mode,
                    glyphBits = existingBits,
                    glyphBitsMax = -1,
                    varHash48 = varHash48,
                };
                fontIndexMap[key] = index;
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

        private static void RegisterDirty(UniTextBase component)
        {
            EnsureInitialized();

            if (component.isRegisteredDirty)
                return;

            if (!component.isActiveAndEnabled)
                return;

            component.isRegisteredDirty = true;
            componentsBuffer.Add(component);
        }

        private static void UnregisterDirty(UniTextBase component)
        {
            component.isRegisteredDirty = false;
        }

        private static bool CanWork
        {
            get
            {
#if UNITY_EDITOR
                if (Reseter.isDomainReloading) return false;
#endif
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
                if (comp == null || !comp.isActiveAndEnabled || !comp.isRegisteredDirty || comp.sourceText.IsEmpty ||
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

            UniTextDebug.BeginSample("PreWillRender");

            FilterAndPrepareComponents(true);
            var count = componentsBuffer.count;
            var totalChars = 0;

            for (var i = 0; i < count; i++)
                totalChars += componentsBuffer[i].sourceText.Length;

            useParallel = totalChars > ParallelCharacterThreshold &&
                          count >= MinComponentsForParallel &&
                          UniTextWorkerPool.IsParallelSupported;

            LogBatchInfo(count, totalChars, useParallel && UseParallel);

            UniTextDebug.BeginSample("FirstPass");
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

            UniTextDebug.EndSample();

            Cat.Meow("[UniText] OnPreWillRenderCanvases completed");
        }

        private static void OnWillRenderCanvases()
        {
            PostProcess();
            AfterProcess?.Invoke();
        }

        private static void PostProcess()
        {
#if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
#endif
            PeriodicAtlasMaintenance();

            if (componentsBuffer.count == 0) return;
            if (!CanWork) return;

            UniTextDebug.BeginSample("WillRender");

            UniTextDebug.BeginSample("FilterPrepare");
            FilterAndPrepareComponents(false);
            UniTextDebug.EndSample();

            var count = componentsBuffer.count;

            UniTextDebug.BeginSample("Rasterize");
            RasterizeGlyphBatches(count);
            EmojiFont.Instance?.SyncMaterialTexture();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("GenerateMeshData");
            if (useParallel && UseParallel)
                UniTextWorkerPool.Execute(componentsBuffer.data, count, static comp => comp.DoGenerateMeshData());
            else
                for (var i = 0; i < count; i++)
                    componentsBuffer[i].DoGenerateMeshData();
            UniTextDebug.EndSample();

            bool anyUpgrades = false;
            for (var i = 0; i < count; i++)
            {
                if (componentsBuffer[i].meshGenerator != null &&
                    componentsBuffer[i].meshGenerator.bandUpgradeRequests.Count > 0)
                { anyUpgrades = true; break; }
            }
            if (anyUpgrades)
            {
                UniTextDebug.BeginSample("BandUpgrades");
                ProcessBandUpgrades(count);
                GlyphAtlas.ForEachInstance(static a => a.FlushPending());
                UniTextDebug.EndSample();
            }

            UniTextDebug.BeginSample("ApplyMeshes");
            for (var i = 0; i < count; i++)
                componentsBuffer[i].DoApplyMesh();
            MeshApplied?.Invoke();
            UniTextDebug.EndSample();

            for (var i = 0; i < componentsBuffer.count; i++)
                componentsBuffer[i].isRegisteredDirty = false;

            componentsBuffer.Clear();
            
            UniTextDebug.EndSample();

            UniTextDebug.FlushFrameLog();

            Cat.Meow("[UniText] OnWillRenderCanvases completed");
        }
        
        private static void RasterizeGlyphBatches(int count)
        {
            UniTextDebug.BeginSample("Rasterization");

            var batchCount = CollectGlyphRequestsFromAllComponents(componentsBuffer, count);
            Cat.Meow($"[UniText Raster] batchCount={batchCount}");

            if (batchCount > 0)
            {
                tempGlyphList ??= new List<uint>(256);

                for (int i = 0; i < batchCount; i++)
                {
                    ref var batch = ref fontBatches[i];
                    if (batch.glyphBitsMax < 0) continue;

                    tempGlyphList.Clear();
                    var glyphBits = batch.glyphBits;
                    int maxWord = batch.glyphBitsMax >> 6;
                    for (int w = 0; w <= maxWord; w++)
                    {
                        var ubits = (ulong)glyphBits[w];
                        if (ubits == 0) continue;
                        int baseIdx = w << 6;
                        for (int b = 0; ubits != 0; b++, ubits >>= 1)
                            if ((ubits & 1) != 0)
                                tempGlyphList.Add((uint)(baseIdx + b));
                    }

                    Cat.Meow($"[UniText Raster] batch[{i}]: font={batch.font?.name}, glyphs={tempGlyphList.Count}, mode={batch.mode}");

                    var batchVarHash = batch.varHash48 != 0
                        ? batch.varHash48
                        : batch.font.DefaultVarHash48;
                    batch.prepared = batch.font.PrepareGlyphBatch(
                        tempGlyphList, batch.mode, batchVarHash, batch.ftCoords);

                    Cat.Meow($"[UniText Raster] batch[{i}]: prepared={batch.prepared.HasValue}");

                    if (!batch.prepared.HasValue)
                        batch.font.TryAddGlyphsBatch(
                            tempGlyphList, batch.mode, batchVarHash, batch.ftCoords);
                }

                int fontsToRender = 0;
                int totalGlyphsToRender = 0;
                for (int i = 0; i < batchCount; i++)
                {
                    if (fontBatches[i].prepared.HasValue)
                    {
                        fontsToRender++;
                        totalGlyphsToRender += fontBatches[i].prepared.Value.filteredGlyphs.count;
                    }
                }

                var timer = new DebugTimer();
                timer.Mark();
                GlyphCurveCache.ResetTimers();

#if !UNITY_WEBGL || UNITY_EDITOR
                if (fontsToRender > 1 && totalGlyphsToRender >= 16)
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
                        {
                            Cat.Meow($"[UniText Raster] RenderPreparedBatch[{i}] START font={fontBatches[i].font?.name}");
                            fontBatches[i].rendered = fontBatches[i].font.RenderPreparedBatch(fontBatches[i].prepared.Value);
                            Cat.Meow($"[UniText Raster] RenderPreparedBatch[{i}] DONE rendered={fontBatches[i].rendered != null}");
                        }
                    }
                }
                timer.Mark();

                long sdfTileArea = 0, msdfTileArea = 0, emojiTileArea = 0;
                int sdfGlyphs = 0, emojiGlyphs = 0;
                for (int i = 0; i < batchCount; i++)
                {
                    if (fontBatches[i].rendered == null) continue;
                    long area = fontBatches[i].font.EstimateTileArea(fontBatches[i].rendered);
                    if (fontBatches[i].font is EmojiFont)
                    {
                        emojiTileArea += area;
                        emojiGlyphs += fontBatches[i].prepared?.filteredGlyphs.count ?? 0;
                    }
                    else if (fontBatches[i].mode == RenderModee.MSDF) msdfTileArea += area;
                    else sdfTileArea += area;
                    if (fontBatches[i].font is not EmojiFont)
                        sdfGlyphs += fontBatches[i].prepared?.filteredGlyphs.count ?? 0;
                }
                if (sdfTileArea > 0) GlyphAtlas.GetInstance(RenderModee.SDF).PreAllocate(sdfTileArea);
                if (msdfTileArea > 0) GlyphAtlas.GetInstance(RenderModee.MSDF).PreAllocate(msdfTileArea);
                if (emojiTileArea > 0)
                {
                    for (int i = 0; i < batchCount; i++)
                        if (fontBatches[i].font is EmojiFont ef)
                        {
                            GlyphAtlas.Emoji?.PreAllocate(emojiTileArea);
                            break;
                        }
                }
                timer.Mark();

                for (int i = 0; i < batchCount; i++)
                {
                    ref var batch = ref fontBatches[i];
                    if (batch.rendered != null)
                        batch.font.PackRenderedBatch(batch.rendered, batch.prepared.Value, batch.mode);
                    if (batch.characterEntries is { Count: > 0 })
                        batch.font.RegisterCharacterEntries(batch.characterEntries);
                }
                for (int i = 0; i < batchCount; i++)
                    fontBatches[i].font.ReleaseBatchProtectedKeys(fontBatches[i].mode);
                timer.Mark();

                GlyphAtlas.ForEachInstance(static a => a.FlushPending());
                timer.Mark();

                Cat.Meow($"[UniText] {sdfGlyphs} sdf + {emojiGlyphs} emoji = {timer.Total:F0}ms | " +
                         $"rasterize={timer.Phase(0):F0}ms " +
                         $"texture={timer.Phase(1):F0}ms " +
                         $"pack={timer.Phase(2):F0}ms " +
                         $"gpu={timer.Phase(3):F0}ms");
            }

            UniTextDebug.EndSample();
        }

        private static void PeriodicAtlasMaintenance()
        {
            int frame = Time.frameCount;
            if (frame % 60 == 0)
            {
                GlyphAtlas.ForEachInstance(static a => a.TryRecyclePages());
                if (frame % 300 == 0)
                {
                    GlyphAtlas.ForEachInstance(static a => a.TryShrinkAtlas());
                    GlyphAtlas.ForEachInstance(static a => a.CompactIfFragmented());
                }
            }
        }

        
        private static Dictionary<(long, RenderModee), UniTextMeshGenerator.BandUpgradeRequest> upgradeMap;

        private static void ProcessBandUpgrades(int count)
        {
            upgradeMap ??= new Dictionary<(long, RenderModee), UniTextMeshGenerator.BandUpgradeRequest>();
            upgradeMap.Clear();

            for (int i = 0; i < count; i++)
            {
                var gen = componentsBuffer[i].meshGenerator;
                if (gen == null) continue;
                var requests = gen.bandUpgradeRequests;
                for (int j = 0; j < requests.Count; j++)
                {
                    var req = requests[j];
                    var mapKey = (req.glyphKey, req.mode);
                    if (!upgradeMap.TryGetValue(mapKey, out var existing)
                        || req.requiredBandPx > existing.requiredBandPx)
                        upgradeMap[mapKey] = req;
                }
            }

            foreach (var kvp in upgradeMap)
            {
                var req = kvp.Value;
                req.font.ReExtractForBandUpgrade(
                    req.glyphIndex, req.varHash48, req.ftCoords,
                    req.mode, req.requiredBandPx);
            }

            Cat.MeowFormat("[UniText] BandUpgrades: {0} unique glyphs upgraded", upgradeMap.Count);
        }

        #endregion

        #region Instance Batch Methods

        /// <summary>
        /// Ensures the first pass (parsing, BiDi, shaping) has run.
        /// No-op in the normal pipeline (Phase 1 already ran).
        /// Enables <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c> to work outside the pipeline.
        /// </summary>
        protected void EnsureFirstPassComplete()
        {
            if (textProcessor != null && textProcessor.HasValidFirstPassData) return;
            if (sourceText.IsEmpty) return;
            UnicodeData.EnsureInitialized();
            if (!UnicodeData.IsInitialized) return;
            if (!ValidateAndInitialize()) return;
            DoFirstPass();
        }

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
                UniTextDebug.BeginSample("EnsurePositions");
                textProcessor.EnsureLines(cached.rect.width, effectiveFontSize, wordWrap);
                var settings = CreateProcessSettings(cached.rect, effectiveFontSize);
                textProcessor.EnsurePositions(settings);
                UniTextDebug.EndSample();
            }

            var glyphs = textProcessor.PositionedGlyphs;
            if (glyphs.IsEmpty) return;

            buffers.virtualPositionedGlyphs.FakeClear();
            BeforeGenerateMesh?.Invoke();

            meshGenerator.FontSize = effectiveFontSize;
            meshGenerator.RenderMode = RenderMode;
            meshGenerator.defaultColor = color;
            meshGenerator.SetCanvasParametersCached(cached.lossyScale, cached.hasWorldCamera);
            meshGenerator.SetRectOffset(cached.rect);

            var virtualGlyphs = buffers.virtualPositionedGlyphs.data != null
                ? buffers.virtualPositionedGlyphs.Span
                : default;

            UniTextDebug.BeginSample("GenMeshOnly");
            meshGenerator.GenerateMeshDataOnly(glyphs, virtualGlyphs);
            UniTextDebug.EndSample();
        }

        private void DoApplyMesh()
        {
            if (sourceText.IsEmpty || meshGenerator == null || !meshGenerator.HasGeneratedData)
            {
                DeInit();
                dirtyFlags = DirtyFlags.None;
                return;
            }

            meshGenerator.AssignAutoMaterials();
            UpdateGlyphAtlasRefCounts();

            UniTextDebug.BeginSample("ApplyToUnity");
            renderData = meshGenerator.ApplyMeshesToUnity();
            UniTextDebug.EndSample();

#if UNITEXT_TESTS
            CopyMeshesForTests();
#endif

            if (textProcessor != null)
            {
                resultWidth = textProcessor.ResultWidth;
                resultHeight = textProcessor.ResultHeight;
            }

            UniTextDebug.BeginSample("SetMesh");
            UpdateRendering();
            UniTextDebug.EndSample();

            meshGenerator.ReturnInstanceBuffers();

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
    }
}
