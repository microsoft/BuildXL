// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NETCOREAPP

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Launcher.Server;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public partial class CacheServerDistributedContentTests : ContentMetadataStoreDistributedContentTests, IClassFixture<LocalRedisFixture>
    {
        public CacheServerDistributedContentTests(
            LocalRedisFixture redis,
            ITestOutputHelper output)
            : base(redis, output)
        {
            UseGrpcServer = true;
            GrpcEnvironment.Initialize(TestGlobal.Logger, new BuildXL.Cache.ContentStore.Grpc.GrpcEnvironmentOptions()
            {
                LoggingVerbosity = BuildXL.Cache.ContentStore.Grpc.GrpcEnvironmentOptions.GrpcVerbosity.Info
            });
        }

        protected override TestServerProvider CreateStore(Context context, DistributedCacheServiceArguments arguments)
        {
            // Need to enable ASP.Net Core GRPC
            arguments.Configuration.DistributedContentSettings.EnableAspNetCoreGrpc = true;
            arguments.Configuration.DistributedContentSettings.EnableAspNetCoreLogging = true;

            var server = new TestCacheServerWrapper(Host, arguments);

            return new TestServerProvider(server, new Func<IContentStore>(() =>
            {
                return server.Host.Store.Task.Result;
            }));
        }

        private class TestServiceHost : IDistributedCacheServiceHost, IDistributedCacheServiceHostInternal
        {
            private readonly TestHost _testHost;
            private readonly IDistributedCacheServiceHost _host;
            private readonly IDistributedCacheServiceHostInternal _hostInternal;

            public TaskCompletionSource<bool> StartupStartedSignal { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<ICacheServerServices> StartupCompletedSignal { get; } = new TaskCompletionSource<ICacheServerServices>();
            public TaskCompletionSource<IContentStore> Store { get; } = new TaskCompletionSource<IContentStore>();

            public TestServiceHost(TestHost testHost, IDistributedCacheServiceHost host = null)
            {
                _testHost = testHost;
                _host = host ?? _testHost;
                _hostInternal = (host as IDistributedCacheServiceHostInternal) ?? _testHost;
            }

            public void OnStartedService()
            {
                _host.OnStartedService();
            }

            public async Task OnStartedServiceAsync(OperationContext context, ICacheServerServices services)
            {
                try
                {
                    await _hostInternal.OnStartedServiceAsync(context, services);

                    var contentServer = (ILocalContentServer<IContentStore>)services;
                    Store.SetResult(contentServer.StoresByName["Default"]);
                    StartupCompletedSignal.SetResult(services);
                }
                catch (Exception ex)
                {
                    Store.SetException(ex);
                    StartupCompletedSignal.SetException(ex);
                }
            }

            public Task OnStartingServiceAsync()
            {
                return _host.OnStartingServiceAsync();
            }

            public Task OnStoppingServiceAsync(OperationContext context)
            {
                return _hostInternal.OnStoppingServiceAsync(context);
            }

            public void OnTeardownCompleted()
            {
                _host.OnTeardownCompleted();
            }

            public void RequestTeardown(string reason)
            {
                _host.RequestTeardown(reason);
            }

            public Task<RetrievedSecrets> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
            {
                return _testHost.RetrieveSecretsAsync(requests, token);
            }
        }

        private class TestCacheServerWrapper : StartupShutdownComponentBase
        {
            public TestServiceHost Host { get; }

            public TestCacheServerWrapper(TestHost testHost, DistributedCacheServiceArguments arguments)
            {
                string[] commandLineArguments = CreateCommandLine(arguments);

                var hostParameters = HostParameters.FromEnvironment();
                var hostInfo = new HostInfo(hostParameters.Stamp, hostParameters.Ring, new List<string>());

                var serviceHost = new CacheServiceStartup.ServiceHost(
                    new string[0],
                    arguments.Configuration,
                    hostParameters,
                    new Context(TestGlobal.Logger));

                Host = new TestServiceHost(testHost, serviceHost);

                var _ = arguments.Cancellation.Register(() => Host.StartupCompletedSignal.TrySetCanceled());

                RunInBackground("RunCacheService", async context =>
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token, arguments.Cancellation))
                    {
                        arguments = arguments with
                        {
                            Host = Host,
                            Cancellation = cts.Token
                        };

                        await DistributedCacheServiceFacade.RunAsync(arguments);

                        Assert.True(cts.IsCancellationRequested, "Cache service task shutdown prematurely");
                    }

                    return BoolResult.Success;
                },
                fireAndForget: false);
            }

            private string[] CreateCommandLine(DistributedCacheServiceArguments arguments)
            {
                var configurationText = DeploymentUtilities.JsonSerialize(arguments.Configuration);
                var configurationPath = Path.Combine(arguments.DataRootPath, "ServerCacheConfiguration.json");
                Directory.CreateDirectory(arguments.DataRootPath);
                File.WriteAllText(configurationPath, configurationText);

                return new[]
                {
                    "--cacheconfigurationPath", configurationPath
                };
            }

            protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
            {
                await base.StartupCoreAsync(context).ThrowIfFailureAsync();

                Host.StartupStartedSignal.SetResult(true);

                await Host.StartupCompletedSignal.Task;

                return BoolResult.Success;
            }

            protected override Tracer Tracer { get; } = new Tracer(nameof(TestCacheServerWrapper));
        }
    }
}

#endif