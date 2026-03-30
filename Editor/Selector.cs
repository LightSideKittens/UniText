using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace LightSide
{
    internal class Selector : EditorWindow
    {
        internal struct SelectorItem
        {
            public string displayName;
            public string searchText;
            public string groupName;
            public int groupOrder;
            public Texture icon;
            public Texture groupIcon;
            public string description;
            public object value;
        }

        private sealed class GroupNode
        {
            public string displayName;
            public int order;
            public Texture icon;
            public readonly List<GroupNode> subGroups = new();
            public readonly List<int> leafItems = new();
        }

        private SelectorItem[] allItems;
        private object currentValue;
        private Action<object> onSelected;
        private bool showNoneOption;

        private GroupNode rootNode;
        private bool hasAnyGroups;

        private string searchString = "";
        private readonly List<int> filteredIndices = new();
        private int flatSelectedIndex = -1;
        private Vector2 flatScrollPos;

        private Vector2 groupScrollPos;
        private readonly HashSet<int> expandedGroups = new();

        private int submenuGroupIndex = -1;
        private SelectorItem[] submenuItems;
        private Vector2 submenuScrollPos;

        private int selectedItemIndex = -1;
        private int selectedGroupIndex = -1;

        private int focusLostFrames;

        private SearchField searchField;
        private bool focusSearch = true;
        private bool showSearchField = true;
        private float baseWidth;
        private float baseHeight;

        private const float SearchHeight = 22f;
        private const float ItemHeight = 20f;
        private const float SeparatorHeight = 1f;
        private const float WindowPadding = 2f;
        private const float IconSize = 16f;
        private const int MaxVisibleItems = 15;
        private const float MinWidth = 200f;
        private const float CheckmarkWidth = 20f;
        private const float SubmenuWidth = 220f;
        private const float ArrowWidth = 20f;
        private const float InlineIndent = 16f;

        private static GUIStyle itemStyle;
        private static GUIStyle itemSelectedStyle;
        private static GUIStyle groupHeaderStyle;
        private static GUIStyle groupHeaderHighlightStyle;
        private static GUIStyle arrowStyle;
        private static GUIStyle descriptionStyle;

        #region Public API

        internal static void Show(Rect buttonRect, SelectorItem[] items, object currentValue,
            Action<object> onSelected, bool showNone = false, bool showSearch = true)
        {
            PreProcessItems(items);

            var window = CreateInstance<Selector>();
            window.allItems = items;
            window.currentValue = currentValue;
            window.onSelected = onSelected;
            window.showNoneOption = showNone;
            window.showSearchField = showSearch;
            window.wantsMouseMove = true;
            window.BuildGroupTree();
            window.RebuildFilter();

            var width = Mathf.Max(buttonRect.width, MinWidth);
            window.baseWidth = width;

            var visibleCount = window.CountInitialVisibleItems();
            var contentHeight = Mathf.Min(visibleCount, MaxVisibleItems) * ItemHeight;
            var height = (showSearch ? SearchHeight : 0) + contentHeight + WindowPadding * 2 + 4;
            window.baseHeight = height;

            var totalWidth = window.hasAnyGroups ? width + SubmenuWidth : width;

            var screenRect = GUIUtility.GUIToScreenRect(buttonRect);
            window.position = new Rect(screenRect.x, screenRect.yMax, totalWidth, height);
            window.ShowPopup();
            window.Focus();
        }

        internal static void ShowForEnum<T>(Rect rect, T currentValue, Action<T> onSelected) where T : Enum
        {
            var names = Enum.GetNames(typeof(T));
            var values = Enum.GetValues(typeof(T));
            var items = new SelectorItem[names.Length];

            for (var i = 0; i < names.Length; i++)
            {
                var niceName = ObjectNames.NicifyVariableName(names[i]);
                items[i] = new SelectorItem
                {
                    displayName = niceName,
                    searchText = niceName.ToLowerInvariant(),
                    value = values.GetValue(i)
                };
            }

            Show(rect, items, currentValue, v => onSelected((T)v));
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            searchField = new SearchField();
            EditorApplication.update += MonitorFocus;
        }

        private void OnDisable()
        {
            EditorApplication.update -= MonitorFocus;
        }

        private void MonitorFocus()
        {
            var focused = focusedWindow;
            if (focused == null || focused == this)
            {
                focusLostFrames = 0;
                return;
            }

            focusLostFrames++;
            if (focusLostFrames >= 5)
                Close();
        }

        private void OnGUI()
        {
            if (allItems == null)
            {
                Close();
                return;
            }

            InitStyles();

            var isSearching = !string.IsNullOrEmpty(searchString);
            var useGrouped = hasAnyGroups && !isSearching;
            var hasSubmenu = useGrouped && submenuGroupIndex >= 0 && submenuItems != null;

            if (useGrouped)
                HandleGroupedKeyboard();
            else
                HandleFlatKeyboard();

            var mainWidth = useGrouped ? baseWidth : position.width;

            float contentY;

            if (showSearchField)
            {
                var searchRect = new Rect(WindowPadding, WindowPadding,
                    mainWidth - WindowPadding * 2, SearchHeight);

                if (focusSearch)
                {
                    searchField.SetFocus();
                    focusSearch = false;
                }

                var newSearch = searchField.OnGUI(searchRect, searchString);
                if (newSearch != searchString)
                {
                    searchString = newSearch;
                    RebuildFilter();
                    flatSelectedIndex = filteredIndices.Count > 0 ? 0 : -1;
                    selectedItemIndex = -1;
                    selectedGroupIndex = -1;
                    CloseSubmenu();

                    var newIsSearching = !string.IsNullOrEmpty(searchString);
                    var wantWidth = hasAnyGroups && !newIsSearching ? baseWidth + SubmenuWidth : baseWidth;
                    if (Mathf.Abs(position.width - wantWidth) > 1f)
                    {
                        var pos = position;
                        pos.width = wantWidth;
                        position = pos;
                    }
                }

                var sepY = searchRect.yMax + 2;
                EditorGUI.DrawRect(new Rect(0, sepY, mainWidth, SeparatorHeight), new Color(0, 0, 0, 0.3f));
                contentY = sepY + SeparatorHeight + 1;
            }
            else
            {
                contentY = WindowPadding;
            }

            var contentRect = new Rect(0, contentY, mainWidth, position.height - contentY);

            if (useGrouped)
                DrawGroupedContent(contentRect);
            else
                DrawFlatContent(contentRect);

            if (useGrouped)
                DrawSubmenuPanel(contentY);

            if (Event.current.type == EventType.MouseMove)
                Repaint();

            if (Event.current.type == EventType.Repaint)
            {
                var borderColor = EditorGUIUtility.isProSkin
                    ? new Color(0.1f, 0.1f, 0.1f, 1f)
                    : new Color(0.35f, 0.35f, 0.35f, 1f);
                var w = position.width;
                var h = position.height;
                EditorGUI.DrawRect(new Rect(0, 0, w, 1), borderColor);
                EditorGUI.DrawRect(new Rect(0, h - 1, w, 1), borderColor);
                EditorGUI.DrawRect(new Rect(0, 0, 1, h), borderColor);
                EditorGUI.DrawRect(new Rect(w - 1, 0, 1, h), borderColor);
            }
        }

        #endregion

        #region Tree Building

        private void BuildGroupTree()
        {
            rootNode = new GroupNode();
            hasAnyGroups = false;

            for (var i = 0; i < allItems.Length; i++)
            {
                var groupPath = allItems[i].groupName;
                if (string.IsNullOrEmpty(groupPath))
                {
                    rootNode.leafItems.Add(i);
                    continue;
                }

                hasAnyGroups = true;
                var parts = groupPath.Split('/');
                var current = rootNode;

                for (var p = 0; p < parts.Length; p++)
                {
                    GroupNode child = null;
                    for (var c = 0; c < current.subGroups.Count; c++)
                    {
                        if (current.subGroups[c].displayName == parts[p])
                        {
                            child = current.subGroups[c];
                            break;
                        }
                    }

                    if (child == null)
                    {
                        child = new GroupNode
                        {
                            displayName = parts[p],
                            order = allItems[i].groupOrder,
                            icon = allItems[i].groupIcon
                        };
                        current.subGroups.Add(child);
                    }

                    current = child;
                }

                current.leafItems.Add(i);
            }

            SortGroupTree(rootNode);
        }

        private static void SortGroupTree(GroupNode node)
        {
            if (node.subGroups.Count > 1)
            {
                node.subGroups.Sort((a, b) =>
                {
                    var oc = a.order.CompareTo(b.order);
                    return oc != 0
                        ? oc
                        : string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
                });
            }

            for (var i = 0; i < node.subGroups.Count; i++)
                SortGroupTree(node.subGroups[i]);
        }

        #endregion

        #region Grouped Mode

        private void DrawGroupedContent(Rect contentRect)
        {
            var group = rootNode;
            var totalHeight = CalculateGroupedTotalHeight(group);

            var hasScrollbar = totalHeight > contentRect.height;
            var viewWidth = contentRect.width - (hasScrollbar ? 14 : 0);
            var viewRect = new Rect(0, 0, viewWidth, totalHeight);

            groupScrollPos = GUI.BeginScrollView(contentRect, groupScrollPos, viewRect);

            var y = 0f;

            if (showNoneOption)
            {
                var noneRect = new Rect(0, y, viewWidth, ItemHeight);
                DrawSelectableItem(noneRect, "(None)", null, currentValue == null,
                    false, IsHovered(noneRect), () => DoSelect(null));
                y += ItemHeight;

                if (group.subGroups.Count > 0 || group.leafItems.Count > 0)
                {
                    EditorGUI.DrawRect(new Rect(4, y, viewWidth - 8, SeparatorHeight),
                        new Color(0, 0, 0, 0.2f));
                    y += 3;
                }
            }

            for (var i = 0; i < group.leafItems.Count; i++)
            {
                var itemIdx = group.leafItems[i];
                ref var item = ref allItems[itemIdx];
                var itemRect = new Rect(0, y, viewWidth, ItemHeight);
                var isCurrent = item.value != null && item.value.Equals(currentValue);
                var isSelected = selectedItemIndex == itemIdx && selectedGroupIndex < 0;
                var isHovered = IsHovered(itemRect);
                var capturedIdx = itemIdx;
                var capturedValue = item.value;

                DrawSelectableItem(itemRect, item.displayName, item.icon, isCurrent,
                    isSelected, isHovered, () =>
                    {
                        if (selectedItemIndex == capturedIdx && selectedGroupIndex < 0)
                            DoSelect(capturedValue);
                        else
                            SelectItem(capturedIdx);
                    });

                y += ItemHeight;
            }

            if (group.leafItems.Count > 0 && group.subGroups.Count > 0)
            {
                EditorGUI.DrawRect(new Rect(4, y, viewWidth - 8, SeparatorHeight),
                    new Color(0, 0, 0, 0.2f));
                y += 3;
            }

            for (var i = 0; i < group.subGroups.Count; i++)
            {
                var sub = group.subGroups[i];
                var isExpanded = expandedGroups.Contains(i);
                var headerRect = new Rect(0, y, viewWidth, ItemHeight);
                var isActive = selectedGroupIndex == i;
                var isHovered = IsHovered(headerRect);

                if (isActive)
                    EditorGUI.DrawRect(headerRect, selectedColor);
                else if (isHovered)
                    EditorGUI.DrawRect(headerRect, hoverColor);

                var arrowLabel = isExpanded ? "\u25BC" : "\u25BA";
                GUI.Label(new Rect(headerRect.x + 4, headerRect.y, 14, headerRect.height), arrowLabel,
                    arrowStyle);

                var labelX = headerRect.x + 18;
                if (sub.icon != null)
                {
                    GUI.DrawTexture(new Rect(labelX, headerRect.y + 2, IconSize, IconSize), sub.icon, ScaleMode.ScaleToFit);
                    labelX += IconSize + 2;
                }

                GUI.Label(
                    new Rect(labelX, headerRect.y,
                        headerRect.xMax - ArrowWidth - labelX, headerRect.height),
                    sub.displayName, isActive || isHovered ? groupHeaderHighlightStyle : groupHeaderStyle);

                GUI.Label(
                    new Rect(headerRect.xMax - ArrowWidth, headerRect.y, ArrowWidth, headerRect.height),
                    "\u25BA", arrowStyle);

                var capturedGroupIdx = i;
                if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
                {
                    SelectGroup(capturedGroupIdx, sub);
                    Event.current.Use();
                }

                y += ItemHeight;

                if (isExpanded)
                {
                    for (var li = 0; li < sub.leafItems.Count; li++)
                    {
                        var itemIdx = sub.leafItems[li];
                        ref var item = ref allItems[itemIdx];
                        var itemRect = new Rect(InlineIndent, y, viewWidth - InlineIndent, ItemHeight);
                        var isCurrent = item.value != null && item.value.Equals(currentValue);
                        var capturedValue = item.value;

                        DrawSelectableItem(itemRect, item.displayName, item.icon, isCurrent,
                            false, IsHovered(itemRect), () => DoSelect(capturedValue));

                        y += ItemHeight;
                    }

                    for (var si = 0; si < sub.subGroups.Count; si++)
                    {
                        var nested = sub.subGroups[si];
                        var nestedKey = (i + 1) * 1000 + si;
                        var nestedExpanded = expandedGroups.Contains(nestedKey);
                        var nestedRect = new Rect(InlineIndent, y, viewWidth - InlineIndent, ItemHeight);
                        var nestedHovered = IsHovered(nestedRect);

                        if (nestedHovered)
                            EditorGUI.DrawRect(nestedRect, hoverColor);

                        var nestedArrow = nestedExpanded ? "\u25BC" : "\u25BA";
                        GUI.Label(new Rect(nestedRect.x + 4, nestedRect.y, 14, nestedRect.height),
                            nestedArrow, arrowStyle);
                        GUI.Label(
                            new Rect(nestedRect.x + 18, nestedRect.y,
                                nestedRect.width - 18, nestedRect.height),
                            nested.displayName, nestedHovered ? groupHeaderHighlightStyle : groupHeaderStyle);

                        if (Event.current.type == EventType.MouseDown &&
                            nestedRect.Contains(Event.current.mousePosition))
                        {
                            if (nestedExpanded)
                                expandedGroups.Remove(nestedKey);
                            else
                                expandedGroups.Add(nestedKey);
                            Event.current.Use();
                        }

                        y += ItemHeight;

                        if (nestedExpanded)
                        {
                            for (var nli = 0; nli < nested.leafItems.Count; nli++)
                            {
                                var nItemIdx = nested.leafItems[nli];
                                ref var nItem = ref allItems[nItemIdx];
                                var nItemRect = new Rect(InlineIndent * 2, y,
                                    viewWidth - InlineIndent * 2, ItemHeight);
                                var nIsCurrent = nItem.value != null && nItem.value.Equals(currentValue);
                                var nCapturedValue = nItem.value;

                                DrawSelectableItem(nItemRect, nItem.displayName, nItem.icon, nIsCurrent,
                                    false, IsHovered(nItemRect), () => DoSelect(nCapturedValue));

                                y += ItemHeight;
                            }
                        }
                    }
                }
            }

            GUI.EndScrollView();
        }

        private float CalculateGroupedTotalHeight(GroupNode group)
        {
            var height = 0f;

            if (showNoneOption)
                height += ItemHeight + 3;

            height += group.leafItems.Count * ItemHeight;

            if (group.leafItems.Count > 0 && group.subGroups.Count > 0)
                height += 3;

            for (var i = 0; i < group.subGroups.Count; i++)
            {
                height += ItemHeight;

                if (expandedGroups.Contains(i))
                {
                    var sub = group.subGroups[i];
                    height += sub.leafItems.Count * ItemHeight;

                    for (var si = 0; si < sub.subGroups.Count; si++)
                    {
                        height += ItemHeight;
                        var nestedKey = (i + 1) * 1000 + si;
                        if (expandedGroups.Contains(nestedKey))
                            height += sub.subGroups[si].leafItems.Count * ItemHeight;
                    }
                }
            }
            return height;
        }

        private void HandleGroupedKeyboard()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;

            if (evt.keyCode == KeyCode.Escape)
            {
                if (submenuGroupIndex >= 0)
                    CloseSubmenu();
                else
                    Close();
                evt.Use();
            }
        }

        #endregion

        #region Submenu Panel

        private void OpenSubmenu(int groupIndex, GroupNode sub)
        {
            var items = CollectGroupItems(sub);
            if (items.Length == 0)
            {
                CloseSubmenu();
                return;
            }

            PreProcessItems(items);

            submenuGroupIndex = groupIndex;
            submenuItems = items;
            submenuScrollPos = Vector2.zero;

            var submenuContentHeight = Mathf.Min(items.Length, MaxVisibleItems) * ItemHeight;
            var submenuNeededHeight = SearchHeight + submenuContentHeight + WindowPadding * 2 + 4;
            var neededHeight = Mathf.Max(baseHeight, submenuNeededHeight);

            if (Mathf.Abs(position.height - neededHeight) > 1f)
            {
                var pos = position;
                pos.height = neededHeight;
                position = pos;
            }
        }

        private void CloseSubmenu()
        {
            if (submenuGroupIndex < 0) return;

            submenuGroupIndex = -1;
            submenuItems = null;

            if (Mathf.Abs(position.height - baseHeight) > 1f)
            {
                var pos = position;
                pos.height = baseHeight;
                position = pos;
            }
        }

        private void DrawSubmenuPanel(float contentY)
        {
            var panelX = baseWidth;
            var panelWidth = SubmenuWidth;

            var bgColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);
            EditorGUI.DrawRect(new Rect(panelX, 0, panelWidth, position.height), bgColor);

            EditorGUI.DrawRect(new Rect(panelX, 0, 1, position.height), new Color(0, 0, 0, 0.3f));
            panelX += 1;
            panelWidth -= 1;

            if (selectedGroupIndex < 0 && selectedItemIndex < 0)
            {
                var hintRect = new Rect(panelX + 8, contentY + 8, panelWidth - 16, ItemHeight);
                GUI.Label(hintRect, "Click an item to preview", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (selectedItemIndex >= 0 && selectedGroupIndex < 0)
            {
                DrawItemDescription(panelX, panelWidth, contentY);
                return;
            }

            if (submenuGroupIndex < 0 || submenuItems == null) return;

            var groupNode = rootNode.subGroups[submenuGroupIndex];
            var headerRect = new Rect(panelX + 4, WindowPadding, panelWidth - 8, SearchHeight);
            GUI.Label(headerRect, groupNode.displayName, groupHeaderStyle);

            var hSepY = headerRect.yMax + 2;
            EditorGUI.DrawRect(new Rect(panelX, hSepY, panelWidth, SeparatorHeight), new Color(0, 0, 0, 0.3f));

            var itemsY = hSepY + SeparatorHeight + 1;
            var itemsHeight = position.height - itemsY;
            var itemsRect = new Rect(panelX, itemsY, panelWidth, itemsHeight);

            var totalItemsHeight = submenuItems.Length * ItemHeight;
            var hasScroll = totalItemsHeight > itemsHeight;
            var itemViewWidth = panelWidth - (hasScroll ? 14 : 0);
            var viewRect = new Rect(0, 0, itemViewWidth, totalItemsHeight);

            submenuScrollPos = GUI.BeginScrollView(itemsRect, submenuScrollPos, viewRect);

            var y = 0f;
            string lastGroup = null;
            var hasMultipleGroups = SubmenuHasMultipleGroups();

            for (var i = 0; i < submenuItems.Length; i++)
            {
                ref var item = ref submenuItems[i];

                if (hasMultipleGroups && !string.IsNullOrEmpty(item.groupName) &&
                    item.groupName != lastGroup)
                {
                    if (lastGroup != null)
                    {
                        EditorGUI.DrawRect(new Rect(4, y, itemViewWidth - 8, SeparatorHeight),
                            new Color(0, 0, 0, 0.2f));
                        y += SeparatorHeight + 2;
                    }

                    var groupHeaderRect = new Rect(0, y, itemViewWidth, ItemHeight);
                    GUI.Label(groupHeaderRect, "  " + item.groupName, groupHeaderStyle);
                    y += ItemHeight;
                    lastGroup = item.groupName;
                }
                else if (hasMultipleGroups && string.IsNullOrEmpty(item.groupName) && lastGroup != null)
                {
                    EditorGUI.DrawRect(new Rect(4, y, itemViewWidth - 8, SeparatorHeight),
                        new Color(0, 0, 0, 0.2f));
                    y += SeparatorHeight + 2;
                    lastGroup = item.groupName;
                }

                var itemRect = new Rect(0, y, itemViewWidth, ItemHeight);
                var isCurrent = item.value != null && item.value.Equals(currentValue);
                var capturedValue = item.value;

                DrawSelectableItem(itemRect, item.displayName, item.icon, isCurrent,
                    false, IsHovered(itemRect), () => DoSelect(capturedValue));

                y += ItemHeight;
            }

            GUI.EndScrollView();
        }

        private void DrawItemDescription(float panelX, float panelWidth, float contentY)
        {
            ref var item = ref allItems[selectedItemIndex];

            var headerY = WindowPadding + 8;
            var headerX = panelX + 8;

            if (item.icon != null)
            {
                GUI.DrawTexture(new Rect(headerX, headerY, 24, 24), item.icon, ScaleMode.ScaleToFit);
                headerX += 28;
            }

            GUI.Label(new Rect(headerX, headerY, panelWidth - (headerX - panelX) - 8, 24),
                item.displayName, groupHeaderStyle);

            var descY = headerY + 30;
            EditorGUI.DrawRect(new Rect(panelX + 4, descY, panelWidth - 8, SeparatorHeight),
                new Color(0, 0, 0, 0.2f));
            descY += SeparatorHeight + 6;

            if (!string.IsNullOrEmpty(item.description))
            {
                var descRect = new Rect(panelX + 8, descY, panelWidth - 16, position.height - descY - 8);
                GUI.Label(descRect, item.description, descriptionStyle);
            }

            var buttonRect = new Rect(panelX + 8, position.height - 30, panelWidth - 16, 22);
            if (GUI.Button(buttonRect, "Apply"))
                DoSelect(item.value);
        }

        private bool SubmenuHasMultipleGroups()
        {
            if (submenuItems == null || submenuItems.Length == 0) return false;

            string first = null;
            for (var i = 0; i < submenuItems.Length; i++)
            {
                var group = submenuItems[i].groupName ?? "";
                if (first == null)
                    first = group;
                else if (first != group)
                    return true;
            }

            return false;
        }

        private SelectorItem[] CollectGroupItems(GroupNode group)
        {
            var result = new List<SelectorItem>();
            CollectRecursive(group, "", result);
            return result.ToArray();
        }

        private void CollectRecursive(GroupNode node, string prefix, List<SelectorItem> result)
        {
            for (var i = 0; i < node.leafItems.Count; i++)
            {
                var item = allItems[node.leafItems[i]];
                item.groupName = prefix;
                result.Add(item);
            }

            for (var i = 0; i < node.subGroups.Count; i++)
            {
                var sub = node.subGroups[i];
                var subPrefix = string.IsNullOrEmpty(prefix)
                    ? sub.displayName
                    : prefix + "/" + sub.displayName;
                CollectRecursive(sub, subPrefix, result);
            }
        }

        #endregion

        #region Select

        private void SelectItem(int itemIndex)
        {
            selectedItemIndex = itemIndex;
            selectedGroupIndex = -1;
            CloseSubmenu();
        }

        private void SelectGroup(int groupIndex, GroupNode sub)
        {
            if (selectedGroupIndex == groupIndex)
            {
                if (expandedGroups.Contains(groupIndex))
                    expandedGroups.Remove(groupIndex);
                else
                    expandedGroups.Add(groupIndex);
                return;
            }

            selectedGroupIndex = groupIndex;
            selectedItemIndex = -1;
            OpenSubmenu(groupIndex, sub);
        }

        private void DoSelect(object value)
        {
            var callback = onSelected;
            Close();
            callback?.Invoke(value);
        }

        #endregion

        #region Flat Mode (search active or no groups)

        private void DrawFlatContent(Rect contentRect)
        {
            if (filteredIndices.Count == 0 && !showNoneOption)
            {
                var noResultRect = new Rect(contentRect.x + 8, contentRect.y + 4,
                    contentRect.width - 16, ItemHeight);
                GUI.Label(noResultRect, "No results", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var totalHeight = CalculateFlatTotalHeight();
            var viewRect = new Rect(0, 0,
                contentRect.width - (totalHeight > contentRect.height ? 14 : 0), totalHeight);

            flatScrollPos = GUI.BeginScrollView(contentRect, flatScrollPos, viewRect);

            var y = 0f;
            string lastGroup = null;

            if (showNoneOption)
            {
                var noneRect = new Rect(0, y, viewRect.width, ItemHeight);
                var isNoneSelected = flatSelectedIndex == -1 && filteredIndices.Count == 0;

                DrawSelectableItem(noneRect, "(None)", null, currentValue == null,
                    isNoneSelected, IsHovered(noneRect), () => DoSelect(null));

                y += ItemHeight;

                if (filteredIndices.Count > 0)
                {
                    EditorGUI.DrawRect(new Rect(4, y, viewRect.width - 8, SeparatorHeight),
                        new Color(0, 0, 0, 0.2f));
                    y += SeparatorHeight + 2;
                }
            }

            var hasMultipleGroups = HasMultipleGroups();

            for (var fi = 0; fi < filteredIndices.Count; fi++)
            {
                var itemIndex = filteredIndices[fi];
                ref var item = ref allItems[itemIndex];

                if (hasMultipleGroups && !string.IsNullOrEmpty(item.groupName) &&
                    item.groupName != lastGroup)
                {
                    if (lastGroup != null)
                    {
                        EditorGUI.DrawRect(new Rect(4, y, viewRect.width - 8, SeparatorHeight),
                            new Color(0, 0, 0, 0.2f));
                        y += SeparatorHeight + 2;
                    }

                    var headerRect = new Rect(0, y, viewRect.width, ItemHeight);
                    GUI.Label(headerRect, "  " + item.groupName, groupHeaderStyle);
                    y += ItemHeight;
                    lastGroup = item.groupName;
                }
                else if (hasMultipleGroups && string.IsNullOrEmpty(item.groupName) && lastGroup != null)
                {
                    EditorGUI.DrawRect(new Rect(4, y, viewRect.width - 8, SeparatorHeight),
                        new Color(0, 0, 0, 0.2f));
                    y += SeparatorHeight + 2;
                    lastGroup = item.groupName;
                }

                var itemRect = new Rect(0, y, viewRect.width, ItemHeight);
                var isCurrent = item.value != null && item.value.Equals(currentValue);
                var isSelected = fi == flatSelectedIndex;
                var capturedValue = item.value;

                DrawSelectableItem(itemRect, item.displayName, item.icon, isCurrent,
                    isSelected, IsHovered(itemRect), () => DoSelect(capturedValue));

                y += ItemHeight;
            }

            GUI.EndScrollView();
        }

        private void HandleFlatKeyboard()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    flatSelectedIndex = Mathf.Min(flatSelectedIndex + 1, filteredIndices.Count - 1);
                    EnsureFlatSelectedVisible();
                    evt.Use();
                    break;

                case KeyCode.UpArrow:
                    flatSelectedIndex = Mathf.Max(flatSelectedIndex - 1, showNoneOption ? -1 : 0);
                    EnsureFlatSelectedVisible();
                    evt.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (flatSelectedIndex >= 0 && flatSelectedIndex < filteredIndices.Count)
                        DoSelect(allItems[filteredIndices[flatSelectedIndex]].value);
                    else if (flatSelectedIndex == -1 && showNoneOption)
                        DoSelect(null);
                    evt.Use();
                    break;

                case KeyCode.Escape:
                    Close();
                    evt.Use();
                    break;
            }
        }

        private void EnsureFlatSelectedVisible()
        {
            if (flatSelectedIndex < 0) return;

            var targetY = flatSelectedIndex * ItemHeight;
            var viewHeight = position.height - SearchHeight - 6;

            if (targetY < flatScrollPos.y)
                flatScrollPos.y = targetY;
            else if (targetY + ItemHeight > flatScrollPos.y + viewHeight)
                flatScrollPos.y = targetY + ItemHeight - viewHeight;

            Repaint();
        }

        #endregion

        #region Shared Helpers

        private static readonly Color hoverColor = new(0.5f, 0.5f, 0.5f, 0.2f);
        private static readonly Color selectedColor = new(0.24f, 0.49f, 0.91f, 0.5f);

        private void DrawSelectableItem(Rect rect, string label, Texture icon, bool isCurrent,
            bool selected, bool hovered, Action onClick)
        {
            if (selected)
                EditorGUI.DrawRect(rect, selectedColor);
            else if (hovered)
                EditorGUI.DrawRect(rect, hoverColor);

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
            {
                onClick?.Invoke();
                evt.Use();
            }

            var x = rect.x + 4;

            if (isCurrent)
                GUI.Label(new Rect(x, rect.y, CheckmarkWidth, rect.height), "\u2713");

            x += CheckmarkWidth;

            if (icon != null)
            {
                GUI.DrawTexture(new Rect(x, rect.y + 2, IconSize, IconSize), icon, ScaleMode.ScaleToFit);
                x += IconSize + 2;
            }

            var highlight = selected || hovered;
            GUI.Label(new Rect(x, rect.y, rect.width - x, rect.height), label,
                highlight ? itemSelectedStyle : itemStyle);
        }

        private static bool IsHovered(Rect rect)
        {
            return rect.Contains(Event.current.mousePosition);
        }

        private static void PreProcessItems(SelectorItem[] items)
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (string.IsNullOrEmpty(items[i].searchText))
                    items[i].searchText = items[i].displayName.ToLowerInvariant();
            }
        }

        private int CountInitialVisibleItems()
        {
            if (hasAnyGroups)
            {
                var count = rootNode.subGroups.Count + rootNode.leafItems.Count;
                if (showNoneOption) count += 2;
                return count;
            }

            return CountFlatVisualItems(allItems, filteredIndices, showNoneOption);
        }

        private void RebuildFilter()
        {
            filteredIndices.Clear();

            if (string.IsNullOrEmpty(searchString))
            {
                for (var i = 0; i < allItems.Length; i++)
                    filteredIndices.Add(i);
                return;
            }

            var parts = searchString.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < allItems.Length; i++)
            {
                var text = allItems[i].searchText;
                var match = true;

                for (var p = 0; p < parts.Length; p++)
                {
                    if (text.IndexOf(parts[p], StringComparison.Ordinal) < 0)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    filteredIndices.Add(i);
            }
        }

        private float CalculateFlatTotalHeight()
        {
            var height = 0f;

            if (showNoneOption)
            {
                height += ItemHeight;
                if (filteredIndices.Count > 0)
                    height += SeparatorHeight + 2;
            }

            var hasMultipleGroups = HasMultipleGroups();
            string lastGroup = null;

            for (var fi = 0; fi < filteredIndices.Count; fi++)
            {
                ref var item = ref allItems[filteredIndices[fi]];

                if (hasMultipleGroups && !string.IsNullOrEmpty(item.groupName) &&
                    item.groupName != lastGroup)
                {
                    if (lastGroup != null)
                        height += SeparatorHeight + 2;

                    height += ItemHeight;
                    lastGroup = item.groupName;
                }
                else if (hasMultipleGroups && string.IsNullOrEmpty(item.groupName) && lastGroup != null)
                {
                    height += SeparatorHeight + 2;
                    lastGroup = item.groupName;
                }

                height += ItemHeight;
            }

            return height;
        }

        private bool HasMultipleGroups()
        {
            if (string.IsNullOrEmpty(searchString))
                return HasMultipleGroupsInAll();

            string first = null;
            for (var fi = 0; fi < filteredIndices.Count; fi++)
            {
                var group = allItems[filteredIndices[fi]].groupName ?? "";
                if (first == null)
                    first = group;
                else if (first != group)
                    return true;
            }

            return false;
        }

        private bool HasMultipleGroupsInAll()
        {
            string first = null;
            for (var i = 0; i < allItems.Length; i++)
            {
                var group = allItems[i].groupName ?? "";
                if (first == null)
                    first = group;
                else if (first != group)
                    return true;
            }

            return false;
        }

        private static int CountFlatVisualItems(SelectorItem[] items, List<int> filtered, bool showNone)
        {
            var count = showNone ? 2 : 0;
            string lastGroup = null;

            for (var fi = 0; fi < filtered.Count; fi++)
            {
                var group = items[filtered[fi]].groupName ?? "";
                if (!string.IsNullOrEmpty(group) && group != lastGroup)
                {
                    count++;
                    lastGroup = group;
                }

                count++;
            }

            return count;
        }

        private static void InitStyles()
        {
            if (itemStyle != null) return;

            itemStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            itemSelectedStyle = new GUIStyle(itemStyle)
            {
                normal = { textColor = Color.white }
            };

            groupHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                padding = new RectOffset(4, 0, 2, 2)
            };

            groupHeaderHighlightStyle = new GUIStyle(groupHeaderStyle)
            {
                normal = { textColor = Color.white }
            };

            arrowStyle = new GUIStyle(itemStyle)
            {
                fontSize = 8,
                alignment = TextAnchor.MiddleCenter
            };

            descriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                padding = new RectOffset(0, 0, 0, 0)
            };
        }

        #endregion
    }
}
