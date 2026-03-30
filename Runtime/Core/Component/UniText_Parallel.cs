using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// UniText partial class with Canvas-specific parallel processing support.
    /// </summary>
    public partial class UniText
    {
#if UNITEXT_TESTS

        public struct TestSegmentFontInfo
        {
            public int fontId;
        }

        #region Test Support

        private List<Mesh> testMeshSnapshots;
        private List<TestSegmentFontInfo> testSegmentFontInfo;
        private static List<Vector4> tempUvBuffer;
        public IReadOnlyList<Mesh> TestMeshSnapshots => testMeshSnapshots;
        public IReadOnlyList<TestSegmentFontInfo> TestSegmentFontInfoList => testSegmentFontInfo;

        protected override void CopyMeshesForTests()
        {
            if (renderData == null || renderData.Count == 0) return;

            testMeshSnapshots ??= new List<Mesh>();
            testSegmentFontInfo ??= new List<TestSegmentFontInfo>();
            tempUvBuffer ??= new List<Vector4>();

            foreach (var m in testMeshSnapshots)
            {
                ObjectUtils.SafeDestroy(m);
            }
            testMeshSnapshots.Clear();
            testSegmentFontInfo.Clear();

            foreach (var rd in renderData)
            {
                var copy = new Mesh();
                copy.vertices = rd.mesh.vertices;
                copy.triangles = rd.mesh.triangles;

                tempUvBuffer.Clear();
                rd.mesh.GetUVs(0, tempUvBuffer);
                copy.SetUVs(0, tempUvBuffer);

                copy.colors32 = rd.mesh.colors32;
                testMeshSnapshots.Add(copy);
            }

            for (var s = 0; s < renderData.Count; s++)
            {
                testSegmentFontInfo.Add(new TestSegmentFontInfo
                {
                    fontId = renderData[s].fontId
                });
            }
        }

        #endregion
#endif
    }
}
