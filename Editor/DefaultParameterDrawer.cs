using System;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(DefaultParameterAttribute))]
    internal class DefaultParameterDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var modifierProp = ParameterFieldUtility.FindModifierProperty(property);
            if (modifierProp == null) return;

            var modType = modifierProp.managedReferenceValue?.GetType();

            if (modType == typeof(CompositeModifier))
                DrawComposite(position, property, modifierProp);
            else
                DrawSingle(position, property, modType);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var modifierProp = ParameterFieldUtility.FindModifierProperty(property);
            if (modifierProp == null) return 0;

            var modType = modifierProp.managedReferenceValue?.GetType();

            if (modType == typeof(CompositeModifier))
                return GetCompositeHeight(property, modifierProp);

            return GetSingleHeight(property, modType);
        }

        #region Single Modifier

        private static void DrawSingle(Rect position, SerializedProperty property, Type modifierType)
        {
            var fields = modifierType != null ? ParameterFieldUtility.GetFields(modifierType) : null;
            if (fields == null || fields.Length == 0) return;

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var y = position.y;

            var hasDefaults = !string.IsNullOrEmpty(property.stringValue);
            var newHasDefaults = EditorGUI.ToggleLeft(
                new Rect(position.x, y, position.width, lineHeight),
                "Default Parameters", hasDefaults);
            y += lineHeight + spacing;

            if (newHasDefaults != hasDefaults)
            {
                property.stringValue = newHasDefaults
                    ? ParameterFieldUtility.BuildFullDefault(fields)
                    : "";
                property.serializedObject.ApplyModifiedProperties();
            }

            if (newHasDefaults)
            {
                ParameterFieldUtility.DrawParameterFields(
                    position.x, position.width, y, fields, property);
            }
        }

        private static float GetSingleHeight(SerializedProperty property, Type modifierType)
        {
            var fields = modifierType != null ? ParameterFieldUtility.GetFields(modifierType) : null;
            if (fields == null || fields.Length == 0) return 0;

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var lines = 1;

            if (!string.IsNullOrEmpty(property.stringValue))
                lines += fields.Length;

            return lines * lineHeight + (lines - 1) * spacing;
        }

        #endregion

        #region Composite Modifier
        
        
        private static void DrawComposite(Rect position, SerializedProperty property,
            SerializedProperty modifierProp)
        {
            var entries = ParameterFieldUtility.GetCompositeEntries(modifierProp);
            if (entries == null || entries.Length == 0) return;

            ParameterFieldUtility.CountCompositeStats(entries, out _, out var totalParams);
            if (totalParams == 0) return;

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var y = position.y;

            var hasDefaults = !string.IsNullOrEmpty(property.stringValue);
            var newHasDefaults = EditorGUI.ToggleLeft(
                new Rect(position.x, y, position.width, lineHeight),
                "Default Parameters", hasDefaults);
            y += lineHeight + spacing;

            if (newHasDefaults != hasDefaults)
            {
                property.stringValue = newHasDefaults
                    ? ParameterFieldUtility.BuildCompositeDefault(entries)
                    : "";
                property.serializedObject.ApplyModifiedProperties();
            }

            if (!newHasDefaults) return;

            ParameterFieldUtility.DrawCompositeSegments(
                position.x, position.width, y, entries, property);
        }

        private static float GetCompositeHeight(SerializedProperty property,
            SerializedProperty modifierProp)
        {
            var entries = ParameterFieldUtility.GetCompositeEntries(modifierProp);
            if (entries == null || entries.Length == 0) return 0;

            ParameterFieldUtility.CountCompositeStats(entries, out _, out var totalParams);
            if (totalParams == 0) return 0;

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var lines = 1;

            if (!string.IsNullOrEmpty(property.stringValue))
                lines += ParameterFieldUtility.GetCompositeSegmentLines(entries, property.propertyPath);

            return lines * lineHeight + (lines - 1) * spacing;
        }

        #endregion
    }
}
