#nullable enable

namespace LightSide
{
    /// <summary>
    /// Specifies the resolved base direction for bidirectional text.
    /// </summary>
    /// <seealso cref="BidiEngine"/>
    internal enum BidiDirection : byte
    {
        /// <summary>Left-to-right direction (e.g., Latin, Cyrillic, Greek).</summary>
        LeftToRight = 0,

        /// <summary>Right-to-left direction (e.g., Arabic, Hebrew).</summary>
        RightToLeft = 1
    }


    /// <summary>
    /// Unicode Bidirectional Algorithm character classes (UAX #9).
    /// </summary>
    /// <remarks>
    /// Each Unicode character has a BiDi class that determines its behavior
    /// in the bidirectional algorithm. Used internally by <see cref="BidiEngine"/>.
    /// </remarks>
    internal enum BidiClass : byte
    {
        /// <summary>L: Left-to-right (Latin, etc.)</summary>
        LeftToRight,
        /// <summary>R: Right-to-left (Hebrew, etc.)</summary>
        RightToLeft,
        /// <summary>AL: Arabic Letter</summary>
        ArabicLetter,

        /// <summary>EN: European Number (0-9)</summary>
        EuropeanNumber,
        /// <summary>ES: European Separator (+, -)</summary>
        EuropeanSeparator,
        /// <summary>ET: European Terminator (currency symbols)</summary>
        EuropeanTerminator,
        /// <summary>AN: Arabic Number</summary>
        ArabicNumber,
        /// <summary>CS: Common Separator (comma, period)</summary>
        CommonSeparator,
        /// <summary>NSM: Nonspacing Mark (combining characters)</summary>
        NonspacingMark,

        /// <summary>BN: Boundary Neutral (formatting characters)</summary>
        BoundaryNeutral,
        /// <summary>B: Paragraph Separator</summary>
        ParagraphSeparator,
        /// <summary>S: Segment Separator (tab)</summary>
        SegmentSeparator,
        /// <summary>WS: White Space</summary>
        WhiteSpace,
        /// <summary>ON: Other Neutral (most punctuation)</summary>
        OtherNeutral,

        /// <summary>LRE: Left-to-Right Embedding</summary>
        LeftToRightEmbedding,
        /// <summary>LRO: Left-to-Right Override</summary>
        LeftToRightOverride,
        /// <summary>RLE: Right-to-Left Embedding</summary>
        RightToLeftEmbedding,
        /// <summary>RLO: Right-to-Left Override</summary>
        RightToLeftOverride,
        /// <summary>PDF: Pop Directional Format</summary>
        PopDirectionalFormat,

        /// <summary>LRI: Left-to-Right Isolate</summary>
        LeftToRightIsolate,
        /// <summary>RLI: Right-to-Left Isolate</summary>
        RightToLeftIsolate,
        /// <summary>FSI: First Strong Isolate</summary>
        FirstStrongIsolate,
        /// <summary>PDI: Pop Directional Isolate</summary>
        PopDirectionalIsolate
    }


    /// <summary>
    /// Paired bracket type for BiDi bracket matching (UAX #9 rule N0).
    /// </summary>
    internal enum BidiPairedBracketType : byte
    {
        /// <summary>Not a bracket.</summary>
        None,
        /// <summary>Opening bracket: (, [, {, etc.</summary>
        Open,
        /// <summary>Closing bracket: ), ], }, etc.</summary>
        Close
    }


    /// <summary>
    /// Unicode General Category property values.
    /// </summary>
    /// <remarks>
    /// Each codepoint belongs to exactly one general category. Categories are used
    /// for character classification in various algorithms including line breaking.
    /// </remarks>
    internal enum GeneralCategory : byte
    {
        /// <summary>Letter, Uppercase</summary>
        Lu,
        /// <summary>Letter, Lowercase</summary>
        Ll,
        /// <summary>Letter, Titlecase</summary>
        Lt,
        /// <summary>Letter, Modifier</summary>
        Lm,
        /// <summary>Letter, Other</summary>
        Lo,

        /// <summary>Mark, Nonspacing</summary>
        Mn,
        /// <summary>Mark, Spacing Combining</summary>
        Mc,
        /// <summary>Mark, Enclosing</summary>
        Me,

