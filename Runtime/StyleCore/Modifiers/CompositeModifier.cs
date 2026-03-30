using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Combines multiple modifiers into a single modifier.</summary>
    /// <remarks>
    /// <para>
    /// Splits the tag parameter by <c>;</c> and passes each segment to the corresponding child modifier.
    /// For example, <c>&lt;wobble-color=3,10,1;#FF0000&gt;</c> passes <c>"3,10,1"</c> to the first modifier
    /// and <c>"#FF0000"</c> to the second.
    /// </para>
    /// <para>
    /// Child modifiers with no corresponding segment (fewer <c>;</c> groups than modifiers) receive
    /// a null parameter and fall back to their defaults.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Utility", 10)]
    [TypeDescription("Combines multiple modifiers into one, splitting parameters by ';'.")]
    public sealed class CompositeModifier : BaseModifier
    {
        /// <summary>Child modifiers to apply in order.</summary>
        [Tooltip("Child modifiers to apply in order. Parameters are split by ';'.")]
        public TypedList<BaseModifier> modifiers = new();

        public override void PrepareForParallel()
        {
            for (var i = 0; i < modifiers.Count; i++)
                modifiers[i]?.PrepareForParallel();
        }

        protected override void OnEnable()
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers[i];
                if (mod == null) continue;
                mod.SetOwner(uniText);
                if (mod.IsInitialized) mod.Disable();
                mod.Prepare();
            }
        }

        protected override void OnDisable() { }

        protected override void OnDestroy()
        {
            for (var i = 0; i < modifiers.Count; i++)
                modifiers[i]?.Destroy();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var count = modifiers.Count;
            if (count == 0) return;

            if (string.IsNullOrEmpty(parameter))
            {
                for (var i = 0; i < count; i++)
                    modifiers[i]?.Apply(start, end, null);
                return;
            }

            var span = parameter.AsSpan();
            var modIndex = 0;

            while (modIndex < count)
            {
                var sepIdx = span.IndexOf(';');
                ReadOnlySpan<char> segment;

                if (sepIdx < 0)
                {
                    segment = span;
                    span = ReadOnlySpan<char>.Empty;
                }
                else
                {
                    segment = span.Slice(0, sepIdx);
                    span = span.Slice(sepIdx + 1);
                }

                var mod = modifiers[modIndex];
                if (mod != null)
                {
                    var segStr = segment.IsEmpty ? null : segment.ToString();
                    mod.Apply(start, end, segStr);
                }

                modIndex++;
                if (sepIdx < 0) break;
            }

            for (var i = modIndex; i < count; i++)
                modifiers[i]?.Apply(start, end, null);
        }
    }
}
