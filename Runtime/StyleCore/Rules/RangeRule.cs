using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    [Serializable]
    [TypeDescription("Applies the modifier to a fixed character range or the entire text.")]
    public class RangeRule : IParseRule
    {
        [Serializable]
        public struct Data
        {
            public string range;
            public string parameter;
        }

        public List<Data> data = new();
        private Range currentRange;

        public int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            return index;
        }

        public void PostParse(ReadOnlySpan<char> cleanText, PooledList<ParsedRange> results)
        {
            var len = cleanText.Length;
            if (len == 0) return;

            for (var i = 0; i < data.Count; i++)
            {
                var d = data[i];
                if (!RangeEx.TryParse(d.range, out currentRange)) RangeEx.TryParse("..", out currentRange);

                var start = Math.Clamp(currentRange.Start.GetOffset(len), 0, len);
                var end = Math.Clamp(currentRange.End.GetOffset(len), 0, len);
                if (start >= end) continue;

                results.Add(new ParsedRange(start, end, d.parameter));
            }
        }
    }
}
