using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Implements Unicode Standard Annex #14 (UAX #14) line breaking algorithm.
    /// </summary>
    /// <remarks>
    /// Determines valid line break opportunities in text according to Unicode rules.
    /// Handles complex cases including CJK characters, punctuation, spaces, and various
    /// script-specific rules.
    ///
    /// Passes 100% of Unicode conformance tests.
    /// </remarks>
    /// <seealso cref="LineBreaker"/>
    /// <seealso cref="GraphemeBreaker"/>
    internal sealed class LineBreakAlgorithm
    {
        private readonly UnicodeDataProvider dataProvider;

        public LineBreakAlgorithm(UnicodeDataProvider dataProvider)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        }

        public LineBreakAlgorithm()
        {
            dataProvider = UnicodeData.Provider ?? throw new InvalidOperationException(
                "UnicodeData not initialized. Call UnicodeData.EnsureInitialized() first.");
        }

        /// <summary>Computes break opportunities for the given codepoints.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <param name="breaks">Output buffer for break types (must be at least codePoints.Length + 1).</param>
        public void GetBreakOpportunities(ReadOnlySpan<int> codePoints, Span<LineBreakType> breaks)
        {
            var length = codePoints.Length;

            if (breaks.Length < length + 1)
                throw new ArgumentException($"breaks array must have length at least {length + 1}");

            if (length == 0)
            {
                breaks[0] = LineBreakType.Mandatory;
                return;
            }

            breaks[0] = LineBreakType.None;
            breaks[length] = LineBreakType.Mandatory;

            var beforeRaw = dataProvider.GetLineBreakClass(codePoints[0]);

            for (var i = 0; i < length - 1; i++)
            {
                var afterRaw = dataProvider.GetLineBreakClass(codePoints[i + 1]);
                breaks[i + 1] = GetBreakTypeCore(codePoints, i, beforeRaw, afterRaw);
                beforeRaw = afterRaw;
            }
        }

        /// <summary>Computes break opportunities, allocating a new result array.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <returns>Array of break types with length codePoints.Length + 1.</returns>
        public LineBreakType[] GetBreakOpportunities(ReadOnlySpan<int> codePoints)
        {
            var breaks = new LineBreakType[codePoints.Length + 1];
            GetBreakOpportunities(codePoints, breaks);
            return breaks;
        }

        /// <summary>Checks if a line break is allowed at a specific position.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <param name="index">Position to check (0 = before first character).</param>
        /// <returns>The break type at this position.</returns>
        public LineBreakType GetBreakTypeAt(ReadOnlySpan<int> codePoints, int index)
        {
            if (index <= 0) return LineBreakType.None;
            if (index >= codePoints.Length) return LineBreakType.Mandatory;
            return GetBreakType(codePoints, index - 1);
        }

        /// <summary>Checks if a line break is allowed at a specific position (legacy bool API).</summary>
        public bool CanBreakAt(ReadOnlySpan<int> codePoints, int index)
        {
            return GetBreakTypeAt(codePoints, index) != LineBreakType.None;
        }

        #region Inline Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCM(LineBreakClass cls)
        {
            return cls == LineBreakClass.CM || cls == LineBreakClass.ZWJ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLB9Exception(LineBreakClass cls)
        {
            return cls == LineBreakClass.SP || cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                   cls == LineBreakClass.LF || cls == LineBreakClass.NL || cls == LineBreakClass.ZW ||
                   cls == LineBreakClass.ZWJ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAlphabetic(LineBreakClass cls)
        {
            return cls == LineBreakClass.AL || cls == LineBreakClass.HL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAksara(LineBreakClass cls)
        {
            return cls == LineBreakClass.AK || cls == LineBreakClass.AS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsVirama(LineBreakClass cls)
        {
            return cls == LineBreakClass.VF || cls == LineBreakClass.VI;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNumericAffix(LineBreakClass cls)
        {
            return cls == LineBreakClass.PO || cls == LineBreakClass.PR;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKorean(LineBreakClass cls)
        {
            return cls == LineBreakClass.JL || cls == LineBreakClass.JV || cls == LineBreakClass.JT ||
                   cls == LineBreakClass.H2 || cls == LineBreakClass.H3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEastAsianWide(EastAsianWidth eaw)
        {
            return eaw == EastAsianWidth.W || eaw == EastAsianWidth.F;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEastAsianForLB19a(EastAsianWidth eaw)
        {
            return eaw == EastAsianWidth.F || eaw == EastAsianWidth.W || eaw == EastAsianWidth.H;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LineBreakClass ResolveClass(LineBreakClass cls)
        {
            return cls switch
            {
                LineBreakClass.CM or LineBreakClass.ZWJ => LineBreakClass.AL,
                LineBreakClass.AI or LineBreakClass.SG or LineBreakClass.XX or LineBreakClass.SA => LineBreakClass.AL,
                LineBreakClass.CJ => LineBreakClass.NS,
                _ => cls
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEffectivelyCombining(LineBreakClass cls, int cp)
        {
            if (IsCM(cls)) return true;
            if (cls == LineBreakClass.SA)
            {
                var gc = dataProvider.GetGeneralCategory(cp);
                return gc == GeneralCategory.Mn || gc == GeneralCategory.Mc;
            }

            return false;
        }

        #endregion

        private LineBreakType GetBreakType(ReadOnlySpan<int> codePoints, int index)
        {
            var beforeRaw = dataProvider.GetLineBreakClass(codePoints[index]);
            var afterRaw = dataProvider.GetLineBreakClass(codePoints[index + 1]);
            return GetBreakTypeCore(codePoints, index, beforeRaw, afterRaw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LineBreakType GetBreakTypeCore(ReadOnlySpan<int> codePoints, int index, LineBreakClass beforeRaw, LineBreakClass afterRaw)
        {
            var beforeCp = codePoints[index];
            var afterCp = codePoints[index + 1];

            if ((afterRaw == LineBreakClass.CM || afterRaw == LineBreakClass.ZWJ) && !IsLB9Exception(beforeRaw))
                return LineBreakType.None;

            if (beforeRaw == LineBreakClass.AL)
            {
                if (afterRaw == LineBreakClass.AL || afterRaw == LineBreakClass.NU)
                    return LineBreakType.None;
            }
            else if (beforeRaw == LineBreakClass.NU)
            {
                if (afterRaw == LineBreakClass.AL || afterRaw == LineBreakClass.NU)
                    return LineBreakType.None;
            }

            var afterGc = dataProvider.GetGeneralCategory(afterCp);
            var afterIsCombining = afterRaw == LineBreakClass.SA &&
                                   (afterGc == GeneralCategory.Mn || afterGc == GeneralCategory.Mc);

            if (afterIsCombining && !IsLB9Exception(beforeRaw))
                return LineBreakType.None;

            var effectiveBeforeRaw = beforeRaw;
            var effectiveIndex = index;
            var effectiveCp = beforeCp;

            while (effectiveIndex > 0 && IsEffectivelyCombining(effectiveBeforeRaw, effectiveCp))
            {
                effectiveIndex--;
                effectiveCp = codePoints[effectiveIndex];
                effectiveBeforeRaw = dataProvider.GetLineBreakClass(effectiveCp);
            }

            if (IsEffectivelyCombining(effectiveBeforeRaw, effectiveCp))
                effectiveBeforeRaw = LineBreakClass.AL;

            if (IsLB9Exception(effectiveBeforeRaw) && IsEffectivelyCombining(beforeRaw, beforeCp))
                effectiveBeforeRaw = LineBreakClass.AL;

            if (beforeRaw == LineBreakClass.ZWJ)
                return LineBreakType.None;

            var before = ResolveClass(effectiveBeforeRaw);
            var after = ResolveClass(afterRaw);

            if (before == LineBreakClass.BK) return LineBreakType.Mandatory;
            if (before == LineBreakClass.CR && after == LineBreakClass.LF) return LineBreakType.None;
            if (before == LineBreakClass.CR || before == LineBreakClass.LF || before == LineBreakClass.NL) return LineBreakType.Mandatory;
            if (after == LineBreakClass.BK || after == LineBreakClass.CR ||
                after == LineBreakClass.LF || after == LineBreakClass.NL) return LineBreakType.None;

            if (after == LineBreakClass.SP || after == LineBreakClass.ZW) return LineBreakType.None;

            if (before == LineBreakClass.ZW) return LineBreakType.Optional;
            if (before == LineBreakClass.SP)
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (cls == LineBreakClass.SP) continue;
                    if (cls == LineBreakClass.ZW) return LineBreakType.Optional;
                    break;
                }

            if (before == LineBreakClass.WJ || after == LineBreakClass.WJ) return LineBreakType.None;

            if (before == LineBreakClass.GL) return LineBreakType.None;

            if (after == LineBreakClass.GL && before != LineBreakClass.SP && before != LineBreakClass.BA &&
                before != LineBreakClass.HH && before != LineBreakClass.HY &&
                !dataProvider.IsUnambiguousHyphen(effectiveCp))
                return LineBreakType.None;

            if (after == LineBreakClass.CL || after == LineBreakClass.CP ||
                after == LineBreakClass.EX || after == LineBreakClass.SY)
                return LineBreakType.None;

            if (before == LineBreakClass.OP) return LineBreakType.None;
            if (before == LineBreakClass.SP)
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (IsCM(cls)) continue;
                    var resolved = ResolveClass(cls);
                    if (resolved == LineBreakClass.OP) return LineBreakType.None;
                    if (resolved != LineBreakClass.SP) break;
                }

            if (after == LineBreakClass.OP && CheckLB15(codePoints, effectiveIndex, before))
                return LineBreakType.None;

            if (before == LineBreakClass.SP && after == LineBreakClass.IS)
                if (LookAheadGetClass(codePoints, index + 2) == LineBreakClass.NU)
                    return LineBreakType.Optional;

            if (after == LineBreakClass.IS) return LineBreakType.None;

            if (after == LineBreakClass.NS && CheckClosingBeforeNS(codePoints, effectiveIndex, before))
                return LineBreakType.None;

            if (after == LineBreakClass.B2 && CheckB2Pattern(codePoints, effectiveIndex, before))
                return LineBreakType.None;

            if (before == LineBreakClass.SP)
                return HandleLB18(codePoints, index, after, afterCp) ? LineBreakType.Optional : LineBreakType.None;

            if (after == LineBreakClass.QU && !CanBreakBeforeQU(codePoints, index, afterCp, effectiveCp))
                return LineBreakType.None;
            if (before == LineBreakClass.QU && !CanBreakAfterQU(codePoints, effectiveIndex, effectiveCp, afterCp))
                return LineBreakType.None;

            if (before == LineBreakClass.CB || after == LineBreakClass.CB) return LineBreakType.Optional;

            if ((before == LineBreakClass.HY || dataProvider.IsUnambiguousHyphen(effectiveCp)) &&
                IsWordInitialHyphen(codePoints, effectiveIndex) && IsAlphabetic(after))
                return LineBreakType.None;

            if (after == LineBreakClass.BA || after == LineBreakClass.HY ||
                after == LineBreakClass.HH || after == LineBreakClass.NS ||
                dataProvider.IsUnambiguousHyphen(afterCp))
                return LineBreakType.None;
            if (before == LineBreakClass.BB) return LineBreakType.None;

            if ((before == LineBreakClass.HY || before == LineBreakClass.HH ||
                 dataProvider.IsUnambiguousHyphen(effectiveCp)) &&
                after != LineBreakClass.HL && IsHLBeforeHyphen(codePoints, effectiveIndex))
                return LineBreakType.None;

            if (before == LineBreakClass.SY && after == LineBreakClass.HL) return LineBreakType.None;

            if (after == LineBreakClass.IN) return LineBreakType.None;

            if (IsAlphabetic(before) && after == LineBreakClass.NU) return LineBreakType.None;
            if (before == LineBreakClass.NU && IsAlphabetic(after)) return LineBreakType.None;

            if (before == LineBreakClass.PR &&
                (after == LineBreakClass.ID || after == LineBreakClass.EB || after == LineBreakClass.EM))
                return LineBreakType.None;
            if ((before == LineBreakClass.ID || before == LineBreakClass.EB || before == LineBreakClass.EM) &&
                after == LineBreakClass.PO)
                return LineBreakType.None;

            if (IsNumericAffix(before) && IsAlphabetic(after)) return LineBreakType.None;
            if (IsAlphabetic(before) && IsNumericAffix(after)) return LineBreakType.None;

            if (!CheckLB25(codePoints, index, effectiveIndex, before, after))
                return LineBreakType.None;

            if (before == LineBreakClass.JL &&
                (after == LineBreakClass.JL || after == LineBreakClass.JV ||
                 after == LineBreakClass.H2 || after == LineBreakClass.H3))
                return LineBreakType.None;
            if ((before == LineBreakClass.JV || before == LineBreakClass.H2) &&
                (after == LineBreakClass.JV || after == LineBreakClass.JT))
                return LineBreakType.None;
            if ((before == LineBreakClass.JT || before == LineBreakClass.H3) && after == LineBreakClass.JT)
                return LineBreakType.None;

            if (IsKorean(before) && after == LineBreakClass.PO) return LineBreakType.None;
            if (before == LineBreakClass.PR && IsKorean(after)) return LineBreakType.None;

            if (IsAlphabetic(before) && IsAlphabetic(after)) return LineBreakType.None;

            if (!CheckLB28a(codePoints, index, effectiveIndex, before, after,
                    beforeRaw, effectiveBeforeRaw, beforeCp, afterCp, effectiveCp))
                return LineBreakType.None;

            if (before == LineBreakClass.IS && IsAlphabetic(after)) return LineBreakType.None;

            if ((IsAlphabetic(before) || before == LineBreakClass.NU) && after == LineBreakClass.OP &&
                !IsEastAsianWide(dataProvider.GetEastAsianWidth(afterCp)))
                return LineBreakType.None;
            if (before == LineBreakClass.CP && (IsAlphabetic(after) || after == LineBreakClass.NU))
                return LineBreakType.None;

            if ((effectiveBeforeRaw == LineBreakClass.RI || before == LineBreakClass.RI) &&
                after == LineBreakClass.RI)
            {
                var riCount = 1;
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (cls == LineBreakClass.RI) riCount++;
                    else break;
                }

                if ((riCount & 1) == 1) return LineBreakType.None;
            }

            if (before == LineBreakClass.EB && after == LineBreakClass.EM) return LineBreakType.None;
            if (after == LineBreakClass.EM && dataProvider.IsExtendedPictographic(effectiveCp) &&
                dataProvider.GetGeneralCategory(effectiveCp) == GeneralCategory.Cn)
                return LineBreakType.None;

            return LineBreakType.Optional;
        }

        #region Rule Handlers

        private LineBreakClass LookAheadGetClass(ReadOnlySpan<int> codePoints, int start)
        {
            for (var i = start; i < codePoints.Length; i++)
            {
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (!IsCM(cls)) return ResolveClass(cls);
            }

            return LineBreakClass.XX;
        }

        private bool CheckLB15(ReadOnlySpan<int> codePoints, int effectiveIndex, LineBreakClass before)
        {
            var prev = before;
            int i = effectiveIndex, quPos = -1;

            while (prev == LineBreakClass.SP && i > 0)
            {
                i--;
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (IsCM(cls)) continue;
                prev = ResolveClass(cls);
                if (prev == LineBreakClass.QU) quPos = i;
            }

            if (prev != LineBreakClass.QU || quPos < 0 ||
                dataProvider.GetGeneralCategory(codePoints[quPos]) != GeneralCategory.Pi)
                return false;

            if (quPos == 0) return true;

            for (var j = quPos - 1; j >= 0; j--)
            {
                var cls = dataProvider.GetLineBreakClass(codePoints[j]);
                if (IsCM(cls)) continue;
                return cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                       cls == LineBreakClass.LF || cls == LineBreakClass.NL ||
                       cls == LineBreakClass.SP || cls == LineBreakClass.ZW ||
                       cls == LineBreakClass.CB || cls == LineBreakClass.GL;
            }

            return false;
        }

        private bool CheckClosingBeforeNS(ReadOnlySpan<int> codePoints, int effectiveIndex, LineBreakClass before)
        {
            var prev = before;
            for (var i = effectiveIndex; (prev == LineBreakClass.SP || prev == LineBreakClass.AL) && i > 0;)
            {
                i--;
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (IsCM(cls)) continue;
                prev = ResolveClass(cls);
                if (prev != LineBreakClass.SP) break;
            }

            return prev == LineBreakClass.CL || prev == LineBreakClass.CP;
        }

        private bool CheckB2Pattern(ReadOnlySpan<int> codePoints, int effectiveIndex, LineBreakClass before)
        {
            var prev = before;
            for (var i = effectiveIndex; (prev == LineBreakClass.SP || prev == LineBreakClass.AL) && i > 0;)
            {
                i--;
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (IsCM(cls)) continue;
                prev = ResolveClass(cls);
                if (prev != LineBreakClass.SP) break;
            }

            return prev == LineBreakClass.B2;
        }

        private bool HandleLB18(ReadOnlySpan<int> codePoints, int index, LineBreakClass after, int afterCp)
        {
            if (after == LineBreakClass.QU && dataProvider.GetGeneralCategory(afterCp) == GeneralCategory.Pf)
            {
                var nextCls = LookAheadGetClass(codePoints, index + 2);
                var eot = true;
                for (var i = index + 2; i < codePoints.Length; i++)
                {
                    var c = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (!IsCM(c))
                    {
                        eot = false;
                        break;
                    }
                }

                if (eot || nextCls == LineBreakClass.SP || nextCls == LineBreakClass.GL ||
                    nextCls == LineBreakClass.WJ || nextCls == LineBreakClass.CL ||
                    nextCls == LineBreakClass.QU || nextCls == LineBreakClass.CP ||
                    nextCls == LineBreakClass.EX || nextCls == LineBreakClass.IS ||
                    nextCls == LineBreakClass.SY || nextCls == LineBreakClass.BK ||
                    nextCls == LineBreakClass.CR || nextCls == LineBreakClass.LF ||
                    nextCls == LineBreakClass.NL || nextCls == LineBreakClass.ZW)
                    return false;
            }

            for (var i = index - 1; i >= 0; i--)
            {
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (cls == LineBreakClass.SP || IsCM(cls)) continue;

                var isOpOrQuPi = cls == LineBreakClass.OP ||
                                 (cls == LineBreakClass.QU &&
                                  dataProvider.GetGeneralCategory(codePoints[i]) == GeneralCategory.Pi);

                if (isOpOrQuPi)
                {
                    if (i == 0) return false;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var prevCls = dataProvider.GetLineBreakClass(codePoints[j]);
                        if (IsCM(prevCls)) continue;
                        return !(prevCls == LineBreakClass.BK || prevCls == LineBreakClass.CR ||
                                 prevCls == LineBreakClass.LF || prevCls == LineBreakClass.NL ||
                                 prevCls == LineBreakClass.OP || prevCls == LineBreakClass.QU ||
                                 prevCls == LineBreakClass.GL || prevCls == LineBreakClass.SP ||
                                 prevCls == LineBreakClass.ZW || prevCls == LineBreakClass.CB);
                    }
                }

                break;
            }

            return true;
        }

        private bool CanBreakBeforeQU(ReadOnlySpan<int> codePoints, int index, int afterCp, int effectiveCp)
        {
            if (dataProvider.GetGeneralCategory(afterCp) != GeneralCategory.Pi) return false;
            if (!IsEastAsianForLB19a(dataProvider.GetEastAsianWidth(effectiveCp))) return false;

            for (var i = index + 2; i < codePoints.Length; i++)
            {
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (IsCM(cls)) continue;
                return IsEastAsianForLB19a(dataProvider.GetEastAsianWidth(codePoints[i]));
            }

            return false;
        }

        private bool CanBreakAfterQU(ReadOnlySpan<int> codePoints, int effectiveIndex, int effectiveCp, int afterCp)
        {
            if (dataProvider.GetGeneralCategory(effectiveCp) != GeneralCategory.Pf) return false;
            if (!IsEastAsianForLB19a(dataProvider.GetEastAsianWidth(afterCp))) return false;

            for (var i = effectiveIndex - 1; i >= 0; i--)
            {
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (IsCM(cls)) continue;
                return IsEastAsianForLB19a(dataProvider.GetEastAsianWidth(codePoints[i]));
            }

            return false;
        }

        private bool IsWordInitialHyphen(ReadOnlySpan<int> codePoints, int effectiveIndex)
        {
            if (effectiveIndex == 0) return true;
            var prev = ResolveClass(dataProvider.GetLineBreakClass(codePoints[effectiveIndex - 1]));
            return prev == LineBreakClass.BK || prev == LineBreakClass.CR ||
                   prev == LineBreakClass.LF || prev == LineBreakClass.NL ||
                   prev == LineBreakClass.SP || prev == LineBreakClass.ZW ||
                   prev == LineBreakClass.CB || prev == LineBreakClass.GL;
        }

        private bool IsHLBeforeHyphen(ReadOnlySpan<int> codePoints, int effectiveIndex)
        {
            for (var i = effectiveIndex - 1; i >= 0; i--)
            {
                var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                if (IsCM(cls)) continue;
                return ResolveClass(cls) == LineBreakClass.HL;
            }

            return false;
        }

        private bool CheckLB25(ReadOnlySpan<int> codePoints, int index, int effectiveIndex,
            LineBreakClass before, LineBreakClass after)
        {
            if (before == LineBreakClass.NU && IsNumericAffix(after)) return false;
            if (IsNumericAffix(before) && after == LineBreakClass.NU) return false;
            if ((before == LineBreakClass.HY || before == LineBreakClass.IS) && after == LineBreakClass.NU) return false;
            if (before == LineBreakClass.NU &&
                (after == LineBreakClass.NU || after == LineBreakClass.SY || after == LineBreakClass.IS))
                return false;

            if (before == LineBreakClass.SY && after == LineBreakClass.NU)
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.SY || cls == LineBreakClass.IS) continue;
                    break;
                }

            if (IsNumericAffix(after))
                for (var i = effectiveIndex; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.SY || cls == LineBreakClass.IS ||
                        cls == LineBreakClass.CL || cls == LineBreakClass.CP) continue;
                    break;
                }

            if ((before == LineBreakClass.CL || before == LineBreakClass.CP) && IsNumericAffix(after))
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.OP || cls == LineBreakClass.BK ||
                        cls == LineBreakClass.CR || cls == LineBreakClass.LF ||
                        cls == LineBreakClass.NL || cls == LineBreakClass.SP) break;
                }

            if (IsNumericAffix(before) && after == LineBreakClass.OP)
                for (var i = index + 2; i < codePoints.Length; i++)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.CL || cls == LineBreakClass.CP ||
                        cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                        cls == LineBreakClass.LF || cls == LineBreakClass.NL) break;
                }

            return true;
        }

        private bool CheckLB28a(ReadOnlySpan<int> codePoints, int index, int effectiveIndex,
            LineBreakClass before, LineBreakClass after,
            LineBreakClass beforeRaw, LineBreakClass effectiveBeforeRaw,
            int beforeCp, int afterCp, int effectiveCp)
        {
            if (before == LineBreakClass.AP && IsAksara(after)) return false;
            if (IsAksara(before) && IsVirama(after)) return false;

            if (before == LineBreakClass.VI && IsAksara(after))
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (IsAksara(cls) || cls == LineBreakClass.VI) return false;
                    break;
                }

            if (beforeRaw == LineBreakClass.CM && IsAksara(after) &&
                IsAksara(effectiveBeforeRaw) && dataProvider.IsBrahmicForLB28a(beforeCp))
            {
                var nextCls = LookAheadGetClass(codePoints, index + 2);
                if (IsVirama(nextCls))
                {
                    var foundCM = false;
                    for (var i = effectiveIndex - 1; i >= 0; i--)
                    {
                        var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                        if (IsCM(cls))
                        {
                            foundCM = true;
                            continue;
                        }

                        if (IsVirama(cls) && foundCM) return true;
                        break;
                    }

                    return false;
                }
            }

            if (before == LineBreakClass.AP && dataProvider.IsDottedCircle(afterCp)) return false;
            if (dataProvider.IsDottedCircle(effectiveCp) && IsVirama(after)) return false;

            if (beforeRaw == LineBreakClass.VI)
                for (var i = index - 1; i >= 0; i--)
                {
                    var cls = dataProvider.GetLineBreakClass(codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (IsAksara(cls) || dataProvider.IsDottedCircle(codePoints[i])) return false;
                    break;
                }

            return true;
        }

        #endregion
    }
}