        /// <summary>Number, Decimal Digit</summary>
        Nd,
        /// <summary>Number, Letter</summary>
        Nl,
        /// <summary>Number, Other</summary>
        No,

        /// <summary>Punctuation, Connector</summary>
        Pc,
        /// <summary>Punctuation, Dash</summary>
        Pd,
        /// <summary>Punctuation, Open</summary>
        Ps,
        /// <summary>Punctuation, Close</summary>
        Pe,
        /// <summary>Punctuation, Initial quote</summary>
        Pi,
        /// <summary>Punctuation, Final quote</summary>
        Pf,
        /// <summary>Punctuation, Other</summary>
        Po,

        /// <summary>Symbol, Math</summary>
        Sm,
        /// <summary>Symbol, Currency</summary>
        Sc,
        /// <summary>Symbol, Modifier</summary>
        Sk,
        /// <summary>Symbol, Other</summary>
        So,

        /// <summary>Separator, Space</summary>
        Zs,
        /// <summary>Separator, Line</summary>
        Zl,
        /// <summary>Separator, Paragraph</summary>
        Zp,

        /// <summary>Other, Control</summary>
        Cc,
        /// <summary>Other, Format</summary>
        Cf,
        /// <summary>Other, Surrogate</summary>
        Cs,
        /// <summary>Other, Private Use</summary>
        Co,
        /// <summary>Other, Not Assigned</summary>
        Cn
    }


    /// <summary>
    /// East Asian Width property values (UAX #11).
    /// </summary>
    /// <remarks>
    /// Used in line breaking (UAX #14) to determine character widths for CJK text processing.
    /// </remarks>
    internal enum EastAsianWidth : byte
    {
        /// <summary>Neutral (not East Asian)</summary>
        N,
        /// <summary>Ambiguous (context-dependent width)</summary>
        A,
        /// <summary>Halfwidth</summary>
        H,
        /// <summary>Wide (typically 2 cells)</summary>
        W,
        /// <summary>Fullwidth</summary>
        F,
        /// <summary>Narrow</summary>
        Na
    }


    /// <summary>
    /// Arabic joining type for cursive script shaping.
    /// </summary>
    /// <remarks>
    /// Determines how Arabic and similar script characters connect to adjacent characters.
    /// Used by HarfBuzz for contextual form selection.
    /// </remarks>
    internal enum JoiningType : byte
    {
        /// <summary>Does not join with adjacent characters.</summary>
        NonJoining,
        /// <summary>Transparent to joining (combining marks).</summary>
        Transparent,
        /// <summary>Causes joining on both sides (TATWEEL, ZWJ).</summary>
        JoinCausing,
        /// <summary>Joins only on the left side.</summary>
        LeftJoining,
        /// <summary>Joins only on the right side.</summary>
        RightJoining,
        /// <summary>Joins on both sides (most Arabic letters).</summary>
        DualJoining
    }


    /// <summary>
    /// Arabic joining group for detailed cursive shaping behavior.
    /// </summary>
    /// <remarks>
    /// Characters in the same joining group typically share similar joining behavior.
    /// Used by HarfBuzz for Arabic, Syriac, and similar script shaping.
    /// </remarks>
    internal enum JoiningGroup : byte
    {
        /// <summary>No joining group assigned.</summary>
        NoJoiningGroup,

        AfricanFeh,
        AfricanNoon,
        AfricanQaf,
        Ain,
        Alaph,
        Alef,
        Beh,
        Beth,
        BurushaskiYehBarree,
        Dal,
        DalathRish,
        E,
        FarsiYeh,
        Fe,
        Feh,
        FinalSemkath,
        Gaf,
        Gamal,
        Hah,
        HanifiRohingyaKinnaYa,
        HanifiRohingyaPa,
        He,
        Heh,
        HehGoal,
        Heth,
        Kaf,
        Kaph,
        KashmiriYeh,
        Khaph,
        KnottedHeh,
        Lam,
        Lamadh,

        MalayalamBha,
        MalayalamJa,
        MalayalamLla,
        MalayalamLlla,
        MalayalamNga,
        MalayalamNna,
        MalayalamNnna,
        MalayalamNya,
        MalayalamRa,
        MalayalamSsa,
        MalayalamTta,

