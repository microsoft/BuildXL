// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

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
        protected RemoteWorkerBase(uint workerId, string name, PipExecutionContext context, string workerIpAddress)
            : base(workerId, name, context, workerIpAddress)
        {
        }

        /// <summary>
        /// Completes when the worker finishes the set up process 
        /// (if successfull, the worker is in the Running state after this completes)
        /// true indicates success, false indicates failure at some step (either attachment or validation of cache connection)
        /// </summary>
        public abstract Task<bool> SetupCompletionTask { get; }

        /// <summary>
        /// Maximum amount of messages per batch in an RPC call
        /// </summary>
        public int MaxMessagesPerBatch
        {
            get => m_maxMessagesPerBatch;
            set => m_maxMessagesPerBatch = value;
        }
        private volatile int m_maxMessagesPerBatch = EngineEnvironmentSettings.MaxMessagesPerBatch.Value;

    }
}
