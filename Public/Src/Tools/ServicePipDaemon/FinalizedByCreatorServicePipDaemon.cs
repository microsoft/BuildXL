// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// This type of daemon implements both a 'Create' and a 'Finalize' operation.
    /// The 'Finalize' operation is ensured to be called exactly once and by the daemon who issued the 'Create' operation 
    /// by the time that daemon stops (i.e. only the first 'Finalize' call has any effect and subsequent ones are ignored, 
    /// and if it is never explicitly called then the command is issued right before the daemon shuts down).
    /// 
    /// The 'Finalize' command can only be issued by the same daemon that called 'Create', or it will fail.
    /// </summary>
    /// <remarks> 
    /// Daemons of this type must ensure that 'Create' and 'Finalize' are called from the same instance, lest 'Finalize' fails
    /// (e.g. both SymbolDaemon and DropDaemon set mustRunOnOrchestrator = true for both operations).
    /// </remarks>
    public abstract class FinalizedByCreatorServicePipDaemon : ServicePipDaemon
    {
        /// <summary>
        /// Positive if finalization was already requested.
        /// </summary>
        private int m_finalizeRequestCounter = 0;

        /// <summary>
        /// True if this daemon issued the 'Create' command
        /// </summary>
        private bool m_wasCreator = false;

        /// <inheritdoc />
        public FinalizedByCreatorServicePipDaemon(IParser parser, DaemonConfig daemonConfig, IIpcLogger logger, IIpcProvider rpcProvider = null, Client client = null)
            : base(parser, daemonConfig, logger, rpcProvider, client) { }

        /// <summary>
        /// Synchronous version of <see cref="FinalizeAsync"/>
        /// </summary>
        public IIpcResult Finalize()
        {
            return FinalizeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finalizes the session. This can only be called successfully if this is the same daemon that created it.
        /// The finalization request will only be issued once: subsequent calls to this method will be ignored and an error will be returned.
        /// </summary>
        public async Task<IIpcResult> FinalizeAsync()
        {
            if (!m_wasCreator)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, "Finalize must be called by the same service that called Create");
            }

            if (Interlocked.Increment(ref m_finalizeRequestCounter) > 1)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, "Finalize was called more than once");
            }

            return await DoFinalizeAsync();
        }

        /// <summary>
        /// Implementation of the finalization command.  
        /// </summary>
        protected abstract Task<IIpcResult> DoFinalizeAsync();

        /// <summary>
        /// Implementation of the creation command 
        /// </summary>
        protected abstract Task<IIpcResult> DoCreateAsync(string name = null);

        /// <summary>
        /// Synchronous version of <see cref="CreateAsync"/>
        /// </summary>
        public IIpcResult Create(string name = null)
        {
            return CreateAsync(name).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates the session
        /// </summary>
        public Task<IIpcResult> CreateAsync(string name = null)
        {
            m_wasCreator = true;
            return DoCreateAsync(name);
        }

        /// <summary>
        /// Requests shut down, causing this daemon to immediately stop listening for TCP/IP
        /// connections. Any pending requests, however, will be processed to completion.
        /// If this daemon issued the 'Create' command, and if 'Finalize' wasn't called explicitly before,
        /// we synchronously issue the command and wait for the result.
        /// </summary>
        public override void RequestStop()
        {
            base.RequestStop(); // Stop listening for connections 

            if (m_wasCreator && Interlocked.Increment(ref m_finalizeRequestCounter) == 1)
            {
                m_logger.Log(LogLevel.Info, LogPrefix + "Issuing the 'Finalize' command before shutting down: this means the command wasn't explicitly issued before.");
                try
                {
                    var result = DoFinalizeAsync().GetAwaiter().GetResult();
                    m_logger.Log(LogLevel.Info, LogPrefix + $"[FINALIZE] result: {result}");
                }
                catch (Exception e)
                {
                    m_logger.Log(LogLevel.Error, $"An exception was thrown while attempting to Finalize before shutting down: {e.ToStringDemystified()}");
                }
            }
        }
    }
}