        ManichaeanAleph,
        ManichaeanAyin,
        ManichaeanBeth,
        ManichaeanDaleth,
        ManichaeanDhamedh,
        ManichaeanFive,
        ManichaeanGimel,
        ManichaeanHeth,
        ManichaeanHundred,
        ManichaeanKaph,
        ManichaeanLamedh,
        ManichaeanMem,
        ManichaeanNun,
        ManichaeanOne,
        ManichaeanPe,
        ManichaeanQoph,
        ManichaeanResh,
        ManichaeanSadhe,
        ManichaeanSamekh,
        ManichaeanTaw,
        ManichaeanTen,
        ManichaeanTeth,
        ManichaeanThamedh,
        ManichaeanTwenty,
        ManichaeanWaw,
        ManichaeanYodh,
        ManichaeanZayin,

        Meem,
        Mim,
        Noon,
        Nun,
        Nya,
        Pe,
        Qaf,
        Qaph,
        Reh,
        ReversedPe,
        RohingyaYeh,
        Sad,
        Sadhe,
        Seen,
        Semkath,
        Shin,
        StraightWaw,
        SwashKaf,
        SyriacWaw,
        Tah,
        Taw,
        TehMarbuta,
        TehMarbutaGoal,
        HamzaOnHehGoal,
        Teth,
        ThinYeh,
        VerticalTail,
        Waw,
        Yeh,
        YehBarree,
        YehWithTail,
        Yudh,
        YudhHe,
        Zain,
        Zhain
    }


    /// <summary>
    /// Unicode Script property values (UAX #24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each codepoint belongs to exactly one script. Used by <see cref="ScriptAnalyzer"/>
    /// to segment text into runs of uniform script for shaping.
    /// </para>
    /// <para>
    /// Some scripts like <see cref="Common"/> (punctuation) and <see cref="Inherited"/>
    /// (combining marks) can appear in text of any script.
    /// </para>
    /// </remarks>
    public enum UnicodeScript : byte
    {
        /// <summary>Script could not be determined.</summary>
        Unknown = 0,
        /// <summary>Characters used in multiple scripts (punctuation, symbols).</summary>
        Common,
        /// <summary>Combining marks that inherit script from base character.</summary>
        Inherited,

        /// <summary>Latin script (English, Spanish, French, etc.)</summary>
        Latin,
        Greek,
        Cyrillic,
        Armenian,
        Hebrew,
        Arabic,
        Syriac,
        Thaana,
        Devanagari,
        Bengali,
        Gurmukhi,
        Gujarati,
        Oriya,
        Tamil,
        Telugu,
        Kannada,
        Malayalam,
        Sinhala,
        Thai,
        Lao,
        Tibetan,
        Myanmar,
        Georgian,
        Hangul,
        Ethiopic,
        Cherokee,
        CanadianAboriginal,
        Ogham,
        Runic,
        Khmer,
        Mongolian,
        Hiragana,
        Katakana,
        Bopomofo,
        Han,
        Yi,
        OldItalic,
        Gothic,
        Deseret,
        Tagalog,
        Hanunoo,
        Buhid,
        Tagbanwa,
        Limbu,
        TaiLe,
        LinearB,
        Ugaritic,
        Shavian,
        Osmanya,
        Cypriot,
        Braille,
        Buginese,
        Coptic,
        NewTaiLue,
        Glagolitic,
        Tifinagh,
        SylotiNagri,
        OldPersian,
        Kharoshthi,
        Balinese,
        Cuneiform,
        Phoenician,
        PhagsPa,
        Nko,
        Sundanese,
        Lepcha,
        OlChiki,
        Vai,
        Saurashtra,
        KayahLi,
        Rejang,
        Lycian,
        Carian,
        Lydian,
        Cham,
        TaiTham,
        TaiViet,
        Avestan,
        EgyptianHieroglyphs,
        Samaritan,
        Lisu,
        Bamum,
        Javanese,
        MeeteiMayek,
        ImperialAramaic,
        OldSouthArabian,
        InscriptionalParthian,
        InscriptionalPahlavi,
        OldTurkic,
        Kaithi,
        Batak,
        Brahmi,
        Mandaic,
        Chakma,
        MeroiticCursive,
        MeroiticHieroglyphs,
        Miao,
        Sharada,
        SoraSompeng,
        Takri,
        CaucasianAlbanian,
        BassaVah,
        Duployan,
        Elbasan,
        Grantha,
        PahawhHmong,
        Khojki,
        LinearA,
        Mahajani,
        Manichaean,
        MendeKikakui,
        Modi,
        Mro,
        OldNorthArabian,
        Nabataean,
        Palmyrene,
        PauCinHau,
        OldPermic,
        PsalterPahlavi,
        Siddham,
        Khudawadi,
        Tirhuta,
        WarangCiti,
        Ahom,
        AnatolianHieroglyphs,
        Hatran,
        Multani,
        OldHungarian,
        SignWriting,
        Adlam,
        Bhaiksuki,
        Marchen,
        Newa,
        Osage,
        Tangut,
        MasaramGondi,
        Nushu,
        Soyombo,
        ZanabazarSquare,
        Dogra,
        GunjalaGondi,
        Makasar,
        Medefaidrin,
        HanifiRohingya,
        Sogdian,
        OldSogdian,
        Elymaic,
        Nandinagari,
        NyiakengPuachueHmong,
        Wancho,
        Chorasmian,
        DivesAkuru,
        KhitanSmallScript,
        Yezidi,
        CyproMinoan,
        OldUyghur,
        Tangsa,
        Toto,
        Vithkuqi,
        Kawi,
        NagMundari,

