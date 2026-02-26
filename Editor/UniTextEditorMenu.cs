using UnityEngine;
using UnityEditor;
using System.Reflection;
namespace LightSide
{
    public class UniTextEditorMenu
    {
        [MenuItem("GameObject/UI/Text - UniText", false, 1990)]
        private static void CreateUniTextObject()
        {
            if(Selection.gameObjects == null || Selection.gameObjects.Length == 0) 
            {
                CreateUniTextObject(null);
            } 
            else
            {
                foreach (var obj in Selection.gameObjects) {
                    CreateUniTextObject(obj);
                }
            }
                
        }

        private static void CreateUniTextObject(GameObject parent)
        {
            var go = new GameObject("UniText");
            if (parent != null) 
            {
                go.transform.SetParent(parent.transform, false);
                go.layer = parent.gameObject.layer;
            }
            var text = go.AddComponent<UniText>();
            var font = text.GetType().GetField("fontStack", BindingFlags.NonPublic | BindingFlags.Instance);
            if (font != null)
            {
                font.SetValue(text, UniTextSettings.DefaultFontStack);
            }
            var appearance = text.GetType().GetField("appearance", BindingFlags.NonPublic | BindingFlags.Instance);
            if (appearance != null)
            {
                appearance.SetValue(text, UniTextSettings.DefaultAppearance);
            }
            Undo.RegisterCreatedObjectUndo(go, "CreateUniTextObject");
            Selection.activeObject = go;
        }
    }
}
