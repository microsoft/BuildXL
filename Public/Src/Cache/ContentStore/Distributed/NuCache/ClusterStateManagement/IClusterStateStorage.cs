// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// TODO: this exists because ClusterManagementStore implements IClusterManagementStore, and we don't want to
    /// implement these verbs for it. When we remove that part of the metadata service, we can remove
    /// IClusterManagementStore and move those verbs here.
    /// </summary>
    public interface IClusterStateStorage : IStartupShutdownSlim
    {
        public Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation);

        /// <summary>
        /// Notifies the store that the specified machine is alive
        /// </summary>
        Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request);

        /// <summary>
        /// Gets updates to the cluster state based on the provided <see cref="GetClusterUpdatesRequest.MaxMachineId"/>
        /// </summary>
        Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request);
    }

    public interface ISecondaryClusterStateStorage : IClusterStateStorage
    {
        public Task<BoolResult> ForceRegisterMachineAsync(OperationContext context, MachineMapping mapping);
    }
}
