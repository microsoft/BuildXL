// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

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

        private int m_effectiveTotalProcessSlots;

        /// <inheritdoc/>
        public override int EffectiveTotalProcessSlots => m_effectiveTotalProcessSlots;

        /// <summary>
        /// Completes when the worker finishes the attachment process (successfully or otherwise)
        /// </summary>
        public abstract Task AttachCompletionTask { get; }

        /// <summary>
        /// Set effective total process slots based on the StatusReported event came from the remote worker
        /// </summary>
        public void SetEffectiveTotalProcessSlots(int newEffectiveProcessSlots)
        {
            m_effectiveTotalProcessSlots = newEffectiveProcessSlots;
        }
    }
}
