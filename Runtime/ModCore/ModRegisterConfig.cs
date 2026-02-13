using System;
using UnityEngine;

namespace LightSide
{
    [CreateAssetMenu(fileName = "ModRegisterConfig", menuName = "UniText/Mod Register Config")]
    public class ModRegisterConfig : ScriptableObject
    {
    #if UNITY_EDITOR
        public event Action Changed;

        private void OnValidate()
        {
            Changed?.Invoke();
        }
    #endif

        [SerializeField]
        [Tooltip("Modifier/rule pairs that define how markup is parsed and applied (e.g., color, bold, links).")]
        public StyledList<ModRegister> modRegisters = new();
    }
}