        Garay,
        GurungKhema,
        KiratRai,
        OlOnal,
        Sunuwar,
        Todhri,
        TuluTigalari,

        BeriaErfe,
        Sidetic,
        TaiYo,
        TolongSiki
    }


    /// <summary>
    /// Unicode Line Break property values (UAX #14).
    /// </summary>
    /// <remarks>
    /// Determines where line breaks can occur in text. Used by <see cref="LineBreakAlgorithm"/>
    /// to compute break opportunities for word wrapping.
    /// </remarks>
    internal enum LineBreakClass : byte
    {
        /// <summary>Unknown or unassigned.</summary>
        Unknown = 0,

        /// <summary>BK: Mandatory Break (e.g., U+2028 LINE SEPARATOR)</summary>
        BK,
        /// <summary>CR: Carriage Return</summary>
        CR,
        /// <summary>LF: Line Feed</summary>
        LF,
        /// <summary>CM: Combining Mark</summary>
        CM,
        /// <summary>NL: Next Line (U+0085)</summary>
        NL,
        /// <summary>SG: Surrogate (should not appear in valid text)</summary>
        SG,
        /// <summary>WJ: Word Joiner (no break)</summary>
        WJ,
        /// <summary>ZW: Zero Width Space (break opportunity)</summary>
        ZW,
        /// <summary>GL: Non-breaking Glue</summary>
        GL,
        /// <summary>SP: Space</summary>
        SP,
        /// <summary>ZWJ: Zero Width Joiner</summary>
        ZWJ,

        /// <summary>B2: Break Opportunity Before and After</summary>
        B2,
        /// <summary>BA: Break After</summary>
        BA,
        /// <summary>BB: Break Before</summary>
        BB,
        /// <summary>HY: Hyphen</summary>
        HY,
        /// <summary>CB: Contingent Break Opportunity</summary>
        CB,

        /// <summary>CL: Close Punctuation</summary>
        CL,
        /// <summary>CP: Close Parenthesis</summary>
        CP,
        /// <summary>EX: Exclamation/Interrogation</summary>
        EX,
        /// <summary>IN: Inseparable</summary>
        IN,
        /// <summary>NS: Nonstarter</summary>
        NS,
        /// <summary>OP: Open Punctuation</summary>
        OP,
        /// <summary>QU: Quotation</summary>
        QU,

        /// <summary>IS: Infix Numeric Separator</summary>
        IS,
        /// <summary>NU: Numeric</summary>
        NU,
        /// <summary>PO: Postfix Numeric</summary>
        PO,
        /// <summary>PR: Prefix Numeric</summary>
        PR,
        /// <summary>SY: Symbols Allowing Break After</summary>
        SY,

