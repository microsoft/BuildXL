// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Core
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
        /// Causes VmCommandProxy to simulate failures
        /// </summary>
        public SandboxedProcessExecutorTestHook SandboxedProcessExecutorTestHook { get; set; }

        /// <summary>
        /// Simulates Detours injection failures for a specified number of times before allowing it to succeed.
        /// A value of -1 (default) means disabled; set to a positive value to activate.
        /// </summary>
         public int? SimulateDetoursInjectionFailureCount { get; set; } = null;
    }
}
