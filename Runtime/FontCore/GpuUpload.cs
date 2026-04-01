using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Per-slice GPU texture upload without Texture2DArray.Apply().
    /// Uses native plugin (D3D11/D3D12/OpenGL/Vulkan/Metal) or jslib (WebGL) to upload
    /// individual slices + mip levels directly to the GPU.
    /// Falls back to Apply() on unsupported renderers.
    ///
    /// Architecture: C# collects all upload requests into a NativeArray,
    /// then issues a single CommandBuffer.IssuePluginEventAndData.
    /// The render thread callback receives a pointer to the entire batch
    /// and processes all uploads atomically. No shared mutable state.
    /// </summary>
    internal static class GpuUpload
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void JS_GpuUpload_TexSubImage3D(
            int texId, int mipLevel, int sliceIndex,
            int width, int height, int bytesPerPixel, IntPtr pixels);

        [DllImport("__Internal")]
        private static extern void JS_GpuUpload_TexSubImage3DRegion(
            int texId, int mipLevel, int sliceIndex,
            int dstX, int dstY, int width, int height,
            int bytesPerPixel, int srcRowPitch, IntPtr pixels);
#else
    #if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        private const string LibName = "__Internal";
    #else
        private const string LibName = "unitext_gpu";
    #endif

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ut_gpu_get_upload_batch_event();

#endif

#if UNITY_EDITOR
        static GpuUpload() => Reseter.UnmanagedCleaning += Cleanup;
