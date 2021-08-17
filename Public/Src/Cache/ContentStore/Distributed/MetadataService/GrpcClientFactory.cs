// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Grpc.Core;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Creates a gRPC connection to the CASaaS master, and returns a client that implements <typeparamref name="T"/>
    /// by talking via gRPC with it.
    /// </summary>
    public class GrpcMasterClientFactory<T> : StartupShutdownComponentBase, IClientFactory<T>
        where T : class
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcMasterClientFactory<T>));


        private readonly IGlobalLocationStore _globalStore;
        private readonly IMasterElectionMechanism _masterElectionMechanism;
        private readonly ClientContentMetadataStoreConfiguration _configuration;
        private readonly LocalClient<T> _localClient;

        private AsyncLazy<Result<ClientHandle>> _currentClientHandleLazy = AsyncLazy<Result<ClientHandle>>.FromResult(null);

        public GrpcMasterClientFactory(
            IGlobalLocationStore globalStore,
            ClientContentMetadataStoreConfiguration configuration,
            IMasterElectionMechanism masterElectionMechanism,
            LocalClient<T> localClient)
        {
            _globalStore = globalStore;
            _masterElectionMechanism = masterElectionMechanism;
            _configuration = configuration;
            _localClient = localClient;

            if (_localClient != null)
            {
                LinkLifetime(_localClient);
            }
        }

        public async ValueTask<T> CreateClientAsync(OperationContext context)
        {
            var lazy = _currentClientHandleLazy;
            var handleResult = await lazy.GetValueAsync();
            var handle = handleResult?.GetValueOrDefault();
            var clusterState = _globalStore.ClusterState;
            var masterId = clusterState.MasterMachineId;

            if (handle == null || masterId != handle.MachineId)
            {
                handleResult = await context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        AsyncLazy<Result<ClientHandle>> oldValue = Interlocked.CompareExchange(
                            ref _currentClientHandleLazy,
                            new AsyncLazy<Result<ClientHandle>>(() => CreateClientHandleAsync(context)),
                            lazy);

                        // Shutdown old channel
                        if (oldValue == lazy && handle?.Channel != null)
                        {
                            await handle.Channel.ShutdownAsync();
                        }

                        lazy = _currentClientHandleLazy;
                        return await lazy.GetValueAsync();
                    });
            }

            return handleResult.ThrowIfFailure().Client;
        }

        private async Task<Result<ClientHandle>> CreateClientHandleAsync(OperationContext context)
        {
            var masterId = _globalStore.ClusterState.MasterMachineId;
            var masterLocationResult = await GetMasterLocationAsync(context, masterId);
            if (!masterLocationResult)
            {
                return new ErrorResult(masterLocationResult);
            }

            if (_localClient != null && _localClient.Location.Equals(masterLocationResult.Value))
            {
                return new ClientHandle(_localClient.Client, null, null, _localClient.Location);
            }
            
            return await ConnectAsync(context, masterId, masterLocationResult.Value);
        }

        private async Task<Result<MachineLocation>> GetMasterLocationAsync(OperationContext context, MachineId? masterId)
        {
            var clusterState = _globalStore.ClusterState;
            if (masterId == null)
            {
                var masterElectionState = await _masterElectionMechanism.GetRoleAsync(context);
                if (masterElectionState.Succeeded)
                {
                    if (masterElectionState.Value.Master.IsValid)
                    {
                        return masterElectionState.Value.Master;
                    }
                }
                else
                {
                    return new ErrorResult(masterElectionState);
                }

                return new ErrorResult("Unknown master");
            }

            if (!clusterState.TryResolve(masterId.Value, out var masterLocation))
            {
                return new ErrorResult($"Can't resolve master id '{masterId}'");
            }

            return masterLocation;
        }

        private Task<Result<ClientHandle>> ConnectAsync(OperationContext context, MachineId? machineId, MachineLocation machineLocation)
        {
            var message = $"MasterId=[{machineId}] MasterLocation=[{machineLocation}]";
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var hostInfo = machineLocation.ExtractHostInfo();
                    var channel = new Channel(hostInfo.host, hostInfo.port ?? _configuration.Port, ChannelCredentials.Insecure);
                    var client = channel.CreateGrpcService<T>(ClientFactory.Create(MetadataServiceSerializer.BinderConfiguration));
                    var handle = new ClientHandle(client, channel, machineId, machineLocation);

                    await channel.ConnectAsync(DateTime.UtcNow + _configuration.ConnectTimeout);
                    return Result.Success(handle);
                },
                extraStartMessage: message,
                extraEndMessage: r => message);
        }

        private record ClientHandle(T Client, ChannelBase Channel, MachineId? MachineId, MachineLocation Machine);
    }
}