        /// <summary>AI: Ambiguous (Alphabetic or Ideographic)</summary>
        AI,
        /// <summary>AL: Alphabetic</summary>
        AL,
        /// <summary>CJ: Conditional Japanese Starter</summary>
        CJ,
        /// <summary>EB: Emoji Base</summary>
        EB,
        /// <summary>EM: Emoji Modifier</summary>
        EM,
        /// <summary>H2: Hangul LV Syllable</summary>
        H2,
        /// <summary>H3: Hangul LVT Syllable</summary>
        H3,
        /// <summary>HL: Hebrew Letter</summary>
        HL,
        /// <summary>ID: Ideographic</summary>
        ID,
        /// <summary>JL: Hangul L Jamo</summary>
        JL,
        /// <summary>JV: Hangul V Jamo</summary>
        JV,
        /// <summary>JT: Hangul T Jamo</summary>
        JT,
        /// <summary>RI: Regional Indicator</summary>
        RI,
        /// <summary>SA: Complex Context Dependent (South East Asian)</summary>
        SA,
        /// <summary>XX: Unknown</summary>
        XX,

        /// <summary>AK: Aksara</summary>
        AK,
        /// <summary>AP: Aksara Pre-Base</summary>
        AP,
        /// <summary>AS: Aksara Start</summary>
        AS,
        /// <summary>VF: Virama Final</summary>
        VF,
        /// <summary>VI: Virama</summary>
        VI,

        /// <summary>HH: History Hyphen (special)</summary>
        HH
    }


    /// <summary>
    /// Grapheme Cluster Break property values (UAX #29).
    /// </summary>
    /// <remarks>
    /// Used by <see cref="GraphemeBreaker"/> to segment text into grapheme clusters
    /// (user-perceived characters). A grapheme cluster may consist of multiple codepoints
    /// (e.g., base character + combining marks, emoji sequences).
    /// </remarks>
    internal enum GraphemeClusterBreak : byte
    {
        /// <summary>All other characters.</summary>
        Other = 0,
        /// <summary>Carriage Return (U+000D).</summary>
        CR,
        /// <summary>Line Feed (U+000A).</summary>
        LF,
        /// <summary>Control characters and formatting characters.</summary>
        Control,
        /// <summary>Extending characters (combining marks, emoji modifiers).</summary>
        Extend,
        /// <summary>Zero Width Joiner (used in emoji sequences).</summary>
        ZWJ,
        /// <summary>Regional Indicator symbols (for flag emoji).</summary>
        Regional_Indicator,
        /// <summary>Prepend characters (certain Indic vowels).</summary>
        Prepend,
        /// <summary>Spacing combining marks.</summary>
        SpacingMark,
        /// <summary>Hangul syllable type L (Leading consonant).</summary>
        L,
        /// <summary>Hangul syllable type V (Vowel).</summary>
        V,
        /// <summary>Hangul syllable type T (Trailing consonant).</summary>
        T,
        /// <summary>Hangul syllable type LV.</summary>
        LV,
        /// <summary>Hangul syllable type LVT.</summary>
        LVT
    }


    /// <summary>
    /// Indic Conjunct Break property values (UAX #29).
    /// </summary>
    /// <remarks>
    /// Used for extended grapheme cluster segmentation in Indic scripts
    /// to correctly handle conjunct consonants.
    /// </remarks>
    internal enum IndicConjunctBreak : byte
    {
        /// <summary>Not an Indic conjunct break character.</summary>
        None = 0,
        /// <summary>Virama or similar linking character.</summary>
        Linker,
        /// <summary>Consonant character.</summary>
        Consonant,
        /// <summary>Extending character in conjunct context.</summary>
        Extend
    }


    /// <summary>
    /// Interface for accessing Unicode character property data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides access to all Unicode properties required by the text processing algorithms:
    /// BiDi (UAX #9), Script (UAX #24), Line Break (UAX #14), Grapheme Break (UAX #29),
    /// and various other properties.
    /// </para>
    /// <para>
    /// The default implementation is <see cref="BinaryUnicodeDataProvider"/> which loads
    /// data from a precompiled binary file for efficient lookup.
    /// </para>
    /// </remarks>
    /// <seealso cref="UnicodeData.Provider"/>
}
