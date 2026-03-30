using System;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.CallingConvention;

namespace LightSide
{
    internal static unsafe class Zstd
    {
    #if (UNITY_IOS || UNITY_TVOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
    #else
        private const string LibraryName = "unitext_native";
    #endif

        private const uint ZstdMagic = 0xFD2FB528;

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_decompress(void* src, int srcSize, void* dst, int dstCapacity);

        [DllImport(LibraryName, CallingConvention = Cdecl)]
        private static extern long ut_zstd_get_frame_content_size(void* src, int srcSize);

    #if UNITY_EDITOR
        private const string EditorLibraryName = "unitext_native_editor";

        [DllImport(EditorLibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_compress_bound(int srcSize);

        [DllImport(EditorLibraryName, CallingConvention = Cdecl)]
        private static extern int ut_zstd_compress(void* src, int srcSize, void* dst, int dstCapacity, int level);

        public static byte[] Compress(byte[] data, int level = 22)
        {
            if (data == null || data.Length == 0) return data;

            int bound = ut_zstd_compress_bound(data.Length);
            var output = new byte[bound];

            fixed (byte* src = data)
            fixed (byte* dst = output)
            {
                int written = ut_zstd_compress(src, data.Length, dst, bound, level);
                if (written <= 0)
                    throw new InvalidOperationException("Zstd compression failed");

                if (written < output.Length)
                    Array.Resize(ref output, written);

                return output;
            }
        }
    #endif

        public static bool IsCompressed(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            return data[0] == 0x28 && data[1] == 0xB5 && data[2] == 0x2F && data[3] == 0xFD;
        }

        public static byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0) return compressedData;

            fixed (byte* src = compressedData)
            {
                long contentSize = ut_zstd_get_frame_content_size(src, compressedData.Length);
                if (contentSize <= 0)
                    throw new InvalidOperationException("Zstd: unable to determine decompressed size");

                var output = new byte[contentSize];
                fixed (byte* dst = output)
                {
                    int written = ut_zstd_decompress(src, compressedData.Length, dst, (int)contentSize);
                    if (written != (int)contentSize)
                        throw new InvalidOperationException(
                            $"Zstd decompression failed: expected {contentSize} bytes, got {written}");

                    return output;
                }
            }
        }
    }
}
