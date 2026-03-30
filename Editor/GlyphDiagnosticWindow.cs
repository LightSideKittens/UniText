#if UNITEXT_DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Diagnostic EditorWindow for inspecting glyph outlines, segment junctions, and SDF tiles.
    /// Helps diagnose rendering artifacts like notches, dips, or spikes at specific glyph locations.
    /// </summary>
    internal unsafe class GlyphDiagnosticWindow : EditorWindow
    {
        [MenuItem("Tools/UniText/Glyph Diagnostic")]
        static void Open()
        {
            var w = GetWindow<GlyphDiagnosticWindow>("Glyph Diagnostic");
            w.minSize = new Vector2(800, 600);
        }

        UniTextFont font;
        string glyphIndexStr = "0";
        string unicodeHexStr = "";
        int tileSize = 64;
        static readonly int[] tileSizes = { 64, 128, 256 };
        static readonly string[] tileSizeLabels = { "64", "128", "256" };
        int tileSizeIdx = 0;

        struct DiagSegment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public int contourIndex;
            public bool isDegenerate;
        }

        DiagSegment[] segments;
        int segmentCount;
        int contourCount;
        int[] contourEndIndices;
        float aspect;
        float glyphH;
        GlyphCurveCache.GlyphCurveData curveData;

        struct JunctionInfo
        {
            public int segA, segB;
            public Vector2 posA, posB;
            public float posError;
            public Vector2 tangentA, tangentB;
            public float angleDeg;
        }

        JunctionInfo[] junctions;

        Texture2D sdfBruteForce;
        Texture2D sdfClosestSeg;
        Texture2D windingTex;
        Texture2D sdfContour;
        float[] bruteForceDistances;
        int[] closestSegIds;
        byte[] windingGrid;

        Texture2D contourTex;
        const int ContourTexSize = 768;

        int selectedTab;
        static readonly string[] tabNames = { "Contour", "SDF Tile", "Data" };
        Vector2 dataScroll;
        Vector2 contourScroll;
        Vector2 sdfScroll;
        string dataText = "";
        bool showControlPoints = true;
        bool showSegmentIndices = true;
        bool showJunctionTangents = true;

        static readonly Color[] segColors =
        {
            new Color(1f, 0.2f, 0.2f), new Color(0.2f, 0.8f, 0.2f), new Color(0.3f, 0.5f, 1f),
            new Color(1f, 0.8f, 0f), new Color(0f, 0.9f, 0.9f), new Color(1f, 0.4f, 0.8f),
            new Color(0.9f, 0.5f, 0.1f), new Color(0.5f, 1f, 0.5f), new Color(0.6f, 0.3f, 1f),
            new Color(1f, 1f, 0.5f), new Color(0.5f, 0.8f, 1f), new Color(1f, 0.6f, 0.6f),
        };

        void OnGUI()
        {
            DrawInputSection();

            EditorGUILayout.Space(4);
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(24));
            EditorGUILayout.Space(4);

            switch (selectedTab)
            {
                case 0: DrawContourTab(); break;
                case 1: DrawSdfTab(); break;
                case 2: DrawDataTab(); break;
            }
        }

        string shapeInputText = "";
        string shapeResultText = "";

        void DrawInputSection()
        {
            EditorGUILayout.BeginVertical("helpBox");

            font = (UniTextFont)EditorGUILayout.ObjectField("Font Asset", font, typeof(UniTextFont), false);

            EditorGUILayout.BeginHorizontal();
            glyphIndexStr = EditorGUILayout.TextField("Glyph Index", glyphIndexStr);
            if (GUILayout.Button("From Unicode →", GUILayout.Width(110)))
                LookupFromUnicode();
            unicodeHexStr = EditorGUILayout.TextField(unicodeHexStr, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            shapeInputText = EditorGUILayout.TextField("Shape Text", shapeInputText);
            if (GUILayout.Button("Shape (RTL)", GUILayout.Width(90)))
                ShapeText(HB.DIRECTION_RTL);
            if (GUILayout.Button("Shape (LTR)", GUILayout.Width(90)))
                ShapeText(HB.DIRECTION_LTR);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(shapeResultText))
                EditorGUILayout.HelpBox(shapeResultText, MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            tileSizeIdx = EditorGUILayout.Popup("Tile Size", tileSizeIdx, tileSizeLabels);
            tileSize = tileSizes[tileSizeIdx];
            EditorGUILayout.EndHorizontal();

            GUI.enabled = font != null;
            if (GUILayout.Button("Extract & Analyze", GUILayout.Height(28)))
                ExtractAndAnalyze();
            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        void ShapeText(int direction)
        {
            if (font == null || string.IsNullOrEmpty(shapeInputText))
            {
                shapeResultText = "Need font and text";
                return;
            }

            var cache = Shaper.GetOrCreateCacheByInstanceId(font);
            if (cache == null || !cache.IsValid)
            {
                shapeResultText = "Failed to create HarfBuzz font";
                return;
            }

            var codepoints = new int[shapeInputText.Length];
            int cpCount = 0;
            for (int i = 0; i < shapeInputText.Length; i++)
            {
                int cp;
                if (char.IsHighSurrogate(shapeInputText[i]) && i + 1 < shapeInputText.Length)
                {
                    cp = char.ConvertToUtf32(shapeInputText[i], shapeInputText[i + 1]);
                    i++;
                }
                else
                {
                    cp = shapeInputText[i];
                }
                codepoints[cpCount++] = cp;
            }

            var buf = HB.CreateBuffer();
            try
            {
                HB.SetDirection(buf, direction);
                HB.SetScript(buf, HB.Script.Arabic);
                HB.AddCodepoints(buf, new ReadOnlySpan<int>(codepoints, 0, cpCount), 0, cpCount);
                HB.Shape(cache.hbFont, buf);

                var glyphs = HB.GetGlyphInfos(buf);
                var sb = new StringBuilder();
                sb.Append($"Shaped {glyphs.Length} glyphs:\n");
                for (int i = 0; i < glyphs.Length; i++)
                {
                    var g = glyphs[i];
                    int srcCp = (g.cluster < cpCount) ? codepoints[g.cluster] : 0;
                    sb.Append($"  [{i}] glyph={g.glyphId}  cluster={g.cluster} (U+{srcCp:X4})  adv={g.xAdvance}\n");
                }
                shapeResultText = sb.ToString();
            }
            finally
            {
                HB.DestroyBuffer(buf);
            }
        }

        void LookupFromUnicode()
        {
            if (font == null) return;
            if (!uint.TryParse(unicodeHexStr, System.Globalization.NumberStyles.HexNumber, null, out uint cp))
            {
                Debug.LogError($"[GlyphDiag] Invalid hex codepoint: {unicodeHexStr}");
                return;
            }

            uint glyph = Shaper.GetGlyphIndex(font, cp);
            if (glyph == 0)
                Debug.LogWarning($"[GlyphDiag] No glyph found for U+{cp:X4}");
            else
                glyphIndexStr = glyph.ToString();
        }

        void ExtractAndAnalyze()
        {
            if (font == null) return;
            if (!uint.TryParse(glyphIndexStr, out uint glyphIndex) || glyphIndex == 0)
            {
                Debug.LogError("[GlyphDiag] Invalid glyph index");
                return;
            }

            if (!font.HasFontData)
            {
                Debug.LogError("[GlyphDiag] Font has no data");
                return;
            }

            var cache = font.CurveCache;
            if (cache == null) { Debug.LogError("[GlyphDiag] CurveCache is null — font may not be loaded"); return; }

            var face = cache.RentFace();
            try
            {
                ExtractRawContours(face, glyphIndex);
            }
            finally
            {
                cache.ReturnFace(face);
            }

            if (segmentCount == 0)
            {
                Debug.LogWarning("[GlyphDiag] No segments extracted");
                return;
            }

            AnalyzeJunctions();
            GenerateBruteForceSdf();
            GenerateContourTexture();
            BuildDataText();
            Repaint();
        }

        void ExtractRawContours(IntPtr face, uint glyphIndex)
        {
            var rawCurves = stackalloc float[2048 * 8];
            var rawTypes = stackalloc int[2048];
            var rawContours = stackalloc int[256];
            int curveCount, cCount;

            int err = FT.OutlineDecompose(face, glyphIndex,
                rawCurves, rawTypes, &curveCount, 2048,
                rawContours, &cCount, 256,
                out int bearingX, out int bearingY, out int advanceX,
                out int width, out int height);

            if (err != 0 || curveCount == 0)
            {
                segmentCount = 0;
                Debug.LogError($"[GlyphDiag] OutlineDecompose error={err}, curves={curveCount}");
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                for (int j = 0; j < 3; j++)
                {
                    float x = c[j * 2], y = c[j * 2 + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            float bboxH = maxY - minY;
            if (bboxH < 1e-6f) bboxH = 1f;
            float invScale = 1f / bboxH;

            segments = new DiagSegment[curveCount];
            segmentCount = curveCount;
            contourCount = cCount;
            contourEndIndices = new int[cCount];

            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                segments[i] = new DiagSegment
                {
                    p0x = (c[0] - minX) * invScale,
                    p0y = (c[1] - minY) * invScale,
                    p1x = (c[2] - minX) * invScale,
                    p1y = (c[3] - minY) * invScale,
                    p2x = (c[4] - minX) * invScale,
                    p2y = (c[5] - minY) * invScale,
                };

                float midX = (segments[i].p0x + segments[i].p2x) * 0.5f;
                float midY = (segments[i].p0y + segments[i].p2y) * 0.5f;
                float dx = segments[i].p1x - midX, dy = segments[i].p1y - midY;
                segments[i].isDegenerate = (dx * dx + dy * dy) < 1e-8f;
            }

            for (int c = 0; c < cCount; c++)
                contourEndIndices[c] = rawContours[c];

            int cStart = 0;
            for (int c = 0; c < cCount; c++)
            {
                int cEnd = contourEndIndices[c];
                for (int i = cStart; i <= cEnd; i++)
                    segments[i].contourIndex = c;
                cStart = cEnd + 1;
            }

            float bboxW = maxX - minX;
            aspect = (bboxH < 1e-6f) ? 1f : bboxW / bboxH;
            glyphH = height / (float)Mathf.Max(font.UnitsPerEm, 1);

            curveData = new GlyphCurveCache.GlyphCurveData
            {
                bboxMinX = minX, bboxMinY = minY,
                bboxMaxX = maxX, bboxMaxY = maxY,
                bearingX = bearingX / (float)font.UnitsPerEm,
                bearingY = bearingY / (float)font.UnitsPerEm,
                advanceX = advanceX / (float)font.UnitsPerEm,
                designWidth = width, designHeight = height,
                isEmpty = false
            };
        }

        void AnalyzeJunctions()
        {
            var list = new List<JunctionInfo>();

            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = contourEndIndices[c];
                int edgeCount = cEnd - cStart + 1;

                for (int i = 0; i < edgeCount; i++)
                {
                    int idxA = cStart + i;
                    int idxB = cStart + ((i + 1) % edgeCount);

                    var sA = segments[idxA];
                    var sB = segments[idxB];

                    Vector2 posA = new Vector2(sA.p2x, sA.p2y);
                    Vector2 posB = new Vector2(sB.p0x, sB.p0y);
                    float posErr = Vector2.Distance(posA, posB);

                    Vector2 tanA = new Vector2(2f * (sA.p2x - sA.p1x), 2f * (sA.p2y - sA.p1y));
                    Vector2 tanB = new Vector2(2f * (sB.p1x - sB.p0x), 2f * (sB.p1y - sB.p0y));

                    float magA = tanA.magnitude;
                    float magB = tanB.magnitude;
                    float angle = 0f;

                    if (magA > 1e-6f && magB > 1e-6f)
                    {
                        tanA /= magA;
                        tanB /= magB;
                        float dot = Mathf.Clamp(Vector2.Dot(tanA, tanB), -1f, 1f);
                        angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
                    }

                    list.Add(new JunctionInfo
                    {
                        segA = idxA, segB = idxB,
                        posA = posA, posB = posB,
                        posError = posErr,
                        tangentA = tanA, tangentB = tanB,
                        angleDeg = angle
                    });
                }

                cStart = cEnd + 1;
            }

            junctions = list.ToArray();
        }

        struct MonoPiece
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public float yMin, yMax;
            public int windingDir;
            public bool isLinear;
        }

        void GenerateBruteForceSdf()
        {
            int ts = tileSize;

            float padGlyph = GlyphAtlas.Pad / Mathf.Max(glyphH, 1e-6f);
            float maxDim = Mathf.Max(aspect, 1f);
            float totalExtent = maxDim + 2f * padGlyph;
            float scale = ts / totalExtent;
            float offsetX = (maxDim - aspect) * 0.5f + padGlyph;
            float offsetY = (maxDim - 1f) * 0.5f + padGlyph;
            float invScale = 1f / scale;

            int pixelCount = ts * ts;
            bruteForceDistances = new float[pixelCount];
            closestSegIds = new int[pixelCount];
            windingGrid = new byte[pixelCount];

            var monoPieces = new List<MonoPiece>();
            for (int si = 0; si < segmentCount; si++)
                YMonotoneSplitDiag(segments[si], monoPieces);

            ComputeWindingBruteForce(monoPieces, ts, scale, offsetX, offsetY);

            for (int y = 0; y < ts; y++)
            {
                for (int x = 0; x < ts; x++)
                {
                    float px = (x + 0.5f) * invScale - offsetX;
                    float py = (y + 0.5f) * invScale - offsetY;

                    float bestDist2 = float.MaxValue;
                    int bestSeg = -1;

                    for (int si = 0; si < segmentCount; si++)
                    {
                        float d2 = ClosestDistSq(px, py, segments[si]);
                        if (d2 < bestDist2)
                        {
                            bestDist2 = d2;
                            bestSeg = si;
                        }
                    }

                    int idx = y * ts + x;
                    float distGlyph = Mathf.Sqrt(bestDist2);
                    float sign = windingGrid[idx] != 0 ? -1f : 1f;
                    float encoded = Mathf.Clamp01(sign * distGlyph * glyphH + 0.5f);

                    bruteForceDistances[idx] = encoded;
                    closestSegIds[idx] = bestSeg;
                }
            }

            GenerateSdfTextures(ts);
        }

        static void YMonotoneSplitDiag(DiagSegment s, List<MonoPiece> output)
        {
            float denom = s.p0y - 2f * s.p1y + s.p2y;
            float tSplit = (Mathf.Abs(denom) > 1e-10f) ? (s.p0y - s.p1y) / denom : -1f;

            if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
            {
                float t = tSplit, mt = 1f - t;
                float m01x = mt * s.p0x + t * s.p1x;
                float m01y = mt * s.p0y + t * s.p1y;
                float m12x = mt * s.p1x + t * s.p2x;
                float m12y = mt * s.p1y + t * s.p2y;
                float mx = mt * m01x + t * m12x;
                float my = mt * m01y + t * m12y;
                AddMonoPiece(output, s.p0x, s.p0y, m01x, m01y, mx, my);
                AddMonoPiece(output, mx, my, m12x, m12y, s.p2x, s.p2y);
            }
            else
            {
                AddMonoPiece(output, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y);
            }
        }

        static void AddMonoPiece(List<MonoPiece> output,
            float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
        {
            var m = new MonoPiece
            {
                p0x = p0x, p0y = p0y, p1x = p1x, p1y = p1y, p2x = p2x, p2y = p2y,
                yMin = Mathf.Min(p0y, p2y), yMax = Mathf.Max(p0y, p2y),
                windingDir = p2y > p0y ? 1 : -1
            };
            float d01x = p1x - p0x, d01y = p1y - p0y;
            float d02x = p2x - p0x, d02y = p2y - p0y;
            m.isLinear = Mathf.Abs(d01x * d02y - d01y * d02x) < 1e-5f;
            output.Add(m);
        }

        void ComputeWindingBruteForce(List<MonoPiece> monoPieces, int ts, float scale, float offsetX, float offsetY)
        {
            float invScale = 1f / scale;

            for (int y = 0; y < ts; y++)
            {
                float py = (y + 0.5f) * invScale - offsetY;
                int[] windingRow = new int[ts];

                for (int mi = 0; mi < monoPieces.Count; mi++)
                {
                    var seg = monoPieces[mi];
                    if (py < seg.yMin || py >= seg.yMax) continue;

                    float xPx;
                    if (seg.isLinear)
                    {
                        float dy = seg.p2y - seg.p0y;
                        if (Mathf.Abs(dy) < 1e-9f) continue;
                        float t = (py - seg.p0y) / dy;
                        xPx = (seg.p0x + t * (seg.p2x - seg.p0x) + offsetX) * scale;
                    }
                    else
                    {
                        float a = seg.p0y - 2f * seg.p1y + seg.p2y;
                        float b = 2f * (seg.p1y - seg.p0y);
                        float c = seg.p0y - py;
                        int roots = SolveQuadDiag(a, b, c, out float t0, out _);
                        if (roots == 0 || t0 < 0f || t0 > 1f) continue;
                        float mt = 1f - t0;
                        xPx = ((mt * mt * seg.p0x + 2f * mt * t0 * seg.p1x + t0 * t0 * seg.p2x) + offsetX) * scale;
                    }

                    int ix = (int)(xPx + 0.5f);
                    if (ix >= 0 && ix < ts)
                        windingRow[ix] += seg.windingDir;
                }

                int winding = 0;
                for (int x = 0; x < ts; x++)
                {
                    winding += windingRow[x];
                    windingGrid[y * ts + x] = (winding != 0) ? (byte)1 : (byte)0;
                }
            }
        }

        static int SolveQuadDiag(float a, float b, float c, out float t0, out float t1)
        {
            t0 = t1 = -1f;
            if (Mathf.Abs(a) < 1e-8f)
            {
                if (Mathf.Abs(b) < 1e-8f) return 0;
                t0 = -c / b;
                return (t0 >= 0f && t0 <= 1f) ? 1 : 0;
            }
            float disc = b * b - 4f * a * c;
            if (disc < -1e-7f) return 0;
            if (disc < 0f) disc = 0f;
            float sqrtDisc = Mathf.Sqrt(disc);
            float q = -0.5f * (b + (b >= 0f ? sqrtDisc : -sqrtDisc));
            if (Mathf.Abs(q) < 1e-12f) { t0 = 0f; t1 = -b / a; }
            else { t0 = q / a; t1 = c / q; }
            bool v0 = t0 >= 0f && t0 <= 1f;
            bool v1 = t1 >= 0f && t1 <= 1f;
            if (v0 && v1) return 2;
            if (v0) return 1;
            if (v1) { t0 = t1; return 1; }
            return 0;
        }

        float ClosestDistSq(float px, float py, DiagSegment s)
        {
            float bestT = 0f;
            float bestD2 = float.MaxValue;

            const int steps = 20;
            for (int j = 0; j <= steps; j++)
            {
                float t = j / (float)steps;
                float mt = 1f - t;
                float bx = mt * mt * s.p0x + 2f * mt * t * s.p1x + t * t * s.p2x;
                float by = mt * mt * s.p0y + 2f * mt * t * s.p1y + t * t * s.p2y;
                float dx = bx - px, dy = by - py;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; bestT = t; }
            }

            for (int iter = 0; iter < 4; iter++)
                bestT = NewtonStep(px, py, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y, bestT);

            float mtn = 1f - bestT;
            float vx = mtn * mtn * s.p0x + 2f * mtn * bestT * s.p1x + bestT * bestT * s.p2x - px;
            float vy = mtn * mtn * s.p0y + 2f * mtn * bestT * s.p1y + bestT * bestT * s.p2y - py;
            return vx * vx + vy * vy;
        }

        static float NewtonStep(float px, float py,
            float ax, float ay, float bx, float by, float cx, float cy, float t)
        {
            float mt = 1f - t;
            float dpx = 2f * ((bx - ax) + (ax - 2f * bx + cx) * t);
            float dpy = 2f * ((by - ay) + (ay - 2f * by + cy) * t);
            float ddpx = 2f * (ax - 2f * bx + cx);
            float ddpy = 2f * (ay - 2f * by + cy);
            float btx = mt * mt * ax + 2f * mt * t * bx + t * t * cx;
            float bty = mt * mt * ay + 2f * mt * t * by + t * t * cy;
            float diffx = btx - px, diffy = bty - py;

            float dpSq = dpx * dpx + dpy * dpy;
            if (dpSq < 1e-6f)
            {
                float ddSq = ddpx * ddpx + ddpy * ddpy;
                if (ddSq < 1e-12f) return t;
                float dot = diffx * ddpx + diffy * ddpy;
                if (dot >= 0f) return t;
                float s = Mathf.Sqrt(-2f * dot / ddSq);
                float t1 = Mathf.Clamp(t + s, 0f, 1f);
                float t2 = Mathf.Clamp(t - s, 0f, 1f);
                float m1 = 1f - t1;
                float d1x = m1 * m1 * ax + 2f * m1 * t1 * bx + t1 * t1 * cx - px;
                float d1y = m1 * m1 * ay + 2f * m1 * t1 * by + t1 * t1 * cy - py;
                float m2 = 1f - t2;
                float d2x = m2 * m2 * ax + 2f * m2 * t2 * bx + t2 * t2 * cx - px;
                float d2y = m2 * m2 * ay + 2f * m2 * t2 * by + t2 * t2 * cy - py;
                return (d1x * d1x + d1y * d1y <= d2x * d2x + d2y * d2y) ? t1 : t2;
            }

            float f = diffx * dpx + diffy * dpy;
            float fp = dpSq + diffx * ddpx + diffy * ddpy;
            if (Mathf.Abs(fp) < 1e-12f) return t;
            float tn = t - f / fp;
            return tn < 0f ? 0f : (tn > 1f ? 1f : tn);
        }

        void GenerateSdfTextures(int ts)
        {
            if (sdfBruteForce != null) DestroyImmediate(sdfBruteForce);
            sdfBruteForce = new Texture2D(ts, ts, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };

            if (sdfClosestSeg != null) DestroyImmediate(sdfClosestSeg);
            sdfClosestSeg = new Texture2D(ts, ts, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };

            if (windingTex != null) DestroyImmediate(windingTex);
            windingTex = new Texture2D(ts, ts, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };

            if (sdfContour != null) DestroyImmediate(sdfContour);
            sdfContour = new Texture2D(ts, ts, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };

            var sdfPixels = new Color[ts * ts];
            var segPixels = new Color[ts * ts];
            var windPixels = new Color[ts * ts];
            var contourPixels = new Color[ts * ts];

            for (int y = 0; y < ts; y++)
            {
                for (int x = 0; x < ts; x++)
                {
                    int idx = y * ts + x;
                    float v = bruteForceDistances[idx];
                    int seg = closestSegIds[idx];
                    bool inside = windingGrid[idx] != 0;

                    sdfPixels[idx] = new Color(v, v, v, 1f);

                    Color sc = seg >= 0 ? segColors[seg % segColors.Length] : Color.black;
                    segPixels[idx] = sc;

                    windPixels[idx] = inside ? new Color(0.1f, 0.3f, 0.6f) : new Color(0.9f, 0.9f, 0.9f);

                    bool isContourPixel = false;
                    if (x > 0 && ((bruteForceDistances[idx] - 0.5f) * (bruteForceDistances[idx - 1] - 0.5f)) < 0)
                        isContourPixel = true;
                    if (y > 0 && ((bruteForceDistances[idx] - 0.5f) * (bruteForceDistances[(y - 1) * ts + x] - 0.5f)) < 0)
                        isContourPixel = true;

                    contourPixels[idx] = isContourPixel ? Color.red : new Color(v, v, v, 1f);
                }
            }

            sdfBruteForce.SetPixels(sdfPixels);
            sdfBruteForce.Apply();

            sdfClosestSeg.SetPixels(segPixels);
            sdfClosestSeg.Apply();

            windingTex.SetPixels(windPixels);
            windingTex.Apply();

            sdfContour.SetPixels(contourPixels);
            sdfContour.Apply();
        }

        void GenerateContourTexture()
        {
            int sz = ContourTexSize;
            if (contourTex != null) DestroyImmediate(contourTex);
            contourTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[sz * sz];
            Color bg = new Color(0.12f, 0.12f, 0.15f, 1f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

            float margin = 0.1f;
            float maxDim = Mathf.Max(aspect, 1f);
            float drawScale = sz * (1f - 2f * margin) / maxDim;
            float ox = sz * margin + (maxDim - aspect) * 0.5f * drawScale;
            float oy = sz * margin + (maxDim - 1f) * 0.5f * drawScale;

            for (int si = 0; si < segmentCount; si++)
            {
                var s = segments[si];
                Color col = segColors[si % segColors.Length];

                int steps = 200;
                for (int j = 0; j <= steps; j++)
                {
                    float t = j / (float)steps;
                    float mt = 1f - t;
                    float gx = mt * mt * s.p0x + 2f * mt * t * s.p1x + t * t * s.p2x;
                    float gy = mt * mt * s.p0y + 2f * mt * t * s.p1y + t * t * s.p2y;

                    int px = (int)(ox + gx * drawScale);
                    int py = (int)(oy + gy * drawScale);

                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int fx = px + dx, fy = py + dy;
                        if (fx >= 0 && fx < sz && fy >= 0 && fy < sz)
                            pixels[fy * sz + fx] = col;
                    }
                }

                if (showControlPoints && !s.isDegenerate)
                {
                    int cpx = (int)(ox + s.p1x * drawScale);
                    int cpy = (int)(oy + s.p1y * drawScale);
                    DrawSquare(pixels, sz, cpx, cpy, 4, col * 0.6f);
                }
            }

            for (int si = 0; si < segmentCount; si++)
            {
                var s = segments[si];
                int ex = (int)(ox + s.p0x * drawScale);
                int ey = (int)(oy + s.p0y * drawScale);
                DrawCircle(pixels, sz, ex, ey, 4, Color.white);

                if (si == segmentCount - 1 || segments[si + 1].contourIndex != s.contourIndex)
                {
                    ex = (int)(ox + s.p2x * drawScale);
                    ey = (int)(oy + s.p2y * drawScale);
                    DrawCircle(pixels, sz, ex, ey, 4, Color.white);
                }
            }

            if (junctions != null)
            {
                foreach (var j in junctions)
                {
                    Color jCol = j.angleDeg > 2f ? Color.red : Color.green;

                    int jx = (int)(ox + j.posA.x * drawScale);
                    int jy = (int)(oy + j.posA.y * drawScale);
                    DrawCircle(pixels, sz, jx, jy, 6, jCol);

                    if (showJunctionTangents)
                    {
                        float arrowLen = 30f;
                        DrawArrow(pixels, sz, jx, jy,
                            jx + (int)(j.tangentA.x * arrowLen),
                            jy + (int)(j.tangentA.y * arrowLen),
                            new Color(1f, 0.5f, 0f));
                        DrawArrow(pixels, sz, jx, jy,
                            jx + (int)(j.tangentB.x * arrowLen),
                            jy + (int)(j.tangentB.y * arrowLen),
                            new Color(0f, 0.8f, 1f));
                    }
                }
            }

            if (showSegmentIndices)
            {
                for (int si = 0; si < segmentCount; si++)
                {
                    var s = segments[si];
                    float midT = 0.5f;
                    float mt = 0.5f;
                    float mx = mt * mt * s.p0x + 2f * mt * midT * s.p1x + midT * midT * s.p2x;
                    float my = mt * mt * s.p0y + 2f * mt * midT * s.p1y + midT * midT * s.p2y;
                    int tx = (int)(ox + mx * drawScale);
                    int ty = (int)(oy + my * drawScale);
                    DrawNumber(pixels, sz, tx, ty, si, Color.white);
                }
            }

            contourTex.SetPixels(pixels);
            contourTex.Apply();
        }

        static void DrawCircle(Color[] pixels, int sz, int cx, int cy, int r, Color col)
        {
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < sz && y >= 0 && y < sz)
                    pixels[y * sz + x] = col;
            }
        }

        static void DrawSquare(Color[] pixels, int sz, int cx, int cy, int half, Color col)
        {
            for (int dy = -half; dy <= half; dy++)
            for (int dx = -half; dx <= half; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < sz && y >= 0 && y < sz)
                    pixels[y * sz + x] = col;
            }
        }

        static void DrawArrow(Color[] pixels, int sz, int x0, int y0, int x1, int y1, Color col)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            int steps = 0, maxSteps = dx + dy + 1;

            while (steps++ < maxSteps)
            {
                if (x >= 0 && x < sz && y >= 0 && y < sz)
                    pixels[y * sz + x] = col;
                if (x == x1 && y == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        static void DrawNumber(Color[] pixels, int sz, int x, int y, int num, Color col)
        {
            string numStr = num.ToString();
            int totalW = numStr.Length * 5 + 2;
            int totalH = 9;
            int startX = x - totalW / 2;
            int startY = y + 8;

            for (int dy = -1; dy <= totalH; dy++)
            for (int dx = -1; dx <= totalW; dx++)
            {
                int px = startX + dx, py = startY + dy;
                if (px >= 0 && px < sz && py >= 0 && py < sz)
                    pixels[py * sz + px] = new Color(0, 0, 0, 0.85f);
            }

            for (int d = 0; d < numStr.Length; d++)
            {
                int digit = numStr[d] - '0';
                if (digit < 0 || digit > 9) continue;
                int ox = startX + d * 5 + 1;
                int oy = startY + 1;
                DrawDigit(pixels, sz, ox, oy, digit, col);
            }
        }

        static void DrawDigit(Color[] pixels, int sz, int ox, int oy, int digit, Color col)
        {
            uint[] patterns =
            {
                0x699996, 0x262226, 0x691246, 0x692196, 0x996F11, 0xF84196, 0x698996, 0xF11248, 0x696996, 0x699716,
            };

            if (digit < 0 || digit > 9) return;
            uint p = patterns[digit];

            for (int row = 0; row < 6; row++)
            {
                int nibble = (int)((p >> ((5 - row) * 4)) & 0xF);
                for (int c = 0; c < 4; c++)
                {
                    if ((nibble & (8 >> c)) != 0)
                    {
                        int px = ox + c, py = oy + row;
                        if (px >= 0 && px < sz && py >= 0 && py < sz)
                            pixels[py * sz + px] = col;
                    }
                }
            }
        }

        void BuildDataText()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"═══ GLYPH INFO ═══");
            sb.AppendLine($"Glyph Index: {glyphIndexStr}");
            sb.AppendLine($"Segments: {segmentCount}   Contours: {contourCount}");
            sb.AppendLine($"Aspect: {aspect:F4}   GlyphH: {glyphH:F4}");
            sb.AppendLine($"Bbox: ({curveData.bboxMinX:F1}, {curveData.bboxMinY:F1}) - ({curveData.bboxMaxX:F1}, {curveData.bboxMaxY:F1})");
            sb.AppendLine($"Design: {curveData.designWidth} x {curveData.designHeight}");
            sb.AppendLine($"Tile size: {tileSize}");

            float padGlyph = GlyphAtlas.Pad / Mathf.Max(glyphH, 1e-6f);
            float maxDim = Mathf.Max(aspect, 1f);
            float totalExtent = maxDim + 2f * padGlyph;
            float sdfScale = tileSize / totalExtent;
            float sdfOffsetX = (maxDim - aspect) * 0.5f + padGlyph;
            float sdfOffsetY = (maxDim - 1f) * 0.5f + padGlyph;
            sb.AppendLine($"SDF Transform: scale={sdfScale:F4}, offset=({sdfOffsetX:F4}, {sdfOffsetY:F4}), pad={padGlyph:F4}");
            sb.AppendLine($"  1 pixel in glyph space = {1f / sdfScale:F6}");
            sb.AppendLine();

            sb.AppendLine($"═══ SEGMENTS (contour order) ═══");
            sb.AppendLine("Segment colors: " + string.Join(", ",
                Enumerable.Range(0, Mathf.Min(segmentCount, segColors.Length))
                    .Select(i => $"{i}=#{ColorUtility.ToHtmlStringRGB(segColors[i])}")));
            sb.AppendLine($"{"Idx",4} {"Ctr",3} {"Type",5} {"p0",28} {"p1 (ctrl)",28} {"p2",28}");
            sb.AppendLine(new string('─', 100));

            for (int i = 0; i < segmentCount; i++)
            {
                var s = segments[i];
                string type = s.isDegenerate ? "LINE " : "CURVE";
                sb.AppendLine($"{i,4} {s.contourIndex,3} {type} " +
                    $"({s.p0x,10:F6}, {s.p0y,10:F6})  ({s.p1x,10:F6}, {s.p1y,10:F6})  ({s.p2x,10:F6}, {s.p2y,10:F6})");
            }

            sb.AppendLine();
            sb.AppendLine($"═══ JUNCTIONS ═══");
            sb.AppendLine($"{"SegA→B",10} {"Pos Error",10} {"Angle°",8} {"Status",12} {"Position (glyph space)",30}");
            sb.AppendLine(new string('─', 80));

            if (junctions != null)
            {
                foreach (var j in junctions)
                {
                    string status;
                    if (j.posError > 1e-3f)
                        status = "GAP!";
                    else if (j.angleDeg > 5f)
                        status = "CORNER";
                    else if (j.angleDeg > 1f)
                        status = "KINK";
                    else
                        status = "smooth";

                    sb.AppendLine($"  {j.segA,3}→{j.segB,-3} {j.posError,10:F6} {j.angleDeg,8:F2} {status,-12} " +
                        $"({j.posA.x:F6}, {j.posA.y:F6})");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"═══ POTENTIAL PROBLEMS ═══");
            bool anyProblems = false;

            if (junctions != null)
            {
                foreach (var j in junctions)
                {
                    if (j.posError > 1e-4f)
                    {
                        sb.AppendLine($"  [GAP] Seg {j.segA}→{j.segB}: position gap of {j.posError:E3} at ({j.posA.x:F4}, {j.posA.y:F4})");
                        anyProblems = true;
                    }

                    if (j.angleDeg > 1f && j.angleDeg < 170f)
                    {
                        sb.AppendLine($"  [KINK] Seg {j.segA}→{j.segB}: tangent angle {j.angleDeg:F2}° at ({j.posA.x:F4}, {j.posA.y:F4})");
                        anyProblems = true;
                    }
                }
            }

            for (int i = 0; i < segmentCount; i++)
            {
                var s = segments[i];
                float dx = s.p2x - s.p0x, dy = s.p2y - s.p0y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-5f)
                {
                    sb.AppendLine($"  [ZERO-LENGTH] Seg {i}: length={len:E3}");
                    anyProblems = true;
                }
            }

            for (int i = 0; i < segmentCount; i++)
            {
                var s = segments[i];
                float d01 = Mathf.Sqrt((s.p1x - s.p0x) * (s.p1x - s.p0x) + (s.p1y - s.p0y) * (s.p1y - s.p0y));
                float d12 = Mathf.Sqrt((s.p2x - s.p1x) * (s.p2x - s.p1x) + (s.p2y - s.p1y) * (s.p2y - s.p1y));
                if (d01 < 1e-5f)
                {
                    sb.AppendLine($"  [COLLAPSED] Seg {i}: p0 ≈ p1 (dist={d01:E3}) — degenerate start");
                    anyProblems = true;
                }
                if (d12 < 1e-5f)
                {
                    sb.AppendLine($"  [COLLAPSED] Seg {i}: p1 ≈ p2 (dist={d12:E3}) — degenerate end");
                    anyProblems = true;
                }
            }

            if (!anyProblems)
                sb.AppendLine("  None detected.");

            sb.AppendLine();
            sb.AppendLine($"═══ SDF BOUNDARY PIXELS (near 0.5 threshold) with lowest Y ═══");
            sb.AppendLine($"Looking for potential notch in bottom region...");

            var boundaryPixels = new List<(int x, int y, float val, int seg)>();
            for (int y = 0; y < tileSize; y++)
            {
                for (int x = 0; x < tileSize; x++)
                {
                    int idx = y * tileSize + x;
                    float v = bruteForceDistances[idx];
                    if (Mathf.Abs(v - 0.5f) < 0.05f)
                        boundaryPixels.Add((x, y, v, closestSegIds[idx]));
                }
            }

            boundaryPixels.Sort((a, b) => a.y.CompareTo(b.y));

            int showCount = Mathf.Min(30, boundaryPixels.Count);
            sb.AppendLine($"Showing {showCount} boundary pixels (lowest Y first = bottom of glyph):");
            sb.AppendLine($"{"Pixel",12} {"SDF Value",10} {"ClosestSeg",11}");
            for (int i = 0; i < showCount; i++)
            {
                var bp = boundaryPixels[i];
                sb.AppendLine($"  ({bp.x,3}, {bp.y,3}) {bp.val,10:F6} seg {bp.seg,3}");
            }

            if (boundaryPixels.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"═══ PIXEL GRID AROUND LOWEST BOUNDARY POINT ═══");

                var lowestPx = boundaryPixels[0];
                int centerX = lowestPx.x, centerY = lowestPx.y;
                int halfW = 6;

                sb.AppendLine($"Center pixel: ({centerX}, {centerY})  SDF={lowestPx.val:F6}  ClosestSeg={lowestPx.seg}");
                sb.AppendLine($"Grid shows SDF values (inside < 0.5 < outside). '*' = contour crossing:");
                sb.AppendLine();

                sb.Append("     ");
                for (int x = centerX - halfW; x <= centerX + halfW; x++)
                    sb.Append($" {x,5}");
                sb.AppendLine();

                for (int y = centerY - halfW; y <= centerY + halfW; y++)
                {
                    sb.Append($"{y,4} |");
                    for (int x = centerX - halfW; x <= centerX + halfW; x++)
                    {
                        if (x >= 0 && x < tileSize && y >= 0 && y < tileSize)
                        {
                            int idx = y * tileSize + x;
                            float v = bruteForceDistances[idx];
                            bool isContour = false;
                            if (x > 0 && x < tileSize && y > 0 && y < tileSize)
                            {
                                float vL = bruteForceDistances[idx - 1];
                                float vD = bruteForceDistances[(y - 1) * tileSize + x];
                                if ((v - 0.5f) * (vL - 0.5f) < 0 || (v - 0.5f) * (vD - 0.5f) < 0)
                                    isContour = true;
                            }
                            string marker = isContour ? "*" : " ";
                            sb.Append($"{marker}{v,4:F2} ");
                        }
                        else
                        {
                            sb.Append("  --- ");
                        }
                    }
                    sb.AppendLine();
                }

                sb.AppendLine();
                sb.AppendLine("Closest segment IDs for same grid:");
                sb.Append("     ");
                for (int x = centerX - halfW; x <= centerX + halfW; x++)
                    sb.Append($"  {x,3}");
                sb.AppendLine();

                for (int y = centerY - halfW; y <= centerY + halfW; y++)
                {
                    sb.Append($"{y,4} |");
                    for (int x = centerX - halfW; x <= centerX + halfW; x++)
                    {
                        if (x >= 0 && x < tileSize && y >= 0 && y < tileSize)
                        {
                            int idx = y * tileSize + x;
                            sb.Append($"  {closestSegIds[idx],3}");
                        }
                        else
                        {
                            sb.Append("  ---");
                        }
                    }
                    sb.AppendLine();
                }
            }

            dataText = sb.ToString();
        }

        void DrawContourTab()
        {
            showControlPoints = EditorGUILayout.Toggle("Show Control Points", showControlPoints);
            showSegmentIndices = EditorGUILayout.Toggle("Show Segment Indices", showSegmentIndices);
            showJunctionTangents = EditorGUILayout.Toggle("Show Junction Tangents", showJunctionTangents);

            if (contourTex == null)
            {
                EditorGUILayout.HelpBox("Click 'Extract & Analyze' to generate contour view.", MessageType.Info);
                return;
            }

            contourScroll = EditorGUILayout.BeginScrollView(contourScroll);

            float availW = EditorGUIUtility.currentViewWidth - 20;
            float texSize = Mathf.Min(availW, ContourTexSize);
            Rect texRect = GUILayoutUtility.GetRect(texSize, texSize);
            GUI.DrawTexture(texRect, contourTex, ScaleMode.ScaleToFit);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Legend:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  White circles = on-curve endpoints");
            EditorGUILayout.LabelField("  Colored squares = off-curve control points");
            EditorGUILayout.LabelField("  Green junction = smooth (<2°)");
            EditorGUILayout.LabelField("  Red junction = kink/corner (>2°)");
            EditorGUILayout.LabelField("  Orange arrow = outgoing tangent of segment A");
            EditorGUILayout.LabelField("  Cyan arrow = incoming tangent of segment B");

            if (junctions != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Junction Summary:", EditorStyles.boldLabel);
                foreach (var j in junctions)
                {
                    string icon = j.angleDeg > 2f ? "(!)" : "(ok)";
                    Color c = j.angleDeg > 2f ? Color.red : Color.green;
                    var style = new GUIStyle(EditorStyles.label) { richText = true };
                    string colorHex = ColorUtility.ToHtmlStringRGB(c);
                    EditorGUILayout.LabelField(
                        $"  <color=#{colorHex}>{icon}</color> Seg {j.segA}→{j.segB}: " +
                        $"angle={j.angleDeg:F2}°, pos=({j.posA.x:F4}, {j.posA.y:F4}), gap={j.posError:E2}",
                        style);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawSdfTab()
        {
            if (sdfBruteForce == null)
            {
                EditorGUILayout.HelpBox("Click 'Extract & Analyze' to generate SDF views.", MessageType.Info);
                return;
            }

            sdfScroll = EditorGUILayout.BeginScrollView(sdfScroll);

            float availW = EditorGUIUtility.currentViewWidth - 20;
            float cellW = (availW - 20) / 2f;
            float cellH = cellW;

            EditorGUILayout.LabelField($"SDF Tile: {tileSize}x{tileSize} (brute-force ground truth)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            DrawTexWithLabel(sdfBruteForce, "SDF (grayscale)", cellW, cellH);
            DrawTexWithLabel(sdfContour, "SDF + 0.5 contour (red)", cellW, cellH);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            DrawTexWithLabel(sdfClosestSeg, "Closest Segment (colored)", cellW, cellH);
            DrawTexWithLabel(windingTex, "Winding (blue=inside)", cellW, cellH);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Pixel Inspector:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Hover over SDF textures to see per-pixel values in the Data tab.");

            EditorGUILayout.EndScrollView();
        }

        void DrawTexWithLabel(Texture2D tex, string label, float w, float h)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(w));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            Rect r = GUILayoutUtility.GetRect(w, h);
            if (tex != null)
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            EditorGUILayout.EndVertical();
        }

        void DrawDataTab()
        {
            if (string.IsNullOrEmpty(dataText))
            {
                EditorGUILayout.HelpBox("Click 'Extract & Analyze' to generate diagnostic data.", MessageType.Info);
                return;
            }

            dataScroll = EditorGUILayout.BeginScrollView(dataScroll);

            var style = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = false,
                wordWrap = false
            };

            EditorGUILayout.TextArea(dataText, style, GUILayout.ExpandHeight(true));

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy to Clipboard"))
                EditorGUIUtility.systemCopyBuffer = dataText;
        }

        void OnDestroy()
        {
            if (sdfBruteForce != null) DestroyImmediate(sdfBruteForce);
            if (sdfClosestSeg != null) DestroyImmediate(sdfClosestSeg);
            if (windingTex != null) DestroyImmediate(windingTex);
            if (sdfContour != null) DestroyImmediate(sdfContour);
            if (contourTex != null) DestroyImmediate(contourTex);
        }
    }
}
#endif