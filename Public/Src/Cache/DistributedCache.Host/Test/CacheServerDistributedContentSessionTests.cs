// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NETCOREAPP

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
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
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class CacheServerDistributedContentTests : ContentMetadataStoreDistributedContentTests, IClassFixture<LocalRedisFixture>
    {
        protected override bool UseGrpcDotNet => true;

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

        /// <inheritdoc />
        protected override IGrpcServerHost<LocalServerConfiguration> GrpcHost { get; } = new CacheServiceStartup.LocalContentServerGrpcDotNetHost();

        protected override TestServerProvider CreateStore(Context context, DistributedCacheServiceArguments arguments)
        {
            // Need to enable gRPC.NET
            arguments.Configuration.DistributedContentSettings.EnableAspNetCoreGrpc = true;
            arguments.Configuration.DistributedContentSettings.EnableAspNetCoreLogging = true;

            var server = new TestCacheServerWrapper(Host, arguments);

            return new TestServerProvider(arguments, server, () => server.Host.Store.Task.Result);
        }

        private class TestServiceHost : IDistributedCacheServiceHost, IDistributedCacheServiceHostInternal
        {
            private readonly TestHost _testHost;
            private readonly IDistributedCacheServiceHost _host;

            public TaskCompletionSource<bool> StartupStartedSignal { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<ICacheServer> StartupCompletedSignal { get; } = new TaskCompletionSource<ICacheServer>();
            public TaskCompletionSource<IContentStore> Store { get; } = new TaskCompletionSource<IContentStore>();

            public TestServiceHost(TestHost testHost, IDistributedCacheServiceHost host = null)
            {
                _testHost = testHost;
                _host = host ?? _testHost;
            }

            public void OnStartedService()
            {
                _host.OnStartedService();
            }

            public Task OnStartedServiceAsync(OperationContext context, ICacheServer services)
            {
                try
                {
                    Store.SetResult(services.GetDefaultStore<IContentStore>());
                    StartupCompletedSignal.SetResult(services);
                }
                catch (Exception ex)
                {
                    Store.SetException(ex);
                    StartupCompletedSignal.SetException(ex);
                }

                return Task.CompletedTask;
            }

            public Task OnStartingServiceAsync()
            {
                return _host.OnStartingServiceAsync();
            }

            public Task OnStoppingServiceAsync(OperationContext context)
            {
                return Task.CompletedTask;
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

        private class TestCacheServerWrapper : StartupShutdownComponentBase, ICacheServer
        {
            public TestServiceHost Host { get; }

            /// <inheritdoc />
            bool ICacheServer.IsProxy => true;

            /// <inheritdoc />
            TStore ICacheServer.GetDefaultStore<TStore>() => throw new NotSupportedException();

            /// <inheritdoc />
            IPushFileHandler ICacheServer.PushFileHandler => throw new NotSupportedException();

            /// <inheritdoc />
            IDistributedStreamStore ICacheServer.StreamStore => throw new NotSupportedException();

            /// <inheritdoc />
            IEnumerable<IGrpcServiceEndpoint> ICacheServer.GrpcEndpoints => throw new NotSupportedException();

            public TestCacheServerWrapper(TestHost testHost, DistributedCacheServiceArguments arguments)
            {
                string[] commandLineArguments = CreateCommandLine(arguments);

                var hostParameters = HostParameters.FromEnvironment();
                var hostInfo = new HostInfo(hostParameters.Stamp, hostParameters.Ring, new List<string>());

                var serviceHost = new CacheServiceStartup.ServiceHost(
                    Array.Empty<string>(),
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

            public override async Task<BoolResult> StartupAsync(Context context)
            {
                var result = await base.StartupAsync(context);

                await Host.StartupCompletedSignal.Task;

                return result;
            }

            protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
            {
                Host.StartupStartedSignal.SetResult(true);

                return BoolResult.SuccessTask;
            }

            protected override Tracer Tracer { get; } = new Tracer(nameof(TestCacheServerWrapper));
        }
    }
}

#endif
