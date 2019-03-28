// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Sessions;

namespace ContentStoreTest.Stores
{
    public class TestServiceClientContentStore : ServiceClientContentStore, ITestServiceClientContentStore
    {
        private const int WaitForServerReadyTimeoutMs = 10000;
        private const int WaitForExitTimeoutMs = 30000;
        private readonly ILogger _logger;
        private readonly ServiceConfiguration _configuration;
        private readonly LocalServerConfiguration _localContentServerConfiguration;
        private readonly TimeSpan? _heartbeatInterval;
        private ServiceProcess _serviceProcess;
        private string _overrideCacheName;
        private bool _doNotStartService;

        public TestServiceClientContentStore(
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentStoreConfiguration configuration,
            TimeSpan? heartbeatInterval,
            ServiceConfiguration serviceConfiguration,
            LocalServerConfiguration localContentServerConfiguration = null)
            : base(logger, fileSystem, configuration)
        {
            _logger = logger;

            _localContentServerConfiguration = localContentServerConfiguration;
            _serviceProcess = new ServiceProcess(_configuration, localContentServerConfiguration, configuration.Scenario, WaitForServerReadyTimeoutMs, WaitForExitTimeoutMs);
            _configuration = serviceConfiguration;
            _heartbeatInterval = heartbeatInterval;
        }

        public void SetOverrideCacheName(string value)
        {
            _overrideCacheName = value;
        }

        public void SetDoNotStartService(bool value)
        {
            _doNotStartService = value;
        }

        public async Task RestartServerAsync(Context context)
        {
            if (_doNotStartService)
            {
                throw new InvalidOperationException();
            }

            await _serviceProcess.ShutdownAsync(context).ShouldBeSuccess();
            _serviceProcess.Dispose();

            _serviceProcess = new ServiceProcess(_configuration, _localContentServerConfiguration, Configuration.Scenario, WaitForServerReadyTimeoutMs, WaitForExitTimeoutMs);
            await _serviceProcess.StartupAsync(context).ShouldBeSuccess();
        }

        public async Task ShutdownServerAsync(Context context)
        {
            await _serviceProcess.ShutdownAsync(context).ShouldBeSuccess();
            _serviceProcess.Dispose();
        }

        protected override async Task<BoolResult> PreStartupAsync(Context context)
        {
            _logger.Debug($"{nameof(TestServiceClientContentStore)}.{nameof(PreStartupAsync)} _doNotStartService={_doNotStartService}");
            if (_doNotStartService)
            {
                return BoolResult.Success;
            }

            var r = await _serviceProcess.StartupAsync(context);

            if (r.Succeeded)
            {
                if (!(await Task.Run(() => LocalContentServer.EnsureRunning(context, Configuration.Scenario, 10000))))
                {
                    return new BoolResult("Failed to detect server ready in separate process");
                }
            }

            return r;
        }

        protected override Task<BoolResult> PostShutdownAsync(Context context)
        {
            return !_doNotStartService && !_serviceProcess.ShutdownStarted
                ? _serviceProcess.ShutdownAsync(context)
                : Task.FromResult(BoolResult.Success);
        }

        // RPC config deferred until session creation because server has to be previously started.
        private ServiceClientRpcConfiguration GetRpcConfig()
        {
            var portReaderFactory =
                new MemoryMappedFileGrpcPortSharingFactory(_logger, _configuration.GrpcPortFileName);
            var portReader = portReaderFactory.GetPortReader();
            var grpcPort = portReader.ReadPort();
            return new ServiceClientRpcConfiguration(grpcPort, _heartbeatInterval);
        }

        public override CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(
            Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(ExecutionTracer, OperationContext(context), name, () =>
            {
                var session = new TestServiceClientContentSession(
                    name,
                    implicitPin,
                    Configuration.RetryPolicy,
                    _configuration.DataRootPath,
                    _overrideCacheName ?? Configuration.CacheName,
                    context.Logger,
                    FileSystem,
                    Configuration.Scenario,
                    this,
                    SessionTracer,
                    GetRpcConfig()
                    );
                return new CreateSessionResult<IReadOnlyContentSession>(session);
            });
        }

        public override CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(ExecutionTracer, OperationContext(context), name, () =>
            {
                var session = new TestServiceClientContentSession(
                    name,
                    implicitPin,
                    Configuration.RetryPolicy,
                    _configuration.DataRootPath,
                    _overrideCacheName ?? Configuration.CacheName,
                    context.Logger,
                    FileSystem,
                    Configuration.Scenario,
                    this,
                    SessionTracer,
                    GetRpcConfig());
                return new CreateSessionResult<IContentSession>(session);
            });
        }

        protected override void DisposeCore()
        {
            _serviceProcess?.Dispose();

            base.DisposeCore();
        }
    }
}
