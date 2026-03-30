using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Invisible singleton that batches all <see cref="UniTextWorld"/> components into combined meshes
    /// for minimal draw calls. Uses a unified shader that handles both face and effects in one pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each component's vertices are duplicated per effect pass + face into a single combined mesh.
    /// UV2 determines the mode: zeros = face, non-zero = effect. Triangle order controls visual
    /// layering (sorted by sorting layer → sorting order). Result: 1 SDF draw call + 1 emoji draw call.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("")]
    public sealed class UniTextWorldBatcher : MonoBehaviour
    {
        private static UniTextWorldBatcher instance;

        private readonly Dictionary<long, BatchGroup> groups = new();
        private readonly Dictionary<UniTextWorld, ComponentSlot> slots = new();

        private bool meshUploadNeeded;

        #region Singleton

        internal static void EnsureInstance()
        {
            if (instance != null) return;

            foreach (var b in Resources.FindObjectsOfTypeAll<UniTextWorldBatcher>())
                ObjectUtils.SafeDestroy(b.gameObject);

            var go = new GameObject("UniTextWorldBatcher")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            instance = go.AddComponent<UniTextWorldBatcher>();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void CleanupOnDomainReload()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<UniTextWorldBatcher>())
                ObjectUtils.SafeDestroy(b.gameObject);
            instance = null;
        }
