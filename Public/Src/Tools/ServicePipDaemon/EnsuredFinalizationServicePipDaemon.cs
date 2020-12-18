// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// Responsible for accepting and handling TCP/IP connections from clients.
    /// This type of daemon implements a 'Finalize' operation, which is ensured to be
    /// called exactly once by the time the daemon stops.
    /// </summary>
    public abstract class EnsuredFinalizationServicePipDaemon : ServicePipDaemon
    {
        /// <summary>
        /// Positive if finalization was already requested.
        /// </summary>
        private int m_finalizeRequestCounter = 0;

        /// <inheritdoc />
        public EnsuredFinalizationServicePipDaemon(IParser parser, DaemonConfig daemonConfig, IIpcLogger logger, IIpcProvider rpcProvider = null, Client client = null)
            : base(parser, daemonConfig, logger, rpcProvider, client) { }

        /// <summary>
        /// Synchronous version of <see cref="FinalizeAsync"/>
        /// </summary>
        public IIpcResult Finalize()
        {
            return FinalizeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finalizes the session. The finalization request will only be issued once,
        /// subsequent calls to this method will be ignored and an error will be returned
        /// </summary>
        public async Task<IIpcResult> FinalizeAsync()
        {
            if (Interlocked.Increment(ref m_finalizeRequestCounter) > 1)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, "Finalize was called more than once");
            }

            return await DoFinalizeAsync();
        }

        /// <summary>
        /// Implementation of the finalization command 
        /// </summary>
        protected abstract Task<IIpcResult> DoFinalizeAsync();

        /// <summary>
        /// Requests shut down, causing this daemon to immediately stop listening for TCP/IP
        /// connections. Any pending requests, however, will be processed to completion.
        /// Before shutting down we request the finalizion the drop.
        /// </summary>
        public override void RequestStop()
        {
            base.RequestStop(); // Stop listening
            try
            {
                // We finalize the drop regardless of the build result
                // If finalization was already requested before, this call we be ignored.
                Finalize();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch (Exception)

            {
                // Ignore
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

    }
}