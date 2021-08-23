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
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
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
    public class GrpcMasterClientFactory<T> : StartupShutdownComponentBase, IClientAccessor<T>
        where T : class
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcMasterClientFactory<T>));

        private readonly IGlobalLocationStore _globalStore;
        private readonly IMasterElectionMechanism _masterElectionMechanism;
        private readonly IClientAccessor<MachineLocation, T> _clientAccessor;

        private AsyncLazy<Result<ClientKey>> _currentClientKeyLazy = AsyncLazy<Result<ClientKey>>.FromResult(null);

        public GrpcMasterClientFactory(
            IGlobalLocationStore globalStore,
            IClientAccessor<MachineLocation, T> clientAccessor,
            IMasterElectionMechanism masterElectionMechanism)
        {
            _globalStore = globalStore;
            _masterElectionMechanism = masterElectionMechanism;
            _clientAccessor = clientAccessor;
            LinkLifetime(clientAccessor);
        }

        private async ValueTask<ClientKey> GetClientAsync(OperationContext context)
        {
            var lazy = _currentClientKeyLazy;
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
                        AsyncLazy<Result<ClientKey>> oldValue = Interlocked.CompareExchange(
                            ref _currentClientKeyLazy,
                            new AsyncLazy<Result<ClientKey>>(() => GetMasterLocationAsync(context, masterId)),
                            lazy);

                        lazy = _currentClientKeyLazy;
                        return await lazy.GetValueAsync();
                    },
                    extraEndMessage: r => r.GetValueOrDefault()?.ToString());
            }

            return handleResult.ThrowIfFailure();
        }

        private async Task<Result<ClientKey>> GetMasterLocationAsync(OperationContext context, MachineId? masterId)
        {
            var clusterState = _globalStore.ClusterState;
            if (masterId != null && clusterState.TryResolve(masterId.Value, out var masterLocation))
            {
                return new ClientKey(masterId, masterLocation);
            }

            if (masterId == null)
            {
                var masterElectionState = await _masterElectionMechanism.GetRoleAsync(context);
                if (masterElectionState.Succeeded)
                {
                    if (masterElectionState.Value.Master.IsValid)
                    {
                        return new ClientKey(masterId, masterElectionState.Value.Master);
                    }
                }
                else
                {
                    return new ErrorResult(masterElectionState);
                }

                return new ErrorResult("Unknown master");
            }
            else
            {
                return new ErrorResult($"Can't resolve master id '{masterId}'");
            }
        }

        public async Task<TResult> UseAsync<TResult>(OperationContext context, Func<T, Task<TResult>> operation)
        {
            var clientKey = await GetClientAsync(context);
            return await _clientAccessor.UseAsync(context, clientKey.Machine, operation);
        }

        private record ClientKey(MachineId? MachineId, MachineLocation Machine);
    }
}
