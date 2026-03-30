using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>
    /// World-space text rendering component with full Unicode support.
    /// Rendering is handled by <see cref="UniTextWorldBatcher"/> — no per-object MeshRenderers.
    /// </summary>
    [ExecuteAlways]
    public class UniTextWorld : UniTextBase
    {
        #region World State

        [SerializeField]
        [Tooltip("Sorting order within the current sorting layer.")]
        private int sortingOrder;

        [SerializeField]
        [Tooltip("Sorting layer ID for render ordering.")]
        private int sortingLayerID;

        #endregion

        #region Public API

        /// <summary>Gets or sets the sorting order for render ordering.</summary>
        public int SortingOrder
        {
            get => sortingOrder;
            set
            {
                if (sortingOrder == value) return;
                sortingOrder = value;
                SetDirty(DirtyFlags.Sorting);
            }
        }

        /// <summary>Gets or sets the sorting layer ID.</summary>
        public int SortingLayerID
        {
            get => sortingLayerID;
            set
            {
                if (sortingLayerID == value) return;
                sortingLayerID = value;
                SetDirty(DirtyFlags.Sorting);
            }
        }

        #endregion

        #region Canvas Pipeline Suppression

        public override void SetAllDirty() { SetDirty(DirtyFlags.All); }
        public override void SetVerticesDirty() { }
        public override void SetMaterialDirty() { }
        protected override void OnCanvasHierarchyChanged() { }
        protected override void UpdateGeometry() { }

#if UNITY_EDITOR
        private bool validateDeferred;

        protected override void OnValidate()
        {
            if (validateDeferred) return;
            validateDeferred = true;
            EditorApplication.update += DeferredValidate;
        }

        private void DeferredValidate()
        {
            EditorApplication.update -= DeferredValidate;
            validateDeferred = false;
            if (this == null) return;
            SetAllDirty();
        }
#endif

        #endregion

        #region Abstract Implementations

        protected override bool GetHasWorldCamera() => true;

        protected override void UpdateRendering()
        {
            UniTextWorldBatcher.CopySlotData(this);
        }

        protected override void ClearAllRenderers()
        {
            UniTextWorldBatcher.ClearSlotData(this);
        }

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            UniTextWorldBatcher.Register(this);
            CleanupLegacySubMeshes();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            UniTextWorldBatcher.Unregister(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CleanupLegacySubMeshes();
        }

        /// <summary>
        /// Removes legacy child GameObjects from the old per-object rendering system.
        /// </summary>
        private void CleanupLegacySubMeshes()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("-_UTWSM_-") || child.name.StartsWith("-_DEBUG_-"))
                    ObjectUtils.SafeDestroy(child.gameObject);
            }
        }

        #endregion
    }
}
