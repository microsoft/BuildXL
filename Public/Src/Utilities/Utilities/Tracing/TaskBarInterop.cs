// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// A class that provides Interop functionality to the Windows TaskBar Apis.
    /// </summary>
    public sealed class TaskbarInterop : IDisposable
    {
        private const string TaskBarListClsId = "56FDF344-FD6D-11d0-958A-006097C9A090";
#pragma warning disable CA1823 // Unused field

        // TODO:for the owner: the following const is not used. Please remove.
        private const string TaskbarList3Iid = "ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf";
#pragma warning restore CA1823 // Unused field

        [SuppressMessage("Microsoft.Reliability", "CA2006:UseSafeHandleToEncapsulateNativeResources")]
        private IntPtr m_consoleHwnd;

        private NativeMethods.ITaskbarList3 m_taskbarList;

        /// <summary>
        /// Initializes the current state.
        /// </summary>
        public void Init()
        {
            try
            {
                // Try to find the Hwnd for the current console. If we can't find it, we'll just silently ignore and not provide a progress bar.
                m_consoleHwnd = NativeMethods.GetConsoleWindow();
                if (m_consoleHwnd == IntPtr.Zero)
                {
                    return;
                }

                m_taskbarList = (NativeMethods.ITaskbarList3)Activator.CreateInstance(Marshal.GetTypeFromCLSID(new Guid(TaskBarListClsId)));
#pragma warning disable RS0015 // Always consume the value returned by methods marked with PreserveSigAttribute
                m_taskbarList.HrInit();
#pragma warning restore RS0015 // Always consume the value returned by methods marked with PreserveSigAttribute
            }
#pragma warning disable ERP022 // TODO: This should really catch the right exceptions
            catch
            {
            }
#pragma warning restore ERP022
        }

        /// <summary>
        /// Sets the progress state of the TaskBar if this is supported by the OS.
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/dd391697(v=vs.85).aspx
        /// </remarks>
        /// <param name="state">The state to set the progress bar to.</param>
        public void SetTaskbarState(TaskbarProgressStates state)
        {
            if (m_taskbarList != null)
            {
                Analysis.IgnoreResult(m_taskbarList.SetProgressState(m_consoleHwnd, state));
            }
        }

        /// <summary>
        /// Sets the progress value of the TaskBar if this is supported by the OS.
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/dd391698(v=vs.85).aspx
        /// </remarks>
        /// <param name="completed">The value that represents the completed 'items'</param>
        /// <param name="total">The total number of 'items' the progress bar reports on.</param>
        public void SetProgressValue(ulong completed, ulong total)
        {
            if (m_taskbarList != null)
            {
                Analysis.IgnoreResult(m_taskbarList.SetProgressValue(m_consoleHwnd, completed, total));
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Usage", "CA2216:DisposableTypesShouldDeclareFinalizer")]
        public void Dispose()
        {
            if (m_taskbarList != null)
            {
                NativeMethods.ITaskbarList3 taskbarList = m_taskbarList;
                m_taskbarList = null;
                if (taskbarList != null)
                {
                    Debug.Assert(Marshal.IsComObject(taskbarList), "TaskBar is not a valid com object");
                    Marshal.ReleaseComObject(taskbarList);
                }
            }
        }

        /// <summary>
        /// Flags enumeration to set the state of the progress bar.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
        [Flags]
        public enum TaskbarProgressStates
        {
            /// <summary>
            /// No progress
            /// </summary>
            NoProgress = 0x00000000,

            /// <summary>
            /// Intermediate
            /// </summary>
            Intermediate = 0x00000001,

            /// <summary>
            /// Normal
            /// </summary>
            Normal = 0x00000002,

            /// <summary>
            /// Error
            /// </summary>
            Error = 0x00000004,

            /// <summary>
            /// Paused
            /// </summary>
            Paused = 0x00000008,
        }

        /// <summary>
        /// Wrapper class to contain all the TaskBar native methods.
        /// </summary>
        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern IntPtr GetConsoleWindow();

            [ComImport]
            [Guid(TaskbarList3Iid)]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            internal interface ITaskbarList3
            {
                // ITaskbarList
                [PreserveSig]
                void HrInit();

                [PreserveSig]
                void AddTab(IntPtr hwnd);

                [PreserveSig]
                void DeleteTab(IntPtr hwnd);

                [PreserveSig]
                void ActivateTab(IntPtr hwnd);

                [PreserveSig]
                void SetActiveAlt(IntPtr hwnd);

                // ITaskbarList2
                [PreserveSig]
                void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

                // ITaskbarList3
                [PreserveSig]
                int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);

                [PreserveSig]
                int SetProgressState(IntPtr hwnd, TaskbarProgressStates tbpFlags);

                [PreserveSig]
                int RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);

                [PreserveSig]
                int UnregisterTab(IntPtr hwndTab);

                [PreserveSig]
                int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

                [PreserveSig]
                int SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);

                [PreserveSig]
                int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ThumbButton[] pButtons);

                [PreserveSig]
                int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ThumbButton[] pButtons);

                [PreserveSig]
                int ThumbBarSetImageList(IntPtr hwnd, [MarshalAs(UnmanagedType.IUnknown)] object himl);

                [PreserveSig]
                int SetOverlayIcon(IntPtr hwnd, SafeHandleZeroOrMinusOneIsInvalid hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);

                [PreserveSig]
                int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);

                [PreserveSig]
                int SetThumbnailClip(IntPtr hwnd, RefRect prcClip);
            }

#pragma warning disable 0169, 0649 // Disable unused field warning
            internal struct ThumbButton
            {
                /// <summary>
                /// WPARAM value for a THUMBBUTTON being clicked.
                /// </summary>
                internal const int THBN_CLICKED = 0x1800;

                internal ThumbButtonMask dwMask;
                internal uint iId;
                internal uint iBitmap;
                internal IntPtr hIcon;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                internal string szTip;

                internal ThumbButtonFlags dwFlags;
            }
#pragma warning restore 0169, 0649

            /// <summary>
            /// Flags enum for Thumb Button mask
            /// </summary>
            [Flags]
            internal enum ThumbButtonMask : uint
            {
                Bitmap = 0x0001,
                Icon = 0x0002,
                ToolTip = 0x0004,
                Flags = 0x0008,
            }

            /// <summary>
            /// Flags enum for Thumb Button
            /// </summary>
            [Flags]
            internal enum ThumbButtonFlags : uint
            {
                Enabled = 0x0000,
                Disabled = 0x0001,
                DismissOnClick = 0x0002,
                NoBackGroun = 0x0004,
                Hidden = 0x0008,
                NonInteractive = 0x0010,
            }

            [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
            [StructLayout(LayoutKind.Sequential)]
            internal sealed class RefRect
            {
                private int m_left;
                private int m_top;
                private int m_right;
                private int m_bottom;

                internal RefRect()
                {
                }

                internal RefRect(int left, int top, int right, int bottom)
                {
                    m_left = left;
                    m_top = top;
                    m_right = right;
                    m_bottom = bottom;
                }

                internal int Width
                {
                    get { return m_right - m_left; }
                }

                internal int Height
                {
                    get { return m_bottom - m_top; }
                }

                internal void Offset(int dx, int dy)
                {
                    m_left += dx;
                    m_top += dy;
                    m_right += dx;
                    m_bottom += dy;
                }

                internal bool IsEmpty
                {
                    get { return m_left >= m_right || m_top >= m_bottom; }
                }
            }
        }
    }
}