#endif

        private void OnEnable()
        {
            if (instance != null && instance != this)
            {
                ObjectUtils.SafeDestroy(gameObject);
                return;
            }

            instance = this;
            UniTextBase.AfterProcess += OnAfterProcess;
        }

        private void OnDisable()
        {
            UniTextBase.AfterProcess -= OnAfterProcess;
            if (instance == this) instance = null;
        }

        private void OnDestroy()
        {
            foreach (var group in groups.Values)
                group.Destroy();
            groups.Clear();
            slots.Clear();
        }

        #endregion

        #region Registration

        internal static void Register(UniTextWorld component)
        {
            EnsureInstance();
            if (instance.slots.ContainsKey(component)) return;

            instance.slots[component] = new ComponentSlot();
        }

        internal static void Unregister(UniTextWorld component)
        {
            if (instance == null) return;

            instance.slots.Remove(component);
            instance.meshUploadNeeded = true;
        }

        #endregion

        #region Data Copy

        internal static void ClearSlotData(UniTextWorld component)
        {
            if (instance == null) return;
            if (!instance.slots.TryGetValue(component, out var slot)) return;
            slot.sdfBaseVertexCount = 0;
            slot.emojiVertexCount = 0;
            slot.numCopies = 0;
            instance.meshUploadNeeded = true;
        }

        internal static void CopySlotData(UniTextWorld component)
        {
            if (instance == null) return;
            instance.CopySlotDataInternal(component);
        }

        private void CopySlotDataInternal(UniTextWorld component)
        {
            var gen = component.MeshGenerator;
            if (gen == null || !gen.HasGeneratedData) return;
            if (!slots.TryGetValue(component, out var slot)) return;

            var sdfVc = gen.SdfVertexCount;
            var sdfTc = gen.SdfTriangleCount;
            var emojiVc = gen.EmojiVertexCount;
            var emojiTc = gen.EmojiTriangleCount;
            var passCount = gen.effectPasses.Count;
            var isMsdf = gen.RenderMode == UniTextBase.RenderModee.MSDF;
            var numCopies = passCount + 1;
            var totalSdfVc = sdfVc * numCopies;
            var totalSdfTc = sdfTc * numCopies;

            var groupKey = isMsdf ? 2L : 1L;
            var group = GetOrCreateGroup(groupKey, isMsdf);

            if (slot.groupKey != groupKey && slot.groupKey != 0)
            {
                if (groups.TryGetValue(slot.groupKey, out var oldGroup))
                    oldGroup.RemoveSlot(slot);
                slot.totalSdfCapacity = 0;
                slot.totalSdfTriCapacity = 0;
                slot.emojiCapacity = 0;
                slot.emojiTriCapacity = 0;
            }

            slot.groupKey = groupKey;

            if (totalSdfVc > slot.totalSdfCapacity || emojiVc > slot.emojiCapacity)
            {
                if (slot.totalSdfTriCapacity > 0 && group.sdfTriangles.data != null)
                    Array.Clear(group.sdfTriangles.data, slot.sdfTriStart, slot.totalSdfTriCapacity);
                if (slot.emojiTriCapacity > 0 && group.emojiTriangles.data != null)
                    Array.Clear(group.emojiTriangles.data, slot.emojiTriStart, slot.emojiTriCapacity);

                var newSdfCap = Math.Max(totalSdfVc, slot.totalSdfCapacity * 2);
                var newSdfTriCap = Math.Max(totalSdfTc, slot.totalSdfTriCapacity * 2);
                var newEmojiCap = Math.Max(emojiVc, slot.emojiCapacity * 2);
                var newEmojiTriCap = Math.Max(emojiTc, slot.emojiTriCapacity * 2);

                slot.totalSdfCapacity = newSdfCap;
                slot.totalSdfTriCapacity = newSdfTriCap;
                slot.emojiCapacity = newEmojiCap;
                slot.emojiTriCapacity = newEmojiTriCap;

                slot.sdfVertexStart = group.sdfVertexUsed;
                slot.sdfTriStart = group.sdfTriUsed;
                group.sdfVertexUsed += newSdfCap;
                group.sdfTriUsed += newSdfTriCap;

                slot.emojiVertexStart = group.emojiVertexUsed;
                slot.emojiTriStart = group.emojiTriUsed;
                group.emojiVertexUsed += newEmojiCap;
                group.emojiTriUsed += newEmojiTriCap;

                group.EnsureBufferCapacity();
            }

            slot.sdfBaseVertexCount = sdfVc;
            slot.emojiVertexCount = emojiVc;
            slot.numCopies = numCopies;
            slot.sortingOrder = component.SortingOrder;
            slot.cachedSortingLayerValue = SortingLayer.GetLayerValueFromID(component.SortingLayerID);

            if (sdfVc == 0 && emojiVc == 0)
            {
                if (slot.totalSdfTriCapacity > 0 && group.sdfTriangles.data != null)
                    Array.Clear(group.sdfTriangles.data, slot.sdfTriStart, slot.totalSdfTriCapacity);
                if (slot.emojiTriCapacity > 0 && group.emojiTriangles.data != null)
                    Array.Clear(group.emojiTriangles.data, slot.emojiTriStart, slot.emojiTriCapacity);
                meshUploadNeeded = true;
                return;
            }

            var matrix = component.transform.localToWorldMatrix;
            var verts = gen.Vertices;
            var uvs0 = gen.Uvs0;
            var uvs1 = gen.Uvs1;
            var uvs3 = gen.Uvs3;
            var colors = gen.Colors;
            var tris = gen.Triangles;
            var passes = gen.effectPasses;

            var triWriteOff = slot.sdfTriStart;

            for (var copy = 0; copy < numCopies; copy++)
            {
                var copyVertOff = slot.sdfVertexStart + copy * sdfVc;

                var isEffect = copy < passCount;

                if (isEffect)
                    passes[copy].apply();

                for (var i = 0; i < sdfVc; i++)
                    group.sdfVertices.data[copyVertOff + i] = matrix.MultiplyPoint3x4(verts[i]);

                Array.Copy(uvs0, 0, group.sdfUvs0.data, copyVertOff, sdfVc);
                Array.Copy(uvs1, 0, group.sdfUvs1.data, copyVertOff, sdfVc);
                Array.Copy(colors, 0, group.sdfColors.data, copyVertOff, sdfVc);

                if (uvs3 != null)
                {
                    group.sdfUvs3.EnsureCount(group.sdfVertexUsed);
                    Array.Copy(uvs3, 0, group.sdfUvs3.data, copyVertOff, sdfVc);
                }

                group.sdfUvs2.EnsureCount(group.sdfVertexUsed);
                if (isEffect && gen.Uvs2 != null)
                    Array.Copy(gen.Uvs2, 0, group.sdfUvs2.data, copyVertOff, sdfVc);
                else
                    Array.Clear(group.sdfUvs2.data, copyVertOff, sdfVc);

                if (isEffect)
                    passes[copy].revert();

                for (var i = 0; i < sdfTc; i++)
                    group.sdfTriangles.data[triWriteOff + i] = tris[i] + copyVertOff;
                triWriteOff += sdfTc;
            }

            var totalWrittenTris = totalSdfTc;
            if (totalWrittenTris < slot.totalSdfTriCapacity)
                Array.Clear(group.sdfTriangles.data, slot.sdfTriStart + totalWrittenTris,
                    slot.totalSdfTriCapacity - totalWrittenTris);

            if (emojiVc > 0)
            {
                var emojiSrcOff = sdfVc;
                var off = slot.emojiVertexStart;

                for (var i = 0; i < emojiVc; i++)
                    group.emojiVertices.data[off + i] = matrix.MultiplyPoint3x4(verts[emojiSrcOff + i]);

                Array.Copy(uvs0, emojiSrcOff, group.emojiUvs0.data, off, emojiVc);
                Array.Copy(colors, emojiSrcOff, group.emojiColors.data, off, emojiVc);

                var emojiTriOff = slot.emojiTriStart;
                var emojiTriSrcOff = sdfTc;
                for (var i = 0; i < emojiTc; i++)
                    group.emojiTriangles.data[emojiTriOff + i] = tris[emojiTriSrcOff + i] + off;

                if (emojiTc < slot.emojiTriCapacity)
                    Array.Clear(group.emojiTriangles.data, emojiTriOff + emojiTc,
                        slot.emojiTriCapacity - emojiTc);
            }

            slot.lastMatrix = matrix;
            meshUploadNeeded = true;
        }

        #endregion

        #region Mesh Upload

        private readonly List<(UniTextWorld component, ComponentSlot slot)> sortBuffer = new();

        private void OnAfterProcess()
        {
            if (meshUploadNeeded)
            {
                UploadMeshes();
                meshUploadNeeded = false;
            }
        }

        private void LateUpdate()
        {
            var anyChanged = false;
            foreach (var kvp in slots)
            {
                var component = kvp.Key;
                if (component == null) continue;
                var slot = kvp.Value;
                if (slot.sdfBaseVertexCount == 0 || slot.groupKey == 0) continue;

                var currentMatrix = component.transform.localToWorldMatrix;
                if (currentMatrix == slot.lastMatrix) continue;

                if (groups.TryGetValue(slot.groupKey, out var group))
                {
                    RetransformSlot(group, slot, currentMatrix);
                    slot.lastMatrix = currentMatrix;
                    anyChanged = true;
                }
            }

            if (anyChanged || meshUploadNeeded)
            {
                UploadMeshes();
                meshUploadNeeded = false;
            }
        }

        private void RetransformSlot(BatchGroup group, ComponentSlot slot, Matrix4x4 newMatrix)
        {
            var reMatrix = newMatrix * slot.lastMatrix.inverse;
            var totalVc = slot.sdfBaseVertexCount * slot.numCopies;

            for (var i = 0; i < totalVc; i++)
            {
                var idx = slot.sdfVertexStart + i;
                group.sdfVertices.data[idx] = reMatrix.MultiplyPoint3x4(group.sdfVertices.data[idx]);
            }

            if (slot.emojiVertexCount > 0)
            {
                for (var i = 0; i < slot.emojiVertexCount; i++)
                {
                    var idx = slot.emojiVertexStart + i;
                    group.emojiVertices.data[idx] = reMatrix.MultiplyPoint3x4(group.emojiVertices.data[idx]);
                }
            }
        }

        private void UploadMeshes()
        {
            foreach (var group in groups.Values)
            {
                group.actualSdfVertexUsed = 0;
                group.actualEmojiVertexUsed = 0;

                sortBuffer.Clear();
                foreach (var kvp in slots)
                {
                    var slot = kvp.Value;
                    if (slot.groupKey == 0 || slot.sdfBaseVertexCount == 0) continue;
                    if (!groups.TryGetValue(slot.groupKey, out var g) || g != group) continue;

                    var svEnd = slot.sdfVertexStart + slot.sdfBaseVertexCount * slot.numCopies;
                    if (svEnd > group.actualSdfVertexUsed) group.actualSdfVertexUsed = svEnd;

                    var evEnd = slot.emojiVertexStart + slot.emojiVertexCount;
                    if (evEnd > group.actualEmojiVertexUsed) group.actualEmojiVertexUsed = evEnd;

                    sortBuffer.Add((kvp.Key, slot));
                }

                sortBuffer.Sort((a, b) =>
                {
                    var layerCmp = a.slot.cachedSortingLayerValue.CompareTo(b.slot.cachedSortingLayerValue);
                    if (layerCmp != 0) return layerCmp;
                    return a.slot.sortingOrder.CompareTo(b.slot.sortingOrder);
                });

                var totalSdfTris = 0;
                var totalEmojiTris = 0;
                foreach (var (_, slot) in sortBuffer)
                {
                    totalSdfTris += slot.sdfBaseVertexCount / 4 * 6 * slot.numCopies;
                    totalEmojiTris += slot.emojiVertexCount / 4 * 6;
                }

                group.sortedSdfTris.EnsureCapacity(totalSdfTris);
                group.sortedEmojiTris.EnsureCapacity(totalEmojiTris);

                var sdfTriOff = 0;
                var emojiTriOff = 0;
                foreach (var (_, slot) in sortBuffer)
                {
                    var trisPerCopy = slot.sdfBaseVertexCount / 4 * 6;

                    for (var copy = 0; copy < slot.numCopies; copy++)
                    {
                        var srcOff = slot.sdfTriStart + copy * trisPerCopy;
                        if (trisPerCopy > 0 && group.sdfTriangles.data != null)
                        {
                            Array.Copy(group.sdfTriangles.data, srcOff,
                                group.sortedSdfTris.data, sdfTriOff, trisPerCopy);
                            sdfTriOff += trisPerCopy;
                        }
                    }

                    var emojiTc = slot.emojiVertexCount / 4 * 6;
                    if (emojiTc > 0 && group.emojiTriangles.data != null)
                    {
                        Array.Copy(group.emojiTriangles.data, slot.emojiTriStart,
                            group.sortedEmojiTris.data, emojiTriOff, emojiTc);
                        emojiTriOff += emojiTc;
                    }
                }

                group.UploadToMeshes(sdfTriOff, emojiTriOff);
            }
        }

        #endregion

        #region Helpers

        private BatchGroup GetOrCreateGroup(long key, bool isMsdf)
        {
            if (groups.TryGetValue(key, out var group)) return group;
            group = new BatchGroup(isMsdf, transform);
            groups[key] = group;
            return group;
        }

        #endregion

        #region ComponentSlot

        private class ComponentSlot
        {
            public long groupKey;

            public int sdfVertexStart;
            public int sdfBaseVertexCount;
            public int numCopies;
            public int totalSdfCapacity;
            public int sdfTriStart;
            public int totalSdfTriCapacity;

            public int emojiVertexStart;
            public int emojiVertexCount;
            public int emojiCapacity;
            public int emojiTriStart;
            public int emojiTriCapacity;

            public int cachedSortingLayerValue;
            public int sortingOrder;

            public Matrix4x4 lastMatrix;
        }

        #endregion

        #region BatchGroup

        private class BatchGroup
        {
            public readonly bool isMsdf;

            public PooledBuffer<Vector3> sdfVertices;
            public PooledBuffer<Vector4> sdfUvs0;
            public PooledBuffer<Vector2> sdfUvs1;
            public PooledBuffer<Vector4> sdfUvs3;
            public PooledBuffer<Vector4> sdfUvs2;
            public PooledBuffer<Color32> sdfColors;
            public PooledBuffer<int> sdfTriangles;
            public int sdfVertexUsed;
            public int sdfTriUsed;
            public int actualSdfVertexUsed;

            public PooledBuffer<Vector3> emojiVertices;
            public PooledBuffer<Vector4> emojiUvs0;
            public PooledBuffer<Color32> emojiColors;
            public PooledBuffer<int> emojiTriangles;
            public int emojiVertexUsed;
            public int emojiTriUsed;
            public int actualEmojiVertexUsed;

            public PooledBuffer<int> sortedSdfTris;
            public PooledBuffer<int> sortedEmojiTris;

            private BatchLayer sdfLayer;
            private BatchLayer emojiLayer;
            private readonly Transform batcherTransform;

            public BatchGroup(bool isMsdf, Transform parent)
            {
                this.isMsdf = isMsdf;
                batcherTransform = parent;
            }

            public void RemoveSlot(ComponentSlot slot)
            {
                slot.sdfBaseVertexCount = 0;
                slot.emojiVertexCount = 0;
                slot.numCopies = 0;
            }

            public void EnsureBufferCapacity()
            {
                sdfVertices.EnsureCount(sdfVertexUsed);
                sdfUvs0.EnsureCount(sdfVertexUsed);
                sdfUvs1.EnsureCount(sdfVertexUsed);
                sdfUvs2.EnsureCount(sdfVertexUsed);
                sdfColors.EnsureCount(sdfVertexUsed);
                sdfTriangles.EnsureCount(sdfTriUsed);

                emojiVertices.EnsureCount(emojiVertexUsed);
                emojiUvs0.EnsureCount(emojiVertexUsed);
                emojiColors.EnsureCount(emojiVertexUsed);
                emojiTriangles.EnsureCount(emojiTriUsed);
            }

            public void UploadToMeshes(int sdfTriCount, int emojiTriCount)
            {
                var mat = isMsdf ? UniTextMaterialCache.Msdf : UniTextMaterialCache.Sdf;

                if (actualSdfVertexUsed > 0 && sdfTriCount > 0)
                {
                    sdfLayer ??= CreateLayer("SDF");
                    sdfLayer.go.SetActive(true);
                    var mesh = sdfLayer.mesh;
                    mesh.Clear();
                    mesh.SetVertices(sdfVertices.data, 0, actualSdfVertexUsed);
                    mesh.SetUVs(0, sdfUvs0.data, 0, actualSdfVertexUsed);
                    mesh.SetUVs(1, sdfUvs1.data, 0, actualSdfVertexUsed);
                    if (sdfUvs3.data != null)
                        mesh.SetUVs(3, sdfUvs3.data, 0, actualSdfVertexUsed);
                    mesh.SetUVs(2, sdfUvs2.data, 0, actualSdfVertexUsed);
                    mesh.SetColors(sdfColors.data, 0, actualSdfVertexUsed);
                    mesh.SetTriangles(sortedSdfTris.data, 0, sdfTriCount, 0);
                    sdfLayer.renderer.sharedMaterial = mat;
                }
                else if (sdfLayer != null)
                {
                    sdfLayer.mesh.Clear();
                    sdfLayer.go.SetActive(false);
                }

                if (actualEmojiVertexUsed > 0 && emojiTriCount > 0)
                {
                    emojiLayer ??= CreateLayer("Emoji");
                    emojiLayer.go.SetActive(true);
                    var mesh = emojiLayer.mesh;
                    mesh.Clear();
                    mesh.SetVertices(emojiVertices.data, 0, actualEmojiVertexUsed);
                    mesh.SetUVs(0, emojiUvs0.data, 0, actualEmojiVertexUsed);
                    mesh.SetColors(emojiColors.data, 0, actualEmojiVertexUsed);
                    mesh.SetTriangles(sortedEmojiTris.data, 0, emojiTriCount, 0);
                    emojiLayer.renderer.sharedMaterial = EmojiFont.Material;
                }
                else if (emojiLayer != null)
                {
                    emojiLayer.mesh.Clear();
                    emojiLayer.go.SetActive(false);
                }
            }

            private BatchLayer CreateLayer(string name)
            {
                var go = new GameObject($"-_UTWB_{name}_-")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                go.transform.SetParent(batcherTransform, false);

                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                var mesh = new Mesh
                {
                    name = $"UniTextWorldBatch_{name}",
                    hideFlags = HideFlags.HideAndDontSave
                };
                filter.sharedMesh = mesh;

                return new BatchLayer { go = go, renderer = renderer, mesh = mesh };
            }

            public void Destroy()
            {
                sdfVertices.Return();
                sdfUvs0.Return();
                sdfUvs1.Return();
                sdfUvs3.Return();
                sdfUvs2.Return();
                sdfColors.Return();
                sdfTriangles.Return();
                sortedSdfTris.Return();

                emojiVertices.Return();
                emojiUvs0.Return();
                emojiColors.Return();
                emojiTriangles.Return();
                sortedEmojiTris.Return();

                if (sdfLayer != null) DestroyLayer(sdfLayer);
                if (emojiLayer != null) DestroyLayer(emojiLayer);
            }

            private static void DestroyLayer(BatchLayer layer)
            {
                if (layer.mesh != null) ObjectUtils.SafeDestroy(layer.mesh);
                if (layer.go != null) ObjectUtils.SafeDestroy(layer.go);
            }
        }

        #endregion

        #region BatchLayer

        private class BatchLayer
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Mesh mesh;
        }

        #endregion
    }
}
