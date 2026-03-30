using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Analyzes TMP rich text markup — tags that need a Style entry on the component
    /// are left as-is (UniText TagRules are user-configurable, any name works),
    /// only tags with NO possible modifier are stripped.
    /// </summary>
    internal static class RichTextConverter
    {
        public struct ConversionResult
        {
            public string text;
            public List<string> warnings;
            /// <summary>TMP tag names found that need Style entries (modifier + TagRule) on the component.</summary>
            public List<RequiredStyle> requiredStyles;
        }

        /// <summary>A Style entry that the migrator should add to the UniText component.</summary>
        public struct RequiredStyle
        {
            public string tagName;
            public string modifierTypeName;
            public string defaultParameter;
        }

        /// <summary>
        /// TMP tags that have a corresponding UniText modifier.
        /// The tag is left in the text as-is; the migrator adds a Style entry with matching TagRule name.
        /// </summary>
        static readonly Dictionary<string, string> tagToModifier = new(StringComparer.OrdinalIgnoreCase)
        {
            { "cspace",       "LetterSpacingModifier" },
            { "line-spacing", "LineHeightModifier" },
            { "sprite",       "ObjModifier" },
            { "uppercase",    "UppercaseModifier" },
            { "lowercase",    "LowercaseModifier" },
            { "smallcaps",    "SmallCapsModifier" },
        };

        /// <summary>Tags that UniText already handles (built-in or commonly configured).</summary>
        static readonly HashSet<string> builtInTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "i", "u", "s", "color", "size", "sup", "sub",
            "gradient", "link", "outline", "shadow",
            "letter-spacing", "line-height", "upper", "lower",
            "obj", "ellipsis", "var",
        };

        /// <summary>Tags with no possible UniText modifier — stripped with warning.</summary>
        static readonly HashSet<string> unsupportedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "font", "align", "indent", "pos", "space", "voffset",
            "rotate", "width", "noparse", "page", "style", "mark",
            "mspace", "margin",
        };

        static readonly Dictionary<string, string> semanticWarnings = new(StringComparer.OrdinalIgnoreCase)
        {
            { "line-spacing", "TMP line-spacing is additive %. Semantic difference from UniText line-height — verify visually." },
            { "sprite",       "TMP sprite indices may not match UniText <obj> keys." },
        };

        /// <summary>
        /// Analyze TMP rich text. Text is returned UNMODIFIED.
        /// Only collects warnings (unsupported tags) and required styles (tags needing a modifier).
        /// </summary>
        public static ConversionResult Convert(string input)
        {
            if (string.IsNullOrEmpty(input))
                return new ConversionResult { text = input };

            var warnings = new List<string>();
            var requiredStyles = new List<RequiredStyle>();
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;

            while (i < input.Length)
            {
                if (input[i] == '<')
                {
                    var tag = ParseTag(input, i);
                    if (tag.valid)
                    {
                        AnalyzeTag(tag, warnings, requiredStyles, seenTags);
                        i = tag.endIndex;
                        continue;
                    }
                }
                i++;
            }

            return new ConversionResult
            {
                text = input,
                warnings = warnings.Count > 0 ? warnings : null,
                requiredStyles = requiredStyles.Count > 0 ? requiredStyles : null,
            };
        }

        struct TagParseResult
        {
            public bool valid;
            public string tagName;
            public string parameter;
            public int endIndex;
        }

        static TagParseResult ParseTag(string text, int start)
        {
            var result = new TagParseResult();

            if (start >= text.Length || text[start] != '<')
                return result;

            int end = text.IndexOf('>', start);
            if (end < 0)
                return result;

            result.endIndex = end + 1;

            int pos = start + 1;

            if (pos < end && text[pos] == '/')
                pos++;

            int nameStart = pos;
            while (pos < end && text[pos] != '=' && text[pos] != ' ' && text[pos] != '/')
                pos++;

            if (pos == nameStart)
                return result;

            result.tagName = text.Substring(nameStart, pos - nameStart).ToLowerInvariant();

            if (pos < end && text[pos] == '=')
            {
                pos++;
                int paramStart = pos;

                if (pos < end && (text[pos] == '"' || text[pos] == '\''))
                {
                    char quote = text[pos];
                    pos++;
                    paramStart = pos;
                    while (pos < end && text[pos] != quote)
                        pos++;
                    result.parameter = text.Substring(paramStart, pos - paramStart);
                }
                else
                {
                    while (pos < end && text[pos] != '/' && text[pos] != ' ')
                        pos++;
                    result.parameter = text.Substring(paramStart, pos - paramStart);
                }
            }

            result.valid = true;
            return result;
        }

        static void AnalyzeTag(TagParseResult tag, List<string> warnings,
            List<RequiredStyle> requiredStyles, HashSet<string> seenTags)
        {
            var name = tag.tagName;

            if (tagToModifier.TryGetValue(name, out var modifierType))
            {
                if (seenTags.Add(name))
                {
                    requiredStyles.Add(new RequiredStyle
                    {
                        tagName = name,
                        modifierTypeName = modifierType,
                        defaultParameter = tag.parameter,
                    });
                }

                if (semanticWarnings.TryGetValue(name, out var warning))
                {
                    if (!warnings.Contains(warning))
                        warnings.Add(warning);
                }
                return;
            }

            if (builtInTags.Contains(name))
                return;

            if (unsupportedTags.Contains(name))
            {
                var w = tag.parameter != null
                    ? $"<{name}={tag.parameter}> has no UniText modifier — will appear as literal text"
                    : $"<{name}> has no UniText modifier — will appear as literal text";
                if (!warnings.Contains(w))
                    warnings.Add(w);
            }
        }
    }
}
