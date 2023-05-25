// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using Grpc.Core;
using ProtoBuf.Grpc.Client;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService;

public class GenericGrpcClientAccessor<TService, TClient> : StartupShutdownComponentBase, IClientAccessor<MachineLocation, TClient>
    where TService : class
    where TClient : class
{
    protected override Tracer Tracer { get; } = new Tracer($"{nameof(GenericGrpcClientAccessor<TService, TClient>)}<{typeof(TService).Name}, {typeof(TClient).Name}>");

    private readonly ConditionalWeakTable<ConnectionHandle, TClient> _clientTable = new();
    private readonly IClientAccessor<MachineLocation, ConnectionHandle> _connectionAccessor;
    private readonly Func<TService, TClient> _factory;
    private readonly LocalClient<TClient>? _localClient;

    public GenericGrpcClientAccessor(IClientAccessor<MachineLocation, ConnectionHandle> connectionAccessor, Func<TService, TClient> factory, LocalClient<TClient>? localClient = null)
    {
        _connectionAccessor = connectionAccessor;
        _factory = factory;
        _localClient = localClient;
        LinkLifetime(connectionAccessor);

        if (_localClient != null)
        {
            LinkLifetime(_localClient);
        }
    }

    public Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<TClient, Task<TResult>> operation)
    {
        if (_localClient?.Location.Equals(key) == true)
        {
            return operation(_localClient.Client);
        }

        return _connectionAccessor.UseAsync(
            context,
            key,
            connectionHandle =>
            {
                var client = _clientTable.GetValue(
                    connectionHandle,
                    h =>
                    {
                        var service = h.Channel.CreateGrpcService<TService>(
                            MetadataServiceSerializer.ClientFactory);
                        return _factory.Invoke(service);
                    });
                return operation(client);
            });
    }
}

public class GrpcClientAccessor<TService> : GenericGrpcClientAccessor<TService, TService>
    where TService : class
{
    public GrpcClientAccessor(IClientAccessor<MachineLocation, ConnectionHandle> connectionAccessor, LocalClient<TService>? localClient = null)
        : base(connectionAccessor, x => x, localClient)
    {
    }
}

public class GrpcConnectionPool : StartupShutdownComponentBase, IClientAccessor<MachineLocation, ConnectionHandle>
{
    protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcConnectionPool));

    private readonly ResourcePool<MachineLocation, ConnectionHandle> _pool;

    /// <nodoc />
    public GrpcConnectionPool(ConnectionPoolConfiguration configuration, OperationContext context, IClock? clock = null)
    {
        _pool = new ResourcePool<MachineLocation, ConnectionHandle>(
            context,
            configuration,
            location => new ConnectionHandle(
                context,
                location,
                configuration.DefaultPort,
                grpcDotNetOptions: configuration.GrpcDotNetOptions,
                connectionTimeout: configuration.ConnectTimeout),
            clock);
    }

    /// <inheritdoc />
    protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
    {
        _pool.Dispose();
        return BoolResult.SuccessTask;
    }

    /// <nodoc />
    public Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<ConnectionHandle, Task<TResult>> operation)
    {
        return _pool.UseAsync(context, key, wrapper => operation(wrapper.Value));
    }
}

public class ConnectionHandle : StartupShutdownSlimBase
{
    protected override Tracer Tracer { get; } = new Tracer(nameof(ConnectionHandle));

    public string Host { get; }

    public int Port { get; }

    internal ChannelBase Channel { get; }

    protected override string GetArgumentsMessage() => $"{Host}:{Port}";

    private readonly TimeSpan _connectionTimeout;

    public ConnectionHandle(
        OperationContext context,
        MachineLocation location,
        int defaultPort,
        IEnumerable<ChannelOption>? grpcCoreOptions = null,
        GrpcDotNetClientOptions? grpcDotNetOptions = null,
        TimeSpan? connectionTimeout = null)
    {
        _connectionTimeout = connectionTimeout ?? Timeout.InfiniteTimeSpan;

        var hostInfo = location.ExtractHostInfo();
        Host = hostInfo.host;
        Port = hostInfo.port ?? defaultPort;


        var useGrpcDotNet = grpcDotNetOptions is not null || grpcCoreOptions is null;
#if !NET6_0_OR_GREATER
        useGrpcDotNet = false;
#endif
        if (useGrpcDotNet)
        {
            grpcDotNetOptions ??= GrpcDotNetClientOptions.Default;
            grpcCoreOptions = null;
        }
        else
        {
            grpcCoreOptions ??= ReadOnlyArray<ChannelOption>.Empty;
            grpcDotNetOptions = null;
        }

        Channel = GrpcChannelFactory.CreateChannel(
            context,
            new ChannelCreationOptions(
                useGrpcDotNet,
                Host,
                Port,
                grpcCoreOptions,
                grpcDotNetOptions),
            channelType: nameof(ConnectionHandle));
    }

    protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
    {
        await Channel.ConnectAsync(SystemClock.Instance, _connectionTimeout);
        return BoolResult.Success;
    }

    protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
    {
        await Channel.ShutdownAsync();
        return BoolResult.Success;
    }
}

public class ConnectionPoolConfiguration : ResourcePoolConfiguration
{
    public required int DefaultPort { get; init; }

    public required TimeSpan ConnectTimeout { get; init; }

    public required bool UseGrpcDotNet { get; init; }

    public required GrpcDotNetClientOptions GrpcDotNetOptions { get; init; }
}
