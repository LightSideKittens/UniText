using System.Text;
using UnityEditor;
using UnityEngine;
using LightSide;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(EscapeTextAreaAttribute))]
    internal sealed class EscapeTextAreaDrawer : PropertyDrawer
    {
        private const float ToggleWidth = 18f;
        private const float Spacing = 4f;

        private static readonly GUIContent EscapeToggleContent = new("E", "Process escape sequences (\\n, \\r, \\t, \\uXXXX, \\xXX)");

        private static string GetPrefKey(SerializedProperty property)
        {
            return $"EscapeTextArea_{property.propertyPath}_{property.serializedObject.targetObject.GetInstanceID()}";
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return EditorGUIUtility.singleLineHeight;

            var attr = (EscapeTextAreaAttribute)attribute;
            var storedValue = property.stringValue ?? string.Empty;

            var prefKey = GetPrefKey(property);
            var escapeEnabled = EditorPrefs.GetBool(prefKey, attr.ProcessEscapes);
            var displayValue = escapeEnabled ? ToDisplayString(storedValue) : storedValue;

            var lineCount = CountLines(displayValue);
            lineCount = Mathf.Clamp(lineCount, attr.MinLines, attr.MaxLines);

            return EditorGUIUtility.singleLineHeight * lineCount;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use EscapeTextArea with string.");
                return;
            }

            var attr = (EscapeTextAreaAttribute)attribute;
            var prefKey = GetPrefKey(property);

            EditorGUI.BeginProperty(position, label, property);

            var textAreaRect = new Rect(
                position.x,
                position.y,
                position.width - ToggleWidth - Spacing,
                position.height
            );

            var toggleRect = new Rect(
                position.xMax - ToggleWidth,
                position.y + (position.height - EditorGUIUtility.singleLineHeight) * 0.5f,
                ToggleWidth,
                EditorGUIUtility.singleLineHeight
            );

            var escapeEnabled = EditorPrefs.GetBool(prefKey, attr.ProcessEscapes);

            EditorGUI.BeginChangeCheck();
            var newEscapeEnabled = GUI.Toggle(toggleRect, escapeEnabled, EscapeToggleContent, EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck())
            {
                escapeEnabled = newEscapeEnabled;
                EditorPrefs.SetBool(prefKey, escapeEnabled);
            }

            var storedValue = property.stringValue ?? string.Empty;
            var displayValue = escapeEnabled ? ToDisplayString(storedValue) : storedValue;

            EditorGUI.BeginChangeCheck();
            var newDisplayValue = EditorGUI.TextArea(textAreaRect, displayValue, EditorStyles.textArea);
            if (EditorGUI.EndChangeCheck())
            {
                var newStoredValue = escapeEnabled ? FromDisplayString(newDisplayValue) : newDisplayValue;
                property.stringValue = newStoredValue;
            }

            EditorGUI.EndProperty();
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;

            var count = 1;
            foreach (var c in text)
                if (c == '\n') count++;
            return count;
        }

        private static string ToDisplayString(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;

            var sb = new StringBuilder(stored.Length + 16);
            foreach (var c in stored)
            {
                switch (c)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\0': sb.Append("\\0"); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (char.IsControl(c) || c > 127 && !char.IsLetterOrDigit(c) && !char.IsPunctuation(c) && !char.IsWhiteSpace(c))
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static string FromDisplayString(string display)
        {
            if (string.IsNullOrEmpty(display) || display.IndexOf('\\') < 0)
                return display;

            var sb = new StringBuilder(display.Length);
            for (var i = 0; i < display.Length; i++)
            {
                if (display[i] != '\\' || i + 1 >= display.Length)
                {
                    sb.Append(display[i]);
                    continue;
                }

                var next = display[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case '0': sb.Append('\0'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case 'u':
                        if (i + 5 < display.Length && TryParseHex(display, i + 2, 4, out var unicodeValue))
                        {
                            sb.Append((char)unicodeValue);
                            i += 5;
                        }
                        else
                        {
                            sb.Append('\\');
                        }
                        break;
                    case 'x':
                        if (i + 3 < display.Length && TryParseHex(display, i + 2, 2, out var hexValue))
                        {
                            sb.Append((char)hexValue);
                            i += 3;
                        }
                        else
                        {
                            sb.Append('\\');
                        }
                        break;
                    default:
                        sb.Append('\\');
                        break;
                }
            }
            return sb.ToString();
        }

        private static bool TryParseHex(string str, int start, int length, out int value)
        {
            value = 0;
            if (start + length > str.Length) return false;

            for (var i = 0; i < length; i++)
            {
                var c = str[start + i];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else return false;

                value = value * 16 + digit;
            }
            return true;
        }
    }

}
