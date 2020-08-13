// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{

    /// <summary>
    /// Hooks for exercising functionality in tests
    /// </summary>
    public class TestHooks
    {
        /// <summary>
        /// Injects a failure when deleting files under a temp directory prior to executing a pip
        /// </summary>
        public bool FailDeletingTempDirectory { get; set; }

        /// <summary>
        /// Causes VmCommandProxy to simulate pip failure
        /// </summary>
        public bool FailVmCommandProxy { get; set; }
    }
}
