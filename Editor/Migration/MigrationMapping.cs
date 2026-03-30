using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LightSide
{
    /// <summary>
    /// Static, readonly mapping tables for TMP → UniText migration.
    /// Single source of truth for all correspondences.
    /// </summary>
    internal static class MigrationMapping
    {
        public const string TmpTextUiGuid     = "f4688fdb7df04437aeb418b961361dc5";
        public const string TmpText3dGuid     = "9541d86e2fd84c1d9990edf0852d74ab";
        public const string TmpInputFieldGuid = "2da0c512f12947e489f739169773d7ca";
        public const string TmpSubMeshUiGuid  = "058cba836c1846c3aa1c5fd2e28aea77";
        public const string TmpSubMeshGuid    = "07994bfe8b0e4adb97d706de5dea48d5";
        public const string TmpFontAssetGuid  = "71c1514a6bd24e1e882cebbe1904ce04";
        public const string TmpSpriteAssetGuid = "84a92b25f83d49b9bc132d206b370281";
        public const string TmpStyleSheetGuid = "ab2114bdc8544297b417dfefe9f1e410";
        public const string TmpSettingsGuid   = "2705215ac5b84b70bacc50632be6e391";
        public const string TmpDropdownGuid   = "7b743370ac3e4ec2a1668f5455a8ef8a";

        public const string UniTextGuid           = "beaa34cb0e58d624bb3a264b28600785";
        public const string UniTextWorldGuid      = "f82394fefa9244d49e439daa1fb85977";
        public const string UniTextInputFieldGuid = "ef56a43311727924f90db00586705ff6";
        public const string UniTextFontGuid       = "f5c059c895b0b3446b609a9e8122a187";
        public const string UniTextFontStackGuid  = "45ecf69df938478886267ac0693c4369";
        public const string UniTextSettingsGuid   = "c1f8983beb12bb84796c28939f4596ab";
        public const string StylePresetGuid       = "6c462a215d824769a04b6e8060809d3b";

        public static readonly Dictionary<string, string> ComponentGuidMap = new()
        {
            { TmpTextUiGuid,     UniTextGuid },
            { TmpText3dGuid,     UniTextWorldGuid },
            { TmpInputFieldGuid, UniTextInputFieldGuid },
        };

        /// <summary>GUIDs of TMP sub-mesh components that should be removed after migration.</summary>
        public static readonly HashSet<string> SubMeshGuids = new()
        {
            TmpSubMeshUiGuid,
            TmpSubMeshGuid,
        };

        /// <summary>All TMP component GUIDs we scan for in scenes/prefabs.</summary>
        public static readonly HashSet<string> AllTmpComponentGuids = new()
        {
            TmpTextUiGuid, TmpText3dGuid, TmpInputFieldGuid,
            TmpSubMeshUiGuid, TmpSubMeshGuid, TmpDropdownGuid,
        };

        /// <summary>All TMP asset GUIDs we scan for in .asset files.</summary>
        public static readonly HashSet<string> TmpAssetGuids = new()
        {
            TmpFontAssetGuid, TmpSpriteAssetGuid, TmpStyleSheetGuid, TmpSettingsGuid,
        };

        public static readonly Dictionary<string, string> TmpGuidToName = new()
        {
            { TmpTextUiGuid,      "TextMeshProUGUI" },
            { TmpText3dGuid,      "TextMeshPro (3D)" },
            { TmpInputFieldGuid,  "TMP_InputField" },
            { TmpSubMeshUiGuid,   "TMP_SubMeshUI" },
            { TmpSubMeshGuid,     "TMP_SubMesh" },
            { TmpFontAssetGuid,   "TMP_FontAsset" },
            { TmpSpriteAssetGuid, "TMP_SpriteAsset" },
            { TmpStyleSheetGuid,  "TMP_StyleSheet" },
            { TmpSettingsGuid,    "TMP_Settings" },
            { TmpDropdownGuid,    "TMP_Dropdown" },
        };

        public static readonly Dictionary<string, string> UniTextGuidToName = new()
        {
            { UniTextGuid,           "UniText" },
            { UniTextWorldGuid,      "UniTextWorld" },
            { UniTextInputFieldGuid, "UniTextInputField" },
        };

        public static string GetTmpName(string guid) =>
            TmpGuidToName.TryGetValue(guid, out var n) ? n : guid;

        public static string GetTargetName(string tmpGuid) =>
            ComponentGuidMap.TryGetValue(tmpGuid, out var uniGuid)
                ? (UniTextGuidToName.TryGetValue(uniGuid, out var n) ? n : uniGuid)
                : "(none)";

        public struct PropertyMapping
        {
            public string tmpField;
            public string uniTextField;
            /// <summary>Null = direct copy. Non-null = special conversion needed.</summary>
            public string conversionNote;
        }

        public static readonly PropertyMapping[] DirectPropertyMappings =
        {
            new() { tmpField = "m_text",             uniTextField = "text",                conversionNote = "RichTextConverter" },
            new() { tmpField = "m_fontSizeBase",     uniTextField = "fontSize",            conversionNote = null },
            new() { tmpField = "m_fontColor",        uniTextField = "m_Color",             conversionNote = null },
            new() { tmpField = "m_enableAutoSizing", uniTextField = "autoSize",            conversionNote = null },
            new() { tmpField = "m_fontSizeMin",      uniTextField = "minFontSize",         conversionNote = null },
            new() { tmpField = "m_fontSizeMax",      uniTextField = "maxFontSize",         conversionNote = null },
        };

        public static (int horizontal, int vertical, string warning) DecomposeAlignment(int tmpAlignment)
        {
            int hBits = tmpAlignment & 0xFF;
            int vBits = (tmpAlignment >> 8) & 0xFF;

            int h;
            string warning = null;

            switch (hBits)
            {
                case 0x01: h = 0; break;
                case 0x02: h = 1; break;
                case 0x04: h = 2; break;
                case 0x08: h = 0; warning = "Justified alignment has no UniText equivalent, defaulted to Left"; break;
                case 0x10: h = 0; warning = "Flush alignment has no UniText equivalent, defaulted to Left"; break;
                case 0x20: h = 0; warning = "Geometry alignment has no UniText equivalent, defaulted to Left"; break;
                default:   h = 0; break;
            }

            int v;
            switch (vBits)
            {
                case 0x01: v = 0; break;
                case 0x02: v = 1; break;
                case 0x04: v = 2; break;
                case 0x08: v = 0; warning = (warning != null ? warning + "; " : "") + "Baseline vertical alignment has no UniText equivalent, defaulted to Top"; break;
                case 0x10: v = 0; warning = (warning != null ? warning + "; " : "") + "Midline vertical alignment has no UniText equivalent, defaulted to Top"; break;
                case 0x20: v = 0; warning = (warning != null ? warning + "; " : "") + "Capline vertical alignment has no UniText equivalent, defaulted to Top"; break;
                default:   v = 0; break;
            }

            return (h, v, warning);
        }

        public struct FontStyleMapping
        {
            public int flag;
            public string modifierTypeName;
            public string tagName;
        }

        public static readonly FontStyleMapping[] FontStyleMappings =
        {
            new() { flag = 1,  modifierTypeName = "BoldModifier",          tagName = "b" },
            new() { flag = 2,  modifierTypeName = "ItalicModifier",        tagName = "i" },
            new() { flag = 4,  modifierTypeName = "UnderlineModifier",     tagName = "u" },
            new() { flag = 8,  modifierTypeName = "LowercaseModifier",     tagName = "lower" },
            new() { flag = 16, modifierTypeName = "UppercaseModifier",     tagName = "upper" },
            new() { flag = 32, modifierTypeName = "SmallCapsModifier",     tagName = "smallcaps" },
            new() { flag = 64, modifierTypeName = "StrikethroughModifier", tagName = "s" },
        };

        public static bool ConvertWordWrap(int tmpWrappingMode) => tmpWrappingMode == 1 || tmpWrappingMode == 2;

        public static readonly Dictionary<int, string> ContentTypeToValidator = new()
        {
            { 2, "IntegerValidator" },
            { 3, "DecimalValidator" },
            { 4, "AlphanumericValidator" },
            { 5, "NameValidator" },
            { 6, "EmailValidator" },
        };

        public struct ScriptPattern
        {
            public Regex regex;
            public string replacement;
            /// <summary>If true, this is a warning-only pattern (no auto-replacement).</summary>
            public bool warningOnly;
            public string warningMessage;
        }

        public static readonly ScriptPattern[] ScriptPatterns = BuildScriptPatterns();

        static ScriptPattern[] BuildScriptPatterns()
        {
            var patterns = new List<ScriptPattern>();

            patterns.Add(Pat(@"using\s+TMPro\s*;", "using LightSide;"));

            patterns.Add(Pat(@"\bTextMeshProUGUI\b", "UniText"));
            patterns.Add(Pat(@"\bTextMeshPro\b", "UniTextWorld"));
            patterns.Add(Pat(@"\bTMP_Text\b", "UniTextBase"));
            patterns.Add(Pat(@"\bTMP_InputField\b", "UniTextInputField"));
            patterns.Add(Pat(@"\bTMP_FontAsset\b", "UniTextFont"));

            patterns.Add(Warn(@"\bTMP_Dropdown\b",    "TMP_Dropdown has no UniText equivalent. Use Unity's standard Dropdown with UniText."));
            patterns.Add(Warn(@"\bTMP_SpriteAsset\b", "TMP_SpriteAsset has no UniText equivalent. Use <obj> tag system."));
            patterns.Add(Warn(@"\bTMP_StyleSheet\b",  "TMP_StyleSheet has no UniText equivalent. Use StylePreset."));

            patterns.Add(Warn(@"(?<=\.)text\b(?!\s*[(\[])",      ".text → .Text (verify this is a TMP variable)"));
            patterns.Add(Warn(@"(?<=\.)fontSize\b",              ".fontSize → .FontSize (verify this is a TMP variable)"));
            patterns.Add(Pat(@"(?<=\.)enableWordWrapping\b", "WordWrap"));
            patterns.Add(Pat(@"(?<=\.)enableAutoSizing\b", "AutoSize"));
            patterns.Add(Pat(@"(?<=\.)fontSizeMin\b", "MinFontSize"));
            patterns.Add(Pat(@"(?<=\.)fontSizeMax\b", "MaxFontSize"));
            patterns.Add(Pat(@"(?<=\.)isRightToLeftText\b", "BaseDirection"));

            patterns.Add(Warn(@"(?<=\.)onValueChanged\b", ".onValueChanged → .ValueChanged (verify this is a TMP variable)"));
            patterns.Add(Warn(@"(?<=\.)onSubmit\b",       ".onSubmit → .Submit (verify this is a TMP variable)"));
            patterns.Add(Pat(@"(?<=\.)onEndEdit\b", "EditEnded"));
            patterns.Add(Warn(@"(?<=\.)onSelect\b",       ".onSelect → .Focused (verify this is a TMP variable)"));
            patterns.Add(Warn(@"(?<=\.)onDeselect\b",     ".onDeselect → .Defocused (verify this is a TMP variable)"));

            patterns.Add(Warn(@"(?<=\.)fontAsset\b",         "fontAsset → use FontStack property with UniTextFontStack"));
            patterns.Add(Warn(@"(?<=\.)alignment\b",         "alignment needs decomposition into HorizontalAlignment + VerticalAlignment"));
            patterns.Add(Warn(@"(?<=\.)characterSpacing\b",  "characterSpacing → use LetterSpacingModifier"));
            patterns.Add(Warn(@"(?<=\.)lineSpacing\b",       "lineSpacing → use LineHeightModifier"));
            patterns.Add(Warn(@"(?<=\.)overflowMode\b",      "overflowMode → use EllipsisModifier for ellipsis"));

            patterns.Add(Warn(@"\bTextAlignmentOptions\.\w+", "TextAlignmentOptions needs decomposition into HorizontalAlignment + VerticalAlignment"));
            patterns.Add(Warn(@"\bFontStyles\.\w+",           "FontStyles flags → use BoldModifier/ItalicModifier/etc."));

            return patterns.ToArray();
        }

        static ScriptPattern Pat(string pattern, string replacement)
        {
            return new ScriptPattern
            {
                regex = new Regex(pattern, RegexOptions.Compiled),
                replacement = replacement,
                warningOnly = false,
            };
        }

        static ScriptPattern Warn(string pattern, string warning)
        {
            return new ScriptPattern
            {
                regex = new Regex(pattern, RegexOptions.Compiled),
                replacement = null,
                warningOnly = true,
                warningMessage = warning,
            };
        }

        public static readonly string[] TmpShaderPrefixes =
        {
            "TextMeshPro/",
            "TMPro/",
            "Hidden/TextMeshPro/",
            "Hidden/TMPro/",
        };

        /// <summary>
        /// Tags that are truly unsupported in UniText (no modifier exists).
        /// Tags like cspace, sprite, uppercase etc. are NOT here — they work via TagRule with matching name.
        /// </summary>
        public static readonly string[] UnsupportedTagPatterns =
        {
            "<font=",
            "<font ",
            "<align=",
            "<indent=",
            "<pos=",
            "<space=",
            "<voffset=",
            "<rotate=",
            "<width=",
            "<noparse>",
            "<page>",
            "<style=",
            "<mark=",
            "<mspace=",
            "<margin=",
        };

        /// <summary>
        /// Tags that have a UniText modifier equivalent — these work as-is if a matching Style entry exists.
        /// Used by the scanner to flag text content that needs Style entries on the component.
        /// </summary>
        public static readonly Dictionary<string, string> TagsNeedingStyleEntry = new(StringComparer.OrdinalIgnoreCase)
        {
            { "cspace",       "LetterSpacingModifier" },
            { "line-spacing", "LineHeightModifier" },
            { "sprite",       "ObjModifier" },
            { "uppercase",    "UppercaseModifier" },
            { "lowercase",    "LowercaseModifier" },
            { "smallcaps",    "SmallCapsModifier" },
        };

        public struct MaterialRecipe
        {
            public string shaderProperty;
            public string description;
            public string uniTextEquivalent;
        }

        public static readonly MaterialRecipe[] MaterialRecipes =
        {
            new() { shaderProperty = "_OutlineColor",   description = "Outline color",   uniTextEquivalent = "OutlineModifier or <outline=width,#color>" },
            new() { shaderProperty = "_OutlineWidth",   description = "Outline width",   uniTextEquivalent = "OutlineModifier or <outline=width,#color>" },
            new() { shaderProperty = "_UnderlayColor",  description = "Shadow/underlay",  uniTextEquivalent = "ShadowModifier or <shadow=x,y,#color,softness>" },
            new() { shaderProperty = "_UnderlayOffsetX", description = "Shadow offset X", uniTextEquivalent = "ShadowModifier or <shadow=x,y,#color,softness>" },
            new() { shaderProperty = "_UnderlayOffsetY", description = "Shadow offset Y", uniTextEquivalent = "ShadowModifier or <shadow=x,y,#color,softness>" },
            new() { shaderProperty = "_GlowColor",      description = "Glow effect",     uniTextEquivalent = "No direct equivalent. Use OutlineModifier with wide, soft settings." },
        };

        public static readonly Dictionary<string, string> AnimationPropertyMap = new()
        {
            { "m_fontColor",   "m_Color" },
            { "m_fontColor.r", "m_Color.r" },
            { "m_fontColor.g", "m_Color.g" },
            { "m_fontColor.b", "m_Color.b" },
            { "m_fontColor.a", "m_Color.a" },
            { "m_fontSize",    "fontSize" },
            { "m_fontSizeBase","fontSize" },
        };
    }
}
