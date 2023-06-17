// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// An implementation of <see cref="IMasterElectionMechanism"/> that doesn't actually do any election. It just returns
/// static values. This is useful for test scenarios where we want to control the master election process, and for real
/// use-cases where there's no master election to be held (for example, because it happens externally), but there's
/// still some concept of a master.
/// </summary>
public class RiggedMasterElectionMechanism : StartupShutdownComponentBase, IMasterElectionMechanism
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new Tracer(nameof(RiggedMasterElectionMechanism));

    /// <inheritdoc />
    public MachineLocation Master { get; }

    /// <inheritdoc />
    public Role Role { get; }

    public RiggedMasterElectionMechanism(MachineLocation master, Role role)
    {
        Master = master;
        Role = role;
    }

    /// <inheritdoc />
    public Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context)
    {
        return Task.FromResult(Result.Success(new MasterElectionState(Master, Role, DateTime.MaxValue)));
    }

    /// <inheritdoc />
    public Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context)
    {
        return Task.FromResult(Result.Success(Role));
    }
}
