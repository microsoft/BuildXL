// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BondTransport;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Microsoft.Bond;
using Void = Microsoft.Bond.Void;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Allows communication with a remote worker process.
    /// </summary>
    internal class BondWorkerClient : IWorkerClient, IBondProxyLogger
    {
        private BondProxyConnectionManager<WorkerProxyAdapter> m_proxyManager;
        private readonly string m_workerName;
        private LoggingContext m_loggingContext;
        private readonly object m_proxyManagerLock = new object();

        /// <summary>
        /// Adapter for Worker_Proxy which implements IBondProxyWithHeartbeat
        /// </summary>
        public sealed class WorkerProxyAdapter : Worker_Proxy, IBondProxyWithHeartbeat
        {
            public WorkerProxyAdapter(IBondTransportClient client)
                : base(client)
            {
            }

#region IBondProxyWithHeartbeat Members

            IAsyncResult IBondProxyWithHeartbeat.BeginRequest<T>(string methodName, Message<T> input, AsyncCallback callback, IBufferAllocator allocator)
            {
                return BeginRequest(methodName, input, allocator, callback, null);
            }

            void IBondProxyWithHeartbeat.CancelRequest(string methodName, IAsyncResult res)
            {
                CancelRequest(methodName, res);
            }

            Message<T> IBondProxyWithHeartbeat.EndRequest<T>(string methodName, IAsyncResult res)
            {
                return EndRequest<T>(methodName, res);
            }

#endregion
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        public BondWorkerClient(
            LoggingContext loggingContext, 
            string workerName, 
            string ipAddress,
            int port,
            DistributionServices services,
            EventHandler onActivateConnection, 
            EventHandler onDeactivateConnection, 
            EventHandler onConnectionTimeOut)
        {
            m_loggingContext = loggingContext;
            m_workerName = workerName;

            m_proxyManager = new BondProxyConnectionManager<WorkerProxyAdapter>(
                client: new BondTcpClient<WorkerProxyAdapter>(
                   new BondHostLog(loggingContext, ipAddress, port),
                   new BondConnectOptions(true, (uint)EngineEnvironmentSettings.DistributionConnectTimeout.Value.TotalMilliseconds)),
                loggingContext: loggingContext,
                services: services,
                createProxyCallback: client => new WorkerProxyAdapter(
                    ResiliencyTestBondTransportClient.TryWrapClientForTest(DistributedBuildRoles.Master, client)));

            m_proxyManager.OnActivateConnection += onActivateConnection;
            m_proxyManager.OnDeactivateConnection += onDeactivateConnection;
            m_proxyManager.OnConnectionTimeOut += onConnectionTimeOut;

            m_proxyManager.Start(ipAddress, port, this);
        }

        public async Task CloseAsync()
        {
            await Task.Yield();

            lock (m_proxyManagerLock)
            {
                // The proxy manager may be null if the RemoteWorker has been disposed. This can happen if everything
                // happened on the master and the Scheduler & Engine are disposed before the worker's acknowledgement for
                // the final state transition comes in. If that happens, just noop here.
                if (m_proxyManager != null)
                {
                    m_proxyManager.Terminate();
                }
            }
        }

        public void Dispose()
        {
            lock (m_proxyManagerLock)
            {
                m_proxyManager?.Dispose();
                m_proxyManager = null;
            }
        }

        /// <summary>
        /// Attaches the worker to the build session and sends the build data
        /// </summary>
        public async Task<RpcCallResult<Unit>> AttachAsync(OpenBond.BuildStartData message)
        {
            var internalBondMessage = message.ToInternalBond();

            var result = await m_proxyManager.Call<BuildStartData, Void>(
                internalBondMessage,
                functionName: "Attach");

            return result.ToUnit();
        }

        public async Task<RpcCallResult<Unit>> ExecutePipsAsync(OpenBond.PipBuildRequest message, IList<long> semiStableHashes)
        {
            var internalBondMessage = message.ToInternalBond();

            var result = await m_proxyManager.Call<PipBuildRequest, Void>(
                internalBondMessage,
                functionName: "ExecutePips",
                description: DistributionHelpers.GetExecuteDescription(semiStableHashes));
            return result.ToUnit();
        }

        public async Task<RpcCallResult<Unit>> ExitAsync(OpenBond.BuildEndData message, CancellationToken cancellationToken = default(CancellationToken))
        {
            var buildEndData = new BuildEndData()
            {
                Failure = message.Failure,
            };

            var result = await m_proxyManager.Call<BuildEndData, Void>(
                buildEndData,
                functionName: "Exit",
                cancellationToken: cancellationToken);
            return result.ToUnit();
        }

        #region IBondProxyLogger Members

        public void LogSuccessfulCall(LoggingContext loggingContext, string functionName, uint retry)
        {
            if (retry != 0)
            {
                Logger.Log.DistributionSuccessfulRetryCallToWorker(loggingContext, m_workerName, functionName);
            }
        }

        public void LogFailedCall(LoggingContext loggingContext, string functionName, uint retry, Failure failure)
        {
            Logger.Log.DistributionFailedToCallWorker(loggingContext, m_workerName, functionName, failure.DescribeIncludingInnerFailures());
        }

        public void LogCallException(LoggingContext loggingContext, string functionName, uint retry, Exception ex)
        {
            Logger.Log.DistributionCallWorkerCodeException(loggingContext, m_workerName, functionName, ExceptionUtilities.GetLogEventMessage(ex));
        }

#endregion
    }

    internal static class RpcCallExtensions
    {
        public static RpcCallResult<Unit> ToUnit(this RpcCallResult<Void> callResult)
        {
            return new RpcCallResult<Unit>(Unit.Void, callResult.Attempts, callResult.Duration, callResult.WaitForConnectionDuration);
        }
    }
}
#endif
