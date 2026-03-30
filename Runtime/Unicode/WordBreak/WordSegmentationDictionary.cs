using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Self-contained dictionary asset for word segmentation of a specific script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each asset holds a binary double-array trie and the target Unicode script.
    /// Add to UniText Settings to enable word segmentation for that script.
    /// </para>
    /// <para>
    /// Create via Tools → UniText → Tools → Dictionary Builder tab.
    /// </para>
    /// </remarks>
    public sealed class WordSegmentationDictionary : ScriptableObject
    {
        [SerializeField] internal UnicodeScript script;
        [SerializeField, HideInInspector] internal byte[] trieData;

        /// <summary>The Unicode script this dictionary targets.</summary>
        public UnicodeScript Script => script;

        /// <summary>Returns true if this dictionary contains valid trie data.</summary>
        public bool IsValid => trieData != null && trieData.Length >= 12;
    }
}
