using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    [Serializable]
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

        public void Finalize(ReadOnlySpan<char> text,PooledList<ParsedRange> results)
        {
            for (var i = 0; i < data.Count; i++)
            {
                var d = data[i];
                if (!RangeEx.TryParse(d.range, out currentRange)) RangeEx.TryParse("..", out currentRange);

                var r = currentRange.GetOffsetAndLength(text.Length);
                var start = r.Offset;
                var end = r.Offset + r.Length;
                results.Add(new ParsedRange(start, end, d.parameter));
            }
        }
    }
}
