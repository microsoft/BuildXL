// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record MasterElectionState(
        /// Current master machine
        MachineLocation Master,
        /// Role of the current machine
        Role Role);

    /// <nodoc />
    public interface IMasterElectionMechanism
    {
        /// <summary>
        /// Obtain role for the current machine
        /// </summary>
        public Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context);

        /// <summary>
        /// Release master role, if the current machine has it
        /// </summary>
        public Task<Result<Role?>> ReleaseRoleIfNecessaryAsync(OperationContext context);
    }
}
