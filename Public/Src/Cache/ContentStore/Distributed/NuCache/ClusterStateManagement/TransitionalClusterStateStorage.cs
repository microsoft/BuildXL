// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// This storage is used only as a transition mechanism for moving from <see cref="RedisGlobalStore"/> to
    /// <see cref="AzureBlobStorageClusterState"/>.
    /// </summary>
    public class TransitionalClusterStateStorage : StartupShutdownSlimBase, IClusterStateStorage
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(TransitionalClusterStateStorage));

        private readonly IClusterStateStorage _primary;
        private readonly ISecondaryClusterStateStorage _secondary;

        public TransitionalClusterStateStorage(IClusterStateStorage primary, ISecondaryClusterStateStorage secondary)
        {
            _primary = primary;
            _secondary = secondary;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return await _primary.StartupAsync(context) & await _secondary.StartupAsync(context);
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return await _primary.ShutdownAsync(context) & await _secondary.ShutdownAsync(context);
        }

        public async Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            var pr = await _primary.RegisterMachineAsync(context, machineLocation);
            if (!pr.Succeeded)
            {
                return pr;
            }

            var mapping = pr.Value!;
            var sr = await _secondary.ForceRegisterMachineAsync(context, mapping);
            if (!sr.Succeeded)
            {
                return Result.FromError<MachineMapping>(sr);
            }

            return Result.Success(mapping);
        }

        public Task<BoolResult> ForceRegisterMachineAsync(OperationContext context, MachineMapping mapping)
        {
            return Task.FromResult(new BoolResult($"Invalid call to {nameof(ForceRegisterMachineAsync)} against {nameof(TransitionalClusterStateStorage)} for mapping {mapping}"));
        }

        public async Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            var pr = _primary.GetClusterUpdatesAsync(context, request);
            var sr = _secondary.GetClusterUpdatesAsync(context, request).FireAndForgetErrorsAsync(context);
            await Task.WhenAll(pr, sr);
            return await pr;
        }

        public async Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            var pr = _primary.HeartbeatAsync(context, request);
            var sr = _secondary.HeartbeatAsync(context, request).FireAndForgetErrorsAsync(context);
            await Task.WhenAll(pr, sr);
            return await pr;
        }
    }
}