#endif

        private static void Cleanup()
        {
            for (int i = 0; i < pendingBuffers.Count; i++)
                pendingBuffers[i].Dispose();
            pendingBuffers.Clear();
            cmdBuffer?.Dispose();
            cmdBuffer = null;
            initialized = false;
            supported = false;
        }

        private static bool initialized;
        private static bool supported;
        private static IntPtr batchEventFunc;
        private static CommandBuffer cmdBuffer;

        private static readonly System.Collections.Generic.List<NativeArray<byte>> pendingBuffers = new();
        private static int lastDisposeFrame = -1;

        /// <summary>True if native per-slice upload is available on this renderer.</summary>
        public static bool IsSupported
        {
            get
            {
                if (!initialized) Initialize();
                return supported;
            }
        }

        private static void Initialize()
        {
            initialized = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            supported = SystemInfo.supports2DArrayTextures;
#else
            try
            {
                batchEventFunc = ut_gpu_get_upload_batch_event();
                var renderer = SystemInfo.graphicsDeviceType;
                bool rendererSupported = renderer switch
                {
                    GraphicsDeviceType.Direct3D11 => true,
                    GraphicsDeviceType.Direct3D12 => true,
                    GraphicsDeviceType.Metal => true,
                    GraphicsDeviceType.OpenGLCore => true,
                    GraphicsDeviceType.OpenGLES3 => true,
                    GraphicsDeviceType.Vulkan => true,
                    _ => false
                };
                supported = batchEventFunc != IntPtr.Zero && rendererSupported;
            }
            catch
            {
                supported = false;
            }
#endif
            supported = false;
            if (supported)
            {
                cmdBuffer = new CommandBuffer { name = "UniText GPU Upload" };
                Cat.Meow($"[GpuUpload] Native per-slice upload initialized {SystemInfo.graphicsDeviceType}");
            }
            else
            {
                Cat.Meow($"[GpuUpload] Not supported, falling back to Apply() {SystemInfo.graphicsDeviceType}");
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct NativeUploadRequest
        {
            public IntPtr nativeTexPtr;
            public IntPtr pixelData;
            public int width;
            public int height;
            public int sliceIndex;
            public int mipLevel;
            public int bytesPerPixel;
            public int dstX;
            public int dstY;
            public int srcRowPitch;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct NativeBatchHeader
        {
            public int count;
            public int padding;
        }

        /// <summary>
        /// Uploads dirty slices of a Texture2DArray to the GPU.
        /// For each dirty slice, uploads all mip levels.
        /// </summary>
        public static unsafe void UploadDirtySlices(Texture2DArray atlas, ulong dirtyMask, int mipCount, IntPtr nativeTexPtr)
        {
            if (dirtyMask == 0) return;
            if (!IsSupported) return;

            int w = atlas.width;
            int h = atlas.height;
            int bpp = BppForFormat(atlas.format);

#if UNITY_WEBGL && !UNITY_EDITOR
            UploadWebGL(nativeTexPtr, atlas, dirtyMask, mipCount, w, h, bpp);
#else
            UploadNative(nativeTexPtr, atlas, dirtyMask, mipCount, w, h, bpp);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static unsafe void UploadWebGL(IntPtr nativeTexPtr, Texture2DArray atlas, ulong dirtyMask, int mipCount, int w, int h, int bpp)
        {
            int texId = (int)nativeTexPtr;
            ulong bits = dirtyMask;
            while (bits != 0)
            {
                int slice = TrailingZeroCount(bits);
                for (int mip = 0; mip < mipCount; mip++)
                {
                    int mipW = Math.Max(1, w >> mip);
                    int mipH = Math.Max(1, h >> mip);
                    var data = atlas.GetPixelData<byte>(mip, slice);
                    JS_GpuUpload_TexSubImage3D(texId, mip, slice, mipW, mipH, bpp,
                        (IntPtr)data.GetUnsafeReadOnlyPtr());
                }
                bits &= bits - 1;
            }
        }
#else
        private static unsafe void UploadNative(IntPtr nativeTexPtr, Texture2DArray atlas, ulong dirtyMask, int mipCount, int w, int h, int bpp)
        {
            int sliceCount = 0;
            {
                ulong tmp = dirtyMask;
                while (tmp != 0) { sliceCount++; tmp &= tmp - 1; }
            }
            int totalRequests = sliceCount * mipCount;

            int headerSize = UnsafeUtility.SizeOf<NativeBatchHeader>();
            int requestSize = UnsafeUtility.SizeOf<NativeUploadRequest>();
            int totalBytes = headerSize + requestSize * totalRequests;

            var batchBuffer = new NativeArray<byte>(totalBytes, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            byte* batchPtr = (byte*)batchBuffer.GetUnsafePtr();

            var header = (NativeBatchHeader*)batchPtr;
            header->count = totalRequests;
            header->padding = 0;

            var requests = (NativeUploadRequest*)(batchPtr + headerSize);

            int idx = 0;
            ulong bits = dirtyMask;
            while (bits != 0)
            {
                int slice = TrailingZeroCount(bits);
                for (int mip = 0; mip < mipCount; mip++)
                {
                    int mipW = Math.Max(1, w >> mip);
                    int mipH = Math.Max(1, h >> mip);
                    var data = atlas.GetPixelData<byte>(mip, slice);

                    requests[idx++] = new NativeUploadRequest
                    {
                        nativeTexPtr = nativeTexPtr,
                        pixelData = (IntPtr)data.GetUnsafeReadOnlyPtr(),
                        width = mipW,
                        height = mipH,
                        sliceIndex = slice,
                        mipLevel = mip,
                        bytesPerPixel = bpp
                    };
                }
                bits &= bits - 1;
            }

            int frame = Time.frameCount;
            if (frame != lastDisposeFrame)
            {
                lastDisposeFrame = frame;
                for (int i = 0; i < pendingBuffers.Count; i++)
                    pendingBuffers[i].Dispose();
                pendingBuffers.Clear();
            }

            cmdBuffer.Clear();
            cmdBuffer.IssuePluginEventAndData(batchEventFunc, 0, (IntPtr)batchPtr);
            Graphics.ExecuteCommandBuffer(cmdBuffer);

            pendingBuffers.Add(batchBuffer);
        }
#endif

        public struct SubRegion
        {
            public int sliceIndex;
            public int dstX, dstY;
            public int width, height;
            public int srcRowPitch;
            public IntPtr pixelData;
        }

        public static unsafe void UploadSubRegions(Texture2DArray atlas, SubRegion* regions, int count, IntPtr nativeTexPtr)
        {
            if (count == 0) return;
            if (!IsSupported) return;

            int bpp = BppForFormat(atlas.format);

#if UNITY_WEBGL && !UNITY_EDITOR
            int texId = (int)nativeTexPtr;
            for (int i = 0; i < count; i++)
            {
                ref var r = ref regions[i];
                JS_GpuUpload_TexSubImage3DRegion(texId, 0, r.sliceIndex,
                    r.dstX, r.dstY, r.width, r.height, bpp, r.srcRowPitch, r.pixelData);
            }
#else
            int headerSize = UnsafeUtility.SizeOf<NativeBatchHeader>();
            int requestSize = UnsafeUtility.SizeOf<NativeUploadRequest>();
            int totalBytes = headerSize + requestSize * count;

            var batchBuffer = new NativeArray<byte>(totalBytes, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            byte* batchPtr = (byte*)batchBuffer.GetUnsafePtr();

            var header = (NativeBatchHeader*)batchPtr;
            header->count = count;
            header->padding = 0;

            var requests = (NativeUploadRequest*)(batchPtr + headerSize);

            for (int i = 0; i < count; i++)
            {
                ref var r = ref regions[i];
                requests[i] = new NativeUploadRequest
                {
                    nativeTexPtr = nativeTexPtr,
                    pixelData = r.pixelData,
                    width = r.width,
                    height = r.height,
                    sliceIndex = r.sliceIndex,
                    mipLevel = 0,
                    bytesPerPixel = bpp,
                    dstX = r.dstX,
                    dstY = r.dstY,
                    srcRowPitch = r.srcRowPitch
                };
            }

            int frame = Time.frameCount;
            if (frame != lastDisposeFrame)
            {
                lastDisposeFrame = frame;
                for (int i = 0; i < pendingBuffers.Count; i++)
                    pendingBuffers[i].Dispose();
                pendingBuffers.Clear();
            }

            cmdBuffer.Clear();
            cmdBuffer.IssuePluginEventAndData(batchEventFunc, 0, (IntPtr)batchPtr);
            Graphics.ExecuteCommandBuffer(cmdBuffer);

            pendingBuffers.Add(batchBuffer);
#endif
        }

        private static int BppForFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.RGBA32: return 4;
                case TextureFormat.RHalf: return 2;
                case TextureFormat.RGBAHalf: return 8;
                default:
                    Debug.LogWarning($"[GpuUpload] Unknown texture format {format}, assuming 4 bpp");
                    return 4;
            }
        }

        private static int TrailingZeroCount(ulong value)
        {
            if (value == 0) return 64;
            int count = 0;
            while ((value & 1) == 0) { count++; value >>= 1; }
            return count;
        }
    }
}
