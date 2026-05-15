using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using LightSide;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(TypeSelectorAttribute))]
    internal class TypeSelectorDrawer : PropertyDrawer
    {
        private struct TypeEntry
        {
            public Type type;
            public string displayName;
            public string groupName;
            public int groupOrder;
        }

        private sealed class TypeCache
        {
            public readonly List<TypeEntry> entries;
            public readonly Dictionary<Type, string> displayNames;
            public readonly bool hasGroups;

            public TypeCache(List<TypeEntry> entries, Dictionary<Type, string> displayNames, bool hasGroups)
            {
                this.entries = entries;
                this.displayNames = displayNames;
                this.hasGroups = hasGroups;
            }
        }

        private static readonly Dictionary<Type, TypeCache> typeCache = new();
        private static readonly string[] suffixes = { "Modifier", "ParseRule", "Rule", "Highlighter" };

        private const float FoldoutOffset = 12f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.LabelField(position, label.text, "[TypeSelector] requires [SerializeReference]");
                return;
            }

            var baseType = GetBaseType(fieldInfo.FieldType);
            if (baseType == null)
            {
                EditorGUI.LabelField(position, label.text, "Cannot determine base type");
                return;
            }

            var cache = GetOrCreateCache(baseType);
            var currentType = property.managedReferenceValue?.GetType();

            const float foldoutWidth = 14f;

            var headerRect = new Rect(position.x + FoldoutOffset, position.y, position.width - FoldoutOffset, EditorGUIUtility.singleLineHeight);
            var hasLabel = !string.IsNullOrEmpty(label.text);
            var hasChildren = property.managedReferenceValue != null && HasVisibleChildren(property);

            Rect dropdownRect;

            if (hasLabel && hasChildren)
            {
                var foldoutRect = new Rect(headerRect.x, headerRect.y, EditorGUIUtility.labelWidth, headerRect.height);
                property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
                dropdownRect = new Rect(headerRect.x + EditorGUIUtility.labelWidth + 2, headerRect.y, headerRect.width - EditorGUIUtility.labelWidth - 2, headerRect.height);
            }
            else if (hasLabel)
            {
                var labelRect = new Rect(headerRect.x, headerRect.y, EditorGUIUtility.labelWidth, headerRect.height);
                EditorGUI.LabelField(labelRect, label);
                dropdownRect = new Rect(labelRect.xMax + 2, headerRect.y, headerRect.width - labelRect.width - 2, headerRect.height);
            }
            else if (hasChildren)
            {
                var foldoutRect = new Rect(headerRect.x, headerRect.y, foldoutWidth, headerRect.height);
                property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);
                dropdownRect = new Rect(headerRect.x + foldoutWidth, headerRect.y, headerRect.width - foldoutWidth, headerRect.height);
            }
            else
            {
                dropdownRect = headerRect;
            }

            var buttonText = GetDisplayName(currentType, cache);
            if (EditorGUI.DropdownButton(dropdownRect, new GUIContent(buttonText), FocusType.Keyboard))
            {
                ShowTypeMenu(dropdownRect, property, cache, currentType);
            }

            if (property.isExpanded && hasChildren)
            {
                const float indentWidth = 15f;
                var totalIndent = FoldoutOffset + indentWidth;
                var yOffset = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                var iterator = property.Copy();
                var endProperty = iterator.GetEndProperty();

                if (iterator.NextVisible(true))
                {
                    do
                    {
                        if (SerializedProperty.EqualContents(iterator, endProperty))
                            break;

                        var childHeight = EditorGUI.GetPropertyHeight(iterator, true);
                        var childRect = new Rect(
                            position.x + totalIndent,
                            position.y + yOffset,
                            position.width - totalIndent,
                            childHeight
                        );

                        EditorGUI.PropertyField(childRect, iterator, true);
                        yOffset += childHeight + EditorGUIUtility.standardVerticalSpacing;
                    }
                    while (iterator.NextVisible(false));
                }
            }
        }

        private static bool HasVisibleChildren(SerializedProperty property)
        {
            var iterator = property.Copy();
            var endProperty = iterator.GetEndProperty();

            if (iterator.NextVisible(true))
                return !SerializedProperty.EqualContents(iterator, endProperty);

            return false;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;

            if (property.propertyType != SerializedPropertyType.ManagedReference)
                return height;

            if (property.isExpanded && property.managedReferenceValue != null)
            {
                var iterator = property.Copy();
                var endProperty = iterator.GetEndProperty();

                if (iterator.NextVisible(true))
                {
                    do
                    {
                        if (SerializedProperty.EqualContents(iterator, endProperty))
                            break;

                        height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
                    }
                    while (iterator.NextVisible(false));
                }
            }

            return height;
        }

        private static Type GetBaseType(Type fieldType)
        {
            if (fieldType.IsArray)
                return fieldType.GetElementType();

            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return fieldType.GetGenericArguments()[0];

            return fieldType;
        }

        private static TypeCache GetOrCreateCache(Type baseType)
        {
            if (typeCache.TryGetValue(baseType, out var cache))
                return cache;

            var entries = new List<TypeEntry>();
            var displayNames = new Dictionary<Type, string>();
            var hasGroups = false;

            var types = UnityEditor.TypeCache.GetTypesDerivedFrom(baseType);

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                var groupAttr = type.GetCustomAttribute<TypeGroupAttribute>();
                var displayName = FormatDisplayName(type.Name);

                if (groupAttr != null && !string.IsNullOrEmpty(groupAttr.GroupName))
                    hasGroups = true;

                entries.Add(new TypeEntry
                {
                    type = type,
                    displayName = displayName,
                    groupName = groupAttr?.GroupName ?? string.Empty,
                    groupOrder = groupAttr?.Order ?? 999
                });

                displayNames[type] = displayName;
            }

            entries.Sort(CompareEntries);

            cache = new TypeCache(entries, displayNames, hasGroups);
            typeCache[baseType] = cache;
            return cache;
        }

        private static int CompareEntries(TypeEntry a, TypeEntry b)
        {
            var orderCmp = a.groupOrder.CompareTo(b.groupOrder);
            if (orderCmp != 0) return orderCmp;

            var groupCmp = string.Compare(a.groupName, b.groupName, StringComparison.Ordinal);
            if (groupCmp != 0) return groupCmp;

            return string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
        }

        private static string FormatDisplayName(string name)
        {
            foreach (var suffix in suffixes)
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix))
                    return name.Substring(0, name.Length - suffix.Length);
            }
            return name;
        }

        private static string GetDisplayName(Type currentType, TypeCache cache)
        {
            if (currentType == null)
                return "(None)";

            return cache.displayNames.TryGetValue(currentType, out var name) ? name : currentType.Name;
        }

        private static void ShowTypeMenu(Rect rect, SerializedProperty property, TypeCache cache, Type currentType)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("None"), currentType == null, () => SetType(property, null));

            if (cache.entries.Count > 0)
                menu.AddSeparator(string.Empty);

            foreach (var entry in cache.entries)
            {
                var path = cache.hasGroups && !string.IsNullOrEmpty(entry.groupName)
                    ? $"{entry.groupName}/{entry.displayName}"
                    : entry.displayName;

                var entryType = entry.type;
                menu.AddItem(new GUIContent(path), entryType == currentType, () => SetType(property, entryType));
            }

            menu.DropDown(rect);
        }

        private static void SetType(SerializedProperty property, Type type)
        {
            property.serializedObject.Update();
            property.managedReferenceValue = type != null ? Activator.CreateInstance(type) : null;
            property.serializedObject.ApplyModifiedProperties();
        }
    }

}
