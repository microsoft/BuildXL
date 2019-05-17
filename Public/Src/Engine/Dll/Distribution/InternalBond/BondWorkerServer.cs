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
    public sealed class BondWorkerServer : Worker_Service, IServer
    {
        private readonly WorkerService m_workerService;
        private BondTcpHost m_server;
        private readonly TracingBondService m_tracingBondService;
        private readonly int m_port;
        private LoggingContext m_loggingContext;

        /// <summary>
        /// Class constructor
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "RemoteWorker disposes the workerClient")]
        public BondWorkerServer(LoggingContext loggingContext, WorkerService workerService, int port, DistributionServices services)
        {
            m_loggingContext = loggingContext;
            m_port = port;
            m_workerService = workerService;
            m_tracingBondService = new TracingBondService(port, this, services, loggingContext,
                TracingBondService.GenerateRegistrationVoid<BuildStartData>("Attach"),
                TracingBondService.GenerateRegistrationVoid<PipBuildRequest>("ExecutePips"),
                TracingBondService.GenerateRegistrationVoidVoid("Exit"),
                TracingBondService.GenerateRegistrationVoidVoid("Heartbeat"));
        }

        /// <nodoc/>
        public void Start(int port)
        {
            // Create a bond host with a tracing bond service to trace bond calls.
            m_server = new BondTcpHost((ushort)m_port, IPAddress.Any, m_tracingBondService);
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_server?.Dispose();
        }

        /// <summary>
        /// Initiates the worker with information about the build and returns data about the worker
        /// </summary>
        public override void Attach(Request<BuildStartData, Void> call)
        {
            BondServiceHelper.HandleRequest(call, (message) =>
            {
                var openBondMessage = message.ToOpenBond();

                m_workerService.AttachCore(openBondMessage, message.SenderName);
            });
        }

        /// <summary>
        /// Exits the pips from the request
        /// </summary>
        public override void ExecutePips(Request<PipBuildRequest, Void> call)
        {
            BondServiceHelper.HandleRequest(call, (message) =>
            {
                var openBondMessage = message.ToOpenBond();

                m_workerService.ExecutePipsCore(openBondMessage);
            });
        }

        /// <summary>
        /// Heartbeat
        /// </summary>
        public override void Heartbeat(Request<RpcMessageBase, Void> call)
        {
            m_workerService.SetLastHeartbeatTimestamp();
            BondServiceHelper.HandleRequest(call, req => new Void());
        }

        /// <summary>
        /// Terminates the worker
        /// </summary>
        public override void Exit(Request<BuildEndData, Void> call)
        {
            m_workerService.BeforeExit();

            call.Dispatch(new Void());

            m_workerService.Exit(timedOut: false, failure: call.RequestObject.Failure);
        }

        /// <nodoc/>
        public void UpdateLoggingContext(LoggingContext loggingContext)
        {
            m_loggingContext = loggingContext;
            m_tracingBondService.UpdateLoggingContext(loggingContext);
        }
    }
}
#endif
