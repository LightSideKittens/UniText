using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    internal static class EmojiSequenceClassifier
    {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmojiCluster(ReadOnlySpan<int> cluster)
        {
            if (cluster.IsEmpty)
                return false;

            var first = cluster[0];
            var length = cluster.Length;

            if ((uint)first < UnicodeData.EmojiRangeThreshold)
            {
                if (UnicodeData.IsKeycapBase(first))
                    return length >= 2 && IsKeycapSequence(cluster);

                if (length >= 2 && cluster[1] == UnicodeData.VariationSelector16)
                    return true;

                return false;
            }

            if (UnicodeData.IsRegionalIndicator(first))
                return length == 2 && UnicodeData.IsRegionalIndicator(cluster[1]);

            if (UnicodeData.IsInCommonEmojiRange(first))
                return true;

            if (first == UnicodeData.BlackFlagEmoji)
                return IsTagSequence(cluster);

            var provider = UnicodeData.Provider;

            if (provider.IsExtendedPictographic(first))
                return true;

            if (provider.IsEmojiPresentation(first))
                return true;

            if (length > 1 && ContainsZWJ(cluster))
                return true;

            if (length >= 2 && cluster[1] == UnicodeData.VariationSelector16)
                return true;

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKeycapSequence(ReadOnlySpan<int> cluster)
        {
            var len = cluster.Length;

            if (cluster[len - 1] != UnicodeData.CombiningEnclosingKeycap)
                return false;

            return len == 2 || (len == 3 && cluster[1] == UnicodeData.VariationSelector16);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsTagSequence(ReadOnlySpan<int> cluster)
        {
            var len = cluster.Length;

            if (len < 3)
                return false;

            if (cluster[len - 1] != UnicodeData.CancelTag)
                return false;

            for (int i = 1; i < len - 1; i++)
            {
                if (!UnicodeData.IsTagSequenceCodepoint(cluster[i]))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsZWJ(ReadOnlySpan<int> cluster)
        {
            for (int i = 1; i < cluster.Length; i++)
            {
                if (cluster[i] == UnicodeData.ZeroWidthJoiner)
                    return true;
            }
            return false;
        }
    }

}
