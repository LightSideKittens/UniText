using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Utility methods for packing effect layer parameters into vertex UV channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by effect modifiers to encode per-glyph layer parameters (color, offsets)
    /// into float UV values that are unpacked in the shader.
    /// </para>
    /// <para>
    /// Packing formats:
    /// <list type="bullet">
    /// <item><see cref="PackColor"/>: Color32 RGBA → uint → float (bit-reinterpret)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class EffectPacking
    {
        /// <summary>
        /// Packs a Color32 into a single float via bit reinterpretation.
        /// </summary>
        /// <param name="c">The color to pack.</param>
        /// <returns>A float whose bits represent the packed RGBA bytes.</returns>
        /// <remarks>
        /// Layout: R in bits 24–31, G in 16–23, B in 8–15, A in 0–7.
        /// Unpacked in shader with <c>asuint</c> and bit shifts.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PackColor(Color32 c)
        {
            var packed = ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;
            return BitConverter.Int32BitsToSingle(unchecked((int)packed));
        }
    }
}
