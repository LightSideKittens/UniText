using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Combines multiple parse rules into a single rule.</summary>
    /// <remarks>
    /// Tries each child rule in order until one matches. Useful for grouping
    /// related rules or creating reusable rule sets.
    /// </remarks>
    [Serializable]
    [TypeGroup("Utility", 3)]
    public sealed class CompositeParseRule : IParseRule
    {
        /// <summary>Child rules to apply in order.</summary>
        [Tooltip("Child rules to apply in order.")]
        public TypedList<IParseRule> rules = new();

        public int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null) continue;

                var result = rule.TryMatch(text, index, results);
                if (result > index) return result;
            }

            return index;
        }

        public void Finalize(ReadOnlySpan<char> text,PooledList<ParsedRange> results)
        {
            for (var i = 0; i < rules.Count; i++)
                rules[i]?.Finalize(text, results);
        }

        public void Reset()
        {
            for (var i = 0; i < rules.Count; i++)
                rules[i]?.Reset();
        }
    }

}
