using UnityEngine;

namespace LightSide
{
    internal static class ObjectUtils
    {
        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
        
        public static void SafeDestroy(Object obj, bool allowDestroyAsset)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj, allowDestroyAsset);
        }
    }
}
