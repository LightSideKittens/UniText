using System;
using UnityEngine;

namespace LightSide
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EscapeTextAreaAttribute : PropertyAttribute
    {
        public int MinLines { get; }
        public int MaxLines { get; }
        public bool ProcessEscapes { get; }

        public EscapeTextAreaAttribute(bool processEscapes = true)
        {
            MinLines = 3;
            MaxLines = 10;
            ProcessEscapes = processEscapes;
        }

        public EscapeTextAreaAttribute(int minLines, int maxLines, bool processEscapes = true)
        {
            MinLines = minLines;
            MaxLines = maxLines;
            ProcessEscapes = processEscapes;
        }
    }

}
