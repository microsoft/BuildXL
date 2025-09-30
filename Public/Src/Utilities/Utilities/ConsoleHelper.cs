// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities
{
    /// <nodoc/>
    public class ConsoleHelper
    {
        private enum GetAncestorFlags
        {
            /// <summary>
            /// Retrieves the parent window. This does not include the owner, as it does with the GetParent function.
            /// </summary>
            GetParent = 1,
            /// <summary>
            /// Retrieves the root window by walking the chain of parent windows.
            /// </summary>
            GetRoot = 2,
            /// <summary>
            /// Retrieves the owned root window by walking the chain of parent and owner windows returned by GetParent.
            /// </summary>
            GetRootOwner = 3
        }

        /// <summary>
        /// Retrieves the handle to the ancestor of the specified window.
        /// </summary>
        /// <param name="hwnd">A handle to the window whose ancestor is to be retrieved.
        /// If this parameter is the desktop window, the function returns NULL. </param>
        /// <param name="flags">The ancestor to be retrieved.</param>
        /// <returns>The return value is the handle to the ancestor window.</returns>
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestorFlags flags);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        /// <summary>
        /// Returns a handle to the owned root window of the current console window
        /// </summary>
        /// <exception cref="InvalidOperationException">On non-Windows OSs</exception>
        public static IntPtr GetConsoleOrTerminalWindow()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                throw new InvalidOperationException("Getting a pointer to the console/terminal is a Windows-only operation");
            }

            IntPtr consoleHandle = GetConsoleWindow();
            IntPtr handle = GetAncestor(consoleHandle, GetAncestorFlags.GetRootOwner);

            return handle;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        /// <summary>
        /// Returns whether or not we can use terminal escape codes for things like progress UI and hyperlinks
        /// </summary>
        public static bool IsVirtualTerminalProcessingEnabled()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                // Non-Windows probably do support virtual terminal processing by default, but just avoid a behavior change by default
                return false;
            }

            const int STD_OUTPUT_HANDLE = -11;
            const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

            IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return false;
            }

            if (GetConsoleMode(handle, out uint mode))
            {
                return (mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0;
            }

            return false;
        }
    }
}
