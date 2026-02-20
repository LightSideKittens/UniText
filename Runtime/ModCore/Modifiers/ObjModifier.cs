using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Manages instantiation and positioning of a RectTransform prefab for inline objects.
    /// </summary>
    internal class RectTransformWrapper
    {
        private static List<ICanvasElement> canvasElementsBuffer;
        public RectTransform instance;
        public RectTransform prefab;
        public Transform parent;
        public Vector2 anchoredPosition;
        public Vector2 pivot;
        public Vector2 sizeDelta;
        private bool created;
        public bool isDirty;
        public bool needDestroy;

        public void Setup()
        {
            if (needDestroy)
            {
                needDestroy = false;
                created = false;
                Destroy();
            }
            
            if (!isDirty) return;
            isDirty = false;
            if (!created)
            {
                if(!prefab) return;
                instance = Object.Instantiate(prefab, parent);
                created = true;
#if UNITY_EDITOR
                instance.gameObject.hideFlags = HideFlags.HideAndDontSave;
                ObjTracker.Track(instance.gameObject, this);
#endif
            }

            instance.localScale = Vector3.one;
            instance.anchorMin = new Vector2(0, 1);
            instance.anchorMax = new Vector2(0, 1);
            instance.anchoredPosition = anchoredPosition;
            instance.pivot = pivot;
            instance.sizeDelta = sizeDelta;

            canvasElementsBuffer ??= new List<ICanvasElement>();
            instance.GetComponentsInChildren(canvasElementsBuffer);
            for (var i = 0; i <= (int)CanvasUpdate.PostLayout; i++)
            for (var j = 0; j < canvasElementsBuffer.Count; j++)
                canvasElementsBuffer[j].Rebuild((CanvasUpdate)i);

            for (var i = (int)CanvasUpdate.PreRender; i < (int)CanvasUpdate.LatePreRender; i++)
            for (var j = 0; j < canvasElementsBuffer.Count; j++)
                canvasElementsBuffer[j].Rebuild((CanvasUpdate)i);
        }


        public void Destroy()
        {
            if(instance == null) return;
            ObjectUtils.SafeDestroy(instance.gameObject);
            instance = null;
            created = false;
        }
    }

    /// <summary>
    /// Defines an inline object that can be embedded within text flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inline objects are referenced by name in the text and rendered as UI prefabs
    /// that flow with the text like characters.
    /// </para>
    /// <para>
    /// All metrics are in em units (relative to fontSize):
    /// <list type="bullet">
    /// <item>width=1, height=1 means the object is fontSize x fontSize pixels</item>
    /// <item>advance=1 means the cursor moves by fontSize pixels after this object</item>
    /// <item>bearingX/Y are offsets relative to fontSize</item>
    /// </list>
    /// </para>
    /// </remarks>
    [Serializable]
    public class InlineObject
    {
        public string name;
        public RectTransform prefab;
        /// <summary>Width in em units (1 = fontSize).</summary>
        public float width = 1;
        /// <summary>Height in em units (1 = fontSize).</summary>
        public float height = 1;
        /// <summary>Horizontal offset in em units.</summary>
        public float bearingX;
        /// <summary>Vertical offset in em units.</summary>
        public float bearingY;
        /// <summary>Advance width in em units (1 = fontSize).</summary>
        public float advance = 1;

        [NonSerialized] public int activeCount;
        [NonSerialized] internal List<RectTransformWrapper> instances = new();

        internal RectTransformWrapper GetOrCreate(Transform parent)
        {
            activeCount++;

            if (activeCount <= instances.Count)
            {
                var instance = instances[activeCount - 1];
                instance.prefab = prefab;
                return instance;
            }

            var wrapper = new RectTransformWrapper();
            wrapper.prefab = prefab;
            wrapper.parent = parent;
            instances.Add(wrapper);
            return wrapper;
        }

        public void UpdateInstances()
        {
            var diff = activeCount - instances.Count;

            if (diff < 0)
            {
                diff *= -1;
                for (var i = 0; i < diff; i++)
                {
                    var last = instances.Count - 1;
                    instances[last].Destroy();
                    instances.RemoveAt(last);
                }
            }

            for (var i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                instance.Setup();
            }
        }
    }

    /// <summary>
    /// Embeds UI prefabs (images, icons, custom elements) inline with text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usage: <c>&lt;obj=iconName&gt;placeholder&lt;/obj&gt;</c>
    /// </para>
    /// <para>
    /// The modifier replaces placeholder text with instantiated prefabs positioned
    /// in the text flow. Objects are defined in the <see cref="objects"/> list
    /// and referenced by name in the tag parameter.
    /// </para>
    /// <para>
    /// All metrics are in em units (relative to fontSize). With width=1, height=1
    /// the object will be fontSize x fontSize pixels, matching the size of a typical character.
    /// </para>
    /// </remarks>
    /// <seealso cref="ObjParseRule"/>
    /// <seealso cref="InlineObject"/>
    [Serializable]
    [TypeGroup("Inline", 5)]
    public class ObjModifier : BaseModifier
    {
        public StyledList<InlineObject> objects = new();

        private FastIntDictionary<InlineObject> clusterToObj;
        private Dictionary<string, InlineObject> objLookup;

        protected override void OnEnable()
        {
            clusterToObj ??= new FastIntDictionary<InlineObject>(16);
            clusterToObj.Clear();

            if (objLookup == null)
            {
                objLookup = new Dictionary<string, InlineObject>(objects.Count);
                for (var i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    if (!string.IsNullOrEmpty(obj.name))
                        objLookup[obj.name] = obj;
                }
            }

            UniText.MeshApplied += OnPreRender;
            uniText.TextProcessor.Shaped += OnShaped;
            uniText.MeshGenerator.OnRebuildStart += OnRebuildStart;
            uniText.MeshGenerator.OnRebuildEnd += OnRebuildEnd;
        }

        protected override void OnDisable()
        {
            UniText.MeshApplied -= OnPreRender;
            uniText.TextProcessor.Shaped -= OnShaped;
            uniText.MeshGenerator.OnRebuildStart -= OnRebuildStart;
            uniText.MeshGenerator.OnRebuildEnd -= OnRebuildEnd;

            for (var i = 0; i < objects.Count; i++)
                objects[i].activeCount = 0;

            UniText.MeshApplied -= CleanupOnDisable;
            UniText.MeshApplied += CleanupOnDisable;
        }

        private void CleanupOnDisable()
        {
            UniText.MeshApplied -= CleanupOnDisable;
            if (uniText == null) return;

            for (var i = 0; i < objects.Count; i++)
                objects[i].UpdateInstances();
        }

        private void OnPreRender()
        {
            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                obj.UpdateInstances();
            }
        }

        protected override void OnDestroy()
        {
            DestroyAllObjects();
            clusterToObj?.Clear();
            clusterToObj = null;
            objLookup?.Clear();
            objLookup = null;
        }
        
        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            if (objLookup == null || !objLookup.TryGetValue(parameter, out var obj)) return;
            clusterToObj[start] = obj;
        }

        private void OnRebuildStart()
        {
            for (var i = 0; i < objects.Count; i++)
                objects[i].activeCount = 0;
        }

        private void OnShaped()
        {
            if (clusterToObj == null || clusterToObj.Count == 0) return;

            var buf = buffers;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : uniText.FontSize;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                float width = 0;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var globalCluster = glyphs[g].cluster;

                    if (clusterToObj.TryGetValue(globalCluster, out var obj))
                    {
                        glyphs[g].glyphId = -1;
                        glyphs[g].advanceX = obj.advance * fontSize;
                        glyphs[g].offsetX = obj.bearingX * fontSize;
                        glyphs[g].offsetY = obj.bearingY * fontSize;
                    }

                    width += glyphs[g].advanceX;
                }

                run.width = width;
            }
        }
        

        private void OnRebuildEnd()
        {
            if (clusterToObj == null || clusterToObj.Count == 0) return;

            var glyphs = uniText.ResultGlyphs;
            var fontSize = UniTextMeshGenerator.Current.FontSize;

            for (var i = 0; i < glyphs.Length; i++)
            {
                if (clusterToObj.TryGetValue(glyphs[i].cluster, out var obj))
                {
                    if (obj.prefab is null) continue;
                    var glyph = glyphs[i];
                    CreateObjectInstance(obj,
                        glyph.x + obj.bearingX * fontSize,
                        -glyph.y + obj.bearingY * fontSize,
                        obj.width * fontSize,
                        obj.height * fontSize);
                }
            }
        }

        
        private void CreateObjectInstance(InlineObject obj, float x, float y, float w, float h)
        {
            if (uniText == null) return;

            var wrapper = obj.GetOrCreate(uniText.cachedTransformData.rectTransform);
            wrapper.isDirty = true;
            var pivot = wrapper.pivot;
            wrapper.anchoredPosition = new Vector2(x + w * pivot.x, y + h * pivot.y);
            wrapper.sizeDelta = new Vector2(w, h);
        }

        private void DestroyAllObjects()
        {
            if (uniText == null) return;
            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                obj.activeCount = 0;
                for (int j = 0; j < obj.instances.Count; j++)
                {
                    obj.instances[j].needDestroy = true;
                }
            }

            UniText.MeshApplied += Destro;

            void Destro()
            {
                UniText.MeshApplied -= Destro;
                if (uniText == null) return;
                for (var i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    obj.UpdateInstances();
                }
            }
        }
    }
}
