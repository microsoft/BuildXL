// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Utility for interacting with the clipboard
    /// </summary>
    internal static class Clipboard
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(IntPtr hwndOwner);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        /// <summary>
        /// Copies the given text to the clipboard.
        /// </summary>
        /// <exception cref="Win32Exception">Throws an exception if the error ocurred during the operation.</exception>
        public static void CopyToClipboard(string text)
        {
            const uint CF_UNICODETEXT = 13;

            try
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    throw new Win32Exception(errorCode);
                }

                var stringPtr = Marshal.StringToHGlobalUni(text);
                if (SetClipboardData(CF_UNICODETEXT, stringPtr) == IntPtr.Zero)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    Marshal.FreeHGlobal(stringPtr);

                    throw new Win32Exception(errorCode);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }
}
