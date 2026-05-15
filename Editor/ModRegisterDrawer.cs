using UnityEditor;
using UnityEngine;
using LightSide;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(ModRegister))]
    internal class ModRegisterDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsArrayElement(property))
            {
                label = new GUIContent(GetCustomLabel(property));
            }

            EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        private static bool IsArrayElement(SerializedProperty property)
        {
            return property.propertyPath.EndsWith("]");
        }

        private static string GetCustomLabel(SerializedProperty property)
        {
            var modifierProp = property.FindPropertyRelative("modifier");
            var ruleProp = property.FindPropertyRelative("rule");

            var modifierName = GetTypeName(modifierProp);
            var ruleName = GetTypeName(ruleProp);

            if (string.IsNullOrEmpty(modifierName) && string.IsNullOrEmpty(ruleName))
                return "(Empty)";

            if (string.IsNullOrEmpty(modifierName))
                return $"?({ruleName})";

            if (string.IsNullOrEmpty(ruleName))
                return $"{modifierName}(?)";

            return $"{modifierName} (Rule: {ruleName})";
        }

        private static string GetTypeName(SerializedProperty prop)
        {
            if (prop == null || prop.managedReferenceValue == null)
                return null;

            var type = prop.managedReferenceValue.GetType();
            var name = type.Name;

            if (name.EndsWith("Modifier"))
                return name.Substring(0, name.Length - 8);
            if (name.EndsWith("ParseRule"))
                return name.Substring(0, name.Length - 9);
            if (name.EndsWith("Rule"))
                return name.Substring(0, name.Length - 4);

            return name;
        }
    }

}
