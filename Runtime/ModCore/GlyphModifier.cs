using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that store per-glyph attribute data.
    /// </summary>
    /// <typeparam name="T">Attribute value type (must be unmanaged).</typeparam>
    /// <remarks>
    /// <para>
    /// GlyphModifier provides automatic buffer management for per-glyph attributes.
    /// Derived classes implement <see cref="GetOnGlyphCallback"/> to receive callbacks
    /// during mesh generation, where they can modify vertex data based on attribute values.
    /// </para>
    /// <para>
    /// Examples of glyph modifiers include: color, bold, italic, underline, strikethrough.
    /// </para>
    /// </remarks>
    /// <seealso cref="BaseModifier"/>
    [Serializable]
    public abstract class GlyphModifier<T> : BaseModifier where T : unmanaged
    {
        /// <summary>The pooled attribute array for per-glyph data.</summary>
        protected PooledArrayAttribute<T> attribute;

        /// <summary>Gets the unique key for this attribute in the buffer system.</summary>
        protected abstract string AttributeKey { get; }
        
        protected sealed override void OnEnable()
        {
            attribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<T>>(AttributeKey);
            var cpCount = buffers.codepoints.count;
            attribute.EnsureCountAndClear(cpCount);
            
            uniText.MeshGenerator.OnGlyph += GetOnGlyphCallback();
        }

        protected sealed override void OnDisable()
        {
            uniText.MeshGenerator.OnGlyph -= GetOnGlyphCallback();
        }

        protected sealed override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKey);
            attribute = null;
        }

        /// <summary>
        /// Called by BaseModifier.Apply(). Ensures buffer capacity and delegates to DoApply.
        /// </summary>
        protected sealed override void OnApply(int start, int end, string parameter)
        {
            DoApply(start, end, parameter);
        }

        /// <summary>
        /// Implements the modifier's effect on the specified range.
        /// Buffer is guaranteed to have sufficient capacity when this is called.
        /// </summary>
        /// <param name="start">Start codepoint index.</param>
        /// <param name="end">End codepoint index (exclusive).</param>
        /// <param name="parameter">Parameter from the tag.</param>
        protected abstract void DoApply(int start, int end, string parameter);

        /// <summary>Returns the callback to invoke during glyph mesh generation.</summary>
        protected abstract Action GetOnGlyphCallback();
    }


    /// <summary>
    /// Extension methods for working with modifier attribute buffers.
    /// </summary>
    public static class ModifierBufferExtensions
    {
        /// <summary>Checks if a byte flag is set at the specified index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlag(this byte[] buffer, int index)
        {
            return buffer != null && (uint)index < (uint)buffer.Length && buffer[index] != 0;
        }

        /// <summary>Checks if any flags are set in the buffer.</summary>
        public static bool HasAnyFlags(this byte[] buffer)
        {
            if (buffer == null) return false;
            var len = buffer.Length;
            var i = 0;
            var limit = len - 7;
            for (; i < limit; i += 8)
                if (buffer[i] != 0 || buffer[i + 1] != 0 || buffer[i + 2] != 0 || buffer[i + 3] != 0 ||
                    buffer[i + 4] != 0 || buffer[i + 5] != 0 || buffer[i + 6] != 0 || buffer[i + 7] != 0)
                    return true;
            for (; i < len; i++)
                if (buffer[i] != 0)
                    return true;
            return false;
        }

        /// <summary>Sets byte flags for a range of indices.</summary>
        public static void SetFlagRange(this byte[] buffer, int start, int end)
        {
            if (buffer == null) return;
            var len = buffer.Length;
            if (start < 0) start = 0;
            if (end > len) end = len;
            for (var i = start; i < end; i++)
                buffer[i] = 1;
        }

        /// <summary>Checks if a uint value is non-zero at the specified index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasValue(this uint[] buffer, int index)
        {
            return buffer != null && (uint)index < (uint)buffer.Length && buffer[index] != 0;
        }

        /// <summary>Gets a uint value or returns 0 if out of bounds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetValueOrDefault(this uint[] buffer, int index)
        {
            if (buffer == null || (uint)index >= (uint)buffer.Length)
                return 0;
            return buffer[index];
        }

        /// <summary>Sets uint values for a range of indices.</summary>
        public static void SetValueRange(this uint[] buffer, int start, int end, uint value)
        {
            if (buffer == null) return;
            var len = buffer.Length;
            if (start < 0) start = 0;
            if (end > len) end = len;
            for (var i = start; i < end; i++)
                buffer[i] = value;
        }
    }

}
