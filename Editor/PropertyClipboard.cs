using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class PropertyClipboard
    {
        #region Managed Reference

        private static string managedTypeName;
        private static string managedJson;

        public static bool HasManagedReference => managedTypeName != null;

        public static void CopyManagedReference(SerializedProperty property)
        {
            var obj = property.managedReferenceValue;
            if (obj == null)
            {
                managedTypeName = null;
                managedJson = null;
                return;
            }
            managedTypeName = obj.GetType().AssemblyQualifiedName;
            managedJson = JsonUtility.ToJson(obj);
        }

        public static bool CanPasteManagedReference(SerializedProperty property)
        {
            if (managedTypeName == null) return false;
            var clipType = Type.GetType(managedTypeName);
            if (clipType == null) return false;
            var baseType = GetManagedReferenceBaseType(property);
            return baseType != null && baseType.IsAssignableFrom(clipType);
        }

        public static void PasteManagedReference(SerializedProperty property)
        {
            var type = Type.GetType(managedTypeName);
            if (type == null) return;
            var newObj = Activator.CreateInstance(type);
            JsonUtility.FromJsonOverwrite(managedJson, newObj);
            property.managedReferenceValue = newObj;
        }

        public static string GetManagedReferenceClipLabel()
        {
            if (managedTypeName == null) return null;
            var type = Type.GetType(managedTypeName);
            return type?.Name;
        }

        #endregion

        #region Array

        private struct ArrayClip
        {
            public bool isManagedRef;
            public ElementClip[] elements;
        }

        private struct ElementClip
        {
            public string typeName;
            public string json;
            public LeafValue[] leaves;
        }

        private static ArrayClip arrayClip;
        private static string arrayClipElementType;

        public static bool HasArrayClip => arrayClip.elements != null;
        public static int ArrayClipCount => arrayClip.elements?.Length ?? 0;

        public static void CopyArray(SerializedProperty arrayProp)
        {
            int count = arrayProp.arraySize;
            bool isManagedRef = count > 0 &&
                arrayProp.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.ManagedReference;

            var elements = new ElementClip[count];
            for (int i = 0; i < count; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                if (isManagedRef)
                {
                    var obj = elem.managedReferenceValue;
                    elements[i] = new ElementClip
                    {
                        typeName = obj?.GetType().AssemblyQualifiedName,
                        json = obj != null ? JsonUtility.ToJson(obj) : null
                    };
                }
                else
                {
                    elements[i] = new ElementClip { leaves = SerializeLeaves(elem) };
                }
            }

            arrayClip = new ArrayClip { isManagedRef = isManagedRef, elements = elements };

            if (count > 0)
            {
                var first = arrayProp.GetArrayElementAtIndex(0);
                arrayClipElementType = isManagedRef
                    ? first.managedReferenceFieldTypename
                    : first.type;
            }
            else
            {
                arrayClipElementType = null;
            }
        }

        public static bool CanPasteArray(SerializedProperty arrayProp)
        {
            if (arrayClip.elements == null) return false;
            if (arrayClipElementType == null) return true;

            if (!arrayClip.isManagedRef)
                return arrayProp.arrayElementType == arrayClipElementType;

            if (arrayProp.arraySize > 0)
            {
                var first = arrayProp.GetArrayElementAtIndex(0);
                return first.propertyType == SerializedPropertyType.ManagedReference
                    && first.managedReferenceFieldTypename == arrayClipElementType;
            }

            return true;
        }

        public static void PasteArray(SerializedProperty arrayProp)
        {
            if (arrayClip.elements == null) return;

            var clip = arrayClip;
            arrayProp.ClearArray();

            for (int i = 0; i < clip.elements.Length; i++)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                var elem = arrayProp.GetArrayElementAtIndex(i);
                PasteIntoElement(elem, clip.elements[i], clip.isManagedRef);
            }
        }

        private static void PasteIntoElement(SerializedProperty elem, ElementClip data, bool isManagedRef)
        {
            if (isManagedRef)
            {
                if (data.typeName != null)
                {
                    var type = Type.GetType(data.typeName);
                    if (type != null)
                    {
                        var obj = Activator.CreateInstance(type);
                        if (data.json != null) JsonUtility.FromJsonOverwrite(data.json, obj);
                        elem.managedReferenceValue = obj;
                    }
                    else
                    {
                        elem.managedReferenceValue = null;
                    }
                }
                else
                {
                    elem.managedReferenceValue = null;
                }
            }
            else
            {
                DeserializeLeaves(elem, data.leaves);
            }
        }

        #endregion

        #region Element

        private static ElementClip? elementClip;
        private static bool elementClipIsManagedRef;
        private static string elementClipType;

        public static bool HasElementClip => elementClip.HasValue;

        public static void CopyElement(SerializedProperty element)
        {
            if (element.propertyType == SerializedPropertyType.ManagedReference)
            {
                elementClipType = element.managedReferenceFieldTypename;
                var obj = element.managedReferenceValue;
                elementClip = new ElementClip
                {
                    typeName = obj?.GetType().AssemblyQualifiedName,
                    json = obj != null ? JsonUtility.ToJson(obj) : null
                };
                elementClipIsManagedRef = true;
            }
            else
            {
                elementClipType = element.type;
                elementClip = new ElementClip { leaves = SerializeLeaves(element) };
                elementClipIsManagedRef = false;
            }
        }

        public static bool CanPasteElement(SerializedProperty targetElement)
        {
            if (!elementClip.HasValue) return false;

            if (elementClipIsManagedRef != (targetElement.propertyType == SerializedPropertyType.ManagedReference))
                return false;

            if (elementClipIsManagedRef)
            {
                if (elementClip.Value.typeName == null) return true;
                var clipType = Type.GetType(elementClip.Value.typeName);
                if (clipType == null) return false;
                var baseType = GetManagedReferenceBaseType(targetElement);
                return baseType != null && baseType.IsAssignableFrom(clipType);
            }

            return targetElement.type == elementClipType;
        }

        public static bool CanPasteElementIntoArray(SerializedProperty arrayProp)
        {
            if (!elementClip.HasValue) return false;

            if (!elementClipIsManagedRef)
                return arrayProp.arrayElementType == elementClipType;

            if (arrayProp.arraySize > 0)
                return CanPasteElement(arrayProp.GetArrayElementAtIndex(0));

            return true;
        }

        public static string GetElementClipLabel()
        {
            if (!elementClip.HasValue) return null;

            if (elementClipIsManagedRef)
            {
                if (elementClip.Value.typeName == null) return "null";
                var type = Type.GetType(elementClip.Value.typeName);
                return type?.Name;
            }

            return elementClipType;
        }

        public static void PasteElement(SerializedProperty element)
        {
            if (!elementClip.HasValue) return;
            PasteIntoElement(element, elementClip.Value, elementClipIsManagedRef);
        }

        public static void PasteElementIntoArray(SerializedProperty arrayProp)
        {
            if (!elementClip.HasValue) return;

            int newIndex = arrayProp.arraySize;
            arrayProp.InsertArrayElementAtIndex(newIndex);
            var elem = arrayProp.GetArrayElementAtIndex(newIndex);
            PasteIntoElement(elem, elementClip.Value, elementClipIsManagedRef);
        }

        public static void DuplicateElement(SerializedProperty arrayProp, int index)
        {
            var srcElem = arrayProp.GetArrayElementAtIndex(index);
            bool isManagedRef = srcElem.propertyType == SerializedPropertyType.ManagedReference;
            ElementClip clip;

            if (isManagedRef)
            {
                var obj = srcElem.managedReferenceValue;
                clip = new ElementClip
                {
                    typeName = obj?.GetType().AssemblyQualifiedName,
                    json = obj != null ? JsonUtility.ToJson(obj) : null
                };
            }
            else
            {
                clip = new ElementClip { leaves = SerializeLeaves(srcElem) };
            }

            int newIndex = index + 1;
            arrayProp.InsertArrayElementAtIndex(newIndex);
            var newElem = arrayProp.GetArrayElementAtIndex(newIndex);
            PasteIntoElement(newElem, clip, isManagedRef);
        }

        #endregion

        #region Leaf Serialization

        private struct LeafValue
        {
            public string path;
            public SerializedPropertyType type;
            public string data;
        }

        private static LeafValue[] SerializeLeaves(SerializedProperty prop)
        {
            var results = new List<LeafValue>();
            var basePath = prop.propertyPath;
            var iter = prop.Copy();
            var end = prop.GetEndProperty();

            if (!iter.NextVisible(true)) return results.ToArray();

            while (true)
            {
                if (SerializedProperty.EqualContents(iter, end)) break;

                if (iter.propertyType == SerializedPropertyType.ManagedReference)
                {
                    var obj = iter.managedReferenceValue;
                    string data = obj != null
                        ? obj.GetType().AssemblyQualifiedName + "\n" + JsonUtility.ToJson(obj)
                        : "";

                    results.Add(new LeafValue
                    {
                        path = iter.propertyPath.Substring(basePath.Length + 1),
                        type = SerializedPropertyType.ManagedReference,
                        data = data
                    });

                    if (!iter.NextVisible(false)) break;
                }
                else if (TrySerializeLeaf(iter, out var data))
                {
                    results.Add(new LeafValue
                    {
                        path = iter.propertyPath.Substring(basePath.Length + 1),
                        type = iter.propertyType,
                        data = data
                    });

                    if (!iter.NextVisible(true)) break;
                }
                else
                {
                    if (!iter.NextVisible(true)) break;
                }
            }

            return results.ToArray();
        }

        private static bool TrySerializeLeaf(SerializedProperty prop, out string data)
        {
            data = null;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    data = prop.longValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Boolean:
                    data = prop.boolValue ? "1" : "0";
                    return true;
                case SerializedPropertyType.Float:
                    data = prop.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.String:
                    data = prop.stringValue ?? "";
                    return true;
                case SerializedPropertyType.Enum:
                    data = prop.enumValueIndex.ToString();
                    return true;
                case SerializedPropertyType.ObjectReference:
                    var obj = prop.objectReferenceValue;
                    data = obj != null ? obj.GetInstanceID().ToString() : "0";
                    return true;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    data = FormattableString.Invariant($"{c.r:R},{c.g:R},{c.b:R},{c.a:R}");
                    return true;
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    data = FormattableString.Invariant($"{v2.x:R},{v2.y:R}");
                    return true;
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    data = FormattableString.Invariant($"{v3.x:R},{v3.y:R},{v3.z:R}");
                    return true;
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    data = FormattableString.Invariant($"{v4.x:R},{v4.y:R},{v4.z:R},{v4.w:R}");
                    return true;
                case SerializedPropertyType.Rect:
                    var r = prop.rectValue;
                    data = FormattableString.Invariant($"{r.x:R},{r.y:R},{r.width:R},{r.height:R}");
                    return true;
                default:
                    return false;
            }
        }

        private static void DeserializeLeaves(SerializedProperty prop, LeafValue[] leaves)
        {
            if (leaves == null) return;

            foreach (var leaf in leaves)
            {
                var child = prop.FindPropertyRelative(leaf.path);
                if (child == null) continue;

                switch (leaf.type)
                {
                    case SerializedPropertyType.Integer:
                        child.longValue = long.Parse(leaf.data, CultureInfo.InvariantCulture);
                        break;
                    case SerializedPropertyType.Boolean:
                        child.boolValue = leaf.data == "1";
                        break;
                    case SerializedPropertyType.Float:
                        child.doubleValue = double.Parse(leaf.data, CultureInfo.InvariantCulture);
                        break;
                    case SerializedPropertyType.String:
                        child.stringValue = leaf.data;
                        break;
                    case SerializedPropertyType.Enum:
                        child.enumValueIndex = int.Parse(leaf.data);
                        break;
                    case SerializedPropertyType.ObjectReference:
                        var id = int.Parse(leaf.data);
                        child.objectReferenceValue = id != 0 ? EditorUtility.InstanceIDToObject(id) : null;
                        break;
                    case SerializedPropertyType.ManagedReference:
                    {
                        if (string.IsNullOrEmpty(leaf.data))
                        {
                            child.managedReferenceValue = null;
                        }
                        else
                        {
                            int sep = leaf.data.IndexOf('\n');
                            var typeName = leaf.data.Substring(0, sep);
                            var json = leaf.data.Substring(sep + 1);
                            var type = Type.GetType(typeName);
                            if (type != null)
                            {
                                var obj = Activator.CreateInstance(type);
                                JsonUtility.FromJsonOverwrite(json, obj);
                                child.managedReferenceValue = obj;
                            }
                            else
                            {
                                child.managedReferenceValue = null;
                            }
                        }
                        break;
                    }
                    case SerializedPropertyType.Color:
                    {
                        var p = leaf.data.Split(',');
                        child.colorValue = new Color(
                            float.Parse(p[0], CultureInfo.InvariantCulture),
                            float.Parse(p[1], CultureInfo.InvariantCulture),
                            float.Parse(p[2], CultureInfo.InvariantCulture),
                            float.Parse(p[3], CultureInfo.InvariantCulture));
                        break;
                    }
                    case SerializedPropertyType.Vector2:
                    {
                        var p = leaf.data.Split(',');
                        child.vector2Value = new Vector2(
                            float.Parse(p[0], CultureInfo.InvariantCulture),
                            float.Parse(p[1], CultureInfo.InvariantCulture));
                        break;
                    }
                    case SerializedPropertyType.Vector3:
                    {
                        var p = leaf.data.Split(',');
                        child.vector3Value = new Vector3(
                            float.Parse(p[0], CultureInfo.InvariantCulture),
                            float.Parse(p[1], CultureInfo.InvariantCulture),
                            float.Parse(p[2], CultureInfo.InvariantCulture));
                        break;
                    }
                    case SerializedPropertyType.Vector4:
                    {
                        var p = leaf.data.Split(',');
                        child.vector4Value = new Vector4(
                            float.Parse(p[0], CultureInfo.InvariantCulture),
                            float.Parse(p[1], CultureInfo.InvariantCulture),
                            float.Parse(p[2], CultureInfo.InvariantCulture),
                            float.Parse(p[3], CultureInfo.InvariantCulture));
                        break;
                    }
                    case SerializedPropertyType.Rect:
                    {
                        var p = leaf.data.Split(',');
                        child.rectValue = new Rect(
                            float.Parse(p[0], CultureInfo.InvariantCulture),
                            float.Parse(p[1], CultureInfo.InvariantCulture),
                            float.Parse(p[2], CultureInfo.InvariantCulture),
                            float.Parse(p[3], CultureInfo.InvariantCulture));
                        break;
                    }
                }
            }
        }

        #endregion

        #region Helpers

        private static Type GetManagedReferenceBaseType(SerializedProperty property)
        {
            var typeName = property.managedReferenceFieldTypename;
            if (string.IsNullOrEmpty(typeName)) return null;

            int spaceIdx = typeName.IndexOf(' ');
            if (spaceIdx < 0) return null;

            try
            {
                var asm = Assembly.Load(typeName.Substring(0, spaceIdx));
                return asm?.GetType(typeName.Substring(spaceIdx + 1));
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
