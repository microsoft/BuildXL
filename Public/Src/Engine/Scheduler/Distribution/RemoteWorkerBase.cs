// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// An abstract remote worker which is exposed to the BuildXL.Scheduler namespace
    /// </summary>
    public abstract class RemoteWorkerBase : Worker
    {
        /// <summary>
        /// Constructor
        /// </summary>
        protected RemoteWorkerBase(uint workerId, string name)
            : base(workerId, name)
        {
            Contract.Ensures(IsRemote);
        }
    }
}
