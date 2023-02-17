// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// Contains some data to be passed to the Sandboxed Process Executor when executing in an external VM.
    /// </summary>
    public class ExternalVMSandboxedProcessData
    {
        /// <summary>
        /// This contains a set of stale outputs from a previous run of this pip that must be cleaned
        /// before performing a retry.
        /// </summary>
        public readonly IReadOnlyList<string> StaleFilesToClean;

        /// <nodoc/>
        public ExternalVMSandboxedProcessData(IReadOnlyList<string> staleFilesToClean)
        {
            Contract.Requires(staleFilesToClean != null);
            StaleFilesToClean = staleFilesToClean;
        }

        #region Serialization
        /// <nodoc/>
        public void Serialize(BuildXLWriter writer) => writer.WriteReadOnlyList(StaleFilesToClean, (w, s) => w.Write(s));

        /// <nodoc/>
        public static ExternalVMSandboxedProcessData Deserialize(BuildXLReader reader) =>
            new ExternalVMSandboxedProcessData(reader.ReadReadOnlyList(r => r.ReadString()));
        #endregion

    }
}
