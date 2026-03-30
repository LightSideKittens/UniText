using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Native multi-file dialog backed by unitext_native_editor library.
    /// Windows: GetOpenFileNameW, macOS: NSOpenPanel, Linux: GTK3.
    /// Editor-only.
    /// </summary>
    internal static class NativeFileDialog
    {
        private const string LibraryName = "unitext_native_editor";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr unitext_open_files_dialog(IntPtr title, IntPtr filters, IntPtr initialDir);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void unitext_free_dialog_result(IntPtr result);

        /// <summary>
        /// Opens a native multi-file selection dialog.
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="filters">Comma-separated extensions, e.g. "ttf,otf,ttc"</param>
        /// <param name="initialDir">Starting directory, or null</param>
        /// <returns>Array of selected file paths, or null if cancelled</returns>
        public static string[] OpenFiles(string title, string filters, string initialDir = null)
        {
            IntPtr titlePtr = ToUtf8Ptr(title);
            IntPtr filtersPtr = ToUtf8Ptr(filters);
            IntPtr dirPtr = ToUtf8Ptr(initialDir);
            try
            {
                IntPtr resultPtr = unitext_open_files_dialog(titlePtr, filtersPtr, dirPtr);
                if (resultPtr == IntPtr.Zero)
                    return null;

                try
                {
                    return ParseNullSeparatedUtf8(resultPtr);
                }
                finally
                {
                    unitext_free_dialog_result(resultPtr);
                }
            }
            finally
            {
                FreeUtf8Ptr(titlePtr);
                FreeUtf8Ptr(filtersPtr);
                FreeUtf8Ptr(dirPtr);
            }
        }

        private static IntPtr ToUtf8Ptr(string str)
        {
            if (str == null) return IntPtr.Zero;
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return ptr;
        }

        private static void FreeUtf8Ptr(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        }

        private static string[] ParseNullSeparatedUtf8(IntPtr ptr)
        {
            var paths = new List<string>();
            int offset = 0;

            while (true)
            {
                int len = 0;
                while (Marshal.ReadByte(ptr, offset + len) != 0)
                    len++;

                if (len == 0) break;

                byte[] bytes = new byte[len];
                Marshal.Copy(IntPtr.Add(ptr, offset), bytes, 0, len);
                paths.Add(Encoding.UTF8.GetString(bytes));
                offset += len + 1;
            }

            return paths.Count > 0 ? paths.ToArray() : null;
        }
    }
}
