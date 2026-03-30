using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightSide
{
    internal static class UniTextObjectMenu
    {
        [MenuItem("GameObject/UI/UniText - Text", false, 2001)]
        private static void CreateText(MenuCommand menuCommand)
        {
            var prefab = UniTextSettings.TextPrefab;
            if (prefab != null)
            {
                var go = Object.Instantiate(prefab);
                go.name = prefab.name;
                Place(go, menuCommand);
                return;
            }

            var textGo = CreateUIObject("Text (UniText)", menuCommand);
            var uniText = textGo.AddComponent<UniText>();

            var rt = textGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220f, 50f);

            SetDefaults(uniText, "Hello cats\ud83c\udf0d");

            Undo.RegisterCreatedObjectUndo(textGo, "Create UniText - Text");
            Selection.activeGameObject = textGo;
        }

        [MenuItem("GameObject/UI/UniText - Button", false, 2002)]
        private static void CreateButton(MenuCommand menuCommand)
        {
            var prefab = UniTextSettings.ButtonPrefab;
            if (prefab != null)
            {
                var go = Object.Instantiate(prefab);
                go.name = prefab.name;
                Place(go, menuCommand);
                return;
            }

            var buttonGo = CreateUIObject("Button (UniText)", menuCommand);
            var buttonRt = buttonGo.GetComponent<RectTransform>();
            buttonRt.sizeDelta = new Vector2(220f, 60f);

            var image = buttonGo.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = new Color(1f, 1f, 1f, 1f);

            var button = buttonGo.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color32(255, 255, 255, 255);
            colors.highlightedColor = new Color32(245, 245, 245, 255);
            colors.pressedColor = new Color32(200, 200, 200, 255);
            colors.selectedColor = new Color32(245, 245, 245, 255);
            colors.disabledColor = new Color32(200, 200, 200, 128);
            button.colors = colors;

            var textGo = new GameObject("Text (UniText)");
            GameObjectUtility.SetParentAndAlign(textGo, buttonGo);

            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var uniText = textGo.AddComponent<UniText>();
            uniText.color = Color.black;
            SetDefaults(uniText, "Button", HorizontalAlignment.Center, VerticalAlignment.Middle);

            Undo.RegisterCreatedObjectUndo(buttonGo, "Create UniText - Button");
            Selection.activeGameObject = buttonGo;
        }

        private static GameObject CreateUIObject(string name, MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;
            var canvas = FindOrCreateCanvas(parent);

            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, canvas.gameObject);

            if (parent != null && parent.GetComponentInParent<Canvas>() != null)
                GameObjectUtility.SetParentAndAlign(go, parent);

            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static void Place(GameObject go, MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;
            var canvas = FindOrCreateCanvas(parent);

            if (parent != null && parent.GetComponentInParent<Canvas>() != null)
                GameObjectUtility.SetParentAndAlign(go, parent);
            else
                GameObjectUtility.SetParentAndAlign(go, canvas.gameObject);

            go.layer = LayerMask.NameToLayer("UI");

            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
        }

        private static Canvas FindOrCreateCanvas(GameObject parent)
        {
            var canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            if (canvas != null) return canvas;

            canvas = Object.FindObjectOfType<Canvas>();
            if (canvas != null) return canvas;

            var canvasGo = new GameObject("Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.layer = LayerMask.NameToLayer("UI");
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var eventGo = new GameObject("EventSystem");
                eventGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                var inputType = System.Type.GetType(
                    "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputType != null)
                    eventGo.AddComponent(inputType);
                else
                    eventGo.AddComponent<StandaloneInputModule>();
#else
                eventGo.AddComponent<StandaloneInputModule>();
#endif
                eventGo.layer = LayerMask.NameToLayer("UI");
                Undo.RegisterCreatedObjectUndo(eventGo, "Create EventSystem");
            }

            return canvas;
        }

        private static void SetDefaults(UniText uniText, string text,
            HorizontalAlignment h = HorizontalAlignment.Left,
            VerticalAlignment v = VerticalAlignment.Top)
        {
            var fontStack = UniTextSettings.DefaultFontStack;
            if (fontStack != null)
                uniText.FontStack = fontStack;

            uniText.HorizontalAlignment = h;
            uniText.VerticalAlignment = v;
            uniText.Text = text;
        }
    }
}
