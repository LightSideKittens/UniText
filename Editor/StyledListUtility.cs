using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using LightSide;

namespace LightSide
{
    internal static class StyledListUtility
    {
        internal struct ListCallbacks
        {
            public Action<SerializedProperty, int> onAdded;
            public Action<SerializedProperty, int> onRemoved;
            public Action<int, int> onReordered;
        }

        private static ListCallbacks activeCallbacks;

        private const float HeaderHeight = 24f;
        private const float ElementHeight = 24f;
        private const float ElementPadding = 4f;
        private const float ListTopPadding = 4f;
        private const float ListBottomPadding = 4f;
        private const float Padding = 6f;
        private const float DragHandleWidth = 20f;
        private const float RemoveButtonWidth = 25f;

        private static GUIStyle headerBackground;
        private static GUIStyle listBackground;
        private static GUIStyle elementBackground;
        private static GUIStyle dragHandle;
        private static GUIStyle footerButton;
        private static GUIContent addIcon;
        private static GUIContent removeIcon;

        private static Color? cachedBaseColor;
        private static bool isProSkinCached;

        private static Color BaseBackgroundColor
        {
            get
            {
                if (cachedBaseColor.HasValue && isProSkinCached == EditorGUIUtility.isProSkin)
                    return cachedBaseColor.Value;

                isProSkinCached = EditorGUIUtility.isProSkin;

                var style = GUI.skin.FindStyle("RL Background") ?? GUI.skin.box;
                var tex = style.normal.background;
                if (tex != null && tex.isReadable)
                {
                    cachedBaseColor = tex.GetPixel(tex.width / 2, tex.height / 2);
                    return cachedBaseColor.Value;
                }

                cachedBaseColor = EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.22f, 0.22f, 1f)
                    : new Color(0.76f, 0.76f, 0.76f, 1f);
                return cachedBaseColor.Value;
            }
        }

        private static Color EvenBackground => BaseBackgroundColor;

        private static Color OddBackground
        {
            get
            {
                var c = BaseBackgroundColor;
                float offset = EditorGUIUtility.isProSkin ? 0.03f : 0.04f;
                return new Color(c.r + offset, c.g + offset, c.b + offset, 1f);
            }
        }

        private static int dragIndex = -1;
        private static int targetIndex = -1;
        private static string dragPropertyPath;
        private static float dragOffset;
        private static float dragMouseY;

        private static MethodInfo moveArrayExpandedState;
        private static bool moveArrayExpandedStateInitialized;

        private static GUIStyle HeaderBackground =>
            headerBackground ??= new GUIStyle("RL Header")
            {
                fixedHeight = 0f,
                stretchHeight = true
            };

        private static GUIStyle ListBackground =>
            listBackground ??= new GUIStyle("RL Background");

        private static GUIStyle ElementBackground =>
            elementBackground ??= new GUIStyle("RL Element");

        private static GUIStyle DragHandle =>
            dragHandle ??= new GUIStyle("RL DragHandle");

        private static GUIStyle FooterButton =>
            footerButton ??= new GUIStyle("RL FooterButton")
            {
                fixedHeight = 0f,
                stretchHeight = true
            };

        private static GUIContent AddIcon =>
            addIcon ??= EditorGUIUtility.TrIconContent("Toolbar Plus", "Add to list");

        private static GUIContent RemoveIcon =>
            removeIcon ??= EditorGUIUtility.TrIconContent("Toolbar Minus", "Remove element");

        public static void DrawStyledList(Rect position, SerializedProperty property, GUIContent label)
        {
            DrawStyledList(position, property, label, default);
        }

        public static void DrawStyledList(Rect position, SerializedProperty property, GUIContent label, ListCallbacks callbacks)
        {
            if (!property.isArray)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            activeCallbacks = callbacks;

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var headerRect = new Rect(position.x, position.y, position.width, HeaderHeight);
            DrawHeader(headerRect, property, label);

            if (property.isExpanded)
            {
                var listRect = new Rect(
                    position.x,
                    position.y + HeaderHeight,
                    position.width,
                    position.height - HeaderHeight
                );
                DrawListBackground(listRect);
                DrawElements(listRect, property);
            }

            EditorGUI.indentLevel = indent;
            activeCallbacks = default;
        }

