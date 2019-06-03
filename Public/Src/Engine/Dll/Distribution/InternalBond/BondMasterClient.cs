// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    /// Endpoint on worker for communicating with master service (mainly sending completion notifications for pip build requests).
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    internal sealed class BondMasterClient : IBondProxyLogger, IMasterClient, IDisposable
    {
#pragma warning disable CA1823 // Unused field
        private const string NotifyEventsFunctionName = "NotifyEvents";
#pragma warning restore CA1823 // Unused field

        private BondProxyConnectionManager<MasterProxyAdapter> m_proxyManager;

        private readonly BondTcpClient<BondMasterClient.MasterProxyAdapter> m_bondTcpClient;


        /// <summary>
        /// Adapter for Master_Proxy which implements IBondProxyWithHeartbeat
        /// </summary>
        public sealed class MasterProxyAdapter : Master_Proxy, IBondProxyWithHeartbeat
        {
            public MasterProxyAdapter(IBondTransportClient client)
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

        private readonly LoggingContext m_loggingContext;
        private readonly string m_ipAddress;
        private readonly int m_port;

        /// <summary>
        /// Class constructor
        /// </summary>
        public BondMasterClient(LoggingContext loggingContext, string ipAddress, int port)
        {
            m_loggingContext = loggingContext;
            m_port = port;
            m_ipAddress = ipAddress;
            m_bondTcpClient = new BondTcpClient<BondMasterClient.MasterProxyAdapter>(
                new BondHostLog(loggingContext, ipAddress, port),
                new BondConnectOptions(true, (uint)EngineEnvironmentSettings.DistributionConnectTimeout.Value.TotalMilliseconds));
        }

        /// <nodoc/>
        public async Task CloseAsync()
        {
            await Task.Yield();
            m_proxyManager?.Terminate();
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_proxyManager?.Dispose();
        }

        /// <nodoc/>
        public void Start(DistributionServices services, EventHandler onConnectionTimeOut)
        {
            m_proxyManager = new BondProxyConnectionManager<MasterProxyAdapter>(
                client: m_bondTcpClient,
                loggingContext: m_loggingContext,
                services: services,
                createProxyCallback: client => new BondMasterClient.MasterProxyAdapter(
                    ResiliencyTestBondTransportClient.TryWrapClientForTest(DistributedBuildRoles.Worker, client)));

            m_proxyManager.OnConnectionTimeOut += onConnectionTimeOut;

            m_proxyManager.Start(m_ipAddress, m_port, this);
        }

        public async Task<RpcCallResult<Unit>> AttachCompletedAsync(OpenBond.AttachCompletionInfo message)
        {
            var internalBondMessage = message.ToInternalBond();

            var result = await m_proxyManager.Call<AttachCompletionInfo, Void>(
                internalBondMessage,
                functionName: "AttachCompleted");

            return result.ToUnit();
        }

        /// <summary>
        /// Notifies the worker of pip completions
        /// </summary>
        public async Task<RpcCallResult<Unit>> NotifyAsync(OpenBond.WorkerNotificationArgs message, IList<long> semiStableHashes)
        {
            var internalBondMessage = message.ToInternalBond();

            var result = await m_proxyManager.Call<WorkerNotificationArgs, Void>(
                internalBondMessage,
                functionName: "Notify",
                description: DistributionHelpers.GetNotifyDescription(message, semiStableHashes));

            return result.ToUnit();
        }

        #region IBondProxyLogger Members

        public void LogSuccessfulCall(LoggingContext loggingContext, string functionName, uint retry)
        {
            if (retry != 0)
            {
                Logger.Log.DistributionSuccessfulRetryCallToMaster(loggingContext, functionName);
            }
        }

        public void LogFailedCall(LoggingContext loggingContext, string functionName, uint retry, Failure failure)
        {
            Logger.Log.DistributionFailedToCallMaster(loggingContext, functionName, failure.DescribeIncludingInnerFailures());
        }

        public void LogCallException(LoggingContext loggingContext, string functionName, uint retry, Exception ex)
        {
            Logger.Log.DistributionCallMasterCodeException(loggingContext, functionName, ExceptionUtilities.GetLogEventMessage(ex));
        }

        #endregion
    }
}
#endif
