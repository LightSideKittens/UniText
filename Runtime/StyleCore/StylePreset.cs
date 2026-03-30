using System;
using UnityEngine;

namespace LightSide
{
    [CreateAssetMenu(fileName = "StylePreset", menuName = "UniText/Style Preset")]
    public class StylePreset : ScriptableObject
    {
    #if UNITY_EDITOR
        public event Action Changed;

        private void OnValidate()
        {
            Changed?.Invoke();
        }
        
        internal void AddStyle_Editor(Style style) 
        { 
            styles.Add(style); 
            OnValidate(); 
        }
        
    #endif

        [SerializeField]
        [Tooltip("Modifier/rule pairs that define how markup is parsed and applied (e.g., color, bold, links).")]
        public StyledList<Style> styles = new();
    }
}
