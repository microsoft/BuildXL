// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Core.Logging;

namespace BuildXL.Plugin
{
    /// <summary>
    /// Abstrct class that implementes <see cref="PluginService.PluginServiceBase" />
    /// It defines the how to handle Start, Stop and SupportedOperation and inherted class should implementd other abstract methods
    /// </summary>
    public abstract class PluginServiceServer : PluginService.PluginServiceBase, IDisposable
    {
        private readonly Server m_server;

        private readonly TaskSourceSlim<Unit> m_shutdownTask = TaskSourceSlim.Create<Unit>();

        /// <nodoc />
        public abstract IList<SupportedOperationResponse.Types.SupportedOperation> SupportedOperations { get; }

        /// <nodoc />
        public ILogger Logger { get; }
        /// <nodoc />
        public int Port { get; }
        /// <nodoc />
        public Interceptor Interceptor { get; }
        /// <nodoc />
        public Task ShutdownCompletionTask => m_shutdownTask.Task;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="port"></param>
        /// <param name="logger"></param>
        public PluginServiceServer(int port, ILogger logger)
        {
            Port = port;
            Logger = logger;

            Interceptor = new PluginGrpcInterceptor(Logger);

            m_server = new Server(GrpcPluginSettings.GetChannelOptions())
            {
                Services = { PluginService.BindService(this).Intercept(Interceptor) },
                Ports = { new ServerPort(IPAddress.Loopback.ToString(), Port, ServerCredentials.Insecure) },
            };
        }

        /// <nodoc />
        public void Start()
        {
            Logger.Info("Server started");
            m_server.Start();
        }

        /// <nodoc />
        private void RequestStop()
        {
            Logger.Info("Received request to stop");
            m_shutdownTask.SetResult(Unit.Void);
        }

        /// <nodoc />
        public void Dispose()
        {
            Logger.Info("Server Shutdown");
            m_server.ShutdownAsync().Wait();
        }

        /// <nodoc />
        public override Task<PluginMessageResponse> Start(PluginMessage request, ServerCallContext context)
        {
            return HandleStart();
        }

        /// <nodoc />
        public override Task<PluginMessageResponse> Stop(PluginMessage request, ServerCallContext context)
        {
            return HandleStop();
        }

        /// <nodoc />
        public override Task<PluginMessageResponse> SupportedOperation(PluginMessage request, ServerCallContext context)
        {
            return HandleSupportedOperation();
        }

        /// <summary>
        /// Implementation of handling send method defined proto
        /// It handles the request based on <see cref="PluginMessage.PayloadCase" />
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<PluginMessageResponse> Send(PluginMessage request, ServerCallContext context)
        {
            switch (request.PayloadCase)
            {
                case PluginMessage.PayloadOneofCase.LogParseMessage:
                    return HandleLogParse(request.LogParseMessage);
                case PluginMessage.PayloadOneofCase.ExitCodeParseMessage:
                    return HandleExitCode(request.ExitCodeParseMessage);
                default:
                    return HandleUnknown();
            }
        }

        private Task<PluginMessageResponse> HandleStart()
        {
            return Task.FromResult<PluginMessageResponse>(new PluginMessageResponse() { Status = true });
        }

        private Task<PluginMessageResponse> HandleStop()
        {
            RequestStop();
            return Task.FromResult<PluginMessageResponse>(new PluginMessageResponse() { Status = true });
        }

        private Task<PluginMessageResponse> HandleUnknown()
        {
            return Task.FromResult<PluginMessageResponse>(new PluginMessageResponse() { Status = false });
        }

        private Task<PluginMessageResponse> HandleSupportedOperation()
        {
            var supportedOperationResponse = new SupportedOperationResponse();
            supportedOperationResponse.Operation.AddRange(SupportedOperations);
            return Task.FromResult<PluginMessageResponse>(new PluginMessageResponse() { Status = true, SupportedOperationResponse = supportedOperationResponse });
        }

        /// <summary>
        /// Log Parse plugin server should implemented this methods
        /// </summary>
        /// <param name="logParseMessage"></param>
        /// <returns>PluginMessageResponse should have LogParsedResult</returns>
        protected virtual Task<PluginMessageResponse> HandleLogParse(LogParseMessage logParseMessage)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Log Parse plugin server should implemented this methods
        /// </summary>
        /// <param name="exitCodeParseMessage"></param>
        /// <returns>PluginMessageResponse should have ExitCodeParseResult</returns>
        protected virtual Task<PluginMessageResponse> HandleExitCode(ExitCodeParseMessage exitCodeParseMessage)
        {
            throw new NotImplementedException();
        }
    }
}
