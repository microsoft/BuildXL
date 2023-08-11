// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Launcher.Server;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using ProtoBuf.Grpc.Client;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

public class GrpcClusterStateStorageTests
{
#if NET6_0_OR_GREATER
    [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
    public async Task RunInMemoryWithGrpcTest()
    {
        var tracingContext = new Context(TestGlobal.Logger);
        var context = new OperationContext(tracingContext);

        var port = PortExtensions.GetNextAvailablePort();

        // Setup server-side
        var service = new GrpcClusterStateStorageService(new InMemoryClusterStateStorage());
        await service.StartupAsync(context).ThrowIfFailureAsync();

        var clusterStateEndpoint = new ProtobufNetGrpcServiceEndpoint<IGrpcClusterStateStorage, GrpcClusterStateStorageService>(nameof(GrpcClusterStateStorageService), service);

        var initializer = new GrpcDotNetHost();
        var grpcHostConfiguration = new GrpcDotNetHostConfiguration(port, GrpcDotNetServerOptions.Default);
        await initializer.StartAsync(context, grpcHostConfiguration, new[] { clusterStateEndpoint })
            .ThrowIfFailureAsync();

        // Setup client-side
        var location = MachineLocation.Create(Environment.MachineName, port);

        var connectionHandle = new ConnectionHandle(context, location, port);
        await connectionHandle.StartupAsync(context).ThrowIfFailureAsync();

        var clusterStateManager = new ClusterStateManager(new ClusterStateManager.Configuration
        {
            PrimaryLocation = location,
        }, new GrpcClusterStateStorageClient(
            configuration: new GrpcClusterStateStorageClient.Configuration(
                TimeSpan.FromMinutes(1),
                RetryPolicyConfiguration.Exponential()),
            accessor: new FixedClientAccessor<IGrpcClusterStateStorage>(
                connectionHandle.Channel.CreateGrpcService<IGrpcClusterStateStorage>(MetadataServiceSerializer.ClientFactory))));

        await clusterStateManager.StartupAsync(context).ThrowIfFailureAsync();

        clusterStateManager.ClusterState.OpenMachines.Should().Contain(clusterStateManager.ClusterState.PrimaryMachineId);
        await clusterStateManager.HeartbeatAsync(context, MachineState.Closed).ThrowIfFailureAsync();
        clusterStateManager.ClusterState.ClosedMachines.Should().Contain(clusterStateManager.ClusterState.PrimaryMachineId);

        await clusterStateManager.ShutdownAsync(context).ThrowIfFailureAsync();
        await connectionHandle.ShutdownAsync(context).ThrowIfFailureAsync();

        await initializer.StopAsync(context, grpcHostConfiguration).ThrowIfFailureAsync();

        await service.ShutdownAsync(context).ThrowIfFailureAsync();
    }
#endif
}