        public static float GetStyledListHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isArray)
                return EditorGUI.GetPropertyHeight(property, label, true);

            float height = HeaderHeight;

            if (property.isExpanded)
            {
                height += ListTopPadding + ListBottomPadding;

                if (property.arraySize == 0)
                {
                    height += ElementHeight + ElementPadding;
                }
                else
                {
                    for (int i = 0; i < property.arraySize; i++)
                    {
                        var element = property.GetArrayElementAtIndex(i);
                        height += EditorGUI.GetPropertyHeight(element, true) + ElementPadding;
                    }
                }
            }

            return height;
        }

        public static void DrawStyledListLayout(SerializedProperty property, GUIContent label)
        {
            DrawStyledListLayout(property, label, default);
        }

        public static void DrawStyledListLayout(SerializedProperty property, GUIContent label, ListCallbacks callbacks)
        {
            var height = GetStyledListHeight(property, label);
            var rect = EditorGUILayout.GetControlRect(false, height);
            DrawStyledList(rect, property, label, callbacks);
        }

        private static void DrawHeader(Rect rect, SerializedProperty property, GUIContent label)
        {
            var evt = Event.current;

            if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
            {
                ShowHeaderContextMenu(property);
                evt.Use();
            }

            if (evt.type == EventType.Repaint)
                HeaderBackground.Draw(rect, false, false, false, false);

            var foldoutRect = new Rect(
                rect.x + Padding + 8f,
                rect.y + 1f,
                rect.width - Padding * 2f - RemoveButtonWidth - 8f,
                rect.height - 2f
            );
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            var addButtonRect = new Rect(
                rect.xMax - RemoveButtonWidth - Padding + 4f,
                rect.y + 2f,
                RemoveButtonWidth,
                rect.height - 4f
            );
            if (GUI.Button(addButtonRect, AddIcon, FooterButton))
            {
                int newIndex = property.arraySize;
                property.arraySize++;
                property.serializedObject.ApplyModifiedProperties();
                activeCallbacks.onAdded?.Invoke(property.GetArrayElementAtIndex(newIndex), newIndex);
            }
        }

        private static void ShowHeaderContextMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();

            if (property.arraySize > 0)
            {
                menu.AddItem(new GUIContent("Clear"), false, () =>
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Clear List");
                    property.ClearArray();
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Clear"));
            }

            menu.ShowAsContext();
        }

        private static void DrawListBackground(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
                ListBackground.Draw(rect, false, false, false, false);
        }

        private static void DrawElements(Rect listRect, SerializedProperty property)
        {
            var contentRect = new Rect(
                listRect.x + 1f,
                listRect.y + ListTopPadding,
                listRect.width - 2f,
                listRect.height - ListTopPadding - ListBottomPadding
            );

            if (property.arraySize == 0)
            {
                DrawEmptyElement(contentRect);
                return;
            }

            var elementHeights = new float[property.arraySize];
            for (int i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                elementHeights[i] = EditorGUI.GetPropertyHeight(element, true) + ElementPadding;
            }

            bool isDragging = dragPropertyPath == property.propertyPath && dragIndex >= 0;
            HandleDragAndDrop(contentRect, property, elementHeights);

            if (isDragging && Event.current.type == EventType.Repaint)
            {
                DrawElementsWhileDragging(contentRect, property, elementHeights);
            }
            else
            {
                float y = contentRect.y;
                for (int i = 0; i < property.arraySize; i++)
                {
                    var element = property.GetArrayElementAtIndex(i);
                    var elementRect = new Rect(contentRect.x, y, contentRect.width, elementHeights[i]);
                    DrawElement(elementRect, property, element, i);
                    y += elementHeights[i];
                }
            }
        }

        private static void DrawElementsWhileDragging(Rect contentRect, SerializedProperty property, float[] elementHeights)
        {
            float draggedElementHeight = elementHeights[dragIndex];

            float draggedY = Mathf.Clamp(
                dragMouseY - dragOffset,
                contentRect.y,
                contentRect.yMax - draggedElementHeight
            );

            float y = contentRect.y;
            bool passedDragTarget = false;

            for (int i = 0; i < property.arraySize; i++)
            {
                if (i == dragIndex)
                    continue;

                if (!passedDragTarget && i >= targetIndex)
                {
                    y += draggedElementHeight;
                    passedDragTarget = true;
                }

                var element = property.GetArrayElementAtIndex(i);
                var elementRect = new Rect(contentRect.x, y, contentRect.width, elementHeights[i]);
                DrawElement(elementRect, property, element, i);

                y += elementHeights[i];
            }

            if (!passedDragTarget)
            {
                y += draggedElementHeight;
            }

            var draggedElement = property.GetArrayElementAtIndex(dragIndex);
            var draggedRect = new Rect(contentRect.x, draggedY, contentRect.width, draggedElementHeight);
            DrawElement(draggedRect, property, draggedElement, dragIndex);
        }

        private static void HandleDragAndDrop(Rect contentRect, SerializedProperty property, float[] elementHeights)
        {
            var evt = Event.current;
            var propertyPath = property.propertyPath;

            var elementYPositions = new float[property.arraySize];
            float y = contentRect.y;
            for (int i = 0; i < property.arraySize; i++)
            {
                elementYPositions[i] = y;
                y += elementHeights[i];
            }

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0)
                    {
                        for (int i = 0; i < property.arraySize; i++)
                        {
                            var handleRect = new Rect(
                                contentRect.x,
                                elementYPositions[i],
                                DragHandleWidth,
                                elementHeights[i]
                            );

                            if (handleRect.Contains(evt.mousePosition))
                            {
                                dragIndex = i;
                                targetIndex = i;
                                dragPropertyPath = propertyPath;
                                dragOffset = evt.mousePosition.y - elementYPositions[i];
                                dragMouseY = evt.mousePosition.y;
                                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                                evt.Use();
                                break;
                            }
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (dragPropertyPath == propertyPath && dragIndex >= 0)
                    {
                        dragMouseY = evt.mousePosition.y;

                        float draggedY = dragMouseY - dragOffset;
                        float draggedMidY = draggedY + elementHeights[dragIndex] / 2f;

                        targetIndex = property.arraySize;
                        float checkY = contentRect.y;

                        for (int i = 0; i < property.arraySize; i++)
                        {
                            if (i == dragIndex)
                                continue;

                            float elementMidY = checkY + elementHeights[i] / 2f;
                            if (draggedMidY < elementMidY)
                            {
                                targetIndex = i;
                                break;
                            }
                            checkY += elementHeights[i];
                        }

                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (dragPropertyPath == propertyPath && dragIndex >= 0)
                    {
                        if (targetIndex != dragIndex)
                        {
                            int finalTargetIndex = StyledListUtility.targetIndex > dragIndex ? StyledListUtility.targetIndex - 1 : StyledListUtility.targetIndex;

                            if (finalTargetIndex != dragIndex)
                            {
                                Undo.RecordObject(property.serializedObject.targetObject, "Reorder List");
                                property.MoveArrayElement(dragIndex, finalTargetIndex);
                                property.serializedObject.ApplyModifiedProperties();

                                MoveArrayExpandedState(property, dragIndex, finalTargetIndex);
                                activeCallbacks.onReordered?.Invoke(dragIndex, finalTargetIndex);
                            }
                        }

                        dragIndex = -1;
                        targetIndex = -1;
                        dragPropertyPath = null;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;

                case EventType.Repaint:
                    if (dragPropertyPath == propertyPath && dragIndex >= 0)
                    {
                        EditorWindow.focusedWindow?.Repaint();
                    }
                    break;
            }
        }

        private static void MoveArrayExpandedState(SerializedProperty property, int fromIndex, int toIndex)
        {
            if (!moveArrayExpandedStateInitialized)
            {
                moveArrayExpandedStateInitialized = true;
                moveArrayExpandedState = typeof(EditorGUIUtility).GetMethod(
                    "MoveArrayExpandedState",
                    BindingFlags.NonPublic | BindingFlags.Static
                );
            }

            moveArrayExpandedState?.Invoke(null, new object[] { property, fromIndex, toIndex });
        }

        private static void DrawEmptyElement(Rect contentRect)
        {
            var elementRect = new Rect(
                contentRect.x,
                contentRect.y,
                contentRect.width,
                ElementHeight + ElementPadding
            );

            if (Event.current.type == EventType.Repaint)
                ElementBackground.Draw(elementRect, false, false, false, false);

            var labelRect = new Rect(
                elementRect.x + Padding,
                elementRect.y,
                elementRect.width - Padding * 2f,
                elementRect.height
            );
            EditorGUI.LabelField(labelRect, "List is Empty");
        }

        private static void DrawElement(Rect rect, SerializedProperty arrayProperty, SerializedProperty element, int index)
        {
            if (Event.current.type == EventType.Repaint)
            {
                var bgColor = index % 2 == 0 ? EvenBackground : OddBackground;
                EditorGUI.DrawRect(rect, bgColor);

                ElementBackground.Draw(rect, false, false, false, false);

                var handleRect = new Rect(
                    rect.x + 5f,
                    rect.y + rect.height / 2f - 3f,
                    10f,
                    6f
                );
                DragHandle.Draw(handleRect, false, false, false, false);
            }

            var dragHandleArea = new Rect(rect.x, rect.y, DragHandleWidth, rect.height);
            EditorGUIUtility.AddCursorRect(dragHandleArea, MouseCursor.Pan);

            const float foldoutArrowWidth = 14f;
            float leftOffset = element.hasVisibleChildren ? foldoutArrowWidth : 0f;
            var contentRect = new Rect(
                rect.x + DragHandleWidth + leftOffset,
                rect.y + ElementPadding / 2f,
                rect.width - DragHandleWidth - leftOffset - RemoveButtonWidth - Padding,
                rect.height - ElementPadding
            );
            EditorGUI.PropertyField(contentRect, element, GUIContent.none, true);

            var removeButtonRect = new Rect(
                rect.xMax - RemoveButtonWidth - 2f,
                rect.y + ElementPadding / 2f,
                RemoveButtonWidth,
                rect.height - ElementPadding
            );
            if (GUI.Button(removeButtonRect, RemoveIcon, FooterButton))
            {
                activeCallbacks.onRemoved?.Invoke(element, index);
                arrayProperty.DeleteArrayElementAtIndex(index);
                arrayProperty.serializedObject.ApplyModifiedProperties();
            }
        }
    }

}
