// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BondTransport;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Defines a bond service wrapper which logs received bond service calls
    /// </summary>
    internal sealed class TracingBondService : BondService
    {
        private readonly BondService m_innerService;
        private LoggingContext m_loggingContext;
        private readonly Stopwatch m_stopwatch;

        private readonly DistributionServices m_services;

        /// <summary>
        /// Class constructor
        /// </summary>
        public TracingBondService(int port, BondService innerService, DistributionServices services, LoggingContext loggingContext, params ServiceFunctionRegistration[] registrations)
            : base(innerService.ServiceName)
        {
            Contract.Requires(innerService != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(services != null);
            Analysis.IgnoreArgument(port);

            m_services = services;
            m_innerService = innerService;
            m_loggingContext = loggingContext;
            m_stopwatch = Stopwatch.StartNew();

            foreach (var registration in registrations)
            {
                registration.Register(this);
            }
        }

        /// <summary>
        /// Updates the logging context used to log bond calls
        /// </summary>
        public void UpdateLoggingContext(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Generates a registration for a function on the service
        /// </summary>
        public static ServiceFunctionRegistration GenerateRegistration<TFrom, TTo>(string functionName)
            where TFrom : RpcMessageBase, Microsoft.Bond.IBondSerializable, new()
            where TTo : Microsoft.Bond.IBondSerializable, new()
        {
            return new ServiceFunctionRegistration<TFrom, TTo>(functionName);
        }

        /// <summary>
        /// Generates a registration for a function on the service (returning void)
        /// </summary>
        public static ServiceFunctionRegistration GenerateRegistrationVoid<TFrom>(string functionName)
            where TFrom : RpcMessageBase, Microsoft.Bond.IBondSerializable, new()
        {
            return new ServiceFunctionRegistration<TFrom, Microsoft.Bond.Void>(functionName);
        }

        /// <summary>
        /// Generates a registration for a function on the service (void argument and void result)
        /// </summary>
        public static ServiceFunctionRegistration GenerateRegistrationVoidVoid(string functionName)
        {
            return new ServiceFunctionRegistration<RpcMessageBase, Microsoft.Bond.Void>(functionName);
        }

        private new static Request<TFrom, TTo> GenerateRequest<TFrom, TTo>(TransportAsyncResult msg, IBondTransportServer server)
            where TFrom : Microsoft.Bond.IBondSerializable, new()
            where TTo : Microsoft.Bond.IBondSerializable, new()
        {
            return BondService.GenerateRequest<TFrom, TTo>(msg, server);
        }

        /// <summary>
        /// Defines a function registration with the tracing bond service
        /// </summary>
        public abstract class ServiceFunctionRegistration
        {
            public abstract void Register(TracingBondService tracingService);
        }

        /// <summary>
        /// Registers a wrapper for a function to log the service call
        /// </summary>
        private sealed class ServiceFunctionRegistration<TFrom, TTo> : ServiceFunctionRegistration
            where TFrom : RpcMessageBase, Microsoft.Bond.IBondSerializable, new()
            where TTo : Microsoft.Bond.IBondSerializable, new()
        {
            private readonly string m_functionName;

            public ServiceFunctionRegistration(string functionName)
            {
                m_functionName = functionName;
            }

            public override void Register(TracingBondService tracingService)
            {
                var innerFunction = tracingService.m_innerService.GetFuncList()[m_functionName];
                tracingService.Register(m_functionName, new FunctionHelper((TransportAsyncResult msg, IBondTransportServer server) =>
                    {
                        var request = GenerateRequest<TFrom, TTo>(msg, server);
                        var callId = request.Message.Context.PacketHeaders.m_nettrace.m_callID.ToSystemGuid();

                        var payload = request.Message.Payload.Value;
                        var senderData = new RpcMachineData()
                        {
                            MachineName = payload.SenderName,
                            MachineId = payload.SenderId,
                            BuildId = payload.BuildId,
                        };

                        Logger.Log.DistributionReceiveBondCallFormat(
                            tracingService.m_loggingContext,
                            senderData,
                            m_functionName,
                            callId,
                            "Received call");

                        var startTime = tracingService.m_stopwatch.Elapsed;

                        try
                        {
                            Contract.Assert(tracingService.m_services != null, "Services is null");
                            Contract.Assert(senderData.BuildId != null, "SenderData BuildId is null");

                            if (tracingService.m_services.Verify(request, senderData.BuildId))
                            {
                                innerFunction(msg, server);
                            }
                        }
                        finally
                        {
                            Logger.Log.DistributionReceiveBondCallFormat(tracingService.m_loggingContext, senderData, m_functionName, callId, "Handled call. Duration={0}", tracingService.m_stopwatch.Elapsed - startTime);
                        }
                    }));
            }
        }
    }
}
#endif
