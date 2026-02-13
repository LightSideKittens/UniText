using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Parses markup and coordinates modifiers to apply formatting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The central parsing engine that processes text through registered <see cref="IParseRule"/> instances,
    /// extracts markup tags, and maps them to <see cref="BaseModifier"/> instances for rendering.
    /// </para>
    /// <para>
    /// Parsing flow:
    /// 1. <see cref="Register"/> pairs rules with modifiers
    /// 2. <see cref="Parse"/> scans text, matches rules, removes/replaces tags
    /// 3. <see cref="Apply"/> invokes modifiers with cleaned text ranges
    /// </para>
    /// </remarks>
    /// <seealso cref="IParseRule"/>
    /// <seealso cref="BaseModifier"/>
    internal sealed class AttributeParser
    {
        /// <summary>Registered rule-modifier pairs in registration order.</summary>
        public readonly List<(IParseRule rule, BaseModifier modifier)> ruleModPairs = new();

        /// <summary>Parsed attribute spans after calling <see cref="Parse"/>.</summary>
        internal readonly PooledList<AttributeSpan> spans = new();

        private readonly List<IParseRule> allRules = new();
        private readonly Dictionary<IParseRule, BaseModifier> ruleToModifier = new();
        private bool rulesDirty;

        private readonly PooledList<ParsedRange> tempRanges = new(32);

        private readonly PooledList<(int start, int end)> tagRemovals = new();
        private readonly PooledList<(int start, int end, string insert)> tagInsertions = new();

        private static readonly RemovalComparerImpl removalComparer = new();
        private static readonly InsertionComparerImpl insertionComparer = new();
        private static readonly RulePriorityComparerImpl rulePriorityComparer = new();

        private sealed class RulePriorityComparerImpl : IComparer<IParseRule>
        {
            public int Compare(IParseRule a, IParseRule b)
            {
                return b.Priority.CompareTo(a.Priority);
            }
        }

        private sealed class RemovalComparerImpl : IComparer<(int start, int end)>
        {
            public int Compare((int start, int end) a, (int start, int end) b)
            {
                return a.start.CompareTo(b.start);
            }
        }

        private sealed class InsertionComparerImpl : IComparer<(int start, int end, string insert)>
        {
            public int Compare((int start, int end, string insert) a, (int start, int end, string insert) b)
            {
                return a.start.CompareTo(b.start);
            }
        }

        private PooledBuffer<int> indexMapBuffer;
        private PooledBuffer<char> cleanTextBuffer;
        private PooledBuffer<int> charToCpMap;

        /// <summary>Gets the parsed text with all markup tags removed (as Span, zero-allocation).</summary>
        public ReadOnlySpan<char> CleanTextSpan => cleanTextBuffer.Span;

        private string cachedCleanTextString;

        /// <summary>Gets the parsed text with all markup tags removed (allocates string on first access).</summary>
        public string CleanText
        {
            get
            {
                if (cachedCleanTextString == null && cleanTextBuffer.count > 0)
                    cachedCleanTextString = new string(cleanTextBuffer.data, 0, cleanTextBuffer.count);
                return cachedCleanTextString ?? string.Empty;
            }
        }


        /// <summary>Registers a parse rule with its associated modifier.</summary>
        /// <param name="rule">The rule that identifies markup in text.</param>
        /// <param name="modifier">The modifier that applies formatting for matched ranges.</param>
        public void Register(IParseRule rule, BaseModifier modifier)
        {
            ruleModPairs.Add((rule, modifier));
            allRules.Add(rule);
            ruleToModifier[rule] = modifier;
            rulesDirty = true;
        }

        /// <summary>Unregisters a modifier and its associated rule.</summary>
        /// <param name="modifier">The modifier to remove.</param>
        public void Unregister(BaseModifier modifier)
        {
            for (var i = ruleModPairs.Count - 1; i >= 0; i--)
            {
                if (ruleModPairs[i].modifier == modifier)
                {
                    var rule = ruleModPairs[i].rule;
                    allRules.Remove(rule);
                    ruleToModifier.Remove(rule);
                    ruleModPairs.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>Deinitializes all registered modifiers.</summary>
        public void DeinitializeModifiers()
        {
            for (var i = 0; i < ruleModPairs.Count; i++) ruleModPairs[i].modifier.Destroy();
        }

        /// <summary>Releases all pooled buffers back to the pool.</summary>
        public void Release()
        {
            spans.Return();
            tempRanges.Return();
            tagRemovals.Return();
            tagInsertions.Return();
            indexMapBuffer.Return();
            cleanTextBuffer.Return();
            charToCpMap.Return();
        }

        /// <summary>Resets all registered modifiers to their initial state.</summary>
        public void ResetModifiers()
        {
            for (var i = 0; i < ruleModPairs.Count; i++) ruleModPairs[i].modifier.Disable();
        }

        /// <summary>Applies all parsed attribute spans to their modifiers.</summary>
        /// <remarks>
        /// <para>
        /// Two-pass application:
        /// 1. Prepare pass - initializes and clears buffers for used modifiers
        /// 2. Apply pass - writes data to buffers in reverse order
        /// </para>
        /// <para>
        /// Converts span indices from UTF-16 code units to Unicode codepoints
        /// before passing to modifiers, since the text processing pipeline
        /// works in codepoint space (HarfBuzz, cluster indices, etc.).
        /// </para>
        /// </remarks>
        public void Apply()
        {
            if (spans.Count == 0)
                return;

            BuildCharToCodepointMap();

            for (var i = 0; i < spans.Count; i++)
            {
                var modifier = spans[i].modifier;
                if (!modifier.IsInitialized)
                    modifier.Prepare();
            }

            var map = charToCpMap.data;
            for (var i = spans.Count - 1; i >= 0; i--)
            {
                ref readonly var span = ref spans[i];
                var cpStart = map[span.start];
                var cpEnd = map[span.end];
                span.modifier.Apply(cpStart, cpEnd, span.parameter);
            }
        }

        /// <summary>
        /// Builds mapping from UTF-16 char indices to Unicode codepoint indices.
        /// Surrogate pairs (2 chars) map to the same codepoint index.
        /// </summary>
        private void BuildCharToCodepointMap()
        {
            var text = cleanTextBuffer.Span;
            var len = text.Length;

            charToCpMap.EnsureCapacity(len + 1);
            var map = charToCpMap.data;

            var cpIndex = 0;
            for (var i = 0; i < len; i++)
            {
                map[i] = cpIndex;
                if (char.IsHighSurrogate(text[i]) && i + 1 < len && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    map[i] = cpIndex;
                }
                cpIndex++;
            }
            map[len] = cpIndex;
        }

        /// <summary>Parses the input text, extracting markup and building clean text.</summary>
        /// <param name="text">The raw text with markup tags to parse.</param>
        public void Parse(ReadOnlySpan<char> text)
        {
            spans.FakeClear();
            tagRemovals.FakeClear();
            tagInsertions.FakeClear();
            cleanTextBuffer.FakeClear();
            cachedCleanTextString = null;

            if (rulesDirty)
            {
                allRules.Sort(rulePriorityComparer);
                rulesDirty = false;
            }

            for (var r = 0; r < allRules.Count; r++)
                allRules[r].Reset();

            if (text.IsEmpty)
                return;

            var index = 0;
            while (index < text.Length)
            {
                var newIndex = index;

                for (var r = 0; r < allRules.Count; r++)
                {
                    var rule = allRules[r];
                    tempRanges.FakeClear();
                    var result = rule.TryMatch(text, index, tempRanges);

                    if (result > index)
                    {
                        newIndex = result;
                        for (var j = 0; j < tempRanges.Count; j++)
                        {
                            ref readonly var range = ref tempRanges[j];
                            ProcessRange(in range);
                            CreateSpanForRule(rule, in range);
                        }

                        break;
                    }
                }

                if (newIndex > index)
                    index = newIndex;
                else
                    index++;
            }

            for (var r = 0; r < allRules.Count; r++)
            {
                var rule = allRules[r];
                tempRanges.FakeClear();
                rule.Finalize(text, tempRanges);

                for (var j = 0; j < tempRanges.Count; j++)
                {
                    ref readonly var range = ref tempRanges[j];
                    ProcessRange(in range);
                    CreateSpanForRule(rule, in range);
                }
            }

            BuildCleanTextAndRemapIndices(text);
        }

        private void ProcessRange(in ParsedRange range)
        {
            if (!range.HasTags) return;

            if (range.IsSelfClosing)
            {
                tagInsertions.Add((range.tagStart, range.tagEnd, range.insertString));
            }
            else
            {
                tagRemovals.Add((range.tagStart, range.tagEnd));
                if (range.closeTagStart != range.closeTagEnd)
                    tagRemovals.Add((range.closeTagStart, range.closeTagEnd));
            }
        }

        private void CreateSpanForRule(IParseRule rule, in ParsedRange range)
        {
            if (ruleToModifier.TryGetValue(rule, out var modifier))
                spans.Add(new AttributeSpan(range.start, range.end, modifier, range.parameter));
        }

        private void BuildCleanTextAndRemapIndices(ReadOnlySpan<char> text)
        {
            tagRemovals.Sort(0, tagRemovals.Count, removalComparer);
            tagInsertions.Sort(0, tagInsertions.Count, insertionComparer);

            var mapSize = text.Length + 1;

            indexMapBuffer.EnsureCapacity(mapSize);
            cleanTextBuffer.EnsureCapacity(text.Length);

            var indexMap = indexMapBuffer.data;

            var offset = 0;
            var removalIdx = 0;
            var insertionIdx = 0;

            for (var i = 0; i <= text.Length; i++)
            {
                while (removalIdx < tagRemovals.Count && i >= tagRemovals[removalIdx].end)
                    removalIdx++;

                if (insertionIdx < tagInsertions.Count && i == tagInsertions[insertionIdx].start)
                {
                    var ins = tagInsertions[insertionIdx];
                    indexMap[i] = cleanTextBuffer.count;
                    AppendToCleanText(ins.insert);
                    offset += ins.end - ins.start - ins.insert.Length;
                    i = ins.end - 1;
                    insertionIdx++;
                    continue;
                }

                if (removalIdx < tagRemovals.Count && i >= tagRemovals[removalIdx].start && i < tagRemovals[removalIdx].end)
                {
                    offset++;
                    indexMap[i] = -1;
                }
                else
                {
                    indexMap[i] = i - offset;
                    if (i < text.Length)
                        cleanTextBuffer[cleanTextBuffer.count++] = text[i];
                }
            }

            RemapSpanIndices(spans, indexMap, mapSize, cleanTextBuffer.count);
        }

        private void AppendToCleanText(string str)
        {
            if (string.IsNullOrEmpty(str)) return;

            cleanTextBuffer.EnsureCapacity(cleanTextBuffer.count + str.Length);
            str.AsSpan().CopyTo(cleanTextBuffer.data.AsSpan(cleanTextBuffer.count));
            cleanTextBuffer.count += str.Length;
        }

        private static void RemapSpanIndices(PooledList<AttributeSpan> spans, int[] indexMap, int mapLength,
            int cleanTextLength)
        {
            for (var i = 0; i < spans.Count; i++)
            {
                ref var span = ref spans[i];

                var startIdx = span.start < 0 ? 0 : span.start >= mapLength ? mapLength - 1 : span.start;
                var endIdx = span.end < 0 ? 0 : span.end >= mapLength ? mapLength - 1 : span.end;

                var newStart = indexMap[startIdx];
                var newEnd = indexMap[endIdx];

                if (newStart < 0)
                {
                    for (var j = startIdx; j < mapLength; j++)
                        if (indexMap[j] >= 0)
                        {
                            newStart = indexMap[j];
                            break;
                        }

                    if (newStart < 0)
                        for (var j = startIdx - 1; j >= 0; j--)
                            if (indexMap[j] >= 0)
                            {
                                newStart = indexMap[j];
                                break;
                            }
                }

                if (newEnd < 0)
                {
                    for (var j = endIdx; j < mapLength; j++)
                        if (indexMap[j] >= 0)
                        {
                            newEnd = indexMap[j];
                            break;
                        }

                    if (newEnd < 0)
                        for (var j = endIdx - 1; j >= 0; j--)
                            if (indexMap[j] >= 0)
                            {
                                newEnd = indexMap[j];
                                break;
                            }
                }

                if (newStart < 0) newStart = 0;
                if (newEnd < 0) newEnd = cleanTextLength;
                if (newEnd > cleanTextLength) newEnd = cleanTextLength;
                if (newStart > newEnd) newStart = newEnd;

                span.start = newStart;
                span.end = newEnd;
            }
        }
    }
}
