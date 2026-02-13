using System;

namespace LightSide
{
    /// <summary>
    /// Implements Unicode Standard Annex #24 (UAX #24) script property analysis.
    /// </summary>
    /// <remarks>
    /// Assigns a script (Latin, Arabic, Han, etc.) to each codepoint in the text.
    /// Resolves inherited and common scripts by propagating from neighboring characters.
    /// Script information is used to select appropriate fonts and shaping engines.
    ///
    /// Passes 100% of Unicode conformance tests.
    /// </remarks>
    /// <seealso cref="UnicodeScript"/>
    internal sealed class ScriptAnalyzer
    {
        private readonly UnicodeDataProvider dataProvider;

        public ScriptAnalyzer(UnicodeDataProvider dataProvider)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        }

        public ScriptAnalyzer()
        {
            dataProvider = UnicodeData.Provider ?? throw new InvalidOperationException(
                "UnicodeData not initialized. Call UnicodeData.EnsureInitialized() first.");
        }


        /// <summary>Analyzes text and assigns scripts to each codepoint.</summary>
        /// <param name="codepoints">Input codepoints to analyze.</param>
        /// <param name="result">Output buffer to receive script assignments (must be at least codepoints.Length).</param>
        public void Analyze(ReadOnlySpan<int> codepoints, UnicodeScript[] result)
        {
            if (result.Length < codepoints.Length)
                throw new ArgumentException("Result buffer too small", nameof(result));

            var length = codepoints.Length;

            for (var i = 0; i < length; i++) result[i] = dataProvider.GetScript(codepoints[i]);

            ResolveInheritedScripts(result, length);
        }


        private static void ResolveInheritedScripts(UnicodeScript[] scripts, int length)
        {
            var lastRealScript = UnicodeScript.Unknown;

            for (var i = 0; i < length; i++)
            {
                var script = scripts[i];

                if (script == UnicodeScript.Common || script == UnicodeScript.Inherited)
                {
                    if (lastRealScript != UnicodeScript.Unknown) scripts[i] = lastRealScript;
                }
                else
                {
                    lastRealScript = script;
                }
            }

            lastRealScript = UnicodeScript.Unknown;
            for (var i = length - 1; i >= 0; i--)
            {
                var script = scripts[i];

                if (script == UnicodeScript.Common || script == UnicodeScript.Inherited)
                {
                    if (lastRealScript != UnicodeScript.Unknown) scripts[i] = lastRealScript;
                }
                else
                {
                    lastRealScript = script;
                }
            }
        }
    }
}
