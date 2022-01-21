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
    public interface IClusterStateStorage : IClusterManagementStore, IStartupShutdownSlim
    {
        public Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation);
    }

    public interface ISecondaryClusterStateStorage : IClusterStateStorage
    {
        public Task<BoolResult> ForceRegisterMachineAsync(OperationContext context, MachineMapping mapping);
    }
}
