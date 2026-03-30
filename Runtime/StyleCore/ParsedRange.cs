

namespace LightSide
{
    /// <summary>
    /// Represents a parsed text range with associated tag information.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="IParseRule"/> implementations to communicate which
    /// text regions should be affected by modifiers and where tags should be stripped.
    /// </remarks>
    public struct ParsedRange
    {
        /// <summary>Start index of the content (after opening tag).</summary>
        public int start;
        /// <summary>End index of the content (before closing tag).</summary>
        public int end;
        /// <summary>Start index of the opening tag, or -1 if no tag.</summary>
        public int tagStart;
        /// <summary>End index of the opening tag.</summary>
        public int tagEnd;
        /// <summary>Start index of the closing tag.</summary>
        public int closeTagStart;
        /// <summary>End index of the closing tag.</summary>
        public int closeTagEnd;
        /// <summary>Parameter extracted from the tag (e.g., color value).</summary>
        public string parameter;
        /// <summary>String to insert for self-closing tags.</summary>
        public string insertString;

        /// <summary>Returns true if this range has associated tags.</summary>
        public bool HasTags => tagStart >= 0;
        /// <summary>Returns true if this is a self-closing tag with inserted content.</summary>
        public bool IsSelfClosing => insertString != null;

        /// <summary>Creates a simple range without tag information.</summary>
        public ParsedRange(int start, int end, string parameter)
        {
            this.start = start;
            this.end = end;
            tagStart = -1;
            tagEnd = -1;
            closeTagStart = -1;
            closeTagEnd = -1;
            this.parameter = parameter;
            insertString = null;
        }

        /// <summary>Creates a range with opening and closing tag positions.</summary>
        public ParsedRange(int tagStart, int tagEnd, int closeTagStart, int closeTagEnd, string parameter = null)
        {
            this.tagStart = tagStart;
            this.tagEnd = tagEnd;
            this.closeTagStart = closeTagStart;
            this.closeTagEnd = closeTagEnd;
            this.parameter = parameter;
            insertString = null;
            start = tagEnd;
            end = closeTagStart;
        }

        /// <summary>Creates a self-closing tag range that inserts content.</summary>
        public static ParsedRange SelfClosing(int tagStart, int tagEnd, string insertString, string parameter = null)
        {
            return new ParsedRange
            {
                tagStart = tagStart,
                tagEnd = tagEnd,
                closeTagStart = tagEnd,
                closeTagEnd = tagEnd,
                start = tagStart,
                end = tagStart,
                parameter = parameter,
                insertString = insertString
            };
        }
    }
}
