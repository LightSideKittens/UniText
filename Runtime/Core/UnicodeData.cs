using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Provides access to Unicode character properties and the global data provider.
    /// </summary>
    /// <remarks>
    /// Contains common Unicode codepoint constants (whitespace, control characters, BiDi marks)
    /// and helper methods for character classification. The static <see cref="Provider"/> property
    /// gives access to the full Unicode property database for script, line break, and grapheme
    /// cluster information.
    /// </remarks>
    /// <seealso cref="UnicodeDataProvider"/>
    public static class UnicodeData
    {
        #region Unicode Codepoint Constants

        public const int Tab = 0x0009;
        public const int LineFeed = 0x000A;
        public const int VerticalTab = 0x000B;
        public const int FormFeed = 0x000C;
        public const int CarriageReturn = 0x000D;
        public const int Space = 0x0020;
        public const int Hyphen = 0x002D;
        public const int NextLine = 0x0085;

        public const int NoBreakSpace = 0x00A0;
        public const int SoftHyphen = 0x00AD;
        public const int NonBreakingHyphen = 0x2011;

        public const int ZeroWidthSpace = 0x200B;
        public const int ZeroWidthNonJoiner = 0x200C;
        public const int ZeroWidthJoiner = 0x200D;
        public const int WordJoiner = 0x2060;

        public const int LeftToRightMark = 0x200E;
        public const int RightToLeftMark = 0x200F;
        public const int ArabicLetterMark = 0x061C;

        public const int LineSeparator = 0x2028;
        public const int ParagraphSeparator = 0x2029;

        #endregion

        #region BiDi Representative Codepoints

        public const int LatinCapitalA = 0x0041;

        public const int HebrewAlef = 0x05D0;

        public const int PlusSign = 0x002B;
        public const int DollarSign = 0x0024;
        public const int ArabicIndicDigitZero = 0x0660;
        public const int Comma = 0x002C;
        public const int CombiningGraveAccent = 0x0300;

        public const int ExclamationMark = 0x0021;

        public const int LeftToRightEmbedding = 0x202A;
        public const int RightToLeftEmbedding = 0x202B;
        public const int PopDirectionalFormat = 0x202C;
        public const int LeftToRightOverride = 0x202D;
        public const int RightToLeftOverride = 0x202E;

        public const int LeftToRightIsolate = 0x2066;
        public const int RightToLeftIsolate = 0x2067;
        public const int FirstStrongIsolate = 0x2068;
        public const int PopDirectionalIsolate = 0x2069;

        #endregion

        #region Other Constants

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLineBreak(int cp)
        {
            return cp == LineFeed || cp == LineSeparator || cp == ParagraphSeparator;
        }

        /// <summary>
        /// Returns true if the codepoint is a line or paragraph separator that produces
        /// a mandatory break (UAX #14 classes BK, CR, LF, NL) and should not be shaped.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMandatoryBreakChar(int cp)
        {
            return cp == LineFeed
                || cp == CarriageReturn
                || cp == VerticalTab
                || cp == FormFeed
                || cp == NextLine
                || cp == LineSeparator
                || cp == ParagraphSeparator;
        }

        public const int LeftParenthesis = 0x0028;
        public const int RightParenthesis = 0x0029;
        public const int LeftPointingAngleBracket = 0x2329;
        public const int RightPointingAngleBracket = 0x232A;
        public const int LeftAngleBracket = 0x3008;
        public const int RightAngleBracket = 0x3009;

        public const int ArabicLam = 0x0644;
        public const int ArabicAlefMaddaAbove = 0x0622;
        public const int ArabicAlefHamzaAbove = 0x0623;
        public const int ArabicAlefHamzaBelow = 0x0625;
        public const int ArabicAlef = 0x0627;

        public const int ArabicLigatureLamAlefMaddaIsolated = 0xFEF5;
        public const int ArabicLigatureLamAlefMaddaFinal = 0xFEF6;
        public const int ArabicLigatureLamAlefHamzaAboveIsolated = 0xFEF7;
        public const int ArabicLigatureLamAlefHamzaAboveFinal = 0xFEF8;
        public const int ArabicLigatureLamAlefHamzaBelowIsolated = 0xFEF9;
        public const int ArabicLigatureLamAlefHamzaBelowFinal = 0xFEFA;
        public const int ArabicLigatureLamAlefIsolated = 0xFEFB;
        public const int ArabicLigatureLamAlefFinal = 0xFEFC;

        public const int ReplacementCharacter = 0xFFFD;
        public const int DottedCircle = 0x25CC;

        public const int ArabicBlockStart = 0x0600;
        public const int ArabicBlockEnd = 0x06FF;
        public const int ArabicSupplementStart = 0x0750;
        public const int ArabicSupplementEnd = 0x077F;
        public const int ArabicExtendedAStart = 0x08A0;
        public const int ArabicExtendedAEnd = 0x08FF;
        public const int ArabicPresentationFormsAStart = 0xFB50;
        public const int ArabicPresentationFormsAEnd = 0xFDFF;
        public const int ArabicPresentationFormsBStart = 0xFE70;
        public const int ArabicPresentationFormsBEnd = 0xFEFF;

        #endregion

        #region Unicode Range Constants

        public const int MaxBmp = 0xFFFF;

        #endregion

        #region Emoji Constants

        public const int VariationSelector15 = 0xFE0E;
        public const int VariationSelector16 = 0xFE0F;
        public const int CombiningEnclosingKeycap = 0x20E3;
        public const int CombiningEnclosingCircleBackslash = 0x20E0;

        public const int RegionalIndicatorStart = 0x1F1E6;
        public const int RegionalIndicatorEnd = 0x1F1FF;

        public const int EmojiModifierStart = 0x1F3FB;
        public const int EmojiModifierEnd = 0x1F3FF;

        public const int TagSequenceStart = 0xE0020;
        public const int TagSequenceEnd = 0xE007E;
        public const int CancelTag = 0xE007F;
        public const int BlackFlagEmoji = 0x1F3F4;

        public const int NumberSign = 0x0023;
        public const int Asterisk = 0x002A;
        public const int DigitZero = 0x0030;
        public const int DigitNine = 0x0039;

        public const int EmojiRangeThreshold = 0x2000;

        public const int CommonEmojiRangeStart = 0x1F000;
        public const int CommonEmojiRangeSize = 0x1000;

        #endregion

        #region Emoji Helper Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRegionalIndicator(int cp)
        {
            return (uint)(cp - RegionalIndicatorStart) <= (uint)(RegionalIndicatorEnd - RegionalIndicatorStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeycapBase(int cp)
        {
            return cp == NumberSign || cp == Asterisk || (uint)(cp - DigitZero) <= (uint)(DigitNine - DigitZero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmojiModifier(int cp)
        {
            return (uint)(cp - EmojiModifierStart) <= (uint)(EmojiModifierEnd - EmojiModifierStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTagSequenceCodepoint(int cp)
        {
            return (uint)(cp - TagSequenceStart) <= (uint)(TagSequenceEnd - TagSequenceStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInCommonEmojiRange(int cp)
        {
            return (uint)(cp - CommonEmojiRangeStart) < CommonEmojiRangeSize;
        }

        #endregion

        private static UnicodeDataProvider provider;

        internal static UnicodeDataProvider Provider
        {
            get
            {
                EnsureInitialized();
                return provider;
            }
        }


        public static bool IsInitialized => provider != null;


        public static void EnsureInitialized()
        {
            if (IsInitialized)
                return;

            var settings = UniTextSettings.Instance;
            if (settings == null || UniTextSettings.UnicodeDataAsset == null)
            {
                Debug.LogError("UnicodeData: Failed to initialize - UniTextSettings or UnicodeDataAsset is null.");
                return;
            }

            try
            {
                provider = new UnicodeDataProvider(UniTextSettings.UnicodeDataAsset.bytes);
                Cat.Meow("[UnicodeData] Initialized");
            }
            catch (Exception ex)
            {
                Debug.LogError($"UnicodeData: Failed to parse Unicode data: {ex.Message}");
            }
        }
    }
}
