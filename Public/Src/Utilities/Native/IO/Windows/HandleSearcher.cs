// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Searches for process ids that have an open handle
    /// </summary>
    /// <remarks>
    /// This utilizes the windows RestartManager to get information about which processes have a handle open
    /// </remarks>
    public class HandleSearcher
    {
        private const int RmRebootReasonNone = 0;
        private const int ERROR_MORE_DATA = 234;

        /// <summary>
        /// Gets ids of any processes using the handle of a file
        /// </summary>
        public static bool GetProcessIdsUsingHandle(string absolutePath, out List<int> processIds)
        {
            processIds = new List<int>();
            uint handle;
            int sessionStartResult = RmStartSession(out handle, 0, Guid.NewGuid().ToString("N"));
            if (sessionStartResult == 0)
            {
                int sessionEndResult;
                try
                {
                    int registerResourcesResult = RmRegisterResources(handle, 1, new string[] { absolutePath }, 0, null, 0, null);
                    if (registerResourcesResult == 0)
                    {
                        RM_PROCESS_INFO[] processInfo = null;
                        uint pnProcInfoNeeded;
                        uint pnProcInfo = 0;
                        uint lpdwRebootReasons = RmRebootReasonNone;
                        int getListResult = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
                        while (getListResult == ERROR_MORE_DATA)
                        {
                            processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                            pnProcInfo = (uint)processInfo.Length;
                            getListResult = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                        }

                        if (getListResult == 0 && processInfo != null)
                        {
                            for (var i = 0; i < pnProcInfo; i++)
                            {
                                RM_PROCESS_INFO procInfo = processInfo[i];
                                processIds.Add(procInfo.Process.dwProcessId);
                            }
                        }
                    }
                }
                finally
                {
                    sessionEndResult = RmEndSession(handle);
                }

                if (sessionEndResult == 0)
                {
                    return true;
                }
            }

            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
#pragma warning disable SA1307 // Field must begin with upper-case letter
            public readonly int dwProcessId;
#pragma warning restore SA1307 // Field must begin with upper-case letter
            public readonly System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        // https://msdn.microsoft.com/en-us/library/windows/desktop/aa373674(v=vs.85).aspx
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
#pragma warning disable CA1823 // Unused field
            private const int CCH_RM_MAX_APP_NAME = 255;
            private const int CCH_RM_MAX_SVC_NAME = 63;
#pragma warning restore CA1823 // Unused field

#pragma warning disable SA1307 // Field must begin with upper-case letter
            public readonly RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public readonly string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public readonly string strServiceShortName;
            public RM_APP_TYPE ApplicationType;
            public readonly uint AppStatus;
            public readonly uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public readonly bool bRestartable;
#pragma warning restore SA1307 // Field must begin with upper-case letter
        }

        // https://msdn.microsoft.com/en-us/library/windows/desktop/aa373670(v=vs.85).aspx
        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000,
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);
    }
}
