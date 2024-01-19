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
using BuildXL.Utilities.Core.Tasks;
using Grpc.Core;
using ProtoBuf.Grpc.Client;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService;

/// <summary>
/// This class is responsible for managing connections to <see cref="TService"/>, while providing users with a
/// <see cref="TClient"/> to access it.
/// </summary>
public class GrpcDotNetClientAccessor<TService, TClient> : StartupShutdownComponentBase, IClientAccessor<MachineLocation, TClient>
    where TService : class
    where TClient : class
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new Tracer($"{nameof(GrpcDotNetClientAccessor<TService, TClient>)}<{typeof(TService).Name}, {typeof(TClient).Name}>");

    private readonly ConditionalWeakTable<ConnectionHandle, TClient> _clientTable = new();
    private readonly IClientAccessor<MachineLocation, ConnectionHandle> _connectionAccessor;
    private readonly Func<MachineLocation, TService, TClient> _factory;
    private readonly IFixedClientAccessor<TClient>? _localClient;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="connectionAccessor">Connection pool. Intended to be used with <see cref="GrpcConnectionMap"/></param>
    /// <param name="factory">
    ///     A function to map from a gRPC DotNet service specification (<see cref="TService"/>) to a
    ///     client that effectively uses the service (<see cref="TClient"/>).
    /// </param>
    /// <param name="localClient">
    ///     Allows for identifying a specific <see cref="TClient"/> that is local to the current process. This is
    ///     useful for preventing unnecessary network traffic or breaking dependencies (the process initialization
    ///     depends on gRPC communication to the current machine).
    /// </param>
    public GrpcDotNetClientAccessor(
        IClientAccessor<MachineLocation, ConnectionHandle> connectionAccessor,
        Func<MachineLocation, TService, TClient> factory,
        IFixedClientAccessor<TClient>? localClient = null)
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

    /// <inheritdoc />
    public Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<TClient, Task<TResult>> operation)
    {
        if (_localClient?.Location.Equals(key) == true)
        {
            return _localClient.UseAsync(context, operation);
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
                        var service = h.Channel.CreateGrpcService<TService>(MetadataServiceSerializer.ClientFactory);
                        return _factory.Invoke(key, service);
                    });
                return operation(client);
            });
    }
}

/// <summary>
/// Maintains a pool of connections to remote machines.
/// </summary>
/// <remarks>
/// Meant to be used with <see cref="GrpcDotNetClientAccessor{TService,TClient}"/>
/// </remarks>
public class GrpcConnectionMap : StartupShutdownComponentBase, IClientAccessor<MachineLocation, ConnectionHandle>
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcConnectionMap));

    /// <summary>
    /// This class is basically an adapter around the resource pool below used along with an adapter for the different
    /// kinds of gRPC connections that we support (<see cref="ConnectionHandle"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="ResourcePool{TKey,TObject}"/> takes care of starting up and shutting down
    /// <see cref="ConnectionHandle"/> as needed according to usage patterns.
    /// </remarks>
    private readonly ResourcePool<MachineLocation, ConnectionHandle> _pool;

    /// <nodoc />
    public GrpcConnectionMap(ConnectionPoolConfiguration configuration, OperationContext context, IClock? clock = null)
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
    public async Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<ConnectionHandle, Task<TResult>> operation)
    {
        // The pattern that's commonly used by our components has a key problem:
        // 1. This is node A, and we're trying to contact node B. However, node B is down.
        // 2. We try to contact node B below. The ResourcePool will try to create a connection to node B.
        // 3. Because B is down, the connection will take the entire ConnectionTimeout until it fails.
        // 4. Because ResourcePool doesn't obey CancellationToken, we'll wait for the entire ConnectionTimeout.
        //
        // If we were willing to wait 1ms for this operation to complete, but the ConnectionTimeout was 1m, we'll wind
        // up waiting 1m every time the operation happens.
        var performTask = _pool.UseAsync(context, key, wrapper => operation(wrapper.Value));
        await TaskUtilities.AwaitWithCancellationAsync(performTask, context.Token);
        return await performTask;
    }
}

public class ConnectionHandle : StartupShutdownSlimBase
{
    /// <inheritdoc />
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

        Channel = GrpcChannelFactory.CreateChannel(
            context,
            new ChannelCreationOptions(
                Host,
                Port,
                grpcCoreOptions,
                grpcDotNetOptions),
            channelType: nameof(ConnectionHandle));
    }

    protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
    {
        await Channel.ConnectAsync(Host, Port, SystemClock.Instance, _connectionTimeout);
        return BoolResult.Success;
    }

    protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
    {
        await Channel.DisconnectAsync();
        return BoolResult.Success;
    }
}

public class ConnectionPoolConfiguration : ResourcePoolConfiguration
{
    public required int DefaultPort { get; init; }

    public required TimeSpan ConnectTimeout { get; init; }

    public required GrpcDotNetClientOptions GrpcDotNetOptions { get; init; }
}
