using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies character spacing (tracking) adjustments to text ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter: spacing value with unit.
    /// <list type="bullet">
    /// <item><c>10</c> — add 10 pixels between characters</item>
    /// <item><c>-5</c> — reduce spacing by 5 pixels</item>
    /// <item><c>0.5em</c> — add 0.5 em (relative to font size)</item>
    /// </list>
    /// </para>
    /// <para>
    /// For cursive joining scripts (Arabic, Syriac, N'Ko, Adlam, etc.), visual kashida
    /// (tatweel) bars are rendered between connected letter pairs using 9-slice SDF rendering,
    /// preserving the appearance of cursive connections at any spacing value.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Adjusts the spacing between characters.")]
    [ParameterField(0, "Spacing", "unit:px|em", "0.3")]
    [ParameterField(1, "Monospace", "bool", "false")]
    public class LetterSpacingModifier : BaseModifier
    {
        private PooledArrayAttribute<float> attribute;
        private PooledArrayAttribute<byte> kashidaAttribute;
        private PooledArrayAttribute<float> scaleAttribute;
        private PooledArrayAttribute<byte> monoAttribute;

        private const int TatweelCodepoint = 0x0640;
        private const string DefaultSpacing = "0.3";
        private const string KashidaAttributeKey = "cspace.kashida";
        private const string ScaleAttributeKey = "cspace.scale";
        private const string MonoAttributeKey = "cspace.mono";
        private bool hasCompressionScales;
        private bool hasMonospace;

        private struct KashidaSegment
        {
            public float startX;
            public float endX;
            public float baselineY;
            public int fontId;
            public long varHash48;
            public Color32 color;
        }

        private KashidaSegment[] kashidaSegments;
        private int kashidaSegmentCount;
        private int kashidaSegmentCapacity;
        private bool kashidaComputed;

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKeys.LetterSpacing);
            buffers.PrepareAttribute(ref kashidaAttribute, KashidaAttributeKey);
            buffers.PrepareAttribute(ref scaleAttribute, ScaleAttributeKey);
            hasCompressionScales = false;
            buffers.PrepareAttribute(ref monoAttribute, MonoAttributeKey);
            hasMonospace = false;

            if (kashidaSegments == null)
            {
                kashidaSegments = UniTextArrayPool<KashidaSegment>.Rent(32);
                kashidaSegmentCapacity = 32;
            }
            kashidaSegmentCount = 0;
            kashidaComputed = false;

            uniText.TextProcessor.Shaped += OnShaped;
            uniText.Rebuilding += OnRebuilding;
            uniText.MeshGenerator.onAfterPage += OnAfterPage;
            uniText.MeshGenerator.onGlyph += OnMeshGlyph;
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped -= OnShaped;
            uniText.Rebuilding -= OnRebuilding;
            uniText.MeshGenerator.onAfterPage -= OnAfterPage;
            uniText.MeshGenerator.onGlyph -= OnMeshGlyph;
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKeys.LetterSpacing);
            buffers?.ReleaseAttributeData(KashidaAttributeKey);
            buffers?.ReleaseAttributeData(ScaleAttributeKey);
            buffers?.ReleaseAttributeData(MonoAttributeKey);
            attribute = null;
            kashidaAttribute = null;
            scaleAttribute = null;
            monoAttribute = null;

            if (kashidaSegments != null)
            {
                UniTextArrayPool<KashidaSegment>.Return(kashidaSegments);
                kashidaSegments = null;
            }
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var reader = new ParameterReader(string.IsNullOrEmpty(parameter) ? DefaultSpacing : parameter);

            if (!reader.NextUnitFloat(out var rawSpacing, out var unit, 0.3f))
                unit = ParameterReader.UnitKind.Absolute;

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
            var spacing = unit == ParameterReader.UnitKind.Em ? rawSpacing * baseSize : rawSpacing;

            var mono = false;
            if (reader.Next(out var monoToken) && !monoToken.IsEmpty)
            {
                mono = monoToken.Length == 4
                    && (monoToken[0] == 't' || monoToken[0] == 'T')
                    && (monoToken[1] == 'r' || monoToken[1] == 'R')
                    && (monoToken[2] == 'u' || monoToken[2] == 'U')
                    && (monoToken[3] == 'e' || monoToken[3] == 'E');
            }

            var cpCount = buffers.codepoints.count;

            var buffer = attribute.buffer.data;
            var clampedEnd = Math.Min(end, cpCount);
            for (var i = start; i < clampedEnd; i++)
                buffer[i] = spacing;

            if (mono)
            {
                var monoBuf = monoAttribute?.buffer.data;
                if (monoBuf != null)
                {
                    for (var i = start; i < clampedEnd; i++)
                        monoBuf[i] = 1;
                    hasMonospace = true;
                }
            }

            buffers.virtualCodepoints.Add((uint)TatweelCodepoint);
        }

        private void OnRebuilding()
        {
            attribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(AttributeKeys.LetterSpacing);
            kashidaAttribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(KashidaAttributeKey);
            scaleAttribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(ScaleAttributeKey);
            monoAttribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(MonoAttributeKey);
            kashidaComputed = false;
        }

        private void OnShaped()
        {
            if (attribute == null)
                return;

            var buffer = attribute.buffer.data;
            if (buffer == null)
                return;

            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var bufLen = buffer.Length;

            if (hasMonospace)
                ApplyMonospace(glyphs, runs, runCount, bufLen);

            ApplySimpleSpacing(buffer, bufLen, glyphs, runs, runCount);

            var codepoints = buf.codepoints.data;
            var scripts = buf.scripts.data;
            var cpCount = buf.codepoints.count;
            FlagKashidaPairs(glyphs, runs, runCount, buffer, bufLen,
                codepoints, scripts, cpCount, UnicodeData.Provider);

            ComputeCompressionScales(glyphs, runs, runCount, buffer, bufLen, scripts, cpCount);

            kashidaComputed = false;
        }

        /// <summary>
        /// Adds spacing to glyph advances for all runs. Skips zero-advance marks.
        /// </summary>
        private static void ApplySimpleSpacing(float[] buffer, int bufLen,
            ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount)
        {
            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var cluster = glyphs[g].cluster;

                    if ((uint)cluster < (uint)bufLen)
                    {
                        var spacing = buffer[cluster];
                        if (spacing != 0f && glyphs[g].advanceX != 0f)
                            glyphs[g].advanceX += spacing;
                    }

                    width += glyphs[g].advanceX;
                }

                run.width = width;
            }
        }

        /// <summary>
        /// Equalizes all monospace-flagged glyph advances to the maximum among them,
        /// centering each glyph within its cell via offsetX adjustment.
        /// </summary>
        private void ApplyMonospace(ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount, int bufLen)
        {
            var monoBuf = monoAttribute?.buffer.data;
            if (monoBuf == null) return;

            var maxAdvance = 0f;
            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    if (glyphs[g].advanceX == 0f) continue;
                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)bufLen || (uint)cluster >= (uint)monoBuf.Length)
                        continue;
                    if (monoBuf[cluster] == 0) continue;
                    if (glyphs[g].advanceX > maxAdvance)
                        maxAdvance = glyphs[g].advanceX;
                }
            }

            if (maxAdvance < 0.001f) return;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float widthDelta = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    if (glyphs[g].advanceX == 0f) continue;
                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)bufLen || (uint)cluster >= (uint)monoBuf.Length)
                        continue;
                    if (monoBuf[cluster] == 0) continue;

                    var original = glyphs[g].advanceX;
                    var delta = maxAdvance - original;
                    glyphs[g].advanceX = maxAdvance;
                    glyphs[g].offsetX += delta * 0.5f;
                    widthDelta += delta;
                }

                run.width += widthDelta;
            }
        }

        /// <summary>
        /// Sets kashida flags on clusters of connected base glyph pairs in cursive runs.
        /// </summary>
        private void FlagKashidaPairs(
            ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount,
            float[] buffer, int bufLen,
            int[] codepoints, UnicodeScript[] scripts, int cpCount,
            UnicodeDataProvider provider)
        {
            var flags = kashidaAttribute?.buffer.data;
            if (flags == null) return;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                if (run.glyphCount < 2) continue;

                var script = GetRunScript(ref run, scripts, cpCount);
                if (!IsCursiveScript(script)) continue;

                var glyphEnd = run.glyphStart + run.glyphCount;

                for (var g = run.glyphStart; g < glyphEnd - 1; g++)
                {
                    if (glyphs[g].advanceX == 0f) continue;

                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)bufLen || buffer[cluster] == 0f)
                        continue;

                    var nextG = g + 1;
                    while (nextG < glyphEnd && glyphs[nextG].advanceX == 0f)
                        nextG++;
                    if (nextG >= glyphEnd) break;

                    var nextCluster = glyphs[nextG].cluster;
                    if (cluster == nextCluster) continue;

                    if (AreConnected(cluster, nextCluster, codepoints, cpCount, provider))
                    {
                        if (IsLamAlefLigature(nextCluster, codepoints, cpCount))
                        {
                            var spacing = buffer[cluster];
                            glyphs[g].advanceX -= spacing;
                            run.width -= spacing;
                            buffer[cluster] = 0f;
                            continue;
                        }

                        if ((uint)cluster < (uint)flags.Length)
                            flags[cluster] = 1;
                    }
                }
            }
        }

        /// <summary>
        /// For cursive scripts with negative spacing, computes per-cluster horizontal
        /// scale factors. Glyphs are compressed instead of overlapping, preserving connections.
        /// </summary>
        private void ComputeCompressionScales(
            ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount,
            float[] spacingBuf, int bufLen, UnicodeScript[] scripts, int cpCount)
        {
            hasCompressionScales = false;
            var scaleBuf = scaleAttribute?.buffer.data;
            if (scaleBuf == null) return;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var script = GetRunScript(ref run, scripts, cpCount);
                if (!IsCursiveScript(script)) continue;

                var glyphEnd = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    if (glyphs[g].advanceX == 0f) continue;

                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)bufLen) continue;

                    var spacing = spacingBuf[cluster];
                    if (spacing >= 0f) continue;

                    var advance = glyphs[g].advanceX;
                    var original = advance - spacing;
                    if (original < 0.001f) continue;

                    var scale = advance / original;
                    if (scale < 0.1f) scale = 0.1f;

                    if ((uint)cluster < (uint)scaleBuf.Length)
                    {
                        scaleBuf[cluster] = scale;
                        hasCompressionScales = true;
                    }
                }
            }
        }

        /// <summary>
        /// Per-glyph callback: compresses quad vertices for cursive glyphs with negative spacing.
        /// </summary>
        private void OnMeshGlyph()
        {
            if (!hasCompressionScales) return;

            var scaleBuf = scaleAttribute?.buffer.data;
            if (scaleBuf == null) return;

            var gen = UniTextMeshGenerator.Current;
            var cluster = gen.currentCluster;
            if ((uint)cluster >= (uint)scaleBuf.Length) return;

            var scale = scaleBuf[cluster];
            if (scale == 0f) return;

            var verts = gen.Vertices;
            var vi = gen.vertexCount - 4;

            var centerX = (verts[vi].x + verts[vi + 2].x) * 0.5f;
            var leftX = centerX + (verts[vi].x - centerX) * scale;
            var rightX = centerX + (verts[vi + 2].x - centerX) * scale;

            verts[vi].x = leftX;
            verts[vi + 1].x = leftX;
            verts[vi + 2].x = rightX;
            verts[vi + 3].x = rightX;
        }

        private void OnAfterPage()
        {
            var gen = UniTextMeshGenerator.Current;
            if (gen == null) return;

            if (!kashidaComputed)
            {
                ComputeKashidaSegments(gen);
                kashidaComputed = true;
            }

            if (kashidaSegmentCount == 0) return;
            RenderKashidaSegments(gen);
        }

        /// <summary>
        /// Builds kashida segments from positioned glyphs that have kashida flags.
        /// </summary>
        private void ComputeKashidaSegments(UniTextMeshGenerator gen)
        {
            kashidaSegmentCount = 0;

            var flags = kashidaAttribute?.buffer.data;
            if (flags == null || !flags.HasAnyFlags()) return;

            var spacingBuf = attribute?.buffer.data;
            if (spacingBuf == null) return;

            var allGlyphs = buffers.positionedGlyphs.data;
            var glyphCount = buffers.positionedGlyphs.count;
            if (glyphCount == 0) return;

            var shapedGlyphs = buffers.shapedGlyphs.data;
            var offsetX = gen.offsetX;
            var offsetY = gen.offsetY;
            var defaultColor = gen.defaultColor;
            var fontProvider = uniText.FontProvider;

            for (var i = 0; i < glyphCount; i++)
            {
                ref readonly var glyph = ref allGlyphs[i];
                var cluster = glyph.cluster;

                if ((uint)cluster >= (uint)flags.Length || flags[cluster] == 0)
                    continue;

                var spacing = ((uint)cluster < (uint)spacingBuf.Length) ? spacingBuf[cluster] : 0f;
                if (spacing == 0f) continue;

                var shapedIdx = glyph.shapedGlyphIndex;
                if (shapedIdx < 0) continue;

                var shapedAdvance = shapedGlyphs[shapedIdx].advanceX;
                if (shapedAdvance < 0.001f) continue;

                var posAdvance = glyph.right - glyph.left;
                var spacingScaled = posAdvance * (spacing / shapedAdvance);

                var kashidaEnd = offsetX + glyph.right;
                var kashidaStart = kashidaEnd - spacingScaled;

                if (kashidaEnd <= kashidaStart + 0.01f) continue;

                var baselineY = offsetY - glyph.y;

                var color = ColorModifier.TryGetColor(buffers, cluster, out var customColor)
                    ? customColor : defaultColor;
                color.a = defaultColor.a;

                var glyphFont = fontProvider.GetFontAsset(glyph.fontId);
                var varHash = glyphFont != null ? buffers.ResolveVarHash48(glyph.fontId, glyphFont) : 0L;

                AddKashidaSegment(kashidaStart, kashidaEnd, baselineY, glyph.fontId, varHash, color);
            }
        }

        private void AddKashidaSegment(float startX, float endX, float baselineY, int fontId, long varHash48, Color32 color)
        {
            UniTextArrayPool<KashidaSegment>.GrowDouble(ref kashidaSegments, ref kashidaSegmentCapacity, kashidaSegmentCount);

            kashidaSegments[kashidaSegmentCount++] = new KashidaSegment
            {
                startX = startX,
                endX = endX,
                baselineY = baselineY,
                fontId = fontId,
                varHash48 = varHash48,
                color = color
            };
        }

        private void RenderKashidaSegments(UniTextMeshGenerator gen)
        {
            var fontProvider = uniText.FontProvider;
            var atlas = GlyphAtlas.GetInstance(gen.RenderMode);

            for (var i = 0; i < kashidaSegmentCount; i++)
            {
                ref var seg = ref kashidaSegments[i];
                DrawKashida(gen, fontProvider, atlas, ref seg);
            }
        }

        /// <summary>
        /// Draws a single kashida bar using 9-slice SDF rendering of the tatweel glyph.
        /// Samples the center column of the tatweel SDF and stretches horizontally.
        /// </summary>
        private static void DrawKashida(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            GlyphAtlas atlas, ref KashidaSegment seg)
        {
            var font = fontProvider.GetFontAsset(seg.fontId);
            if (font == null) return;

            var tatweelIndex = font.GetGlyphIndexForUnicode((uint)TatweelCodepoint);
            if (tatweelIndex == 0) return;

            var varHash = seg.varHash48;
            if (!atlas.TryGetEntry(varHash, tatweelIndex, out var entry) || entry.encodedTile < 0)
                return;

            var glyphLookup = font.GlyphLookupTable;
            if (glyphLookup == null ||
                !glyphLookup.TryGetValue(UniTextFont.GlyphKey(varHash, tatweelIndex), out var glyphData))
                return;

            gen.TrackGlyphKey(GlyphAtlas.MakeKey(varHash, tatweelIndex));

            var metrics = glyphData.metrics;
            var upem = (float)font.UnitsPerEm;
            var metricsFactor = gen.FontSize * font.FontScale;

            var glyphH = metrics.height / upem;
            if (glyphH < 1e-6f) return;
            var glyphW = metrics.width / upem;
            var aspect = glyphW / glyphH;

            const float sdfPadding = 0.02f;
            var padEm = sdfPadding * glyphH;
            var bearingXNorm = metrics.horizontalBearingX / upem;
            var bearingYNorm = metrics.horizontalBearingY / upem;
            var advanceNorm = metrics.horizontalAdvance / upem;

            var topY = seg.baselineY + (bearingYNorm + padEm) * metricsFactor;
            var bottomY = topY - (1f + sdfPadding * 2f) * glyphH * metricsFactor;

            var leftPad = (bearingXNorm - padEm) * metricsFactor;
            var rightPad = (bearingXNorm + glyphW + padEm - advanceNorm) * metricsFactor;

            gen.EnsureCapacity(4, 6);

            var verts = gen.Vertices;
            var uvData = gen.Uvs0;
            var uv1Data = gen.Uvs1;
            var cols = gen.Colors;
            var tris = gen.Triangles;

            var vertIdx = gen.vertexCount;
            var triIdx = gen.triangleCount;

            var tileIdx = (float)(entry.encodedTile + entry.pageIndex * GlyphAtlas.PageStride);
            var centerX = aspect * 0.5f;

            verts[vertIdx]     = new Vector3(seg.startX + leftPad, bottomY, 0);
            verts[vertIdx + 1] = new Vector3(seg.startX + leftPad, topY, 0);
            verts[vertIdx + 2] = new Vector3(seg.endX + rightPad, topY, 0);
            verts[vertIdx + 3] = new Vector3(seg.endX + rightPad, bottomY, 0);

            var uvBottom = -sdfPadding;
            var uvTop = 1f + sdfPadding;

            uvData[vertIdx]     = new Vector4(centerX, uvBottom, tileIdx, glyphH);
            uvData[vertIdx + 1] = new Vector4(centerX, uvTop, tileIdx, glyphH);
            uvData[vertIdx + 2] = new Vector4(centerX, uvTop, tileIdx, glyphH);
            uvData[vertIdx + 3] = new Vector4(centerX, uvBottom, tileIdx, glyphH);

            var uv1Val = new Vector2(aspect, 0);
            uv1Data[vertIdx]     = uv1Val;
            uv1Data[vertIdx + 1] = uv1Val;
            uv1Data[vertIdx + 2] = uv1Val;
            uv1Data[vertIdx + 3] = uv1Val;

            cols[vertIdx]     = seg.color;
            cols[vertIdx + 1] = seg.color;
            cols[vertIdx + 2] = seg.color;
            cols[vertIdx + 3] = seg.color;

            var localI0 = vertIdx - gen.CurrentSegmentVertexStart;
            tris[triIdx]     = localI0;
            tris[triIdx + 1] = localI0 + 1;
            tris[triIdx + 2] = localI0 + 2;
            tris[triIdx + 3] = localI0 + 2;
            tris[triIdx + 4] = localI0 + 3;
            tris[triIdx + 5] = localI0;

            gen.vertexCount += 4;
            gen.triangleCount += 6;
        }

        /// <summary>
        /// Returns true if the Unicode script uses cursive joining.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCursiveScript(UnicodeScript script)
        {
            return script == UnicodeScript.Arabic
                || script == UnicodeScript.Syriac
                || script == UnicodeScript.Mandaic
                || script == UnicodeScript.Nko
                || script == UnicodeScript.Mongolian
                || script == UnicodeScript.Adlam
                || script == UnicodeScript.HanifiRohingya;
        }

        /// <summary>
        /// Returns the script for a shaped run by sampling its first codepoint.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static UnicodeScript GetRunScript(ref ShapedRun run,
            UnicodeScript[] scripts, int cpCount)
        {
            var start = run.range.start;
            return (uint)start < (uint)cpCount ? scripts[start] : UnicodeScript.Common;
        }

        /// <summary>
        /// Determines whether two codepoints form a cursive connection
        /// per the Unicode Arabic Joining Algorithm.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AreConnected(int clusterA, int clusterB,
            int[] codepoints, int cpCount, UnicodeDataProvider provider)
        {
            var earlier = Math.Min(clusterA, clusterB);
            var later = Math.Max(clusterA, clusterB);

            if ((uint)earlier >= (uint)cpCount || (uint)later >= (uint)cpCount)
                return false;

            var jtEarlier = provider.GetJoiningType(codepoints[earlier]);
            var jtLater = provider.GetJoiningType(codepoints[later]);

            return JoinsLeft(jtEarlier) && JoinsRight(jtLater);
        }

        /// <summary>
        /// Returns true if the codepoint at the given cluster is lam (U+0644) followed
        /// by an alef variant, forming a mandatory lam-alef ligature with diagonal connection.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLamAlefLigature(int cluster, int[] codepoints, int cpCount)
        {
            if ((uint)cluster >= (uint)cpCount || codepoints[cluster] != 0x0644)
                return false;

            var next = cluster + 1;
            if ((uint)next >= (uint)cpCount)
                return false;

            var cp = codepoints[next];
            return cp == 0x0627 || cp == 0x0622 || cp == 0x0623 || cp == 0x0625 || cp == 0x0671;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool JoinsLeft(JoiningType jt)
        {
            return jt == JoiningType.DualJoining
                || jt == JoiningType.LeftJoining
                || jt == JoiningType.JoinCausing;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool JoinsRight(JoiningType jt)
        {
            return jt == JoiningType.DualJoining
                || jt == JoiningType.RightJoining
                || jt == JoiningType.JoinCausing;
        }
    }

}
