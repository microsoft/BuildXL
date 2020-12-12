// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace BuildXL.Utilities
{

    /// <summary>
    /// Hooks for exercising functionality in SandboxedProcessExecutor Tests
    /// </summary>
    public class SandboxedProcessExecutorTestHook
    {
        /// <summary>
        /// Injects a failure while pinging the host from VM in SandboxedProcessExecutor
        /// </summary>
        public bool FailVmConnection { get; set; }

        #region Serialization

        /// <nodoc />
        public void Serialize(Stream stream)
        {
            using (var writer = new BuildXLWriter(false, stream, true, true))
            {
                writer.Write(FailVmConnection);
            }
        }

        /// <nodoc />
        public static SandboxedProcessExecutorTestHook Deserialize(Stream stream)
        {
            using (var reader = new BuildXLReader(false, stream, true))
            {
                bool failVmConnection = reader.ReadBoolean();

                return new SandboxedProcessExecutorTestHook {
                    FailVmConnection = failVmConnection
                };
            }
        }

        #endregion
    }
}
