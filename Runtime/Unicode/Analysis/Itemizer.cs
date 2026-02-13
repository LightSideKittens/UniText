using System;


namespace LightSide
{
    internal sealed class Itemizer
    {
        public Itemizer()
        {
        }


        public void Itemize(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<byte> bidiLevels,
            UnicodeScript[] scripts,
            UniTextFontProvider fontProvider,
            TextRun[] runs,
            ref int runCount)
        {
            runCount = 0;

            if (codepoints.IsEmpty)
                return;

            var defaultFontId = fontProvider?.MainFontId ?? 0;

            var runStart = 0;
            var currentLevel = bidiLevels[0];
            var currentScript = scripts[0];
            var currentFontId = fontProvider?.FindFontForCodepoint(codepoints[0]) ?? defaultFontId;

            for (var i = 1; i < codepoints.Length; i++)
            {
                bool needBreak = bidiLevels[i] != currentLevel;

                if (scripts[i] != currentScript)
                    needBreak = true;

                var fontId = fontProvider?.FindFontForCodepoint(codepoints[i]) ?? defaultFontId;
                if (fontId != currentFontId)
                    needBreak = true;

                if (needBreak)
                {
                    EnsureCapacity(ref runs, runCount + 1);
                    runs[runCount++] = new TextRun
                    {
                        range = new TextRange(runStart, i - runStart),
                        bidiLevel = currentLevel,
                        script = currentScript,
                        fontId = currentFontId
                    };

                    runStart = i;
                    currentLevel = bidiLevels[i];
                    currentScript = scripts[i];
                    currentFontId = fontId;
                }
            }

            EnsureCapacity(ref runs, runCount + 1);
            runs[runCount++] = new TextRun
            {
                range = new TextRange(runStart, codepoints.Length - runStart),
                bidiLevel = currentLevel,
                script = currentScript,
                fontId = currentFontId
            };
        }

        private static void EnsureCapacity(ref TextRun[] array, int required)
        {
            if (array.Length >= required) return;
            Array.Resize(ref array, Math.Max(required, array.Length * 2));
        }
    }
}
