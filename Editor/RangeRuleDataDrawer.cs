using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(RangeRule.Data))]
    internal class RangeRuleDataDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var rangeProp = property.FindPropertyRelative("range");
            var paramProp = property.FindPropertyRelative("parameter");

            var y = position.y;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), rangeProp);
            y += lineHeight + spacing;

            var modifierProp = ParameterFieldUtility.FindModifierProperty(property);
            var modType = modifierProp?.managedReferenceValue?.GetType();

            if (modType == typeof(CompositeModifier))
            {
                var entries = ParameterFieldUtility.GetCompositeEntries(modifierProp);
                if (entries != null && entries.Length > 0)
                {
                    EnsureDefault(paramProp, ParameterFieldUtility.BuildCompositeDefault(entries));
                    ParameterFieldUtility.DrawCompositeSegments(
                        position.x, position.width, y, entries, paramProp);
                }
                return;
            }

            var fields = modType != null ? ParameterFieldUtility.GetFields(modType) : null;

            if (fields == null || fields.Length == 0)
            {
                if (modType == null)
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), paramProp);
                return;
            }

            EnsureDefault(paramProp, ParameterFieldUtility.BuildFullDefault(fields));
            ParameterFieldUtility.DrawParameterFields(position.x, position.width, y, fields, paramProp);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var lines = 1;

            var modifierProp = ParameterFieldUtility.FindModifierProperty(property);
            var modType = modifierProp?.managedReferenceValue?.GetType();

            if (modType == typeof(CompositeModifier))
            {
                var entries = ParameterFieldUtility.GetCompositeEntries(modifierProp);
                if (entries != null)
                    lines += ParameterFieldUtility.GetCompositeSegmentLines(entries,
                        property.FindPropertyRelative("parameter").propertyPath);
            }
            else
            {
                var fields = modType != null ? ParameterFieldUtility.GetFields(modType) : null;

                if (fields != null && fields.Length > 0)
                    lines += fields.Length;
                else if (modType == null)
                    lines += 1;
            }

            return lines * lineHeight + (lines - 1) * spacing;
        }

        private static void EnsureDefault(SerializedProperty paramProp, string defaultValue)
        {
            if (!string.IsNullOrEmpty(paramProp.stringValue)) return;
            paramProp.stringValue = defaultValue;
            paramProp.serializedObject.ApplyModifiedProperties();
        }
    }
}
