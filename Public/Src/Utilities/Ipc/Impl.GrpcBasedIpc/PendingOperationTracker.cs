// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.Net;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Grpc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Grpc.Core;

namespace BuildXL.Ipc.GrpcBasedIpc
{
    /// <summary>
    /// Our IPC logic assumes pending requests after requesting stop will be 
    /// satisfied, but gRPC asks the user to keep track of that before calling Shutdown.
    /// This class keeps track of pending operations and waits for them to finish before
    /// calling the relevant shutdown callback, once a stop is requested.
    /// </summary>
    internal class PendingOperationTracker : IStoppable
    {
        private volatile bool m_stopped;
        public bool Stopped => m_stopped;

        private readonly TaskCompletionSource m_pendingCallsCompletion = new TaskCompletionSource();
        private Task PendingCalls => m_pendingCallsCompletion.Task;
        
        private IIpcLogger Logger { get; }

        private readonly string m_name;

        /// <summary>
        /// Task that completes whenever the shutdown callback completes, after a stop being requested and when all pending calls have been serviced
        /// </summary>
        public Task Completion => m_completionSource.Task;
        private readonly TaskCompletionSource m_completionSource = new TaskCompletionSource();

        private int m_pendingCalls;

        // The consumers of this class specify this callback that performs the operations that should be done on stop
        private readonly Func<Task> m_onStop;

        /// <nodoc />
        public PendingOperationTracker(Func<Task> onStopAsync, string name, IIpcLogger logger)
        {
            m_name = name;
            m_onStop = onStopAsync;
            Logger = logger;
        }

        public async Task<T> PerformOperationAsync<T>(Func<Task<T>> operation, T afterStopResult = default)
        {
            if (m_stopped)
            {
                return afterStopResult;
            }

            Interlocked.Increment(ref m_pendingCalls);
            try
            {
                return await operation();
            }
            finally
            {
                if (Interlocked.Decrement(ref m_pendingCalls) == 0)
                {
                    if (m_stopped)
                    {
                        Verbose("Stopping because all calls were serviced");
                        m_pendingCallsCompletion.TrySetResult();
                    }
                }
            }
        }

        public void RequestStop()
        {
            Logger.Verbose("STOP requested", m_name);
            if (!m_stopped)
            {
                m_stopped = true;
                StopAsync().Forget();
            }
        }

        /// <summary>
        /// Asynchronous stop - will set the completion when it is done
        /// </summary>
        private async Task StopAsync()
        {
            if (m_pendingCalls > 0)
            {
                // TODO [maly]: After verifying for a while that this doesn't throw we might want to get rid of PendingOperationTracker. 
                throw new Exception("An IPC client was stopped while a call was still pending. We don't expect this to happen!");
            }

            try
            {
                Verbose("Stopping....");
                await m_onStop();
            }
            catch (Exception e)
            {
                Logger.Error("[PendingOperationTracker: {0}] An exception ocurred while stopping the service. Detals: {0}", m_name, e.ToStringDemystified());
            }

            Logger.Verbose("Stopped");
            m_completionSource.SetResult();
        }

        void IDisposable.Dispose()
        {
        }

        private void Verbose(string message)
        {
            Logger.Verbose("[PendingOperationTracker: {0}] {1}", m_name, message);
        }
    }
}

#endif