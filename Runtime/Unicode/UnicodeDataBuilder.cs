using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LightSide
{
    /// <summary>
    /// Internal structure for accumulating Unicode properties during data building.
    /// </summary>
    internal struct UnicodeProps
    {
        public BidiClass bidiClass;
        public JoiningType joiningType;
        public JoiningGroup joiningGroup;
        public UnicodeScript script;
        public LineBreakClass lineBreakClass;
        public bool extendedPictographic;
        public bool emojiPresentation;
        public bool emojiModifierBase;
        public GeneralCategory generalCategory;
        public EastAsianWidth eastAsianWidth;
        public GraphemeClusterBreak graphemeClusterBreak;
        public IndicConjunctBreak indicConjunctBreak;
        public bool defaultIgnorable;
    }


    /// <summary>
    /// Entry storing script extension data for codepoints that belong to multiple scripts.
    /// </summary>
    internal struct ScriptExtensionEntry
    {
        public int startCodePoint;
        public int endCodePoint;
        public UnicodeScript[] scripts;

        public ScriptExtensionEntry(int start, int end, UnicodeScript[] scripts)
        {
            startCodePoint = start;
            endCodePoint = end;
            this.scripts = scripts;
        }
    }


    /// <summary>
    /// Editor-time utility for building Unicode property data from UCD text files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parses Unicode Character Database files (DerivedBidiClass.txt, Scripts.txt, etc.)
    /// and compiles them into binary format for runtime use by <see cref="UnicodeDataProvider"/>.
    /// </para>
    /// <para>
    /// This class is only used during asset building and should not be included in runtime builds.
    /// </para>
    /// </remarks>
    /// <seealso cref="UnicodeBinaryWriter"/>
    internal class UnicodeDataBuilder
    {
        private const int MaxCodePoint = 0x10FFFF;
        private const int ScalarCount = MaxCodePoint + 1;

        private readonly UnicodeProps[] props;
        private readonly List<ScriptExtensionEntry> scriptExtensions = new();

        public UnicodeDataBuilder()
        {
            props = new UnicodeProps[ScalarCount];
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            for (var cp = 0; cp < ScalarCount; cp++)
            {
                props[cp].bidiClass = BidiClass.LeftToRight;
                props[cp].joiningType = JoiningType.NonJoining;
                props[cp].joiningGroup = JoiningGroup.NoJoiningGroup;
                props[cp].script = UnicodeScript.Unknown;
                props[cp].lineBreakClass = LineBreakClass.XX;
                props[cp].generalCategory = GeneralCategory.Cn;
                props[cp].eastAsianWidth = EastAsianWidth.N;
                props[cp].indicConjunctBreak = IndicConjunctBreak.None;
            }
        }

        public void LoadDerivedBidiClass(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var classPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || classPart.Length == 0)
                    continue;

                var bidiClass = ParseBidiClass(classPart);

                ParseRangeAndApply(codeRangePart, cp => props[cp].bidiClass = bidiClass);
            }
        }

        public void LoadArabicShaping(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 4)
                    continue;

                var codePart = semi[0].Trim();
                var joiningTypePart = semi[2].Trim();
                var joiningGroupPart = semi[3].Trim();

                if (codePart.Length == 0 || joiningTypePart.Length == 0 || joiningGroupPart.Length == 0)
                    continue;

                var codePoint = ParseHexCodePoint(codePart);
                if (codePoint < 0 || codePoint > MaxCodePoint)
                    continue;

                var joiningType = ParseJoiningType(joiningTypePart);
                var joiningGroup = ParseJoiningGroup(joiningGroupPart);

                props[codePoint].joiningType = joiningType;
                props[codePoint].joiningGroup = joiningGroup;
            }
        }


        public void LoadDerivedJoiningType(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var typePart = semi[1].Trim();

                if (codeRangePart.Length == 0 || typePart.Length == 0)
                    continue;

                var joiningType = ParseJoiningType(typePart);

                ParseRangeAndApply(codeRangePart, cp => props[cp].joiningType = joiningType);
            }
        }

        public void LoadScripts(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var scriptPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || scriptPart.Length == 0)
                    continue;

                var script = ParseScript(scriptPart);

                ParseRangeAndApply(codeRangePart, cp => props[cp].script = script);
            }
        }

        public void LoadLineBreak(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var lbcPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || lbcPart.Length == 0)
                    continue;

                var lbc = ParseLineBreakClass(lbcPart);

                ParseRangeAndApply(codeRangePart, cp => props[cp].lineBreakClass = lbc);
            }
        }


        public void LoadEmojiData(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var propertyPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || propertyPart.Length == 0)
                    continue;

                switch (propertyPart)
                {
                    case "Extended_Pictographic":
                        ParseRangeAndApply(codeRangePart, cp => props[cp].extendedPictographic = true);
                        break;
                    case "Emoji_Presentation":
                        ParseRangeAndApply(codeRangePart, cp => props[cp].emojiPresentation = true);
                        break;
                    case "Emoji_Modifier_Base":
                        ParseRangeAndApply(codeRangePart, cp => props[cp].emojiModifierBase = true);
                        break;
                }
            }
        }


        public void LoadGeneralCategory(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var gcPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || gcPart.Length == 0)
                    continue;

                var gc = ParseGeneralCategory(gcPart);
                ParseRangeAndApply(codeRangePart, cp => props[cp].generalCategory = gc);
            }
        }


        public void LoadEastAsianWidth(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var eawPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || eawPart.Length == 0)
                    continue;

                var eaw = ParseEastAsianWidth(eawPart);
                ParseRangeAndApply(codeRangePart, cp => props[cp].eastAsianWidth = eaw);
            }
        }


        public void LoadGraphemeBreakProperty(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var gcbPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || gcbPart.Length == 0)
                    continue;

                var gcb = ParseGraphemeClusterBreak(gcbPart);
                ParseRangeAndApply(codeRangePart, cp => props[cp].graphemeClusterBreak = gcb);
            }
        }


        public void LoadIndicConjunctBreak(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!line.Contains("InCB"))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 3)
                    continue;

                var codeRangePart = semi[0].Trim();
                var propertyName = semi[1].Trim();
                var valuePart = semi[2].Trim();

                if (propertyName != "InCB")
                    continue;

                if (codeRangePart.Length == 0 || valuePart.Length == 0)
                    continue;

                var incb = ParseIndicConjunctBreak(valuePart);
                if (incb != IndicConjunctBreak.None)
                    ParseRangeAndApply(codeRangePart, cp => props[cp].indicConjunctBreak = incb);
            }
        }


        public void LoadDefaultIgnorable(string path)
        {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!line.Contains("Default_Ignorable_Code_Point"))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var propertyName = semi[1].Trim();

                if (propertyName != "Default_Ignorable_Code_Point")
                    continue;

                if (codeRangePart.Length == 0)
                    continue;

                ParseRangeAndApply(codeRangePart, cp => props[cp].defaultIgnorable = true);
            }
        }


        public void LoadScriptExtensions(string path)
        {
            scriptExtensions.Clear();

            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var hashIdx = line.IndexOf('#');
                if (hashIdx >= 0)
                    line = line.Substring(0, hashIdx);

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var semi = line.Split(';');
                if (semi.Length < 2)
                    continue;

                var codeRangePart = semi[0].Trim();
                var scriptsPart = semi[1].Trim();

                if (codeRangePart.Length == 0 || scriptsPart.Length == 0)
                    continue;

                int start, end;
                if (codeRangePart.Contains(".."))
                {
                    var rangeParts = codeRangePart.Split(new[] { ".." }, StringSplitOptions.None);
                    start = int.Parse(rangeParts[0], NumberStyles.HexNumber);
                    end = int.Parse(rangeParts[1], NumberStyles.HexNumber);
                }
                else
                {
                    start = end = int.Parse(codeRangePart, NumberStyles.HexNumber);
                }

                var scriptNames = scriptsPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var scripts = new List<UnicodeScript>();

                foreach (var name in scriptNames)
                {
                    var script = ParseScriptShortName(name);
                    if (script != UnicodeScript.Unknown)
                        scripts.Add(script);
                }

                if (scripts.Count > 0) scriptExtensions.Add(new ScriptExtensionEntry(start, end, scripts.ToArray()));
            }

            scriptExtensions.Sort((a, b) => a.startCodePoint.CompareTo(b.startCodePoint));
        }


        public IReadOnlyList<ScriptExtensionEntry> GetScriptExtensionEntries()
        {
            return scriptExtensions;
        }

        private static IndicConjunctBreak ParseIndicConjunctBreak(string value)
        {
            return value switch
            {
                "Linker" => IndicConjunctBreak.Linker,
                "Consonant" => IndicConjunctBreak.Consonant,
                "Extend" => IndicConjunctBreak.Extend,
                "None" => IndicConjunctBreak.None,
                _ => IndicConjunctBreak.None
            };
        }

        private static GeneralCategory ParseGeneralCategory(string value)
        {
            return value switch
            {
                "Lu" => GeneralCategory.Lu,
                "Ll" => GeneralCategory.Ll,
                "Lt" => GeneralCategory.Lt,
                "Lm" => GeneralCategory.Lm,
                "Lo" => GeneralCategory.Lo,
                "Mn" => GeneralCategory.Mn,
                "Mc" => GeneralCategory.Mc,
                "Me" => GeneralCategory.Me,
                "Nd" => GeneralCategory.Nd,
                "Nl" => GeneralCategory.Nl,
                "No" => GeneralCategory.No,
                "Pc" => GeneralCategory.Pc,
                "Pd" => GeneralCategory.Pd,
                "Ps" => GeneralCategory.Ps,
                "Pe" => GeneralCategory.Pe,
                "Pi" => GeneralCategory.Pi,
                "Pf" => GeneralCategory.Pf,
                "Po" => GeneralCategory.Po,
                "Sm" => GeneralCategory.Sm,
                "Sc" => GeneralCategory.Sc,
                "Sk" => GeneralCategory.Sk,
                "So" => GeneralCategory.So,
                "Zs" => GeneralCategory.Zs,
                "Zl" => GeneralCategory.Zl,
                "Zp" => GeneralCategory.Zp,
                "Cc" => GeneralCategory.Cc,
                "Cf" => GeneralCategory.Cf,
                "Cs" => GeneralCategory.Cs,
                "Co" => GeneralCategory.Co,
                "Cn" => GeneralCategory.Cn,
                _ => GeneralCategory.Cn
            };
        }

        private static EastAsianWidth ParseEastAsianWidth(string value)
        {
            return value switch
            {
                "N" => EastAsianWidth.N,
                "A" => EastAsianWidth.A,
                "H" => EastAsianWidth.H,
                "W" => EastAsianWidth.W,
                "F" => EastAsianWidth.F,
                "Na" => EastAsianWidth.Na,
                _ => EastAsianWidth.N
            };
        }

        private static GraphemeClusterBreak ParseGraphemeClusterBreak(string value)
        {
            return value switch
            {
                "CR" => GraphemeClusterBreak.CR,
                "LF" => GraphemeClusterBreak.LF,
                "Control" => GraphemeClusterBreak.Control,
                "Extend" => GraphemeClusterBreak.Extend,
                "ZWJ" => GraphemeClusterBreak.ZWJ,
                "Regional_Indicator" => GraphemeClusterBreak.Regional_Indicator,
                "Prepend" => GraphemeClusterBreak.Prepend,
                "SpacingMark" => GraphemeClusterBreak.SpacingMark,
                "L" => GraphemeClusterBreak.L,
                "V" => GraphemeClusterBreak.V,
                "T" => GraphemeClusterBreak.T,
                "LV" => GraphemeClusterBreak.LV,
                "LVT" => GraphemeClusterBreak.LVT,
                _ => GraphemeClusterBreak.Other
            };
        }

        private void ParseRangeAndApply(string codeRangePart, Action<int> apply)
        {
            int rangeStart, rangeEnd;

            var dotsIndex = codeRangePart.IndexOf("..", StringComparison.Ordinal);
            if (dotsIndex >= 0)
            {
                var startHex = codeRangePart.Substring(0, dotsIndex);
                var endHex = codeRangePart.Substring(dotsIndex + 2);

                rangeStart = ParseHexCodePoint(startHex);
                rangeEnd = ParseHexCodePoint(endHex);
            }
            else
            {
                rangeStart = ParseHexCodePoint(codeRangePart);
                rangeEnd = rangeStart;
            }

            if (rangeStart < 0 || rangeEnd < 0 || rangeStart > rangeEnd)
                return;

            if (rangeEnd > MaxCodePoint)
                rangeEnd = MaxCodePoint;

            for (var cp = rangeStart; cp <= rangeEnd; cp++) apply(cp);
        }

        private static string StripComment(string line)
        {
            var hashIndex = line.IndexOf('#');
            if (hashIndex >= 0)
                line = line.Substring(0, hashIndex);
            return line.Trim();
        }

        private static int ParseHexCodePoint(string hex)
        {
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return value;
            return -1;
        }

        private static BidiClass ParseBidiClass(string value)
        {
            return value switch
            {
                "L" => BidiClass.LeftToRight,
                "R" => BidiClass.RightToLeft,
                "AL" => BidiClass.ArabicLetter,
                "EN" => BidiClass.EuropeanNumber,
                "ES" => BidiClass.EuropeanSeparator,
                "ET" => BidiClass.EuropeanTerminator,
                "AN" => BidiClass.ArabicNumber,
                "CS" => BidiClass.CommonSeparator,
                "NSM" => BidiClass.NonspacingMark,
                "BN" => BidiClass.BoundaryNeutral,
                "B" => BidiClass.ParagraphSeparator,
                "S" => BidiClass.SegmentSeparator,
                "WS" => BidiClass.WhiteSpace,
                "ON" => BidiClass.OtherNeutral,
                "LRE" => BidiClass.LeftToRightEmbedding,
                "LRO" => BidiClass.LeftToRightOverride,
                "RLE" => BidiClass.RightToLeftEmbedding,
                "RLO" => BidiClass.RightToLeftOverride,
                "PDF" => BidiClass.PopDirectionalFormat,
                "LRI" => BidiClass.LeftToRightIsolate,
                "RLI" => BidiClass.RightToLeftIsolate,
                "FSI" => BidiClass.FirstStrongIsolate,
                "PDI" => BidiClass.PopDirectionalIsolate,
                _ => throw new InvalidDataException($"Unknown Bidi_Class value '{value}'.")
            };
        }

        private static JoiningType ParseJoiningType(string value)
        {
            return value switch
            {
                "U" => JoiningType.NonJoining,
                "T" => JoiningType.Transparent,
                "C" => JoiningType.JoinCausing,
                "L" => JoiningType.LeftJoining,
                "R" => JoiningType.RightJoining,
                "D" => JoiningType.DualJoining,
                _ => JoiningType.NonJoining
            };
        }

        private static JoiningGroup ParseJoiningGroup(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return JoiningGroup.NoJoiningGroup;

            var parts = value.Trim().Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();

            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    sb.Append(part.Substring(1).ToLowerInvariant());
            }

            var enumName = sb.ToString();

            if (Enum.TryParse<JoiningGroup>(enumName, out var result))
                return result;

            return JoiningGroup.NoJoiningGroup;
        }

        private static UnicodeScript ParseScript(string value)
        {
            var enumName = value.Trim().Replace("_", "");

            if (Enum.TryParse<UnicodeScript>(enumName, true, out var result))
                return result;

            return UnicodeScript.Unknown;
        }


        private static UnicodeScript ParseScriptShortName(string shortName)
        {
            return shortName.Trim() switch
            {
                "Adlm" => UnicodeScript.Adlam,
                "Aghb" => UnicodeScript.CaucasianAlbanian,
                "Ahom" => UnicodeScript.Ahom,
                "Arab" => UnicodeScript.Arabic,
                "Armi" => UnicodeScript.ImperialAramaic,
                "Armn" => UnicodeScript.Armenian,
                "Avst" => UnicodeScript.Avestan,
                "Bali" => UnicodeScript.Balinese,
                "Bamu" => UnicodeScript.Bamum,
                "Bass" => UnicodeScript.BassaVah,
                "Batk" => UnicodeScript.Batak,
                "Beng" => UnicodeScript.Bengali,
                "Bhks" => UnicodeScript.Bhaiksuki,
                "Bopo" => UnicodeScript.Bopomofo,
                "Brah" => UnicodeScript.Brahmi,
                "Brai" => UnicodeScript.Braille,
                "Bugi" => UnicodeScript.Buginese,
                "Buhd" => UnicodeScript.Buhid,
                "Cakm" => UnicodeScript.Chakma,
                "Cans" => UnicodeScript.CanadianAboriginal,
                "Cari" => UnicodeScript.Carian,
                "Cham" => UnicodeScript.Cham,
                "Cher" => UnicodeScript.Cherokee,
                "Chrs" => UnicodeScript.Chorasmian,
                "Copt" => UnicodeScript.Coptic,
                "Cpmn" => UnicodeScript.CyproMinoan,
                "Cprt" => UnicodeScript.Cypriot,
                "Cyrl" => UnicodeScript.Cyrillic,
                "Deva" => UnicodeScript.Devanagari,
                "Diak" => UnicodeScript.DivesAkuru,
                "Dogr" => UnicodeScript.Dogra,
                "Dsrt" => UnicodeScript.Deseret,
                "Dupl" => UnicodeScript.Duployan,
                "Egyp" => UnicodeScript.EgyptianHieroglyphs,
                "Elba" => UnicodeScript.Elbasan,
                "Elym" => UnicodeScript.Elymaic,
                "Ethi" => UnicodeScript.Ethiopic,
                "Gara" => UnicodeScript.Garay,
                "Geor" => UnicodeScript.Georgian,
                "Glag" => UnicodeScript.Glagolitic,
                "Gong" => UnicodeScript.GunjalaGondi,
                "Gonm" => UnicodeScript.MasaramGondi,
                "Goth" => UnicodeScript.Gothic,
                "Gran" => UnicodeScript.Grantha,
                "Grek" => UnicodeScript.Greek,
                "Gujr" => UnicodeScript.Gujarati,
                "Gukh" => UnicodeScript.GurungKhema,
                "Guru" => UnicodeScript.Gurmukhi,
                "Hang" => UnicodeScript.Hangul,
                "Hani" => UnicodeScript.Han,
                "Hano" => UnicodeScript.Hanunoo,
                "Hatr" => UnicodeScript.Hatran,
                "Hebr" => UnicodeScript.Hebrew,
                "Hira" => UnicodeScript.Hiragana,
                "Hluw" => UnicodeScript.AnatolianHieroglyphs,
                "Hmng" => UnicodeScript.PahawhHmong,
                "Hmnp" => UnicodeScript.NyiakengPuachueHmong,
                "Hung" => UnicodeScript.OldHungarian,
                "Ital" => UnicodeScript.OldItalic,
                "Java" => UnicodeScript.Javanese,
                "Kali" => UnicodeScript.KayahLi,
                "Kana" => UnicodeScript.Katakana,
                "Kawi" => UnicodeScript.Kawi,
                "Khar" => UnicodeScript.Kharoshthi,
                "Khmr" => UnicodeScript.Khmer,
                "Khoj" => UnicodeScript.Khojki,
                "Kits" => UnicodeScript.KhitanSmallScript,
                "Knda" => UnicodeScript.Kannada,
                "Krai" => UnicodeScript.KiratRai,
                "Kthi" => UnicodeScript.Kaithi,
                "Lana" => UnicodeScript.TaiTham,
                "Laoo" => UnicodeScript.Lao,
                "Latn" => UnicodeScript.Latin,
                "Lepc" => UnicodeScript.Lepcha,
                "Limb" => UnicodeScript.Limbu,
                "Lina" => UnicodeScript.LinearA,
                "Linb" => UnicodeScript.LinearB,
                "Lisu" => UnicodeScript.Lisu,
                "Lyci" => UnicodeScript.Lycian,
                "Lydi" => UnicodeScript.Lydian,
                "Mahj" => UnicodeScript.Mahajani,
                "Maka" => UnicodeScript.Makasar,
                "Mand" => UnicodeScript.Mandaic,
                "Mani" => UnicodeScript.Manichaean,
                "Marc" => UnicodeScript.Marchen,
                "Medf" => UnicodeScript.Medefaidrin,
                "Mend" => UnicodeScript.MendeKikakui,
                "Merc" => UnicodeScript.MeroiticCursive,
                "Mero" => UnicodeScript.MeroiticHieroglyphs,
                "Mlym" => UnicodeScript.Malayalam,
                "Modi" => UnicodeScript.Modi,
                "Mong" => UnicodeScript.Mongolian,
                "Mroo" => UnicodeScript.Mro,
                "Mtei" => UnicodeScript.MeeteiMayek,
                "Mult" => UnicodeScript.Multani,
                "Mymr" => UnicodeScript.Myanmar,
                "Nagm" => UnicodeScript.NagMundari,
                "Nand" => UnicodeScript.Nandinagari,
                "Narb" => UnicodeScript.OldNorthArabian,
                "Nbat" => UnicodeScript.Nabataean,
                "Newa" => UnicodeScript.Newa,
                "Nkoo" => UnicodeScript.Nko,
                "Nshu" => UnicodeScript.Nushu,
                "Ogam" => UnicodeScript.Ogham,
                "Olck" => UnicodeScript.OlChiki,
                "Onao" => UnicodeScript.OlOnal,
                "Orkh" => UnicodeScript.OldTurkic,
                "Orya" => UnicodeScript.Oriya,
                "Osge" => UnicodeScript.Osage,
                "Osma" => UnicodeScript.Osmanya,
                "Ougr" => UnicodeScript.OldUyghur,
                "Palm" => UnicodeScript.Palmyrene,
                "Pauc" => UnicodeScript.PauCinHau,
                "Perm" => UnicodeScript.OldPermic,
                "Phag" => UnicodeScript.PhagsPa,
                "Phli" => UnicodeScript.InscriptionalPahlavi,
                "Phlp" => UnicodeScript.PsalterPahlavi,
                "Phnx" => UnicodeScript.Phoenician,
                "Plrd" => UnicodeScript.Miao,
                "Prti" => UnicodeScript.InscriptionalParthian,
                "Rjng" => UnicodeScript.Rejang,
                "Rohg" => UnicodeScript.HanifiRohingya,
                "Runr" => UnicodeScript.Runic,
                "Samr" => UnicodeScript.Samaritan,
                "Sarb" => UnicodeScript.OldSouthArabian,
                "Saur" => UnicodeScript.Saurashtra,
                "Sgnw" => UnicodeScript.SignWriting,
                "Shaw" => UnicodeScript.Shavian,
                "Shrd" => UnicodeScript.Sharada,
                "Sidd" => UnicodeScript.Siddham,
                "Sind" => UnicodeScript.Khudawadi,
                "Sinh" => UnicodeScript.Sinhala,
                "Sogd" => UnicodeScript.Sogdian,
                "Sogo" => UnicodeScript.OldSogdian,
                "Sora" => UnicodeScript.SoraSompeng,
                "Soyo" => UnicodeScript.Soyombo,
                "Sund" => UnicodeScript.Sundanese,
                "Sunu" => UnicodeScript.Sunuwar,
                "Sylo" => UnicodeScript.SylotiNagri,
                "Syrc" => UnicodeScript.Syriac,
                "Tagb" => UnicodeScript.Tagbanwa,
                "Takr" => UnicodeScript.Takri,
                "Tale" => UnicodeScript.TaiLe,
                "Talu" => UnicodeScript.NewTaiLue,
                "Taml" => UnicodeScript.Tamil,
                "Tang" => UnicodeScript.Tangut,
                "Tavt" => UnicodeScript.TaiViet,
                "Telu" => UnicodeScript.Telugu,
                "Tfng" => UnicodeScript.Tifinagh,
                "Tglg" => UnicodeScript.Tagalog,
                "Thaa" => UnicodeScript.Thaana,
                "Thai" => UnicodeScript.Thai,
                "Tibt" => UnicodeScript.Tibetan,
                "Tirh" => UnicodeScript.Tirhuta,
                "Tnsa" => UnicodeScript.Tangsa,
                "Todr" => UnicodeScript.Todhri,
                "Toto" => UnicodeScript.Toto,
                "Tutg" => UnicodeScript.TuluTigalari,
                "Ugar" => UnicodeScript.Ugaritic,
                "Vaii" => UnicodeScript.Vai,
                "Vith" => UnicodeScript.Vithkuqi,
                "Wara" => UnicodeScript.WarangCiti,
                "Wcho" => UnicodeScript.Wancho,
                "Xpeo" => UnicodeScript.OldPersian,
                "Xsux" => UnicodeScript.Cuneiform,
                "Yezi" => UnicodeScript.Yezidi,
                "Yiii" => UnicodeScript.Yi,
                "Zanb" => UnicodeScript.ZanabazarSquare,
                "Zinh" => UnicodeScript.Inherited,
                "Zyyy" => UnicodeScript.Common,
                "Zzzz" => UnicodeScript.Unknown,
                _ => UnicodeScript.Unknown
            };
        }

        private static LineBreakClass ParseLineBreakClass(string value)
        {
            if (Enum.TryParse<LineBreakClass>(value.Trim(), out var result))
                return result;

            return LineBreakClass.XX;
        }

        public List<RangeEntry> BuildRangeEntries()
        {
            var result = new List<RangeEntry>();

            var currentStart = 0;
            var current = props[0];

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var p = props[cp];

                var same =
                    p.bidiClass == current.bidiClass &&
                    p.joiningType == current.joiningType &&
                    p.joiningGroup == current.joiningGroup;

                if (!same)
                {
                    result.Add(new RangeEntry(
                        currentStart,
                        cp - 1,
                        current.bidiClass,
                        current.joiningType,
                        current.joiningGroup));

                    currentStart = cp;
                    current = p;
                }
            }

            result.Add(new RangeEntry(
                currentStart,
                MaxCodePoint,
                current.bidiClass,
                current.joiningType,
                current.joiningGroup));

            return result;
        }

        public List<ScriptRangeEntry> BuildScriptRangeEntries()
        {
            var result = new List<ScriptRangeEntry>();

            var currentStart = 0;
            var currentScript = props[0].script;

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var script = props[cp].script;

                if (script != currentScript)
                {
                    result.Add(new ScriptRangeEntry(currentStart, cp - 1, currentScript));
                    currentStart = cp;
                    currentScript = script;
                }
            }

            result.Add(new ScriptRangeEntry(currentStart, MaxCodePoint, currentScript));

            return result;
        }

        public List<LineBreakRangeEntry> BuildLineBreakRangeEntries()
        {
            var result = new List<LineBreakRangeEntry>();

            var currentStart = 0;
            var currentLbc = props[0].lineBreakClass;

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var lbc = props[cp].lineBreakClass;

                if (lbc != currentLbc)
                {
                    result.Add(new LineBreakRangeEntry(currentStart, cp - 1, currentLbc));
                    currentStart = cp;
                    currentLbc = lbc;
                }
            }

            result.Add(new LineBreakRangeEntry(currentStart, MaxCodePoint, currentLbc));

            return result;
        }


        public List<ExtendedPictographicRangeEntry> BuildExtendedPictographicRangeEntries()
        {
            var result = new List<ExtendedPictographicRangeEntry>();

            var currentStart = -1;
            var inRange = false;

            for (var cp = 0; cp <= MaxCodePoint; cp++)
            {
                var ep = props[cp].extendedPictographic;

                if (ep && !inRange)
                {
                    currentStart = cp;
                    inRange = true;
                }
                else if (!ep && inRange)
                {
                    result.Add(new ExtendedPictographicRangeEntry(currentStart, cp - 1));
                    inRange = false;
                }
            }

            if (inRange) result.Add(new ExtendedPictographicRangeEntry(currentStart, MaxCodePoint));

            return result;
        }


        public List<EmojiPresentationRangeEntry> BuildEmojiPresentationRangeEntries()
        {
            var result = new List<EmojiPresentationRangeEntry>();

            var currentStart = -1;
            var inRange = false;

            for (var cp = 0; cp <= MaxCodePoint; cp++)
            {
                var ep = props[cp].emojiPresentation;

                if (ep && !inRange)
                {
                    currentStart = cp;
                    inRange = true;
                }
                else if (!ep && inRange)
                {
                    result.Add(new EmojiPresentationRangeEntry(currentStart, cp - 1));
                    inRange = false;
                }
            }

            if (inRange) result.Add(new EmojiPresentationRangeEntry(currentStart, MaxCodePoint));

            return result;
        }


        public List<EmojiModifierBaseRangeEntry> BuildEmojiModifierBaseRangeEntries()
        {
            var result = new List<EmojiModifierBaseRangeEntry>();

            var currentStart = -1;
            var inRange = false;

            for (var cp = 0; cp <= MaxCodePoint; cp++)
            {
                var emb = props[cp].emojiModifierBase;

                if (emb && !inRange)
                {
                    currentStart = cp;
                    inRange = true;
                }
                else if (!emb && inRange)
                {
                    result.Add(new EmojiModifierBaseRangeEntry(currentStart, cp - 1));
                    inRange = false;
                }
            }

            if (inRange) result.Add(new EmojiModifierBaseRangeEntry(currentStart, MaxCodePoint));

            return result;
        }


        public List<GeneralCategoryRangeEntry> BuildGeneralCategoryRangeEntries()
        {
            var result = new List<GeneralCategoryRangeEntry>();

            var currentStart = 0;
            var currentGc = props[0].generalCategory;

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var gc = props[cp].generalCategory;

                if (gc != currentGc)
                {
                    result.Add(new GeneralCategoryRangeEntry(currentStart, cp - 1, currentGc));
                    currentStart = cp;
                    currentGc = gc;
                }
            }

            result.Add(new GeneralCategoryRangeEntry(currentStart, MaxCodePoint, currentGc));

            return result;
        }


        public List<EastAsianWidthRangeEntry> BuildEastAsianWidthRangeEntries()
        {
            var result = new List<EastAsianWidthRangeEntry>();

            var currentStart = 0;
            var currentEaw = props[0].eastAsianWidth;

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var eaw = props[cp].eastAsianWidth;

                if (eaw != currentEaw)
                {
                    result.Add(new EastAsianWidthRangeEntry(currentStart, cp - 1, currentEaw));
                    currentStart = cp;
                    currentEaw = eaw;
                }
            }

            result.Add(new EastAsianWidthRangeEntry(currentStart, MaxCodePoint, currentEaw));

            return result;
        }


        public List<GraphemeBreakRangeEntry> BuildGraphemeBreakRangeEntries()
        {
            var result = new List<GraphemeBreakRangeEntry>();

            var currentStart = 0;
            var currentGcb = props[0].graphemeClusterBreak;

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var gcb = props[cp].graphemeClusterBreak;

                if (gcb != currentGcb)
                {
                    if (currentGcb != GraphemeClusterBreak.Other)
                        result.Add(new GraphemeBreakRangeEntry(currentStart, cp - 1, currentGcb));
                    currentStart = cp;
                    currentGcb = gcb;
                }
            }

            if (currentGcb != GraphemeClusterBreak.Other)
                result.Add(new GraphemeBreakRangeEntry(currentStart, MaxCodePoint, currentGcb));

            return result;
        }


        public List<IndicConjunctBreakRangeEntry> BuildIndicConjunctBreakRangeEntries()
        {
            var result = new List<IndicConjunctBreakRangeEntry>();

            var currentStart = 0;
            var currentIncb = props[0].indicConjunctBreak;

            for (var cp = 1; cp <= MaxCodePoint; cp++)
            {
                var incb = props[cp].indicConjunctBreak;

                if (incb != currentIncb)
                {
                    if (currentIncb != IndicConjunctBreak.None)
                        result.Add(new IndicConjunctBreakRangeEntry(currentStart, cp - 1, currentIncb));
                    currentStart = cp;
                    currentIncb = incb;
                }
            }

            if (currentIncb != IndicConjunctBreak.None)
                result.Add(new IndicConjunctBreakRangeEntry(currentStart, MaxCodePoint, currentIncb));

            return result;
        }


        public List<DefaultIgnorableRangeEntry> BuildDefaultIgnorableRangeEntries()
        {
            var result = new List<DefaultIgnorableRangeEntry>();

            var currentStart = -1;
            var inRange = false;

            for (var cp = 0; cp <= MaxCodePoint; cp++)
            {
                var di = props[cp].defaultIgnorable;

                if (di && !inRange)
                {
                    currentStart = cp;
                    inRange = true;
                }
                else if (!di && inRange)
                {
                    result.Add(new DefaultIgnorableRangeEntry(currentStart, cp - 1));
                    inRange = false;
                }
            }

            if (inRange) result.Add(new DefaultIgnorableRangeEntry(currentStart, MaxCodePoint));

            return result;
        }

        public static List<MirrorEntry> BuildMirrorEntries(string bidiMirroringPath)
        {
            if (string.IsNullOrEmpty(bidiMirroringPath))
                throw new ArgumentNullException(nameof(bidiMirroringPath));

            var result = new List<MirrorEntry>();

            using var reader = new StreamReader(bidiMirroringPath);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 2)
                    continue;

                var codePart = parts[0].Trim();
                var mirrorPart = parts[1].Trim();

                if (codePart.Length == 0 || mirrorPart.Length == 0)
                    continue;

                var codePoint = ParseHexCodePoint(codePart);
                var mirrored = ParseHexCodePoint(mirrorPart);

                if (codePoint < 0 || codePoint > MaxCodePoint)
                    continue;
                if (mirrored < 0 || mirrored > MaxCodePoint)
                    continue;

                result.Add(new MirrorEntry(codePoint, mirrored));
            }

            result.Sort((a, b) => a.codePoint.CompareTo(b.codePoint));

            return result;
        }

        public static List<BracketEntry> BuildBracketEntries(string bidiBracketsPath)
        {
            if (string.IsNullOrEmpty(bidiBracketsPath))
                throw new ArgumentNullException(nameof(bidiBracketsPath));

            var result = new List<BracketEntry>();

            using var reader = new StreamReader(bidiBracketsPath);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = StripComment(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 3)
                    continue;

                var codePart = parts[0].Trim();
                var pairedPart = parts[1].Trim();
                var typePart = parts[2].Trim();

                if (codePart.Length == 0 || typePart.Length == 0)
                    continue;

                var codePoint = ParseHexCodePoint(codePart);
                if (codePoint < 0 || codePoint > MaxCodePoint)
                    continue;

                int pairedCodePoint;

                if (pairedPart.Length == 0 ||
                    string.Equals(pairedPart, "<none>", StringComparison.OrdinalIgnoreCase))
                {
                    pairedCodePoint = codePoint;
                }
                else
                {
                    pairedCodePoint = ParseHexCodePoint(pairedPart);
                    if (pairedCodePoint < 0 || pairedCodePoint > MaxCodePoint)
                        continue;
                }

                var bracketType = typePart.ToUpperInvariant() switch
                {
                    "O" => BidiPairedBracketType.Open,
                    "C" => BidiPairedBracketType.Close,
                    "N" => BidiPairedBracketType.None,
                    _ => throw new InvalidDataException($"Unknown Bidi_Paired_Bracket_Type '{typePart}'.")
                };

                result.Add(new BracketEntry(codePoint, pairedCodePoint, bracketType));
            }

            result.Sort((a, b) => a.codePoint.CompareTo(b.codePoint));

            return result;
        }
    }


    /// <summary>
    /// Writes compiled Unicode property data to a binary file.
    /// </summary>
    /// <remarks>
    /// Produces binary blobs that can be loaded by <see cref="UnicodeDataProvider"/>.
    /// Editor-time only utility.
    /// </remarks>
    internal static class UnicodeBinaryWriter
    {
        private const uint Magic = 0x554C5452;

        /// <summary>
        /// Writes all Unicode property data to a binary file.
        /// </summary>
        /// <param name="outputPath">Output file path.</param>
        /// <param name="ranges">BiDi and joining property ranges.</param>
        /// <param name="mirrors">BiDi mirror glyph mappings.</param>
        /// <param name="brackets">Paired bracket data.</param>
        /// <param name="scripts">Script property ranges.</param>
        /// <param name="lineBreaks">Line break class ranges.</param>
        /// <param name="extendedPictographics">Extended pictographic ranges.</param>
        /// <param name="generalCategories">General category ranges.</param>
        /// <param name="eastAsianWidths">East Asian width ranges.</param>
        /// <param name="graphemeBreaks">Grapheme cluster break ranges.</param>
        /// <param name="indicConjunctBreaks">Indic conjunct break ranges.</param>
        /// <param name="scriptExtensions">Script extension ranges.</param>
        /// <param name="defaultIgnorables">Default ignorable ranges.</param>
        /// <param name="emojiPresentations">Emoji presentation ranges.</param>
        /// <param name="emojiModifierBases">Emoji modifier base ranges.</param>
        public static void WriteBinary(
            string outputPath,
            IReadOnlyList<RangeEntry> ranges,
            IReadOnlyList<MirrorEntry> mirrors,
            IReadOnlyList<BracketEntry> brackets,
            IReadOnlyList<ScriptRangeEntry> scripts,
            IReadOnlyList<LineBreakRangeEntry> lineBreaks,
            IReadOnlyList<ExtendedPictographicRangeEntry> extendedPictographics,
            IReadOnlyList<GeneralCategoryRangeEntry> generalCategories,
            IReadOnlyList<EastAsianWidthRangeEntry> eastAsianWidths,
            IReadOnlyList<GraphemeBreakRangeEntry> graphemeBreaks,
            IReadOnlyList<IndicConjunctBreakRangeEntry> indicConjunctBreaks,
            IReadOnlyList<ScriptExtensionEntry> scriptExtensions,
            IReadOnlyList<DefaultIgnorableRangeEntry> defaultIgnorables,
            IReadOnlyList<EmojiPresentationRangeEntry> emojiPresentations,
            IReadOnlyList<EmojiModifierBaseRangeEntry> emojiModifierBases)
        {
            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);

            writer.Write(Magic);
            writer.Write(0x110000);

            for (var i = 0; i < 28; i++)
                writer.Write((uint)0);

            var rangeOffset = stream.Position;
            writer.Write((uint)ranges.Count);
            foreach (var r in ranges)
            {
                writer.Write((uint)r.startCodePoint);
                writer.Write((uint)r.endCodePoint);
                writer.Write((byte)r.bidiClass);
                writer.Write((byte)r.joiningType);
                writer.Write((byte)r.joiningGroup);
                writer.Write((byte)0);
            }

            var rangeLength = (uint)(stream.Position - rangeOffset);

            var mirrorOffset = stream.Position;
            writer.Write((uint)mirrors.Count);
            foreach (var m in mirrors)
            {
                writer.Write((uint)m.codePoint);
                writer.Write((uint)m.mirroredCodePoint);
            }

            var mirrorLength = (uint)(stream.Position - mirrorOffset);

            var bracketOffset = stream.Position;
            writer.Write((uint)brackets.Count);
            foreach (var b in brackets)
            {
                writer.Write((uint)b.codePoint);
                writer.Write((uint)b.pairedCodePoint);
                writer.Write((byte)b.bracketType);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var bracketLength = (uint)(stream.Position - bracketOffset);

            var scriptOffset = stream.Position;
            writer.Write((uint)scripts.Count);
            foreach (var s in scripts)
            {
                writer.Write((uint)s.startCodePoint);
                writer.Write((uint)s.endCodePoint);
                writer.Write((byte)s.script);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var scriptLength = (uint)(stream.Position - scriptOffset);

            var lineBreakOffset = stream.Position;
            writer.Write((uint)lineBreaks.Count);
            foreach (var lb in lineBreaks)
            {
                writer.Write((uint)lb.startCodePoint);
                writer.Write((uint)lb.endCodePoint);
                writer.Write((byte)lb.lineBreakClass);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var lineBreakLength = (uint)(stream.Position - lineBreakOffset);

            var extPictOffset = stream.Position;
            writer.Write((uint)extendedPictographics.Count);
            foreach (var ep in extendedPictographics)
            {
                writer.Write((uint)ep.startCodePoint);
                writer.Write((uint)ep.endCodePoint);
            }

            var extPictLength = (uint)(stream.Position - extPictOffset);

            var gcOffset = stream.Position;
            writer.Write((uint)generalCategories.Count);
            foreach (var gc in generalCategories)
            {
                writer.Write((uint)gc.startCodePoint);
                writer.Write((uint)gc.endCodePoint);
                writer.Write((byte)gc.generalCategory);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var gcLength = (uint)(stream.Position - gcOffset);

            var eawOffset = stream.Position;
            writer.Write((uint)eastAsianWidths.Count);
            foreach (var eaw in eastAsianWidths)
            {
                writer.Write((uint)eaw.startCodePoint);
                writer.Write((uint)eaw.endCodePoint);
                writer.Write((byte)eaw.eastAsianWidth);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var eawLength = (uint)(stream.Position - eawOffset);

            var gcbOffset = stream.Position;
            writer.Write((uint)graphemeBreaks.Count);
            foreach (var gcb in graphemeBreaks)
            {
                writer.Write((uint)gcb.startCodePoint);
                writer.Write((uint)gcb.endCodePoint);
                writer.Write((byte)gcb.graphemeBreak);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var gcbLength = (uint)(stream.Position - gcbOffset);

            var incbOffset = stream.Position;
            writer.Write((uint)indicConjunctBreaks.Count);
            foreach (var incb in indicConjunctBreaks)
            {
                writer.Write((uint)incb.startCodePoint);
                writer.Write((uint)incb.endCodePoint);
                writer.Write((byte)incb.indicConjunctBreak);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            var incbLength = (uint)(stream.Position - incbOffset);

            var scxOffset = stream.Position;
            writer.Write((uint)scriptExtensions.Count);
            foreach (var scx in scriptExtensions)
            {
                writer.Write((uint)scx.startCodePoint);
                writer.Write((uint)scx.endCodePoint);
                writer.Write((byte)scx.scripts.Length);
                foreach (var script in scx.scripts) writer.Write((byte)script);

                var totalBytes = 8 + 1 + scx.scripts.Length;
                var padding = (4 - totalBytes % 4) % 4;
                for (var p = 0; p < padding; p++)
                    writer.Write((byte)0);
            }

            var scxLength = (uint)(stream.Position - scxOffset);

            var diOffset = stream.Position;
            writer.Write((uint)defaultIgnorables.Count);
            foreach (var di in defaultIgnorables)
            {
                writer.Write((uint)di.startCodePoint);
                writer.Write((uint)di.endCodePoint);
            }

            var diLength = (uint)(stream.Position - diOffset);

            var epOffset = stream.Position;
            writer.Write((uint)emojiPresentations.Count);
            foreach (var ep in emojiPresentations)
            {
                writer.Write((uint)ep.startCodePoint);
                writer.Write((uint)ep.endCodePoint);
            }

            var epLength = (uint)(stream.Position - epOffset);

            var embOffset = stream.Position;
            writer.Write((uint)emojiModifierBases.Count);
            foreach (var emb in emojiModifierBases)
            {
                writer.Write((uint)emb.startCodePoint);
                writer.Write((uint)emb.endCodePoint);
            }

            var embLength = (uint)(stream.Position - embOffset);

            stream.Position = 8;
            writer.Write((uint)rangeOffset);
            writer.Write(rangeLength);
            writer.Write((uint)mirrorOffset);
            writer.Write(mirrorLength);
            writer.Write((uint)bracketOffset);
            writer.Write(bracketLength);
            writer.Write((uint)scriptOffset);
            writer.Write(scriptLength);
            writer.Write((uint)lineBreakOffset);
            writer.Write(lineBreakLength);
            writer.Write((uint)extPictOffset);
            writer.Write(extPictLength);
            writer.Write((uint)gcOffset);
            writer.Write(gcLength);
            writer.Write((uint)eawOffset);
            writer.Write(eawLength);
            writer.Write((uint)gcbOffset);
            writer.Write(gcbLength);
            writer.Write((uint)incbOffset);
            writer.Write(incbLength);
            writer.Write((uint)scxOffset);
            writer.Write(scxLength);
            writer.Write((uint)diOffset);
            writer.Write(diLength);
            writer.Write((uint)epOffset);
            writer.Write(epLength);
            writer.Write((uint)embOffset);
            writer.Write(embLength);

            writer.Flush();
        }
    }
}
