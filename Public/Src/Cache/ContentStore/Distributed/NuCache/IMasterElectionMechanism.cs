// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record MasterElectionState(
        /// Current master machine
        MachineLocation Master,
        /// Role of the current machine
        Role Role)
    {
        public static MasterElectionState DefaultWorker = new MasterElectionState(Master: default, Role: Role.Worker);
    } 

    /// <nodoc />
    public interface IMasterElectionMechanism : IStartupShutdownSlim
    {
        /// <summary>
        /// Obtain the last known master to this machine
        /// </summary>
        public MachineLocation Master { get; }

        /// <summary>
        /// Obtains the last known role for this machine
        /// </summary>
        public Role Role { get; }

        /// <summary>
        /// Update and obtain role for the current machine
        /// </summary>
        public Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context);

        /// <summary>
        /// Release master role, if the current machine has it
        /// </summary>
        public Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context, bool shuttingDown);
    }
}
