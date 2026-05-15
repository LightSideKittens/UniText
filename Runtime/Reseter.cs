#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class Reseter
    {
        /// <summary>Phase 1: Stop threads and managed state cleanup.</summary>
        public static event Action ManagedCleaning;

        /// <summary>Phase 2: Dispose high-level resources (fonts, faces, caches).</summary>
        public static event Action UnmanagedCleaning;

        /// <summary>Phase 3: Shutdown low-level libraries (FreeType library). Must be LAST.</summary>
        public static event Action LibraryShutdown;

        static Reseter()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ManagedReset()
        {
            ManagedCleaning?.Invoke();
        }

        private static void OnBeforeAssemblyReload()
        {
            Cat.Meow("[Reseter] Phase 1: ManagedCleaning");
            ManagedCleaning?.Invoke();

            Cat.Meow("[Reseter] Phase 2: UnmanagedCleaning");
            UnmanagedCleaning?.Invoke();
            FT.ResetUnloadCounter();


            Cat.Meow("[Reseter] Phase 3: LibraryShutdown");
            LibraryShutdown?.Invoke();

            Cat.Meow("[Reseter] Cleanup completed");
        }
    }
}
#endif
