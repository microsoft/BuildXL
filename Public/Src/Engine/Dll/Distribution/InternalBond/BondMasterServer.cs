// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System.Diagnostics.CodeAnalysis;
using System.Net;
using BondTransport;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Bond;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// BondMaster service impl
    /// </summary>
    public sealed class BondMasterServer : Master_Service, IServer
    {
        private readonly LoggingContext m_loggingContext;
        private readonly MasterService m_masterService;
        private BondTcpHost m_server;

        /// <summary>
        /// Class constructor
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "RemoteWorker disposes the workerClient")]
        public BondMasterServer(LoggingContext loggingContext, MasterService masterService)
        {
            m_loggingContext = loggingContext;
            m_masterService = masterService;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            // Create a bond host with a tracing bond service to trace bond calls.
            m_server = new BondTcpHost((ushort)port, IPAddress.Any, new TracingBondService(port, this, m_masterService.DistributionServices, m_loggingContext,
                TracingBondService.GenerateRegistrationVoid<AttachCompletionInfo>("AttachCompleted"),
                TracingBondService.GenerateRegistrationVoid<WorkerNotificationArgs>("Notify"),
                TracingBondService.GenerateRegistrationVoidVoid("Heartbeat")));
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_server.Dispose();
        }

        #region Service Methods

        /// <inheritdoc/>
        public override void AttachCompleted(Request<AttachCompletionInfo, Void> call)
        {
            BondServiceHelper.HandleRequest(call, (message) =>
            {
                var openBondMessage = message.ToOpenBond();

                m_masterService.AttachCompleted(openBondMessage);
            });
        }

        /// <inheritdoc/>
        public override void Notify(Request<WorkerNotificationArgs, Void> call)
        {
            BondServiceHelper.HandleRequest(call, (message) =>
            {
                var openBondMessage = message.ToOpenBond();

                m_masterService.ReceivedWorkerNotificationAsync(openBondMessage);
            });
        }

        /// <inheritdoc/>
        public override void Heartbeat(Request<RpcMessageBase, Void> call)
        {
            BondServiceHelper.HandleRequest(call, req => { });
        }

        #endregion Service Methods
    }
}
#endif
