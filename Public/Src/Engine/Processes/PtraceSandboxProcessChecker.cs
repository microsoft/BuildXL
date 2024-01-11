// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// Determines whether a Linux binary requires running under the ptrace sandbox.
    /// </summary>
    public class PtraceSandboxProcessChecker
    {
        private readonly UnixObjectFileDumpUtils m_dumpUtils;
        private readonly UnixGetCapUtils m_getCapUtils;
        
        private PtraceSandboxProcessChecker()
        {
            m_dumpUtils = UnixObjectFileDumpUtils.CreateObjDump();
            m_getCapUtils = UnixGetCapUtils.CreateGetCap();
        }

        /// <nodoc/>
        public static PtraceSandboxProcessChecker Instance { get; } = new PtraceSandboxProcessChecker();

        /// <summary>
        /// Returns whether all the tools required to detect whether a binary requires the ptrace sandbox are installed in the system
        /// </summary>
        public static bool AreRequiredToolsInstalled(out string error)
        {
            if (!UnixObjectFileDumpUtils.IsObjDumpInstalled.Value)
            {
                error = "The objdump utility is not installed on this machine. BuildXL uses this to detect processes that may not work properly with its sandbox. Please install it by running 'apt-get install binutils'.";
                return false;
            }

            if (!UnixGetCapUtils.IsGetCapInstalled.Value)
            {
                error = "The getcap utility is not installed on this machine. BuildXL uses this to detect processes that may not work properly with its sandbox. Please install it by running 'apt-get install libcap-progs'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Returns whether the given binary requires ptrace
        /// </summary>
        public bool BinaryRequiresPTraceSandbox(string binary) => m_dumpUtils.IsBinaryStaticallyLinked(binary) || m_getCapUtils.BinaryContainsCapabilities(binary);
    }
}
