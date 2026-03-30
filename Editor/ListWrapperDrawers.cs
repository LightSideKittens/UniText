using UnityEditor;
using UnityEngine;
using LightSide;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(TypedList<>))]
    internal class TypedListDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var items = property.FindPropertyRelative("items");
            return StyledListUtility.GetStyledListHeight(items, label);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var items = property.FindPropertyRelative("items");
            StyledListUtility.DrawStyledList(position, items, label);
        }
    }

    [CustomPropertyDrawer(typeof(StyledList<>))]
    internal class StyledListDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var items = property.FindPropertyRelative("items");
            return StyledListUtility.GetStyledListHeight(items, label);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var items = property.FindPropertyRelative("items");
            StyledListUtility.DrawStyledList(position, items, label);
        }
    }

}
