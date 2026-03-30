using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class ParameterFieldUtility
    {
        internal struct ParamField
        {
            public int order;
            public string name;
            public string type;
            public string defaultValue;
        }

        internal struct CompositeEntry
        {
            public string displayName;
            public ParamField[] fields;
        }

        private static readonly Dictionary<Type, ParamField[]> fieldCache = new();
        private static readonly Dictionary<string, int> pendingUnitSelections = new();
        internal static readonly Dictionary<string, bool> compositeFoldouts = new();

        #region Modifier Type Resolution

        internal static SerializedProperty FindModifierProperty(SerializedProperty property)
        {
            var path = property.propertyPath;
            var searchStart = 0;

            while (true)
            {
                var ruleIdx = path.IndexOf(".rule", searchStart, StringComparison.Ordinal);
                if (ruleIdx < 0)
                    break;

                var afterRule = ruleIdx + 5;
                if (afterRule < path.Length && path[afterRule] != '.')
                {
                    searchStart = afterRule;
                    continue;
                }

                var modifierPath = path.Substring(0, ruleIdx) + ".modifier";
                var modifierProp = property.serializedObject.FindProperty(modifierPath);

                if (modifierProp != null && modifierProp.propertyType == SerializedPropertyType.ManagedReference)
                    return modifierProp;

                searchStart = afterRule;
            }

            return null;
        }

        internal static Type FindModifierType(SerializedProperty property)
        {
            return FindModifierProperty(property)?.managedReferenceValue?.GetType();
        }

        internal static CompositeEntry[] GetCompositeEntries(SerializedProperty modifierProp)
        {
            var itemsProp = modifierProp.FindPropertyRelative("modifiers.items");
            if (itemsProp == null || !itemsProp.isArray) return null;

            var count = itemsProp.arraySize;
            var entries = new CompositeEntry[count];

            for (var i = 0; i < count; i++)
            {
                var childProp = itemsProp.GetArrayElementAtIndex(i);
                var childType = childProp.managedReferenceValue?.GetType();

                entries[i].displayName = childType != null
                    ? ObjectNames.NicifyVariableName(StripSuffix(childType.Name, "Modifier"))
                    : "Empty";
                entries[i].fields = childType != null ? GetFields(childType) : Array.Empty<ParamField>();
            }

            return entries;
        }

        private static string StripSuffix(string name, string suffix)
        {
            return name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;
        }

        internal static ParamField[] GetFields(Type modifierType)
        {
            if (fieldCache.TryGetValue(modifierType, out var cached))
                return cached;

            var attrs = modifierType.GetCustomAttributes<ParameterFieldAttribute>(true);
            var list = new List<ParamField>();

            foreach (var attr in attrs)
            {
                list.Add(new ParamField
                {
                    order = attr.Order,
                    name = attr.Name,
                    type = attr.Type,
                    defaultValue = attr.Default
                });
            }

            list.Sort((a, b) => a.order.CompareTo(b.order));
            var result = list.ToArray();
            fieldCache[modifierType] = result;
            return result;
        }

        #endregion

        #region High-Level Drawing

        internal static float DrawParameterFields(float x, float width, float y,
            ParamField[] fields, SerializedProperty paramProp)
        {
            var totalHeight = GetParameterFieldsHeight(fields);
            var propertyRect = new Rect(x, y, width, totalHeight);
            EditorGUI.BeginProperty(propertyRect, GUIContent.none, paramProp);

            y = DrawParameterFieldsForSegment(x, width, y, fields,
                paramProp.stringValue, out var newValue, paramProp.propertyPath);

            if (newValue != paramProp.stringValue)
            {
                paramProp.stringValue = newValue;
                paramProp.serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.EndProperty();
            return y;
        }

        internal static float DrawParameterFieldsForSegment(float x, float width, float y,
            ParamField[] fields, string segment, out string newSegment, string propertyPath = null)
        {
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            var savedLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(savedLabelWidth, width * 0.4f);

            var tokens = SplitParameter(segment);
            var changed = false;

            for (var i = 0; i < fields.Length; i++)
            {
                ref var field = ref fields[i];
                var token = i < tokens.Length && tokens[i] != "~" ? tokens[i] : field.defaultValue;
                var fieldRect = new Rect(x, y, width, lineHeight);
                var newToken = DrawField(fieldRect, field, token, propertyPath);

                if (newToken != token)
                {
                    if (i < tokens.Length)
                        tokens[i] = newToken;
                    else
                    {
                        var expanded = new string[fields.Length];
                        for (var j = 0; j < expanded.Length; j++)
                            expanded[j] = j < tokens.Length ? tokens[j] : fields[j].defaultValue;
                        expanded[i] = newToken;
                        tokens = expanded;
                    }
                    changed = true;
                }

                y += lineHeight + spacing;
            }

            EditorGUIUtility.labelWidth = savedLabelWidth;

            newSegment = changed ? JoinParameter(tokens, fields) : segment;
            return y;
        }

        internal static float GetParameterFieldsHeight(ParamField[] fields)
        {
            if (fields == null || fields.Length == 0) return 0;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            return fields.Length * (lineHeight + spacing);
        }

        internal static string BuildFullDefault(ParamField[] fields)
        {
            if (fields == null || fields.Length == 0) return "";
            if (fields.Length == 1) return ResolveVisualDefault(in fields[0]);

            var tokens = new string[fields.Length];
            for (var i = 0; i < fields.Length; i++)
                tokens[i] = ResolveVisualDefault(in fields[i]);
            return string.Join(",", tokens);
        }

        internal static string BuildCompositeDefault(CompositeEntry[] entries)
        {
            var segments = new string[entries.Length];
            for (var i = 0; i < entries.Length; i++)
                segments[i] = BuildFullDefault(entries[i].fields);

            var last = segments.Length - 1;
            while (last > 0 && string.IsNullOrEmpty(segments[last]))
                last--;

            if (last == 0) return segments[0];
            return string.Join(";", segments, 0, last + 1);
        }

        private static string ResolveVisualDefault(in ParamField field)
        {
            if (!string.IsNullOrEmpty(field.defaultValue)) return field.defaultValue;

            var type = field.type;
            if (type == "float" || type.StartsWith("float(")) return "0";
            if (type == "int" || type.StartsWith("int(")) return "0";
            if (type == "color") return "#FFFFFFFF";
            if (type == "bool") return "false";
            if (type.StartsWith("enum:"))
            {
                var options = ResolveEnumOptions(type);
                return options != null && options.Length > 0 ? options[0] : "";
            }
            if (type.StartsWith("unit")) return "0";
            return "";
        }

        internal static void CountCompositeStats(CompositeEntry[] entries,
            out int modsWithFields, out int totalParams)
        {
            modsWithFields = 0;
            totalParams = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].fields.Length > 0)
                {
                    modsWithFields++;
                    totalParams += entries[i].fields.Length;
                }
            }
        }

        internal static float DrawCompositeSegments(float x, float width, float y,
            CompositeEntry[] entries, SerializedProperty paramProp)
        {
            CountCompositeStats(entries, out var modsWithFields, out var totalParams);
            if (totalParams == 0) return y;

            var useFoldouts = modsWithFields > 1 && totalParams > 1;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            var fullValue = paramProp.stringValue ?? "";
            var segments = fullValue.Split(';');

            if (segments.Length < entries.Length)
            {
                var expanded = new string[entries.Length];
                Array.Copy(segments, expanded, segments.Length);
                for (var i = segments.Length; i < entries.Length; i++)
                    expanded[i] = "";
                segments = expanded;
            }

            var anyChanged = false;

            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].fields.Length == 0) continue;

                if (useFoldouts)
                {
                    var foldoutKey = paramProp.propertyPath + ";" + i;
                    if (!compositeFoldouts.TryGetValue(foldoutKey, out var expanded))
                        expanded = true;

                    expanded = EditorGUI.Foldout(
                        new Rect(x, y, width, lineHeight),
                        expanded, entries[i].displayName, true);
                    compositeFoldouts[foldoutKey] = expanded;
                    y += lineHeight + spacing;

                    if (!expanded) continue;
                }

                var segment = i < segments.Length ? segments[i] : "";
                y = DrawParameterFieldsForSegment(
                    x, width, y, entries[i].fields,
                    segment, out var newSegment, paramProp.propertyPath);

                if (newSegment != segment)
                {
                    segments[i] = newSegment;
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                var last = segments.Length - 1;
                while (last > 0 && string.IsNullOrEmpty(segments[last]))
                    last--;

                paramProp.stringValue = string.Join(";", segments, 0, last + 1);
                paramProp.serializedObject.ApplyModifiedProperties();
            }

            return y;
        }

        internal static int GetCompositeSegmentLines(CompositeEntry[] entries, string propertyPath)
        {
            CountCompositeStats(entries, out var modsWithFields, out var totalParams);
            if (totalParams == 0) return 0;

            var useFoldouts = modsWithFields > 1 && totalParams > 1;
            var lines = 0;

            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].fields.Length == 0) continue;

                if (useFoldouts)
                {
                    lines++;
                    var foldoutKey = propertyPath + ";" + i;
                    if (!compositeFoldouts.TryGetValue(foldoutKey, out var expanded))
                        expanded = true;
                    if (expanded)
                        lines += entries[i].fields.Length;
                }
                else
                {
                    lines += entries[i].fields.Length;
                }
            }

            return lines;
        }

        #endregion

        #region Field Drawing

        internal static string DrawField(Rect rect, ParamField field, string token, string propertyPath = null)
        {
            var type = field.type;

            if (type == "float" || type.StartsWith("float("))
                return DrawFloatField(rect, field.name, token, type, field.defaultValue);
            if (type == "int" || type.StartsWith("int("))
                return DrawIntField(rect, field.name, token, type, field.defaultValue);
            if (type == "color")
                return DrawColorField(rect, field.name, token, field.defaultValue);
            if (type == "bool")
                return DrawBoolField(rect, field.name, token, field.defaultValue);
            if (type == "string")
                return DrawStringField(rect, field.name, token);
            if (type.StartsWith("enum:"))
                return DrawEnumField(rect, field.name, token, type, field.defaultValue);
            if (type.StartsWith("unit"))
                return DrawUnitField(rect, field.name, token, type, field.defaultValue, propertyPath);

            return DrawStringField(rect, field.name, token);
        }

        private static string DrawFloatField(Rect rect, string label, string token, string type, string defaultValue)
        {
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                float.TryParse(defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            float newValue;
            if (TryParseRange(type, out var min, out var max))
                newValue = EditorGUI.Slider(rect, label, value, min, max);
            else
                newValue = EditorGUI.FloatField(rect, label, value);

            return newValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string DrawIntField(Rect rect, string label, string token, string type, string defaultValue)
        {
            if (!int.TryParse(token, out var value))
                int.TryParse(defaultValue, out value);

            int newValue;
            if (TryParseRange(type, out var min, out var max))
                newValue = EditorGUI.IntSlider(rect, label, value, (int)min, (int)max);
            else
                newValue = EditorGUI.IntField(rect, label, value);

            return newValue.ToString();
        }

        private static bool TryParseRange(string type, out float min, out float max)
        {
            min = 0f;
            max = 1f;

            var open = type.IndexOf('(');
            if (open < 0) return false;

            var close = type.IndexOf(')', open);
            if (close < 0) return false;

            var inner = type.AsSpan(open + 1, close - open - 1);
            var comma = inner.IndexOf(',');
            if (comma < 0) return false;

            return float.TryParse(inner[..comma].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out min)
                && float.TryParse(inner[(comma + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out max);
        }

        private static string DrawColorField(Rect rect, string label, string token, string defaultValue)
        {
            Color color;
            if (!ColorParsing.TryParse(token, out var c32))
            {
                if (!ColorParsing.TryParse(defaultValue, out c32))
                    c32 = Color.white;
            }
            color = c32;

            var newColor = EditorGUI.ColorField(rect, label, color);
            var c = (Color32)newColor;
            return $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
        }

        private static string DrawBoolField(Rect rect, string label, string token, string defaultValue)
        {
            if (!bool.TryParse(token, out var value))
                bool.TryParse(defaultValue, out value);

            var newValue = EditorGUI.Toggle(rect, label, value);
            return newValue.ToString().ToLowerInvariant();
        }

        private static string DrawStringField(Rect rect, string label, string token)
        {
            return EditorGUI.TextField(rect, label, token ?? "");
        }

        private static string DrawEnumField(Rect rect, string label, string token, string type, string defaultValue)
        {
            var options = ResolveEnumOptions(type);
            if (options == null || options.Length == 0)
                return DrawStringField(rect, label, token);

            var selectedIndex = Array.IndexOf(options, token);
            if (selectedIndex < 0)
                selectedIndex = Array.IndexOf(options, defaultValue);
            if (selectedIndex < 0)
                selectedIndex = 0;

            var newIndex = EditorGUI.Popup(rect, label, selectedIndex, options);
            return options[newIndex];
        }

        private static string[] ResolveEnumOptions(string type)
        {
            var spec = type.Substring(5);
            if (spec.Length > 0 && spec[0] == '@')
            {
                var key = spec.Substring(1);
                if (ParameterProviders.TryGetOptions(key, out var dynamicOptions))
                {
                    var list = new List<string>();
                    foreach (var option in dynamicOptions)
                        list.Add(option);
                    return list.ToArray();
                }
                return null;
            }
            return spec.Split('|');
        }

        private struct UnitEntry
        {
            public string name;
            public bool hasRange;
            public bool isInt;
            public float min, max;
        }

        private static UnitEntry[] ParseUnitEntries(string type)
        {
            var colonIdx = type.IndexOf(':');
            if (colonIdx < 0) return Array.Empty<UnitEntry>();

            var parts = type.Substring(colonIdx + 1).Split('|');
            var result = new UnitEntry[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                var entry = parts[i];
                var bracketOpen = entry.IndexOfAny(bracketChars);

                if (bracketOpen < 0)
                {
                    result[i].name = entry;
                    continue;
                }

                result[i].name = entry.Substring(0, bracketOpen);
                result[i].isInt = entry[bracketOpen] == '[';

                var bracketClose = entry.IndexOf(result[i].isInt ? ']' : ')', bracketOpen);
                if (bracketClose > bracketOpen)
                {
                    var inner = entry.AsSpan(bracketOpen + 1, bracketClose - bracketOpen - 1);
                    var comma = inner.IndexOf(',');
                    if (comma >= 0)
                    {
                        result[i].hasRange =
                            float.TryParse(inner[..comma].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i].min) &&
                            float.TryParse(inner[(comma + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i].max);
                    }
                }
            }

            return result;
        }

        private static readonly char[] bracketChars = { '(', '[' };

        private static string DrawUnitField(Rect rect, string label, string token, string type,
            string defaultValue, string propertyPath)
        {
            var entries = ParseUnitEntries(type);
            if (entries.Length == 0) return DrawStringField(rect, label, token);

            var unitNames = new string[entries.Length];
            for (var i = 0; i < entries.Length; i++)
                unitNames[i] = entries[i].name;

            ParseUnitValue(token, unitNames, out var value, out var unitIndex);

            if (unitIndex < 0)
            {
                ParseUnitValue(defaultValue, unitNames, out value, out unitIndex);
                if (unitIndex < 0)
                    unitIndex = 0;
            }

            var pendingKey = (propertyPath ?? "") + "|" + label;
            if (pendingUnitSelections.TryGetValue(pendingKey, out var pendingIndex))
            {
                unitIndex = pendingIndex;
                pendingUnitSelections.Remove(pendingKey);
            }

            var unitDropdownWidth = 52f;
            var gap = 2f;

            var valueRect = new Rect(rect.x, rect.y, rect.width - unitDropdownWidth - gap, rect.height);
            var unitRect = new Rect(valueRect.xMax + gap, rect.y, unitDropdownWidth, rect.height);

            ref var current = ref entries[unitIndex];
            float newValue;

            if (current.hasRange && current.isInt)
                newValue = EditorGUI.IntSlider(valueRect, label, (int)value, (int)current.min, (int)current.max);
            else if (current.hasRange)
                newValue = EditorGUI.Slider(valueRect, label, value, current.min, current.max);
            else
                newValue = EditorGUI.FloatField(valueRect, label, value);

            var unitLabels = GetUnitLabels(unitNames);

            if (GUI.Button(unitRect, unitLabels[unitIndex], EditorStyles.popup))
            {
                var items = new Selector.SelectorItem[unitNames.Length];
                for (var i = 0; i < unitNames.Length; i++)
                {
                    items[i] = new Selector.SelectorItem
                    {
                        displayName = unitLabels[i],
                        value = i,
                        groupOrder = i
                    };
                }

                var capturedKey = pendingKey;
                Selector.Show(unitRect, items, unitIndex, v =>
                {
                    pendingUnitSelections[capturedKey] = (int)v;
                }, showSearch: false);
            }

            return SerializeUnitValue(newValue, unitNames[unitIndex]);
        }

        #endregion

        #region Unit Parsing

        private static void ParseUnitValue(string token, string[] units, out float value, out int unitIndex)
        {
            value = 0f;
            unitIndex = -1;

            if (string.IsNullOrEmpty(token))
                return;

            if (Array.IndexOf(units, "%") >= 0 && token.EndsWith("%"))
            {
                if (float.TryParse(token.AsSpan(0, token.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    unitIndex = Array.IndexOf(units, "%");
                    return;
                }
            }

            if (Array.IndexOf(units, "em") >= 0 && token.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(token.AsSpan(0, token.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    unitIndex = Array.IndexOf(units, "em");
                    return;
                }
            }

            if (Array.IndexOf(units, "delta") >= 0 && token.Length > 1 && (token[0] == '+' || token[0] == '-'))
            {
                if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    unitIndex = Array.IndexOf(units, "delta");
                    return;
                }
            }

            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                for (var u = 0; u < units.Length; u++)
                {
                    var unit = units[u];
                    if (unit != "%" && unit != "em" && unit != "delta")
                    {
                        unitIndex = u;
                        return;
                    }
                }
            }
        }

        private static string SerializeUnitValue(float value, string unit)
        {
            var numStr = value.ToString(CultureInfo.InvariantCulture);

            switch (unit)
            {
                case "px": return numStr;
                case "%": return numStr + "%";
                case "em": return numStr + "em";
                case "delta":
                    return value >= 0 ? "+" + numStr : numStr;
                default: return numStr;
            }
        }

        private static string[] GetUnitLabels(string[] units)
        {
            var labels = new string[units.Length];
            for (var i = 0; i < units.Length; i++)
            {
                labels[i] = units[i] switch
                {
                    "delta" => "±",
                    _ => units[i]
                };
            }
            return labels;
        }

        #endregion

        #region Parameter Serialization

        internal static string[] SplitParameter(string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return Array.Empty<string>();

            var parts = parameter.Split(',');
            for (var i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();

            return parts;
        }

        internal static string JoinParameter(string[] tokens, ParamField[] fields)
        {
            var last = tokens.Length - 1;
            while (last >= 0 && (string.IsNullOrEmpty(tokens[last]) || tokens[last] == "~"))
                last--;

            if (last < 0)
                return "";

            if (last == 0)
                return tokens[0];

            return string.Join(",", tokens, 0, last + 1);
        }

        #endregion
    }
}
